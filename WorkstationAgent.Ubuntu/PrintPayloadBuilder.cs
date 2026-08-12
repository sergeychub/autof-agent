using System.Globalization;
using System.Text;

namespace WorkstationAgent.Ubuntu;

internal sealed class PrintPayloadBuilder
{
    private const byte Xp58Windows1251CharacterTable = 23;
    private readonly ImageMagickRasterizer _rasterizer;

    public PrintPayloadBuilder(ImageMagickRasterizer rasterizer)
    {
        _rasterizer = rasterizer;
    }

    public async Task<byte[]> BuildAsync(
        AgentSettings settings,
        PrinterEndpointSettings endpoint,
        PrintJobRequest request,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.ContentType, "raw-base64", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Base64Payload))
            {
                throw new InvalidOperationException("base64Payload is required for raw-base64 print jobs.");
            }
            return Convert.FromBase64String(request.Base64Payload);
        }

        if (string.Equals(request.ContentType, "text", StringComparison.OrdinalIgnoreCase))
        {
            return BuildText(endpoint, request);
        }

        if (string.Equals(request.ContentType, "document", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Document is null)
            {
                throw new InvalidOperationException("document is required for contentType=document.");
            }
            return await BuildDocumentAsync(endpoint, request.Document, cancellationToken);
        }

        if (string.Equals(request.ContentType, "tspl-label", StringComparison.OrdinalIgnoreCase))
        {
            if (request.TsplLabel is null)
            {
                throw new InvalidOperationException("tsplLabel is required for contentType=tspl-label.");
            }
            return await BuildTsplAsync(endpoint, request.TsplLabel, cancellationToken);
        }

        throw new InvalidOperationException($"Unsupported print content type '{request.ContentType}'.");
    }

    public byte[] BuildReceiptTest(AgentSettings settings)
    {
        var endpoint = settings.ReceiptPrinter;
        var encoding = Encoding.GetEncoding(endpoint.CharacterEncoding);
        using var stream = new MemoryStream();
        Write(stream, 0x1B, 0x40, 0x1B, 0x61, 0x01);
        WriteText(stream, encoding, "Avtoforward Agent for Ubuntu\n");
        Write(stream, 0x1B, 0x61, 0x00);
        WriteText(stream, encoding, "------------------------------\n");
        WriteText(stream, encoding, $"Agent: {settings.AgentName}\n");
        WriteText(stream, encoding, $"Machine: {Environment.MachineName}\n");
        WriteText(stream, encoding, $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        for (var i = 0; i < Math.Max(1, endpoint.FeedLinesAfterPrint); i++)
        {
            WriteText(stream, encoding, "\n");
        }
        return stream.ToArray();
    }

    public async Task<byte[]> BuildLabelTestAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        var endpoint = settings.LabelPrinter;
        var label = new TsplLabel
        {
            WidthMm = endpoint.LabelWidthMm,
            HeightMm = endpoint.LabelHeightMm,
            GapMm = endpoint.GapMm,
            Direction = endpoint.Direction,
            Speed = endpoint.Speed,
            Density = endpoint.Density,
            Copies = 1,
            Elements =
            [
                new TsplElement { Type = "box", X = 5, Y = 5, X2 = 235, Y2 = 150, LineWidth = 2 },
                new TsplElement { Type = "text", X = 15, Y = 15, Text = "UBUNTU AGENT OK", Font = "2" },
                new TsplElement { Type = "barcode", X = 15, Y = 48, Content = "TEST-123456", Height = 45 },
                new TsplElement { Type = "text", X = 15, Y = 120, Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Font = "1" }
            ]
        };
        return await BuildTsplAsync(endpoint, label, cancellationToken);
    }

    private static byte[] BuildText(PrinterEndpointSettings endpoint, PrintJobRequest request)
    {
        var encoding = Encoding.GetEncoding(
            string.IsNullOrWhiteSpace(request.Encoding) ? endpoint.CharacterEncoding : request.Encoding);
        using var stream = new MemoryStream();
        Write(stream, 0x1B, 0x40);
        var text = (request.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        WriteText(stream, encoding, text);
        if (!text.EndsWith('\n'))
        {
            WriteText(stream, encoding, "\n");
        }
        for (var i = 0; i < Math.Max(0, request.FeedLinesAfterPrint ?? endpoint.FeedLinesAfterPrint); i++)
        {
            WriteText(stream, encoding, "\n");
        }
        return stream.ToArray();
    }

    private async Task<byte[]> BuildDocumentAsync(
        PrinterEndpointSettings endpoint,
        PrintDocument document,
        CancellationToken cancellationToken)
    {
        var encoding = Encoding.GetEncoding("windows-1251");
        using var stream = new MemoryStream();
        Write(stream, 0x1B, 0x40, 0x1B, 0x74, Xp58Windows1251CharacterTable);

        foreach (var block in document.Blocks)
        {
            switch (block.Type.Trim().ToLowerInvariant())
            {
                case "text":
                    WriteAlign(stream, block.Align);
                    Write(stream, 0x1B, 0x45, block.Emphasis == true ? (byte)1 : (byte)0);
                    WriteText(stream, encoding, (block.Text ?? string.Empty) + "\n");
                    Write(stream, 0x1B, 0x45, 0x00);
                    WriteAlign(stream, "left");
                    break;
                case "image-base64":
                    if (!string.IsNullOrWhiteSpace(block.Base64Image))
                    {
                        var bitmap = await _rasterizer.RasterizeImageAsync(
                            block.Base64Image,
                            block.MaxWidthDots ?? endpoint.MaxImageWidthDots,
                            block.Threshold ?? 180,
                            cancellationToken);
                        WriteEscPosBitmap(stream, bitmap);
                        Write(stream, 0x1B, 0x74, Xp58Windows1251CharacterTable);
                    }
                    break;
                case "qr":
                    if (!string.IsNullOrWhiteSpace(block.Content))
                    {
                        WriteAlign(stream, block.Align ?? "center");
                        WriteQr(stream, block.Content, block.ModuleSize ?? 5, block.ErrorCorrection ?? 49);
                        WriteAlign(stream, "left");
                    }
                    break;
                case "barcode":
                    if (!string.IsNullOrWhiteSpace(block.Content))
                    {
                        WriteAlign(stream, block.Align ?? "center");
                        WriteBarcode(stream, block.Content, block.Width ?? 2);
                        WriteAlign(stream, "left");
                    }
                    break;
                case "feed":
                    WriteText(stream, encoding, new string('\n', Math.Max(0, block.Lines ?? 1)));
                    break;
                case "rule":
                    var character = string.IsNullOrWhiteSpace(block.Character) ? '-' : block.Character[0];
                    WriteText(stream, encoding, new string(character, Math.Max(1, block.Width ?? 30)) + "\n");
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported document block type '{block.Type}'.");
            }
        }
        return stream.ToArray();
    }

    private async Task<byte[]> BuildTsplAsync(
        PrinterEndpointSettings endpoint,
        TsplLabel label,
        CancellationToken cancellationToken)
    {
        var encoding = Encoding.GetEncoding(
            string.IsNullOrWhiteSpace(label.CharacterEncoding) ? endpoint.CharacterEncoding : label.CharacterEncoding);
        using var stream = new MemoryStream();
        WriteLine(stream, $"SIZE {(label.WidthMm ?? endpoint.LabelWidthMm).ToString("0.##", CultureInfo.InvariantCulture)} mm,{(label.HeightMm ?? endpoint.LabelHeightMm).ToString("0.##", CultureInfo.InvariantCulture)} mm", Encoding.ASCII);
        WriteLine(stream, $"GAP {(label.GapMm ?? endpoint.GapMm).ToString("0.##", CultureInfo.InvariantCulture)} mm,0 mm", Encoding.ASCII);
        WriteLine(stream, $"DIRECTION {label.Direction ?? endpoint.Direction}", Encoding.ASCII);
        WriteLine(stream, $"SPEED {label.Speed ?? endpoint.Speed}", Encoding.ASCII);
        WriteLine(stream, $"DENSITY {label.Density ?? endpoint.Density}", Encoding.ASCII);
        var codePage = string.IsNullOrWhiteSpace(label.CodePage) ? endpoint.CodePage : label.CodePage;
        if (!string.IsNullOrWhiteSpace(codePage))
        {
            WriteLine(stream, $"CODEPAGE {codePage}", Encoding.ASCII);
        }
        WriteLine(stream, "CLS", Encoding.ASCII);

        foreach (var element in label.Elements)
        {
            switch (element.Type.Trim().ToLowerInvariant())
            {
                case "text":
                    WriteLine(
                        stream,
                        $"TEXT {element.X},{element.Y},\"{(string.IsNullOrWhiteSpace(element.Font) ? "3" : element.Font)}\",{element.Rotation},{Math.Max(1, element.XMultiplier)},{Math.Max(1, element.YMultiplier)},\"{Escape(element.Text ?? string.Empty)}\"",
                        encoding);
                    break;
                case "text-bitmap":
                    var textBitmap = await _rasterizer.RasterizeTextAsync(
                        element.Text ?? string.Empty,
                        element.Width ?? 128,
                        element.Height ?? 18,
                        element.FontSize ?? 14,
                        element.Bold == true,
                        cancellationToken);
                    WriteTsplBitmap(stream, element, textBitmap);
                    break;
                case "bitmap":
                    if (string.IsNullOrWhiteSpace(element.Base64Image))
                    {
                        throw new InvalidOperationException("TSPL bitmap element requires base64Image.");
                    }
                    var bitmap = await _rasterizer.RasterizeImageAsync(
                        element.Base64Image,
                        element.Width ?? 800,
                        180,
                        cancellationToken);
                    WriteTsplBitmap(stream, element, bitmap);
                    break;
                case "barcode":
                    WriteLine(stream, $"BARCODE {element.X},{element.Y},\"{MapBarcodeType(element.BarcodeType)}\",{element.Height ?? 60},{element.Readable ?? 1},{element.Rotation},{element.Narrow ?? 2},{element.Wide ?? 2},\"{Escape(element.Content ?? string.Empty)}\"", Encoding.ASCII);
                    break;
                case "qr":
                    WriteLine(stream, $"QRCODE {element.X},{element.Y},{(string.IsNullOrWhiteSpace(element.Ecc) ? "M" : element.Ecc.ToUpperInvariant())},{element.CellWidth ?? 4},A,{element.Rotation},\"{Escape(element.Content ?? string.Empty)}\"", Encoding.ASCII);
                    break;
                case "box":
                    WriteLine(stream, $"BOX {element.X},{element.Y},{element.X2 ?? element.X},{element.Y2 ?? element.Y},{element.LineWidth ?? 1}", Encoding.ASCII);
                    break;
                case "bar":
                    WriteLine(stream, $"BAR {element.X},{element.Y},{element.Width ?? 10},{element.Height ?? 10}", Encoding.ASCII);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported TSPL element type '{element.Type}'.");
            }
        }

        WriteLine(stream, $"PRINT {Math.Max(1, label.Copies ?? 1)}", Encoding.ASCII);
        return stream.ToArray();
    }

    internal static void WriteEscPosBitmap(Stream stream, MonochromeBitmap bitmap)
    {
        Write(stream, 0x1B, 0x61, 0x01);
        Write(stream, 0x1D, 0x76, 0x30, 0x00,
            (byte)(bitmap.WidthBytes & 0xFF), (byte)((bitmap.WidthBytes >> 8) & 0xFF),
            (byte)(bitmap.Height & 0xFF), (byte)((bitmap.Height >> 8) & 0xFF));
        stream.Write(bitmap.Data);
        Write(stream, 0x0A, 0x1B, 0x61, 0x00);
    }

    internal static void WriteTsplBitmap(Stream stream, TsplElement element, MonochromeBitmap bitmap)
    {
        var header = Encoding.ASCII.GetBytes(
            $"BITMAP {element.X},{element.Y},{bitmap.WidthBytes},{bitmap.Height},{Math.Max(0, element.BitmapMode ?? 0)},");
        stream.Write(header);
        stream.Write(bitmap.Data);
        Write(stream, 0x0D, 0x0A);
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

    private static void WriteQr(Stream stream, string content, int moduleSize, int errorCorrection)
    {
        var data = Encoding.UTF8.GetBytes(content.Trim());
        var storeLength = data.Length + 3;
        Write(stream, 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, (byte)Math.Clamp(moduleSize, 1, 16));
        Write(stream, 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, (byte)errorCorrection);
        Write(stream, 0x1D, 0x28, 0x6B, (byte)(storeLength & 0xFF), (byte)((storeLength >> 8) & 0xFF), 0x31, 0x50, 0x30);
        stream.Write(data);
        Write(stream, 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30, 0x0A);
    }

    private static void WriteBarcode(Stream stream, string content, int width)
    {
        var data = Encoding.ASCII.GetBytes(content.Trim().ToUpperInvariant());
        Write(stream, 0x1D, 0x48, 0x00, 0x1D, 0x68, 40, 0x1D, 0x77, (byte)Math.Clamp(width, 2, 6));
        Write(stream, 0x1D, 0x6B, 0x45, (byte)Math.Min(255, data.Length));
        stream.Write(data, 0, Math.Min(255, data.Length));
        Write(stream, 0x0A);
    }

    private static string MapBarcodeType(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "39" or "CODE39" => "39",
        "93" or "CODE93" => "93",
        "EAN13" => "EAN13",
        "EAN8" => "EAN8",
        "UPCA" => "UPCA",
        "UPCE" => "UPCE",
        "CODABAR" => "CODABAR",
        "I25" or "ITF" => "I25",
        _ => "128"
    };

    private static string Escape(string value) => value.Replace("\"", "\\\"");

    private static void WriteLine(Stream stream, string value, Encoding encoding) =>
        WriteText(stream, encoding, value + "\r\n");

    private static void WriteText(Stream stream, Encoding encoding, string value) =>
        stream.Write(encoding.GetBytes(value));

    private static void Write(Stream stream, params byte[] value) => stream.Write(value);
}
