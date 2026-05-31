using System.Text.Json;

namespace WorkstationAgent.Updater;

internal sealed class AgentRuntimeSettings
{
    public string ApiBaseUrl { get; init; } = string.Empty;

    public string? ApiKey { get; init; }

    public static AgentRuntimeSettings Load(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new AgentRuntimeSettings();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var root = document.RootElement;
        return new AgentRuntimeSettings
        {
            ApiBaseUrl = ReadString(root, "apiBaseUrl"),
            ApiKey = ReadString(root, "apiKey")
        };
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
