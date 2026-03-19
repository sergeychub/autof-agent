using System.Drawing;
using System.Drawing.Drawing2D;

namespace WorkstationAgent.Branding;

internal static class AvtoforwardBranding
{
    public const string AppName = "Автофорвад Agent";
    public const string CompanyName = "Автофорвад";

    public static Icon CreateTrayIcon()
    {
        var logo = CreateHeaderLogoBitmap(64, 64);
        var handle = logo.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static Bitmap CreateHeaderLogoBitmap(int width, int height)
    {
        var logoPath = GetAssetPath("logo.png");
        if (logoPath is not null)
        {
            using var original = new Bitmap(logoPath);
            using var cropped = CropVisibleArea(original);
            return ResizeBitmap(cropped, width, height, Color.Transparent);
        }

        return CreateFallbackBitmap(width, height);
    }

    private static Bitmap ResizeBitmap(Bitmap original, int width, int height, Color background)
    {
        var bitmap = new Bitmap(width, height);
        bitmap.MakeTransparent();

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(background);

        var scale = Math.Min((float)width / original.Width, (float)height / original.Height);
        var drawWidth = Math.Max(1, (int)Math.Round(original.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(original.Height * scale));
        var x = (width - drawWidth) / 2;
        var y = (height - drawHeight) / 2;
        graphics.DrawImage(original, new Rectangle(x, y, drawWidth, drawHeight));

        return bitmap;
    }

    private static Bitmap CropVisibleArea(Bitmap source)
    {
        var left = source.Width;
        var top = source.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (!IsBackgroundPixel(pixel))
                {
                    if (x < left) left = x;
                    if (y < top) top = y;
                    if (x > right) right = x;
                    if (y > bottom) bottom = y;
                }
            }
        }

        if (right < left || bottom < top)
        {
            return new Bitmap(source);
        }

        var rect = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        return source.Clone(rect, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private static bool IsBackgroundPixel(Color pixel)
    {
        if (pixel.A <= 8)
        {
            return true;
        }

        return pixel.R >= 245 && pixel.G >= 245 && pixel.B >= 245;
    }

    private static string? GetAssetPath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", fileName)
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private static Bitmap CreateFallbackBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var brush = new LinearGradientBrush(
            new Rectangle(0, 0, width, height),
            Color.FromArgb(130, 8, 7),
            Color.FromArgb(234, 53, 22),
            LinearGradientMode.ForwardDiagonal);
        graphics.FillEllipse(brush, 0, 0, width, height);
        using var cutout = new SolidBrush(Color.White);
        graphics.FillEllipse(cutout, width * 0.28f, height * 0.22f, width * 0.36f, height * 0.56f);

        return bitmap;
    }
}
