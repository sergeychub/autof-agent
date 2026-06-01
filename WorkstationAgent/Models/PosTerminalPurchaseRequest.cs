using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class PosTerminalPurchaseRequest
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "UAH";
}
