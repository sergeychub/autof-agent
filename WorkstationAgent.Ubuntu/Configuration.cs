using System.Text.Json.Serialization;

namespace WorkstationAgent.Ubuntu;

internal sealed class AgentSettings
{
    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = Environment.MachineName;

    [JsonPropertyName("reportedUserName")]
    public string? ReportedUserName { get; set; }

    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; set; } = "http://localhost:3000";

    [JsonPropertyName("socketIoUrl")]
    public string? SocketIoUrl { get; set; }

    [JsonPropertyName("registrationToken")]
    public string? RegistrationToken { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("receiptPrinter")]
    public PrinterEndpointSettings ReceiptPrinter { get; set; } = new();

    [JsonPropertyName("labelPrinter")]
    public PrinterEndpointSettings LabelPrinter { get; set; } = new() { Enabled = false };

    [JsonPropertyName("posTerminal")]
    public PosTerminalSettings PosTerminal { get; set; } = new();

    [JsonPropertyName("reconnectDelaySeconds")]
    public int ReconnectDelaySeconds { get; set; } = 5;

    [JsonPropertyName("heartbeatIntervalSeconds")]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    [JsonPropertyName("updateChannel")]
    public string UpdateChannel { get; set; } = "main";

    [JsonPropertyName("logFilePath")]
    public string? LogFilePath { get; set; }
}

internal static class PrinterRoles
{
    public const string Receipt = "receipt";
    public const string Label = "label";

    public static bool IsLabel(string? value) =>
        string.Equals(value, Label, StringComparison.OrdinalIgnoreCase);
}

internal static class PrinterTransportMode
{
    public const string Cups = "cups";
    public const string Device = "device";
    public const string Tcp = "tcp";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Cups, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Device, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Tcp, StringComparison.OrdinalIgnoreCase);
}

internal sealed class PrinterEndpointSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = PrinterTransportMode.Cups;

    [JsonPropertyName("printerName")]
    public string PrinterName { get; set; } = string.Empty;

    [JsonPropertyName("devicePath")]
    public string? DevicePath { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; } = 9100;

    [JsonPropertyName("connectTimeoutSeconds")]
    public int ConnectTimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("characterEncoding")]
    public string CharacterEncoding { get; set; } = "cp866";

    [JsonPropertyName("feedLinesAfterPrint")]
    public int FeedLinesAfterPrint { get; set; } = 4;

    [JsonPropertyName("maxImageWidthDots")]
    public int MaxImageWidthDots { get; set; } = 384;

    [JsonPropertyName("labelWidthMm")]
    public double LabelWidthMm { get; set; } = 58;

    [JsonPropertyName("labelHeightMm")]
    public double LabelHeightMm { get; set; } = 40;

    [JsonPropertyName("gapMm")]
    public double GapMm { get; set; } = 2;

    [JsonPropertyName("direction")]
    public int Direction { get; set; }

    [JsonPropertyName("speed")]
    public int Speed { get; set; } = 2;

    [JsonPropertyName("density")]
    public int Density { get; set; } = 8;

    [JsonPropertyName("codePage")]
    public string? CodePage { get; set; }
}

internal sealed class PosTerminalSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("host")]
    public string Host { get; set; } = "192.168.0.103";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 2000;

    [JsonPropertyName("merchantId")]
    public string MerchantId { get; set; } = "1";

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 180;
}

internal sealed class AgentIdentity
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("socketIoUrl")]
    public string SocketIoUrl { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(DeviceId) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(SocketIoUrl);
}
