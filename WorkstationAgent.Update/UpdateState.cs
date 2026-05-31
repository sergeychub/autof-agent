using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkstationAgent.Update;

public sealed class UpdateState
{
    [JsonPropertyName("releaseId")]
    public string? ReleaseId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = UpdateStatuses.Unknown;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UpdateStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _statePath;

    public UpdateStateStore(string statePath)
    {
        _statePath = statePath;
    }

    public UpdateState Read()
    {
        if (!File.Exists(_statePath))
        {
            return new UpdateState();
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<UpdateState>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UpdateState();
        }
        catch
        {
            return new UpdateState
            {
                Status = UpdateStatuses.Unknown,
                Message = "Update state could not be read."
            };
        }
    }

    public string ReadStatus()
    {
        return Read().Status;
    }

    public void Write(string status, string? releaseId = null, string? version = null, string? message = null)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var state = new UpdateState
        {
            Status = status,
            ReleaseId = releaseId,
            Version = version,
            Message = message,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, JsonOptions));
    }
}
