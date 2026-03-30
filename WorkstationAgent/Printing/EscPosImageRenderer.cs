using System.Drawing;
using System.Text;
using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal sealed class EscPosImageRenderer
{
    public byte[] BuildLogoTest(AgentSettings settings, string logoPath)
    {
        using var stream = new MemoryStream();
        var receipt = settings.ReceiptPrinter;
        var encoding = Encoding.GetEncoding(receipt.CharacterEncoding);

        Write(stream, 0x1B, 0x40);
        Write(stream, 0x1B, 0x61, 0x01);
        WriteText(stream, encoding, "Logo test print" + Environment.NewLine + Environment.NewLine);

        PrintVariant(stream, encoding, settings, logoPath, "1. Threshold 180", MonochromeMode.Threshold180);
        PrintVariant(stream, encoding, settings, logoPath, "2. High contrast 140", MonochromeMode.Threshold140);
        PrintVariant(stream, encoding, settings, logoPath, "3. Dither", MonochromeMode.Dither);
        PrintVariant(stream, encoding, settings, logoPath, "4. Logo optimized", MonochromeMode.LogoOptimized);
        PrintVariant(stream, encoding, settings, logoPath, "5. Thermal preset", MonochromeMode.ThermalPreset);

        for (var i = 0; i < Math.Max(1, receipt.FeedLinesAfterPrint); i++)
        {
            WriteText(stream, encoding, Environment.NewLine);
        }

        return stream.ToArray();
    }

    public void WriteImageFromBase64(Stream stream, AgentSettings settings, string base64Image)
    {
        var bytes = Convert.FromBase64String(base64Image);
        using var input = new MemoryStream(bytes);
        using var bitmap = new Bitmap(input);
        WriteImage(stream, settings, bitmap, MonochromeMode.Threshold180);
    }

    public void WriteImageFromFile(Stream stream, AgentSettings settings, string path)
    {
        using var bitmap = new Bitmap(path);
        WriteImage(stream, settings, bitmap, MonochromeMode.Threshold180);
    }

    public void WriteImage(Stream stream, AgentSettings settings, Bitmap source, MonochromeMode mode)
    {
        var receipt = settings.ReceiptPrinter;
        using var bitmap = ResizeToPrintableWidth(source, receipt.MaxImageWidthDots);
        if (string.Equals(receipt.ImageCommandMode, "esc-star", StringComparison.OrdinalIgnoreCase))
        {
            WriteEscStar(stream, bitmap, mode);
            return;
        }

        WriteGsV0(stream, bitmap, mode);
    }

    private static Bitmap ResizeToPrintableWidth(Bitmap source, int maxWidthDots)
    {
        var width = Math.Min(source.Width, Math.Max(1, maxWidthDots));
        var scale = width / (double)source.Width;
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return new Bitmap(source, new Size(width, height));
    }

    private static void WriteGsV0(Stream stream, Bitmap bitmap, MonochromeMode mode)
    {
        var mono = ToMonochrome(bitmap, mode);
        var widthBytes = (bitmap.Width + 7) / 8;
        Write(stream, 0x1B, 0x61, 0x01);
        Write(stream, 0x1D, 0x76, 0x30, 0x00, (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
            (byte)(bitmap.Height & 0xFF), (byte)((bitmap.Height >> 8) & 0xFF));

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var xByte = 0; xByte < widthBytes; xByte++)
            {
                byte value = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = xByte * 8 + bit;
                    if (x < bitmap.Width && mono[x, y])
                    {
                        value |= (byte)(0x80 >> bit);
                    }
                }

                stream.WriteByte(value);
            }
        }

        Write(stream, 0x0A, 0x1B, 0x61, 0x00);
    }

    private static void WriteEscStar(Stream stream, Bitmap bitmap, MonochromeMode mode)
    {
        var mono = ToMonochrome(bitmap, mode);
        Write(stream, 0x1B, 0x61, 0x01);
        Write(stream, 0x1B, 0x33, 24);

        for (var rowOffset = 0; rowOffset < bitmap.Height; rowOffset += 24)
        {
            Write(stream, 0x1B, 0x2A, 33, (byte)(bitmap.Width & 0xFF), (byte)((bitmap.Width >> 8) & 0xFF));

            for (var x = 0; x < bitmap.Width; x++)
            {
                for (var slice = 0; slice < 3; slice++)
                {
                    byte value = 0;
                    for (var bit = 0; bit < 8; bit++)
                    {
                        var y = rowOffset + slice * 8 + bit;
                        if (y < bitmap.Height && mono[x, y])
                        {
                            value |= (byte)(0x80 >> bit);
                        }
                    }

                    stream.WriteByte(value);
                }
            }

            stream.WriteByte(0x0A);
        }

        Write(stream, 0x1B, 0x32, 0x1B, 0x61, 0x00);
    }

    private static bool[,] ToMonochrome(Bitmap bitmap, MonochromeMode mode)
    {
        if (mode == MonochromeMode.LogoOptimized)
        {
            return ToMonochromeAutoContrastDither(bitmap);
        }

        if (mode == MonochromeMode.ThermalPreset)
        {
            return ToMonochromeThermalPreset(bitmap);
        }

        return mode == MonochromeMode.Dither
            ? ToMonochromeDither(bitmap)
            : ToMonochromeThreshold(bitmap, mode == MonochromeMode.Threshold140 ? 140 : 180);
    }

    private static bool[,] ToMonochromeThreshold(Bitmap bitmap, int threshold)
    {
        var mono = new bool[bitmap.Width, bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A <= 16)
                {
                    mono[x, y] = false;
                    continue;
                }

                var luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                mono[x, y] = luminance < threshold;
            }
        }

        return mono;
    }

    private static bool[,] ToMonochromeDither(Bitmap bitmap)
    {
        var mono = new bool[bitmap.Width, bitmap.Height];
        var luminance = BuildLuminanceMap(bitmap);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var oldPixel = luminance[x, y];
                var newPixel = oldPixel < 170 ? 0 : 255;
                var error = oldPixel - newPixel;
                mono[x, y] = newPixel == 0;

                Diffuse(luminance, bitmap.Width, bitmap.Height, x + 1, y, error * 7 / 16);
                Diffuse(luminance, bitmap.Width, bitmap.Height, x - 1, y + 1, error * 3 / 16);
                Diffuse(luminance, bitmap.Width, bitmap.Height, x, y + 1, error * 5 / 16);
                Diffuse(luminance, bitmap.Width, bitmap.Height, x + 1, y + 1, error * 1 / 16);
            }
        }

        return mono;
    }

    private static bool[,] ToMonochromeAutoContrastDither(Bitmap bitmap)
    {
        var mono = new bool[bitmap.Width, bitmap.Height];
        var luminance = BuildLogoContrastMap(bitmap, gamma: 0.92, floor: 18, ceiling: 237);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var oldPixel = luminance[x, y];
                var newPixel = oldPixel < 176 ? 0 : 255;
                var error = oldPixel - newPixel;
                mono[x, y] = newPixel == 0;

                Diffuse(luminance, bitmap.Width, bitmap.Height, x + 1, y, error * 7 / 16);
                Diffuse(luminance, bitmap.Width, bitmap.Height, x - 1, y + 1, error * 3 / 16);
                Diffuse(luminance, bitmap.Width, bitmap.Height, x, y + 1, error * 5 / 16);
                Diffuse(luminance, bitmap.Width, bitmap.Height, x + 1, y + 1, error * 1 / 16);
            }
        }

        return mono;
    }

    private static bool[,] ToMonochromeThermalPreset(Bitmap bitmap)
    {
        var luminance = BuildLogoContrastMap(bitmap, gamma: 0.96, floor: 24, ceiling: 220);
        var mono = new bool[bitmap.Width, bitmap.Height];
        var bayer4 =
            new[,]
            {
                { 0, 8, 2, 10 },
                { 12, 4, 14, 6 },
                { 3, 11, 1, 9 },
                { 15, 7, 13, 5 }
            };

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var threshold = (bayer4[y % 4, x % 4] + 0.5) / 16.0;
                var normalized = luminance[x, y] / 255.0;
                mono[x, y] = normalized < threshold;
            }
        }

        return mono;
    }

    private static double[,] BuildLogoContrastMap(Bitmap bitmap, double gamma, double floor, double ceiling)
    {
        var luminance = BuildLuminanceMap(bitmap);
        var min = 255.0;
        var max = 0.0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A <= 16)
                {
                    continue;
                }

                min = Math.Min(min, luminance[x, y]);
                max = Math.Max(max, luminance[x, y]);
            }
        }

        if (max - min < 1)
        {
            return luminance;
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A <= 16)
                {
                    luminance[x, y] = 255;
                    continue;
                }

                var normalized = (luminance[x, y] - min) / (max - min);
                normalized = Math.Clamp(normalized, 0, 1);
                normalized = Math.Pow(normalized, gamma);
                luminance[x, y] = floor + normalized * (ceiling - floor);
            }
        }

        return luminance;
    }

    private static double[,] BuildLuminanceMap(Bitmap bitmap)
    {
        var luminance = new double[bitmap.Width, bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                luminance[x, y] = pixel.A <= 16
                    ? 255
                    : (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000.0;
            }
        }

        return luminance;
    }

    private static void Diffuse(double[,] luminance, int width, int height, int x, int y, double amount)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        luminance[x, y] = Math.Clamp(luminance[x, y] + amount, 0, 255);
    }

    private void PrintVariant(Stream stream, Encoding encoding, AgentSettings settings, string logoPath, string title, MonochromeMode mode)
    {
        WriteText(stream, encoding, title + Environment.NewLine);
        using var bitmap = new Bitmap(logoPath);
        WriteImage(stream, settings, bitmap, mode);
        WriteText(stream, encoding, Environment.NewLine);
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

    internal enum MonochromeMode
    {
        Threshold180,
        Threshold140,
        Dither,
        LogoOptimized,
        ThermalPreset
    }
}
