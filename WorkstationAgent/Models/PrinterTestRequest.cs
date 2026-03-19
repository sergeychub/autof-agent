using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class PrinterTestRequest
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = string.Empty;
}
