using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class RegisterWorkstationAgentRequest
{
    [JsonPropertyName("registrationToken")]
    public required string RegistrationToken { get; init; }

    [JsonPropertyName("proposedAgentName")]
    public required string ProposedAgentName { get; init; }

    [JsonPropertyName("machineName")]
    public required string MachineName { get; init; }

    [JsonPropertyName("userName")]
    public required string UserName { get; init; }

    [JsonPropertyName("deviceFingerprint")]
    public string? DeviceFingerprint { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }
}
