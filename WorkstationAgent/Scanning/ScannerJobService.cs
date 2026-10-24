using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;

namespace WorkstationAgent.Scanning;

internal sealed class ScannerJobService : IAsyncDisposable
{
    private readonly AgentSettings _settings;
    private readonly FileLogger _logger;
    private readonly IDocumentScanner _scanner;
    private readonly string _directory;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Task> _tasks = new();
    private readonly Func<object, Task> _sendStatus;
    private readonly CancellationTokenSource _shutdown = new();
    public bool IsBusy => !_jobs.IsEmpty;

    public ScannerJobService(AgentSettings settings, FileLogger logger, string directory, Func<object, Task> sendStatus,
        IDocumentScanner? scanner = null, HttpClient? http = null)
    {
        _settings = settings;
        _logger = logger;
        _directory = directory;
        Directory.CreateDirectory(directory);
        _scanner = scanner ?? new DocumentScanner(Path.Combine(directory, "temp"));
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _sendStatus = sendStatus;
    }

    public async Task<ScannerCapability[]> Discover(CancellationToken ct)
    {
        try { return await _scanner.Discover(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.Error("Scanner discovery failed.", ex); return []; }
    }

    public void Start(JsonElement command)
    {
        foreach (var previous in _tasks.Where(pair => pair.Value.IsCompleted)) _tasks.TryRemove(previous.Key, out _);
        if (!command.TryGetProperty("jobId", out var id) || !Guid.TryParse(id.GetString(), out var jobId)) return;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        if (!_jobs.TryAdd(jobId, cts)) { cts.Dispose(); return; }
        var source = command.TryGetProperty("source", out var s) ? s.GetString() : "Flatbed";
        var uploadOnly = command.TryGetProperty("uploadOnly", out var resume) && resume.ValueKind == JsonValueKind.True;
        _tasks[jobId] = Task.Run(async () =>
        {
            try { await Process(jobId, source ?? "Flatbed", uploadOnly, cts.Token); }
            finally { _jobs.TryRemove(jobId, out _); cts.Dispose(); }
        });
    }

    public void Cancel(JsonElement command)
    {
        if (command.TryGetProperty("jobId", out var id) && Guid.TryParse(id.GetString(), out var jobId)
            && _jobs.TryGetValue(jobId, out var cts)) cts.Cancel();
    }

    private HttpRequestMessage Request(HttpMethod method, Guid id, string suffix = "")
    {
        var request = new HttpRequestMessage(method, _settings.ApiBaseUrl.TrimEnd('/') + $"/document-receiving/agent/jobs/{id}{suffix}");
        request.Headers.Add("x-device-id", _settings.DeviceId);
        request.Headers.Add("x-api-key", _settings.ApiKey);
        return request;
    }

    private async Task<JsonElement> GetJob(Guid id, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, id);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    private async Task Status(Guid jobId, string status, string? error = null)
    {
        try { await _sendStatus(new { jobId, status, error }); }
        catch (Exception ex) { _logger.Error($"Scanner status delivery failed: {jobId} {status}", ex); }
    }

    private async Task Process(Guid id, string source, bool uploadOnly, CancellationToken ct)
    {
        string state = "RUNNING";
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? heartbeat = null;
        try
        {
            var job = await GetJob(id, ct);
            var initial = job.GetProperty("status").GetString();
            if (initial is not ("PENDING" or "RUNNING" or "UPLOADING")) return;
            var singlePage = job.TryGetProperty("replacesPageId", out var replacement) && replacement.ValueKind == JsonValueKind.Number;
            var path = Path.Combine(_directory, id + (singlePage ? ".png" : ".pdf"));
            var ready = path + ".ready";
            // Explicit recovery may send an existing file only. Missing markers,
            // missing files and partial scans must never fall back to moving paper.
            if (uploadOnly && (!File.Exists(ready) || !File.Exists(path) || new FileInfo(path).Length == 0))
                throw new InvalidOperationException("Готовый скан для повторной отправки не найден. Сохранённые файлы не изменены; повторное сканирование не запускалось.");
            await Status(id, state);
            heartbeat = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(20), heartbeatStop.Token);
                        await Status(id, state);
                    }
                }
                catch (OperationCanceledException) { }
            });
            if (!File.Exists(ready))
            {
                // Recheck at the hardware boundary as files may change after
                // the initial recovery check or while status delivery awaits.
                if (uploadOnly) throw new InvalidOperationException("Метка готового скана исчезла. Повторное сканирование не запускалось.");
                // Persist before moving paper. Replayed commands must never feed another batch.
                using (var marker = new FileStream(path + ".started", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    marker.Flush(true);
                var pages = await _scanner.Scan(job.GetProperty("scannerKey").GetString()!,
                    job.GetProperty("mode").GetString()!, source, singlePage, path,
                    count => _logger.Info($"Scanner job {id}: {count} pages captured."), ct);
                await File.WriteAllTextAsync(ready, pages.ToString(), ct);
                _logger.Info($"Scanner job {id}: batch saved locally ({pages} pages).");
            }
            ScannerLimits.EnsureFileSize(new FileInfo(path).Length);
            state = "UPLOADING";
            await Status(id, state);
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // Check cancellation after scan/before every upload attempt.
                    var current = (await GetJob(id, ct)).GetProperty("status").GetString();
                    if (current == "COMPLETED") break;
                    if (current is not ("PENDING" or "RUNNING" or "UPLOADING")) return;
                    using var request = Request(HttpMethod.Post, id, "/result");
                    using var multipart = new MultipartFormDataContent();
                    var file = new StreamContent(File.OpenRead(path));
                    file.Headers.ContentType = new MediaTypeHeaderValue(singlePage ? "image/png" : "application/pdf");
                    multipart.Add(file, "file", Path.GetFileName(path));
                    request.Content = multipart;
                    using var response = await _http.SendAsync(request, ct);
                    if (response.IsSuccessStatusCode) break;
                    if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                        throw new HttpRequestException($"Upload failed: {(int)response.StatusCode}");
                    throw new InvalidOperationException($"Сервер отклонил пачку ({(int)response.StatusCode}). PDF сохранён на компьютере агента.");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 3 && ex is HttpRequestException or TaskCanceledException)
                {
                    _logger.Error($"Scanner job {id}: retrying upload without rescanning.", ex);
                    await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct);
                }
            }
            _logger.Info($"Scanner job {id}: upload completed.");
            // Keep only the durable receipt; successful document copies live in the CRM.
            File.Delete(path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { await Status(id, "CANCELLED"); }
        catch (Exception ex)
        {
            _logger.Error($"Scanner job {id} failed. Any captured pages remain in {_directory}.", ex);
            await Status(id, "FAILED", $"{ex.Message} Уже полученные страницы сохранены локально в папке scans агента.");
        }
        finally
        {
            heartbeatStop.Cancel();
            if (heartbeat != null) await heartbeat;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        await Task.WhenAll(_tasks.Values);
        _scanner.Dispose();
        _http.Dispose();
        _shutdown.Dispose();
    }

    internal Task WaitForIdleAsync() => Task.WhenAll(_tasks.Values);
}
