using System.Text.Json.Serialization;

namespace WorkstationAgent.Update;

public sealed class UpdateReportRequest
{
    [JsonPropertyName("releaseId")]
    public string? ReleaseId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = UpdateStatuses.Unknown;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
