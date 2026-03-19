using System.Text;
using WorkstationAgent.Configuration;
using WorkstationAgent.Models;

namespace WorkstationAgent.Printing;

internal sealed class EscPosDocumentBuilder
{
    private const byte Xp58Windows1251CharacterTable = 23;
    private readonly EscPosImageRenderer _imageRenderer;

    public EscPosDocumentBuilder(EscPosImageRenderer imageRenderer)
    {
        _imageRenderer = imageRenderer;
    }

    public byte[] Build(AgentSettings settings, PrintDocument document)
    {
        var encoding = Encoding.GetEncoding("windows-1251");
        using var stream = new MemoryStream();
        Write(stream, 0x1B, 0x40);
        Write(stream, 0x1B, 0x74, Xp58Windows1251CharacterTable);

        foreach (var block in document.Blocks)
        {
            switch (block.Type?.Trim().ToLowerInvariant())
            {
                case "text":
                    WriteAlign(stream, block.Align);
                    WriteEmphasis(stream, block.Emphasis == true);
                    WriteText(stream, encoding, (block.Text ?? string.Empty) + Environment.NewLine);
                    WriteEmphasis(stream, false);
                    WriteAlign(stream, "left");
                    break;

                case "image-base64":
                    if (!string.IsNullOrWhiteSpace(block.Base64Image))
                    {
                        _imageRenderer.WriteImageFromBase64(stream, ApplyBlockSettings(settings, block), block.Base64Image);
                        Write(stream, 0x1B, 0x74, Xp58Windows1251CharacterTable);
                    }
                    break;

                case "qr":
                    if (!string.IsNullOrWhiteSpace(block.Content))
                    {
                        WriteAlign(stream, block.Align ?? "center");
                        WriteQr(stream, block.Content, block.ModuleSize ?? 5, block.ErrorCorrection ?? 49);
                        WriteAlign(stream, "left");
                        Write(stream, 0x1B, 0x74, Xp58Windows1251CharacterTable);
                    }
                    break;

                case "barcode":
                    if (!string.IsNullOrWhiteSpace(block.Content))
                    {
                        WriteAlign(stream, block.Align ?? "center");
                        WriteBarcode(stream, block.Content, block.BarcodeType, block.Width ?? 2);
                        WriteAlign(stream, "left");
                        Write(stream, 0x1B, 0x74, Xp58Windows1251CharacterTable);
                    }
                    break;

                case "feed":
                    for (var i = 0; i < Math.Max(0, block.Lines ?? 1); i++)
                    {
                        WriteText(stream, encoding, Environment.NewLine);
                    }
                    break;

                case "rule":
                    var width = Math.Max(1, block.Width ?? 30);
                    var character = string.IsNullOrWhiteSpace(block.Character) ? "-" : block.Character![..1];
                    WriteText(stream, encoding, new string(character[0], width) + Environment.NewLine);
                    break;
            }
        }

        return stream.ToArray();
    }

    private static AgentSettings ApplyBlockSettings(AgentSettings settings, PrintDocumentBlock block)
    {
        return new AgentSettings
        {
            DeviceId = settings.DeviceId,
            AgentName = settings.AgentName,
            ApiBaseUrl = settings.ApiBaseUrl,
            SocketIoUrl = settings.SocketIoUrl,
            ApiKey = settings.ApiKey,
            StartWithWindows = settings.StartWithWindows,
            PrinterEnabled = settings.PrinterEnabled,
            PrinterName = settings.PrinterName,
            TransportMode = settings.TransportMode,
            UsbVendorId = settings.UsbVendorId,
            UsbProductId = settings.UsbProductId,
            UsbInterfaceNumber = settings.UsbInterfaceNumber,
            UsbOutEndpoint = settings.UsbOutEndpoint,
            UsbWriteTimeoutMs = settings.UsbWriteTimeoutMs,
            ImageCommandMode = settings.ImageCommandMode,
            MaxImageWidthDots = block.MaxWidthDots ?? settings.MaxImageWidthDots,
            PaperWidth = settings.PaperWidth,
            CharacterEncoding = settings.CharacterEncoding,
            FeedLinesAfterPrint = settings.FeedLinesAfterPrint,
            ReconnectDelaySeconds = settings.ReconnectDelaySeconds,
            PingIntervalSeconds = settings.PingIntervalSeconds,
            LogFilePath = settings.LogFilePath
        };
    }

    private static void WriteAlign(Stream stream, string? align)
    {
        var value = align?.Trim().ToLowerInvariant() switch
        {
            "center" => (byte)1,
            "right" => (byte)2,
            _ => (byte)0
        };

        Write(stream, 0x1B, 0x61, value);
    }

    private static void WriteEmphasis(Stream stream, bool enabled)
    {
        Write(stream, 0x1B, 0x45, enabled ? (byte)1 : (byte)0);
    }

    private static void WriteQr(Stream stream, string content, int moduleSize, int errorCorrection)
    {
        var data = Encoding.UTF8.GetBytes(content.Trim());
        if (data.Length == 0)
        {
            return;
        }

        var storeLength = data.Length + 3;
        Write(stream, 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, (byte)moduleSize);
        Write(stream, 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, (byte)errorCorrection);
        Write(stream, 0x1D, 0x28, 0x6B, (byte)(storeLength & 0xFF), (byte)((storeLength >> 8) & 0xFF), 0x31, 0x50, 0x30);
        stream.Write(data, 0, data.Length);
        Write(stream, 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30, 0x0A);
    }

    private static void WriteBarcode(Stream stream, string content, string? barcodeType, int width)
    {
        var normalized = content.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return;
        }

        var type = barcodeType?.Trim().ToLowerInvariant() switch
        {
            "code39" => (byte)0x45,
            _ => (byte)0x45
        };

        var data = Encoding.ASCII.GetBytes(normalized);
        var barcodeWidth = Math.Clamp(width, 2, 6);
        var height = barcodeWidth >= 4 ? 24 : 40;
        Write(stream, 0x1D, 0x48, 0x00);
        Write(stream, 0x1D, 0x68, (byte)height);
        Write(stream, 0x1D, 0x77, (byte)barcodeWidth);
        Write(stream, 0x1D, 0x6B, type, (byte)data.Length);
        stream.Write(data, 0, data.Length);
        Write(stream, 0x0A);
    }

    private static void WriteText(Stream stream, Encoding encoding, string text)
    {
        var bytes = encoding.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void Write(Stream stream, params byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
    }
}
