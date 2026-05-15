using System.Globalization;
using System.Text;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using WorkstationAgent.Configuration;
using WorkstationAgent.Models;

namespace WorkstationAgent.Printing;

internal sealed class TsplPayloadBuilder
{
    /// <summary>
    /// Builds a TSPL byte payload for a structured label.
    /// Coordinates (x, y, x2, y2, width, height for elements) are in dots.
    /// At 203 DPI: 1 mm = ~8 dots. At 300 DPI: 1 mm = 12 dots.
    /// </summary>
    public byte[] Build(AgentSettings settings, TsplLabel label)
    {
        var labelSettings = settings.LabelPrinter;
        var widthMm = label.WidthMm ?? labelSettings.TsplLabelWidthMm;
        var heightMm = label.HeightMm ?? labelSettings.TsplLabelHeightMm;
        var gapMm = label.GapMm ?? labelSettings.TsplLabelGapMm;
        var direction = label.Direction ?? labelSettings.TsplDirection;
        var copies = Math.Max(1, label.Copies ?? 1);
        var speed = label.Speed ?? labelSettings.TsplSpeed;
        var density = label.Density ?? labelSettings.TsplDensity;
        var characterEncoding = string.IsNullOrWhiteSpace(label.CharacterEncoding)
            ? labelSettings.CharacterEncoding
            : label.CharacterEncoding;
        var codePage = string.IsNullOrWhiteSpace(label.CodePage)
            ? labelSettings.CodePage
            : label.CodePage;
        var commandEncoding = Encoding.GetEncoding(characterEncoding);

        using var stream = new MemoryStream();
        WriteLine(stream, $"SIZE {widthMm.ToString("F1", CultureInfo.InvariantCulture)} mm, {heightMm.ToString("F1", CultureInfo.InvariantCulture)} mm", commandEncoding);
        WriteLine(stream, $"GAP {gapMm.ToString("F1", CultureInfo.InvariantCulture)} mm, 0 mm", commandEncoding);
        WriteLine(stream, $"DIRECTION {direction}", commandEncoding);
        WriteLine(stream, "REFERENCE 0,0", commandEncoding);
        WriteLine(stream, "OFFSET 0 mm", commandEncoding);
        WriteLine(stream, $"SPEED {speed}", commandEncoding);
        WriteLine(stream, $"DENSITY {density}", commandEncoding);
        if (!string.IsNullOrWhiteSpace(codePage))
        {
            WriteLine(stream, $"CODEPAGE {codePage}", commandEncoding);
        }

        WriteLine(stream, "CLS", commandEncoding);

        foreach (var element in label.Elements)
        {
            AppendElement(stream, element, commandEncoding);
        }

        WriteLine(stream, $"PRINT {copies},1", commandEncoding);
        return stream.ToArray();
    }

    private static void AppendElement(Stream stream, TsplElement element, Encoding commandEncoding)
    {
        switch (element.Type?.Trim().ToLowerInvariant())
        {
            case "text":
                var font = string.IsNullOrWhiteSpace(element.Font) ? "3" : element.Font;
                var text = EscapeString(element.Text ?? string.Empty);
                WriteLine(
                    stream,
                    $"TEXT {element.X},{element.Y},\"{font}\",{element.Rotation},{element.XMultiplier},{element.YMultiplier},\"{text}\"",
                    commandEncoding);
                break;

            case "text-bitmap":
                using (var bitmap = RenderTextBitmap(element))
                {
                    WriteBitmapElement(stream, element, bitmap);
                }
                break;

            case "bitmap":
                using (var bitmap = DecodeBitmap(element.Base64Image))
                {
                    WriteBitmapElement(stream, element, bitmap);
                }
                break;

            case "barcode":
                var barcodeType = MapBarcodeType(element.BarcodeType);
                var bcHeight = element.Height ?? 60;
                var readable = element.Readable ?? 1;
                var narrow = element.Narrow ?? 2;
                var wide = element.Wide ?? 2;
                var bcContent = EscapeString(element.Content ?? string.Empty);
                WriteLine(
                    stream,
                    $"BARCODE {element.X},{element.Y},\"{barcodeType}\",{bcHeight},{readable},{element.Rotation},{narrow},{wide},\"{bcContent}\"",
                    commandEncoding);
                break;

            case "qr":
                var ecc = string.IsNullOrWhiteSpace(element.Ecc) ? "M" : element.Ecc.ToUpperInvariant();
                var cellWidth = element.CellWidth ?? 4;
                var qrContent = EscapeString(element.Content ?? string.Empty);
                WriteLine(
                    stream,
                    $"QRCODE {element.X},{element.Y},{ecc},{cellWidth},A,{element.Rotation},\"{qrContent}\"",
                    commandEncoding);
                break;

            case "box":
                var x2 = element.X2 ?? element.X;
                var y2 = element.Y2 ?? element.Y;
                var lineWidth = element.LineWidth ?? 1;
                WriteLine(stream, $"BOX {element.X},{element.Y},{x2},{y2},{lineWidth}", commandEncoding);
                break;

            case "bar":
                var barWidth = element.Width ?? 10;
                var barHeight = element.Height ?? 10;
                WriteLine(stream, $"BAR {element.X},{element.Y},{barWidth},{barHeight}", commandEncoding);
                break;
        }
    }

    private static void WriteBitmapElement(Stream stream, TsplElement element, Bitmap bitmap)
    {
        var bitmapData = BuildBitmapData(bitmap, out var widthBytes, out var heightDots);
        var mode = Math.Max(0, element.BitmapMode ?? 0);
        var header = Encoding.ASCII.GetBytes($"BITMAP {element.X},{element.Y},{widthBytes},{heightDots},{mode},");
        stream.Write(header, 0, header.Length);
        stream.Write(bitmapData, 0, bitmapData.Length);
        stream.WriteByte((byte)'\r');
        stream.WriteByte((byte)'\n');
    }

    private static Bitmap RenderTextBitmap(TsplElement element)
    {
        var text = element.Text ?? string.Empty;
        var width = Math.Max(8, element.Width ?? 128);
        var height = Math.Max(8, element.Height ?? 14);
        var fontSize = Math.Max(8, element.FontSize ?? Math.Max(8, height - 2));
        var fontStyle = element.Bold == true ? FontStyle.Bold : FontStyle.Regular;
        var bitmap = new Bitmap(width, height);
        bitmap.SetResolution(203, 203);

        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Tahoma", fontSize, fontStyle, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoClip
        };
        graphics.Clear(Color.White);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        graphics.DrawString(text, font, Brushes.Black, new RectangleF(0, -1, width, height + 2), format);

        return bitmap;
    }

    private static Bitmap DecodeBitmap(string? base64Image)
    {
        var normalized = NormalizeBase64(base64Image);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Bitmap element requires base64Image.");
        }

        using var imageStream = new MemoryStream(Convert.FromBase64String(normalized));
        using var source = new Bitmap(imageStream);
        return new Bitmap(source);
    }

    private static string NormalizeBase64(string? base64Image)
    {
        var value = string.IsNullOrWhiteSpace(base64Image) ? string.Empty : base64Image.Trim();
        var commaIndex = value.IndexOf(',');
        return commaIndex >= 0 ? value[(commaIndex + 1)..] : value;
    }

    private static byte[] BuildBitmapData(Bitmap bitmap, out int widthBytes, out int heightDots)
    {
        widthBytes = Math.Max(1, (bitmap.Width + 7) / 8);
        heightDots = Math.Max(1, bitmap.Height);
        var data = new byte[widthBytes * heightDots];

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var byteIndex = 0; byteIndex < widthBytes; byteIndex++)
            {
                byte packed = 0;

                for (var bit = 0; bit < 8; bit++)
                {
                    var x = byteIndex * 8 + bit;
                    if (x >= bitmap.Width)
                    {
                        packed |= (byte)(0x80 >> bit);
                        continue;
                    }

                    var pixel = bitmap.GetPixel(x, y);
                    var luminance = (pixel.R + pixel.G + pixel.B) / 3;
                    if (pixel.A <= 0 || luminance >= 200)
                    {
                        packed |= (byte)(0x80 >> bit);
                    }
                }

                data[y * widthBytes + byteIndex] = packed;
            }
        }

        return data;
    }

    private static void WriteLine(Stream stream, string value, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value + "\r\n");
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string EscapeString(string value) => value.Replace("\"", "\\\"");

    private static string MapBarcodeType(string? barcodeType)
    {
        return barcodeType?.Trim().ToUpperInvariant() switch
        {
            "128" or "CODE128" => "128",
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
    }
}
