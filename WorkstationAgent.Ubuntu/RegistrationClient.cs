using System.Net.Http.Json;
using System.Text.Json;

namespace WorkstationAgent.Ubuntu;

internal sealed class RegistrationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public RegistrationClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AgentIdentity> RegisterAsync(
        AgentSettings settings,
        AgentIdentity currentIdentity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.RegistrationToken))
        {
            throw new InvalidOperationException(
                "registrationToken is required until the agent has registered and written its state file.");
        }

        var request = new RegisterAgentRequest
        {
            RegistrationToken = settings.RegistrationToken.Trim(),
            ProposedAgentName = settings.AgentName.Trim(),
            MachineName = Environment.MachineName,
            UserName = AgentRuntime.UserName(settings),
            DeviceFingerprint = AgentRuntime.DeviceFingerprint(),
            DeviceId = string.IsNullOrWhiteSpace(currentIdentity.DeviceId) ? null : currentIdentity.DeviceId
        };
        var endpoint = settings.ApiBaseUrl.Trim().TrimEnd('/') + "/workstation-agent/register";
        using var response = await _httpClient.PostAsJsonAsync(endpoint, request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (responseBody.Length > 1000)
            {
                responseBody = responseBody[..1000];
            }
            throw new HttpRequestException(
                $"Agent registration failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {responseBody}");
        }

        var registration = await response.Content.ReadFromJsonAsync<RegisterAgentResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Agent registration returned an empty response.");
        return new AgentIdentity
        {
            DeviceId = registration.DeviceId,
            AgentName = registration.AgentName,
            ApiKey = registration.ApiKey,
            SocketIoUrl = string.IsNullOrWhiteSpace(settings.SocketIoUrl)
                ? registration.SocketIoUrl
                : settings.SocketIoUrl.Trim()
        };
    }
}
