using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;

namespace WorkstationAgent.Services;

internal sealed record CashDrawerAttempt(Guid Id, DateTimeOffset OccurredAt, bool Success, string? Error);

internal sealed class CashDrawerJournal(string directory, AgentSettings settings, FileLogger logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue(CashDrawerAttempt attempt)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{attempt.Id:N}.json");
        var temporaryFile = file + ".tmp";
        File.WriteAllText(temporaryFile, JsonSerializer.Serialize(attempt, JsonOptions));
        File.Move(temporaryFile, file);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) return;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            do
            {
                try
                {
                    await UploadPendingAsync(client, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException ||
                    (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    logger.Error("Cash drawer journal upload deferred.", ex);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task UploadPendingAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return;

        var endpoint = settings.ApiBaseUrl.TrimEnd('/') + "/workstation-agent/cash-drawer/events";
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CashDrawerAttempt? attempt;
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                attempt = JsonSerializer.Deserialize<CashDrawerAttempt>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.Error($"Could not read cash drawer event {Path.GetFileName(file)}.", ex);
                continue;
            }

            if (attempt is null) continue;
            using var response = await client.PostAsJsonAsync(endpoint, attempt, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                File.Delete(file);
                continue;
            }

            logger.Error($"Cash drawer event {attempt.Id} was not accepted (HTTP {(int)response.StatusCode}).");
            if ((int)response.StatusCode is not (400 or 409)) break;
        }
    }
}
