using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class RegisterWorkstationAgentResponse
{
    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("agentName")]
    public required string AgentName { get; init; }

    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; init; }

    [JsonPropertyName("socketIoUrl")]
    public required string SocketIoUrl { get; init; }
}
