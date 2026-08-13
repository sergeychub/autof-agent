using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkstationAgent.Update;

namespace WorkstationAgent.Ubuntu;

internal enum UbuntuUpdateOutcome
{
    Disabled,
    Current,
    Updated
}

internal sealed class UbuntuUpdateService
{
    internal const string Runtime = "linux-x64";
    private const string BinaryName = "WorkstationAgent.Ubuntu";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex SafeReleaseIdPattern = new(
        "^[a-zA-Z0-9._-]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly AgentSettings _settings;
    private readonly AgentIdentity _identity;
    private readonly AgentLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly UpdateStateStore _stateStore;
    private readonly string _updatesDirectory;
    private readonly string _targetBinaryPath;
    private readonly string _currentVersion;
    private readonly string _manifestPublicKeyPem;
    private readonly Func<CancellationToken, Task> _restartAgent;

    public UbuntuUpdateService(
        AgentSettings settings,
        AgentIdentity identity,
        AgentLogger logger,
        HttpClient httpClient,
        UpdateStateStore stateStore,
        string updatesDirectory,
        string targetBinaryPath,
        string currentVersion,
        string manifestPublicKeyPem,
        Func<CancellationToken, Task> restartAgent)
    {
        _settings = settings;
        _identity = identity;
        _logger = logger;
        _httpClient = httpClient;
        _stateStore = stateStore;
        _updatesDirectory = Path.GetFullPath(updatesDirectory);
        _targetBinaryPath = Path.GetFullPath(targetBinaryPath);
        _currentVersion = currentVersion;
        _manifestPublicKeyPem = manifestPublicKeyPem;
        _restartAgent = restartAgent;
    }

    public async Task<UbuntuUpdateOutcome> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_settings.AutoUpdateEnabled)
        {
            _logger.Info("Auto-update is disabled.");
            _stateStore.Write(UpdateStatuses.Skipped, message: "Auto-update is disabled.");
            return UbuntuUpdateOutcome.Disabled;
        }
        if (!_identity.IsComplete)
        {
            throw new InvalidOperationException("Auto-update requires a registered agent identity.");
        }
        if (!UpdateTrust.IsConfigured(_manifestPublicKeyPem))
        {
            throw new InvalidOperationException("Auto-update manifest public key is not configured.");
        }

        UpdateManifest? manifest = null;
        try
        {
            manifest = await GetLatestManifestAsync(cancellationToken);
            if (manifest is null)
            {
                _logger.Info($"No {Runtime} update is available for {_currentVersion}.");
                return UbuntuUpdateOutcome.Current;
            }

            ValidateManifest(manifest);
            if (!UpdateVersionComparer.IsUpdateAvailable(manifest.Version, _currentVersion))
            {
                _logger.Info($"Release {manifest.Version} is not newer than {_currentVersion}.");
                return UbuntuUpdateOutcome.Current;
            }

            _stateStore.Write(
                UpdateStatuses.Available,
                manifest.ReleaseId,
                manifest.Version,
                "Update manifest accepted.");
            await TryReportAsync(manifest, UpdateStatuses.Available, "Update manifest accepted.", cancellationToken);

            var releaseDirectory = Path.Combine(_updatesDirectory, manifest.ReleaseId);
            var archivePath = Path.Combine(releaseDirectory, "artifact.zip");
            Directory.CreateDirectory(releaseDirectory);
            _stateStore.Write(
                UpdateStatuses.Downloading,
                manifest.ReleaseId,
                manifest.Version,
                "Downloading update.");
            await TryReportAsync(manifest, UpdateStatuses.Downloading, "Downloading update.", cancellationToken);
            await DownloadArchiveAsync(manifest, archivePath, cancellationToken);
            VerifyArchive(manifest, archivePath);

            var stagingDirectory = Path.Combine(releaseDirectory, "staging");
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
            Directory.CreateDirectory(stagingDirectory);
            ZipFile.ExtractToDirectory(archivePath, stagingDirectory, overwriteFiles: true);
            var payloadBinaryPath = FindPayloadBinary(stagingDirectory);

            _stateStore.Write(
                UpdateStatuses.Installing,
                manifest.ReleaseId,
                manifest.Version,
                "Installing update.");
            await TryReportAsync(manifest, UpdateStatuses.Installing, "Installing update.", cancellationToken);
            await InstallAndRestartAsync(payloadBinaryPath, cancellationToken);

            _stateStore.Write(
                UpdateStatuses.Success,
                manifest.ReleaseId,
                manifest.Version,
                "Update installed and agent restarted.");
            await TryReportAsync(
                manifest,
                UpdateStatuses.Success,
                "Update installed and agent restarted.",
                cancellationToken);
            _logger.Info($"Update installed. Release={manifest.ReleaseId}, Version={manifest.Version}");
            return UbuntuUpdateOutcome.Updated;
        }
        catch (Exception ex)
        {
            _stateStore.Write(UpdateStatuses.Failure, manifest?.ReleaseId, manifest?.Version, ex.Message);
            await TryReportAsync(manifest, UpdateStatuses.Failure, ex.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task<UpdateManifest?> GetLatestManifestAsync(CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(
            $"/workstation-agent/update/latest?channel={Uri.EscapeDataString(_settings.UpdateChannel)}" +
            $"&runtime={Uri.EscapeDataString(Runtime)}" +
            $"&currentVersion={Uri.EscapeDataString(_currentVersion)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddAuthHeaders(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Update latest endpoint failed ({(int)response.StatusCode}): {error}");
        }

        return await response.Content.ReadFromJsonAsync<UpdateManifest>(
            ManifestJsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Update latest endpoint returned an empty manifest.");
    }

    private void ValidateManifest(UpdateManifest manifest)
    {
        if (!SafeReleaseIdPattern.IsMatch(manifest.ReleaseId ?? string.Empty))
        {
            throw new InvalidOperationException("Update releaseId contains unsupported characters.");
        }
        if (!string.Equals(manifest.Channel, _settings.UpdateChannel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Update manifest channel '{manifest.Channel}' does not match '{_settings.UpdateChannel}'.");
        }
        if (!string.Equals(manifest.Runtime, Runtime, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Update manifest runtime '{manifest.Runtime}' does not match '{Runtime}'.");
        }
        if (!UpdateManifestVerifier.Verify(manifest, _manifestPublicKeyPem, out var signatureError))
        {
            throw new InvalidOperationException(signatureError);
        }
    }

    private async Task DownloadArchiveAsync(
        UpdateManifest manifest,
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildDownloadEndpoint(manifest.DownloadUrl));
        AddAuthHeaders(request);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(archivePath);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static void VerifyArchive(UpdateManifest manifest, string archivePath)
    {
        var length = new FileInfo(archivePath).Length;
        if (manifest.SizeBytes <= 0 || length != manifest.SizeBytes)
        {
            throw new InvalidOperationException(
                $"Downloaded archive size mismatch. Expected={manifest.SizeBytes}, Actual={length}");
        }

        using var stream = File.OpenRead(archivePath);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Downloaded archive SHA-256 mismatch. Expected={manifest.Sha256}, Actual={actualSha256}");
        }
    }

    private static string FindPayloadBinary(string stagingDirectory)
    {
        var candidates = Directory.GetFiles(stagingDirectory, BinaryName, SearchOption.AllDirectories);
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException($"Update archive does not contain {BinaryName}."),
            _ => throw new InvalidOperationException($"Update archive contains multiple {BinaryName} files.")
        };
    }

    private async Task InstallAndRestartAsync(string payloadBinaryPath, CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetDirectoryName(_targetBinaryPath)
            ?? throw new InvalidOperationException("Target binary directory could not be resolved.");
        Directory.CreateDirectory(targetDirectory);
        var replacementPath = _targetBinaryPath + ".new";
        var backupPath = _targetBinaryPath + ".previous";

        File.Copy(payloadBinaryPath, replacementPath, overwrite: true);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                replacementPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        if (File.Exists(_targetBinaryPath))
        {
            File.Copy(_targetBinaryPath, backupPath, overwrite: true);
        }
        File.Move(replacementPath, _targetBinaryPath, overwrite: true);

        try
        {
            await _restartAgent(cancellationToken);
        }
        catch
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, _targetBinaryPath, overwrite: true);
            }
            throw;
        }
    }

    private async Task TryReportAsync(
        UpdateManifest? manifest,
        string status,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildEndpoint("/workstation-agent/update/report"))
            {
                Content = JsonContent.Create(new UpdateReportRequest
                {
                    ReleaseId = manifest?.ReleaseId,
                    Version = manifest?.Version,
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
        catch (Exception ex)
        {
            _logger.Error("Update report failed.", ex);
        }
    }

    private Uri BuildDownloadEndpoint(string downloadUrl)
    {
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        return BuildEndpoint(downloadUrl);
    }

    private Uri BuildEndpoint(string path)
    {
        var baseUri = new Uri(_settings.ApiBaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _identity.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Api-Key", _identity.ApiKey);
    }
}
