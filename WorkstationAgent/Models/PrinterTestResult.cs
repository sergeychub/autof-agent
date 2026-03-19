using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class PrinterTestResult
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("printerName")]
    public required string PrinterName { get; init; }

    [JsonPropertyName("printedAt")]
    public string? PrintedAt { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
