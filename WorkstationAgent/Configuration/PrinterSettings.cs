using System.Text.Json.Serialization;
using WorkstationAgent.Printing;

namespace WorkstationAgent.Configuration;

internal static class PrinterRoles
{
    public const string Receipt = "receipt";
    public const string Label = "label";

    public static bool IsLabel(string? value)
    {
        return string.Equals(value, Label, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReceipt(string? value)
    {
        return string.Equals(value, Receipt, StringComparison.OrdinalIgnoreCase);
    }
}

internal class PrinterEndpointSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("printerName")]
    public string PrinterName { get; set; } = string.Empty;

    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = PrinterTransportMode.WindowsSpooler;

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

    protected void CopyBaseTo(PrinterEndpointSettings destination)
    {
        destination.Enabled = Enabled;
        destination.PrinterName = PrinterName;
        destination.TransportMode = TransportMode;
        destination.UsbVendorId = UsbVendorId;
        destination.UsbProductId = UsbProductId;
        destination.UsbInterfaceNumber = UsbInterfaceNumber;
        destination.UsbOutEndpoint = UsbOutEndpoint;
        destination.UsbWriteTimeoutMs = UsbWriteTimeoutMs;
    }
}

internal sealed class ReceiptPrinterSettings : PrinterEndpointSettings
{
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

    public ReceiptPrinterSettings Clone()
    {
        var clone = new ReceiptPrinterSettings
        {
            ImageCommandMode = ImageCommandMode,
            MaxImageWidthDots = MaxImageWidthDots,
            PaperWidth = PaperWidth,
            CharacterEncoding = CharacterEncoding,
            FeedLinesAfterPrint = FeedLinesAfterPrint
        };

        CopyBaseTo(clone);
        return clone;
    }
}

internal sealed class LabelPrinterSettings : PrinterEndpointSettings
{
    public LabelPrinterSettings()
    {
        Enabled = false;
    }

    [JsonPropertyName("characterEncoding")]
    public string CharacterEncoding { get; set; } = "ascii";

    [JsonPropertyName("codePage")]
    public string? CodePage { get; set; }

    [JsonPropertyName("labelWidthMm")]
    public double TsplLabelWidthMm { get; set; } = 30.0;

    [JsonPropertyName("labelHeightMm")]
    public double TsplLabelHeightMm { get; set; } = 20.0;

    [JsonPropertyName("gapMm")]
    public double TsplLabelGapMm { get; set; } = 2.0;

    [JsonPropertyName("direction")]
    public int TsplDirection { get; set; } = 0;

    [JsonPropertyName("speed")]
    public int TsplSpeed { get; set; } = 2;

    [JsonPropertyName("density")]
    public int TsplDensity { get; set; } = 8;

    public LabelPrinterSettings Clone()
    {
        var clone = new LabelPrinterSettings
        {
            Enabled = Enabled,
            CharacterEncoding = CharacterEncoding,
            CodePage = CodePage,
            TsplLabelWidthMm = TsplLabelWidthMm,
            TsplLabelHeightMm = TsplLabelHeightMm,
            TsplLabelGapMm = TsplLabelGapMm,
            TsplDirection = TsplDirection,
            TsplSpeed = TsplSpeed,
            TsplDensity = TsplDensity
        };

        CopyBaseTo(clone);
        clone.Enabled = Enabled;
        return clone;
    }
}
