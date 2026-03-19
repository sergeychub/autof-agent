using System.Text.Json.Serialization;

namespace WorkstationAgent.Configuration;

internal sealed class AgentSettings
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = Environment.MachineName;

    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; set; } = "http://localhost:3000";

    [JsonPropertyName("socketIoUrl")]
    public string SocketIoUrl { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = true;

    [JsonPropertyName("printerEnabled")]
    public bool PrinterEnabled { get; set; } = true;

    [JsonPropertyName("printerName")]
    public string PrinterName { get; set; } = string.Empty;

    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = "windows-spooler";

    [JsonPropertyName("usbVendorId")]
    public string? UsbVendorId { get; set; }

    [JsonPropertyName("usbProductId")]
    public string? UsbProductId { get; set; }

    [JsonPropertyName("usbInterfaceNumber")]
    public int? UsbInterfaceNumber { get; set; }

    [JsonPropertyName("usbOutEndpoint")]
    public string? UsbOutEndpoint { get; set; }

    [JsonPropertyName("usbWriteTimeoutMs")]
    public int UsbWriteTimeoutMs { get; set; } = 3000;

    [JsonPropertyName("imageCommandMode")]
    public string ImageCommandMode { get; set; } = "gs-v-0";

    [JsonPropertyName("maxImageWidthDots")]
    public int MaxImageWidthDots { get; set; } = 384;

    [JsonPropertyName("paperWidth")]
    public string PaperWidth { get; set; } = "58mm";

    [JsonPropertyName("characterEncoding")]
    public string CharacterEncoding { get; set; } = "cp866";

    [JsonPropertyName("feedLinesAfterPrint")]
    public int FeedLinesAfterPrint { get; set; } = 4;

    [JsonPropertyName("reconnectDelaySeconds")]
    public int ReconnectDelaySeconds { get; set; } = 5;

    [JsonPropertyName("pingIntervalSeconds")]
    public int PingIntervalSeconds { get; set; } = 30;

    [JsonPropertyName("logFilePath")]
    public string? LogFilePath { get; set; }

    public static AgentSettings CreateDefault()
    {
        return new AgentSettings();
    }
}
