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

    [JsonPropertyName("receiptPrinter")]
    public ReceiptPrinterSettings ReceiptPrinter { get; set; } = new();

    [JsonPropertyName("labelPrinter")]
    public LabelPrinterSettings LabelPrinter { get; set; } = new();

    [JsonPropertyName("reconnectDelaySeconds")]
    public int ReconnectDelaySeconds { get; set; } = 5;

    [JsonPropertyName("pingIntervalSeconds")]
    public int PingIntervalSeconds { get; set; } = 30;

    [JsonPropertyName("logFilePath")]
    public string? LogFilePath { get; set; }

    [JsonPropertyName("printerEnabled")]
    public bool LegacyPrinterEnabled
    {
        set => ReceiptPrinter.Enabled = value;
    }

    [JsonPropertyName("printerName")]
    public string LegacyPrinterName
    {
        set => ReceiptPrinter.PrinterName = value;
    }

    [JsonPropertyName("transportMode")]
    public string LegacyTransportMode
    {
        set => ReceiptPrinter.TransportMode = value;
    }

    [JsonPropertyName("usbVendorId")]
    public string? LegacyUsbVendorId
    {
        set => ReceiptPrinter.UsbVendorId = value;
    }

    [JsonPropertyName("usbProductId")]
    public string? LegacyUsbProductId
    {
        set => ReceiptPrinter.UsbProductId = value;
    }

    [JsonPropertyName("usbInterfaceNumber")]
    public int? LegacyUsbInterfaceNumber
    {
        set => ReceiptPrinter.UsbInterfaceNumber = value;
    }

    [JsonPropertyName("usbOutEndpoint")]
    public string? LegacyUsbOutEndpoint
    {
        set => ReceiptPrinter.UsbOutEndpoint = value;
    }

    [JsonPropertyName("usbWriteTimeoutMs")]
    public int LegacyUsbWriteTimeoutMs
    {
        set => ReceiptPrinter.UsbWriteTimeoutMs = value;
    }

    [JsonPropertyName("imageCommandMode")]
    public string LegacyImageCommandMode
    {
        set => ReceiptPrinter.ImageCommandMode = value;
    }

    [JsonPropertyName("maxImageWidthDots")]
    public int LegacyMaxImageWidthDots
    {
        set => ReceiptPrinter.MaxImageWidthDots = value;
    }

    [JsonPropertyName("paperWidth")]
    public string LegacyPaperWidth
    {
        set => ReceiptPrinter.PaperWidth = value;
    }

    [JsonPropertyName("characterEncoding")]
    public string LegacyCharacterEncoding
    {
        set => ReceiptPrinter.CharacterEncoding = value;
    }

    [JsonPropertyName("feedLinesAfterPrint")]
    public int LegacyFeedLinesAfterPrint
    {
        set => ReceiptPrinter.FeedLinesAfterPrint = value;
    }

    [JsonPropertyName("tsplLabelWidthMm")]
    public double LegacyTsplLabelWidthMm
    {
        set => LabelPrinter.TsplLabelWidthMm = value;
    }

    [JsonPropertyName("tsplLabelHeightMm")]
    public double LegacyTsplLabelHeightMm
    {
        set => LabelPrinter.TsplLabelHeightMm = value;
    }

    [JsonPropertyName("tsplLabelGapMm")]
    public double LegacyTsplLabelGapMm
    {
        set => LabelPrinter.TsplLabelGapMm = value;
    }

    [JsonPropertyName("tsplDirection")]
    public int LegacyTsplDirection
    {
        set => LabelPrinter.TsplDirection = value;
    }

    [JsonPropertyName("tsplSpeed")]
    public int LegacyTsplSpeed
    {
        set => LabelPrinter.TsplSpeed = value;
    }

    [JsonPropertyName("tsplDensity")]
    public int LegacyTsplDensity
    {
        set => LabelPrinter.TsplDensity = value;
    }

    public static AgentSettings CreateDefault()
    {
        return new AgentSettings();
    }

    public AgentSettings Clone()
    {
        return new AgentSettings
        {
            DeviceId = DeviceId,
            AgentName = AgentName,
            ApiBaseUrl = ApiBaseUrl,
            SocketIoUrl = SocketIoUrl,
            ApiKey = ApiKey,
            StartWithWindows = StartWithWindows,
            ReceiptPrinter = ReceiptPrinter.Clone(),
            LabelPrinter = LabelPrinter.Clone(),
            ReconnectDelaySeconds = ReconnectDelaySeconds,
            PingIntervalSeconds = PingIntervalSeconds,
            LogFilePath = LogFilePath
        };
    }
}
