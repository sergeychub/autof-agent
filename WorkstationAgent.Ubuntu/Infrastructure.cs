using System.Reflection;
using System.Text.Json;

namespace WorkstationAgent.Ubuntu;

internal sealed class AgentLogger
{
    private readonly object _sync = new();
    private readonly string? _filePath;

    public AgentLogger(string? filePath)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message} {exception}");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {message}";
        lock (_sync)
        {
            Console.WriteLine(line);
            if (_filePath is null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.AppendAllText(_filePath, line + Environment.NewLine);
        }
    }
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public AgentSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Agent configuration was not found at '{path}'. Copy agentsettings.example.json and edit it before starting the service.",
                path);
        }

        var settings = JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(path), ReadOptions)
            ?? throw new InvalidOperationException($"Agent configuration '{path}' is empty.");
        settings.ReceiptPrinter ??= new PrinterEndpointSettings();
        settings.LabelPrinter ??= new PrinterEndpointSettings { Enabled = false };
        settings.PosTerminal ??= new PosTerminalSettings();
        Validate(settings);
        return settings;
    }

    private static void Validate(AgentSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentName))
        {
            throw new InvalidOperationException("agentName is required.");
        }
        if (!Uri.TryCreate(settings.ApiBaseUrl, UriKind.Absolute, out var apiUri) ||
            (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("apiBaseUrl must be an absolute HTTP or HTTPS URL.");
        }
        if (settings.ReconnectDelaySeconds < 1 || settings.HeartbeatIntervalSeconds < 1)
        {
            throw new InvalidOperationException("Reconnect and heartbeat intervals must be greater than zero.");
        }

        ValidatePrinter(settings.ReceiptPrinter, "receiptPrinter");
        ValidatePrinter(settings.LabelPrinter, "labelPrinter");

        if (settings.PosTerminal.Enabled &&
            (string.IsNullOrWhiteSpace(settings.PosTerminal.Host) || settings.PosTerminal.Port is < 1 or > 65535))
        {
            throw new InvalidOperationException("Enabled posTerminal requires a host and a valid port.");
        }
    }

    private static void ValidatePrinter(PrinterEndpointSettings printer, string name)
    {
        if (!printer.Enabled)
        {
            return;
        }
        if (!PrinterTransportMode.IsSupported(printer.TransportMode))
        {
            throw new InvalidOperationException($"{name}.transportMode must be cups, device, or tcp.");
        }
        if (string.Equals(printer.TransportMode, PrinterTransportMode.Cups, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(printer.PrinterName))
        {
            throw new InvalidOperationException($"{name}.printerName is required for the CUPS transport.");
        }
        if (string.Equals(printer.TransportMode, PrinterTransportMode.Device, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(printer.DevicePath) || !Path.IsPathFullyQualified(printer.DevicePath)))
        {
            throw new InvalidOperationException($"{name}.devicePath must be an absolute path for the device transport.");
        }
        if (string.Equals(printer.TransportMode, PrinterTransportMode.Tcp, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(printer.Host) || printer.Port is < 1 or > 65535))
        {
            throw new InvalidOperationException($"{name} requires a host and a valid port for the TCP transport.");
        }
    }
}

internal sealed class IdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public IdentityStore(string path)
    {
        _path = path;
    }

    public bool Exists => File.Exists(_path);

    public AgentIdentity Load(AgentSettings settings)
    {
        if (Exists)
        {
            var stored = JsonSerializer.Deserialize<AgentIdentity>(File.ReadAllText(_path), JsonOptions);
            if (stored?.IsComplete == true)
            {
                ApplySocketOverride(stored, settings);
                return stored;
            }
        }

        var imported = new AgentIdentity
        {
            DeviceId = settings.DeviceId?.Trim() ?? string.Empty,
            AgentName = settings.AgentName.Trim(),
            ApiKey = settings.ApiKey?.Trim() ?? string.Empty,
            SocketIoUrl = settings.SocketIoUrl?.Trim() ?? string.Empty
        };
        return imported;
    }

    public void Save(AgentIdentity identity)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(identity, JsonOptions));
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temporaryPath, _path, true);
    }

    private static void ApplySocketOverride(AgentIdentity identity, AgentSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SocketIoUrl))
        {
            identity.SocketIoUrl = settings.SocketIoUrl.Trim();
        }
    }
}

internal static class AgentRuntime
{
    public static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? "unknown";

    public static string UserName(AgentSettings settings) =>
        string.IsNullOrWhiteSpace(settings.ReportedUserName) ? Environment.UserName : settings.ReportedUserName.Trim();

    public static string DeviceFingerprint()
    {
        const string machineIdPath = "/etc/machine-id";
        if (File.Exists(machineIdPath))
        {
            var machineId = File.ReadAllText(machineIdPath).Trim();
            if (!string.IsNullOrWhiteSpace(machineId))
            {
                return $"linux-machine-id:{machineId}";
            }
        }
        return $"linux-host:{Environment.MachineName}";
    }
}
