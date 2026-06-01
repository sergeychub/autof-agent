using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class PosTerminalCancelRequest
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;
}
