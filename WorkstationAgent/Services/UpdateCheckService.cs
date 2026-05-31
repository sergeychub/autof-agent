using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Update;

namespace WorkstationAgent.Services;

internal sealed class UpdateCheckService : IDisposable
{
    private const string Runtime = "win-x64";
    private const string UpdaterTaskName = "AvtoforwardAgentUpdater";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AgentSettings _settings;
    private readonly AgentPaths _paths;
    private readonly FileLogger _logger;
    private readonly UpdateStateStore _stateStore;
    private readonly HttpClient _httpClient;
    private bool _publicKeyWarningLogged;

    public UpdateCheckService(
        AgentSettings settings,
        AgentPaths paths,
        FileLogger logger,
        UpdateStateStore stateStore)
    {
        _settings = settings;
        _paths = paths;
        _logger = logger;
        _stateStore = stateStore;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_settings.AutoUpdateEnabled)
        {
            _logger.Info("Auto-update is disabled.");
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await DelayWithJitterAsync(cancellationToken);

                try
                {
                    await CheckOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("Update check failed.", ex);
                    TryWriteState(UpdateStatuses.Failure, message: ex.Message);
                }

                await DelayIntervalAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        if (!UpdateTrust.IsConfigured(UpdateTrust.ManifestPublicKeyPem))
        {
            if (!_publicKeyWarningLogged)
            {
                _logger.Error("Auto-update is enabled, but the manifest public key placeholder has not been replaced.");
                _publicKeyWarningLogged = true;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.Error("Auto-update skipped because apiKey is empty.");
            TryWriteState(UpdateStatuses.Skipped, message: "apiKey is empty.");
            return;
        }

        var endpoint = BuildLatestEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Update latest endpoint failed ({(int)response.StatusCode}): {error}");
        }

        var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(cancellationToken: cancellationToken);
        if (manifest is null)
        {
            throw new InvalidOperationException("Update latest endpoint returned an empty manifest.");
        }

        ValidateManifest(manifest);
        if (!UpdateVersionComparer.IsUpdateAvailable(manifest.Version, AgentVersionProvider.CurrentVersion))
        {
            return;
        }

        Directory.CreateDirectory(_paths.UpdatesDirectory);
        await File.WriteAllTextAsync(
            _paths.PendingUpdateManifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions),
            cancellationToken);

        TryWriteState(UpdateStatuses.Available, manifest.ReleaseId, manifest.Version, "Update manifest accepted.");
        await TryReportAsync(manifest, UpdateStatuses.Available, "Update manifest accepted.", cancellationToken);
        await StartUpdaterTaskAsync(manifest, cancellationToken);
    }

    private void ValidateManifest(UpdateManifest manifest)
    {
        if (!string.Equals(manifest.Channel, _settings.UpdateChannel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Update manifest channel '{manifest.Channel}' does not match '{_settings.UpdateChannel}'.");
        }

        if (!string.Equals(manifest.Runtime, Runtime, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Update manifest runtime '{manifest.Runtime}' does not match '{Runtime}'.");
        }

        if (!UpdateManifestVerifier.Verify(manifest, UpdateTrust.ManifestPublicKeyPem, out var signatureError))
        {
            throw new InvalidOperationException(signatureError);
        }
    }

    private async Task StartUpdaterTaskAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Run /TN \"{UpdaterTaskName}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("Unable to start schtasks.exe.");

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var output = string.Join(" ", new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value)));
            throw new InvalidOperationException($"Updater scheduled task could not be started. ExitCode={process.ExitCode}. {output}");
        }

        _logger.Info($"Updater scheduled task started for release {manifest.ReleaseId}.");
    }

    private async Task ReportAsync(UpdateManifest manifest, string status, string message, CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint("/workstation-agent/update/report");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new UpdateReportRequest
            {
                ReleaseId = manifest.ReleaseId,
                Version = manifest.Version,
                Status = status,
                Message = message,
                TimestampUtc = DateTimeOffset.UtcNow
            })
        };
        AddAuthHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.Error($"Update report failed ({(int)response.StatusCode}).");
        }
    }

    private void TryWriteState(string status, string? releaseId = null, string? version = null, string? message = null)
    {
        try
        {
            _stateStore.Write(status, releaseId, version, message);
        }
        catch (Exception ex)
        {
            _logger.Error("Update state could not be written.", ex);
        }
    }

    private async Task TryReportAsync(UpdateManifest manifest, string status, string message, CancellationToken cancellationToken)
    {
        try
        {
            await ReportAsync(manifest, status, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error("Update report failed.", ex);
        }
    }

    private string BuildLatestEndpoint()
    {
        var query = string.Join("&", new[]
        {
            $"channel={Uri.EscapeDataString(_settings.UpdateChannel)}",
            $"runtime={Uri.EscapeDataString(Runtime)}",
            $"currentVersion={Uri.EscapeDataString(AgentVersionProvider.CurrentVersion)}"
        });

        return $"{BuildEndpoint("/workstation-agent/update/latest")}?{query}";
    }

    private string BuildEndpoint(string path)
    {
        return $"{_settings.ApiBaseUrl.Trim().TrimEnd('/')}{path}";
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);
    }

    private Task DelayWithJitterAsync(CancellationToken cancellationToken)
    {
        var jitterMinutes = Math.Max(0, _settings.UpdateJitterMinutes);
        if (jitterMinutes == 0)
        {
            return Task.CompletedTask;
        }

        var jitter = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * TimeSpan.FromMinutes(jitterMinutes).TotalMilliseconds);
        return Task.Delay(jitter, cancellationToken);
    }

    private Task DelayIntervalAsync(CancellationToken cancellationToken)
    {
        var intervalMinutes = Math.Max(1, _settings.UpdateCheckIntervalMinutes);
        return Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cancellationToken);
    }
}
