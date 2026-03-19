using System.Net.Http.Json;
using System.Text.Json;
using WorkstationAgent.Configuration;
using WorkstationAgent.Models;

namespace WorkstationAgent.Services;

internal sealed class WorkstationRegistrationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<RegisterWorkstationAgentResponse> RegisterAsync(
        AgentSettings settings,
        string registrationToken,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        var request = new RegisterWorkstationAgentRequest
        {
            RegistrationToken = registrationToken,
            ProposedAgentName = settings.AgentName,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            DeviceFingerprint = Environment.MachineName,
            DeviceId = string.IsNullOrWhiteSpace(settings.DeviceId) ? null : settings.DeviceId
        };

        var endpoint = BuildEndpoint(settings.ApiBaseUrl);
        using var response = await httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Agent registration failed ({(int)response.StatusCode}): {payload}");
        }

        var registration = JsonSerializer.Deserialize<RegisterWorkstationAgentResponse>(payload, JsonOptions);
        if (registration is null)
        {
            throw new InvalidOperationException("Agent registration response was empty.");
        }

        return registration;
    }

    private static string BuildEndpoint(string apiBaseUrl)
    {
        var trimmed = apiBaseUrl.Trim().TrimEnd('/');
        return $"{trimmed}/workstation-agent/register";
    }
}
