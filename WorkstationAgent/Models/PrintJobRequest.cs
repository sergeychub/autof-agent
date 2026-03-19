using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class PrintJobRequest
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("base64Payload")]
    public string? Base64Payload { get; init; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; init; }

    [JsonPropertyName("documentName")]
    public string? DocumentName { get; init; }

    [JsonPropertyName("feedLinesAfterPrint")]
    public int? FeedLinesAfterPrint { get; init; }

    [JsonPropertyName("document")]
    public PrintDocument? Document { get; init; }
}
