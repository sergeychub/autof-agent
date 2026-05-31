using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using WorkstationAgent.Update;

namespace WorkstationAgent.Updater;

internal static class Program
{
    private const string AgentProcessName = "WorkstationAgent";
    private const string AgentExecutableName = "WorkstationAgent.exe";
    private const string MutexName = "Local\\AvtoforwardAgentUpdater";

    private static async Task<int> Main(string[] args)
    {
        var paths = UpdaterPaths.Create(args);
        ValidateInstallDirectory(paths);
        Directory.CreateDirectory(paths.BaseDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
        TryGrantProgramDataAccess(paths);
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            Log(paths, "Another updater instance is already running.");
            return 0;
        }

        try
        {
            Directory.CreateDirectory(paths.UpdatesDirectory);
            var settings = AgentRuntimeSettings.Load(paths.SettingsPath);
            var manifest = await LoadManifestAsync(paths.PendingManifestPath);

            if (!UpdateManifestVerifier.Verify(manifest, UpdateTrust.ManifestPublicKeyPem, out var signatureError))
            {
                throw new InvalidOperationException(signatureError);
            }

            var stateStore = new UpdateStateStore(paths.UpdateStatePath);
            stateStore.Write(UpdateStatuses.Downloading, manifest.ReleaseId, manifest.Version, "Downloading update.");
            await ReportAsync(paths, settings, manifest, UpdateStatuses.Downloading, "Downloading update.");

            var archivePath = await DownloadArchiveAsync(paths, settings, manifest);
            VerifyArchive(paths, manifest, archivePath);

            stateStore.Write(UpdateStatuses.Installing, manifest.ReleaseId, manifest.Version, "Installing update.");
            await ReportAsync(paths, settings, manifest, UpdateStatuses.Installing, "Installing update.");

            var stagingPath = ExtractArchive(paths, manifest, archivePath);
            InstallRelease(paths, stagingPath);
            TryStartAgent(paths);

            stateStore.Write(UpdateStatuses.Success, manifest.ReleaseId, manifest.Version, "Update installed.");
            await ReportAsync(paths, settings, manifest, UpdateStatuses.Success, "Update installed.");
            TryDelete(paths.PendingManifestPath);
            Log(paths, $"Update installed. Release={manifest.ReleaseId}, Version={manifest.Version}");
            return 0;
        }
        catch (Exception ex)
        {
            Log(paths, ex.ToString());
            new UpdateStateStore(paths.UpdateStatePath).Write(UpdateStatuses.Failure, message: ex.Message);
            await TryReportFailureAsync(paths, ex.Message);
            return 1;
        }
    }

    private static async Task<UpdateManifest> LoadManifestAsync(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Pending update manifest was not found.", manifestPath);
        }

        var json = await File.ReadAllTextAsync(manifestPath);
        return JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Pending update manifest is empty.");
    }

    private static void ValidateInstallDirectory(UpdaterPaths paths)
    {
        var installDirectory = Path.GetFullPath(paths.InstallDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var expectedSuffix = Path.Combine("Avtoforward", "Agent");
        if (!installDirectory.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Install directory is not supported: {paths.InstallDirectory}");
        }
    }

    private static async Task<string> DownloadArchiveAsync(
        UpdaterPaths paths,
        AgentRuntimeSettings settings,
        UpdateManifest manifest)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        var archivePath = Path.Combine(paths.DownloadsDirectory, $"{SafeFileName(manifest.ReleaseId)}.zip");
        Directory.CreateDirectory(paths.DownloadsDirectory);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildDownloadUri(settings, manifest));
        AddAuthHeaders(request, settings);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(archivePath);
        await source.CopyToAsync(target);
        return archivePath;
    }

    private static void VerifyArchive(UpdaterPaths paths, UpdateManifest manifest, string archivePath)
    {
        var fileInfo = new FileInfo(archivePath);
        if (manifest.SizeBytes > 0 && fileInfo.Length != manifest.SizeBytes)
        {
            throw new InvalidOperationException(
                $"Downloaded archive size mismatch. Expected={manifest.SizeBytes}, Actual={fileInfo.Length}");
        }

        using var stream = File.OpenRead(archivePath);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Downloaded archive SHA-256 mismatch. Expected={manifest.Sha256}, Actual={actualSha256}");
        }

        Log(paths, $"Archive verified. Path={archivePath}");
    }

    private static string ExtractArchive(UpdaterPaths paths, UpdateManifest manifest, string archivePath)
    {
        var stagingPath = Path.Combine(paths.StagingDirectory, SafeFileName(manifest.ReleaseId));
        if (Directory.Exists(stagingPath))
        {
            Directory.Delete(stagingPath, recursive: true);
        }

        Directory.CreateDirectory(stagingPath);
        ZipFile.ExtractToDirectory(archivePath, stagingPath);
        var payloadRoot = ResolvePayloadRoot(stagingPath);
        Log(paths, $"Archive extracted. Payload={payloadRoot}");
        return payloadRoot;
    }

    private static string ResolvePayloadRoot(string stagingPath)
    {
        if (File.Exists(Path.Combine(stagingPath, AgentExecutableName)))
        {
            return stagingPath;
        }

        var candidates = Directory.GetDirectories(stagingPath)
            .Where(directory => File.Exists(Path.Combine(directory, AgentExecutableName)))
            .ToArray();

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        throw new InvalidOperationException("Update archive does not contain WorkstationAgent.exe at a supported root.");
    }

    private static void InstallRelease(UpdaterPaths paths, string payloadRoot)
    {
        var backupPath = Path.Combine(
            paths.BackupsDirectory,
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{SafeFileName(Path.GetFileName(payloadRoot))}");

        Directory.CreateDirectory(paths.BackupsDirectory);
        StopAgent(paths);

        var installParent = Path.GetDirectoryName(paths.InstallDirectory)
            ?? throw new InvalidOperationException("Install directory has no parent.");
        Directory.CreateDirectory(installParent);

        var movedCurrentInstall = false;
        if (Directory.Exists(paths.InstallDirectory))
        {
            Directory.Move(paths.InstallDirectory, backupPath);
            movedCurrentInstall = true;
        }

        try
        {
            Directory.Move(payloadRoot, paths.InstallDirectory);
        }
        catch
        {
            if (Directory.Exists(paths.InstallDirectory))
            {
                Directory.Delete(paths.InstallDirectory, recursive: true);
            }

            if (movedCurrentInstall && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, paths.InstallDirectory);
            }

            throw;
        }
    }

    private static void StopAgent(UpdaterPaths paths)
    {
        var expectedExePath = Path.GetFullPath(Path.Combine(paths.InstallDirectory, AgentExecutableName));
        foreach (var process in Process.GetProcessesByName(AgentProcessName))
        {
            try
            {
                var processPath = process.MainModule?.FileName;
                if (!string.Equals(Path.GetFullPath(processPath ?? string.Empty), expectedExePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.CloseMainWindow();
                if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(TimeSpan.FromSeconds(10));
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void TryStartAgent(UpdaterPaths paths)
    {
        var exePath = Path.Combine(paths.InstallDirectory, AgentExecutableName);
        if (!File.Exists(exePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = paths.InstallDirectory,
            UseShellExecute = true
        });
    }

    private static async Task ReportAsync(
        UpdaterPaths paths,
        AgentRuntimeSettings settings,
        UpdateManifest manifest,
        string status,
        string message)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return;
        }

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{settings.ApiBaseUrl.Trim().TrimEnd('/')}/workstation-agent/update/report")
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

            AddAuthHeaders(request, settings);
            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log(paths, $"Update report failed. Status={(int)response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Log(paths, $"Update report failed: {ex.Message}");
        }
    }

    private static async Task TryReportFailureAsync(UpdaterPaths paths, string message)
    {
        try
        {
            var settings = AgentRuntimeSettings.Load(paths.SettingsPath);
            var manifest = File.Exists(paths.PendingManifestPath)
                ? await LoadManifestAsync(paths.PendingManifestPath)
                : new UpdateManifest();

            await ReportAsync(paths, settings, manifest, UpdateStatuses.Failure, message);
        }
        catch
        {
        }
    }

    private static Uri BuildDownloadUri(AgentRuntimeSettings settings, UpdateManifest manifest)
    {
        if (Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
        {
            throw new InvalidOperationException("Manifest downloadUrl is relative, but apiBaseUrl is empty.");
        }

        return new Uri(new Uri($"{settings.ApiBaseUrl.Trim().TrimEnd('/')}/"), manifest.DownloadUrl.TrimStart('/'));
    }

    private static void AddAuthHeaders(HttpRequestMessage request, AgentRuntimeSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '-' : character));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryGrantProgramDataAccess(UpdaterPaths paths)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var identity = WindowsIdentity.GetCurrent().Name;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "icacls.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            process.StartInfo.ArgumentList.Add(paths.BaseDirectory);
            process.StartInfo.ArgumentList.Add("/grant");
            process.StartInfo.ArgumentList.Add($"{identity}:(OI)(CI)M");
            process.StartInfo.ArgumentList.Add("/T");
            process.StartInfo.ArgumentList.Add("/C");
            process.Start();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
            }

            Log(paths, $"ProgramData ACL refreshed for {identity}. ExitCode={process.ExitCode}");
        }
        catch (Exception ex)
        {
            Log(paths, $"ProgramData ACL refresh failed: {ex.Message}");
        }
    }

    private static void Log(UpdaterPaths paths, string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}";
        File.AppendAllText(paths.LogFilePath, line);
    }
}
