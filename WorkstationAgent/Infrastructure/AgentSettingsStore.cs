using System.Text.Json;
using WorkstationAgent.Configuration;

namespace WorkstationAgent.Infrastructure;

internal sealed class AgentSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AgentPaths _paths;

    public AgentSettingsStore(AgentPaths paths)
    {
        _paths = paths;
    }

    public string SettingsPath => _paths.ConfigPath;

    public bool Exists()
    {
        return File.Exists(_paths.ConfigPath);
    }

    public AgentSettings Load()
    {
        if (!Exists())
        {
            return AgentSettings.CreateDefault();
        }

        var json = File.ReadAllText(_paths.ConfigPath);
        var settings = JsonSerializer.Deserialize<AgentSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        settings ??= AgentSettings.CreateDefault();
        settings.ReceiptPrinter ??= new ReceiptPrinterSettings();
        settings.LabelPrinter ??= new LabelPrinterSettings();
        settings.PosTerminal ??= new PosTerminalSettings();

        if (string.IsNullOrWhiteSpace(settings.PosTerminal.Host))
        {
            settings.PosTerminal.Host = "192.168.0.103";
        }

        if (settings.PosTerminal.Port <= 0)
        {
            settings.PosTerminal.Port = 2000;
        }

        if (string.IsNullOrWhiteSpace(settings.PosTerminal.MerchantId))
        {
            settings.PosTerminal.MerchantId = "1";
        }

        if (settings.PosTerminal.TimeoutSeconds <= 0)
        {
            settings.PosTerminal.TimeoutSeconds = 180;
        }

        return settings;
    }

    public void Save(AgentSettings settings)
    {
        _paths.EnsureDirectories();
        settings.LogFilePath ??= _paths.LogFilePath;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_paths.ConfigPath, json);
    }

    public bool RequiresSetup(AgentSettings settings)
    {
        return !Exists()
            || string.IsNullOrWhiteSpace(settings.DeviceId)
            || string.IsNullOrWhiteSpace(settings.AgentName)
            || string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            || string.IsNullOrWhiteSpace(settings.SocketIoUrl)
            || string.IsNullOrWhiteSpace(settings.ApiKey);
    }
}
