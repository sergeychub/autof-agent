using System.Diagnostics;
using System.Globalization;

namespace WorkstationAgent.Ubuntu;

internal sealed record MonochromeBitmap(int Width, int Height, byte[] Data)
{
    public int WidthBytes => (Width + 7) / 8;
}

internal sealed class ImageMagickRasterizer
{
    private readonly string _executable = ResolveExecutable();

    public async Task<MonochromeBitmap> RasterizeImageAsync(
        string base64Image,
        int maxWidthDots,
        int threshold,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeBase64(base64Image);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Image block requires base64Image.");
        }

        var input = Convert.FromBase64String(normalized);
        var thresholdPercent = Math.Clamp(threshold, 0, 255) * 100d / 255d;
        var output = await RunAsync(
            [
                "-",
                "-auto-orient",
                "-resize", $"{Math.Max(1, maxWidthDots)}x>",
                "-background", "white",
                "-alpha", "remove",
                "-colorspace", "Gray",
                "-threshold", thresholdPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%",
                "pbm:-"
            ],
            input,
            cancellationToken);
        return ParseBinaryPbm(output);
    }

    public async Task<MonochromeBitmap> RasterizeTextAsync(
        string text,
        int width,
        int height,
        int fontSize,
        bool bold,
        CancellationToken cancellationToken)
    {
        var output = await RunAsync(
            [
                "-background", "white",
                "-fill", "black",
                "-font", "DejaVu-Sans",
                "-weight", bold ? "700" : "400",
                "-pointsize", Math.Max(8, fontSize).ToString(CultureInfo.InvariantCulture),
                "-size", $"{Math.Max(8, width)}x{Math.Max(8, height)}",
                "-gravity", "northwest",
                "caption:" + text,
                "-colorspace", "Gray",
                "-threshold", "70%",
                "pbm:-"
            ],
            null,
            cancellationToken);
        return ParseBinaryPbm(output);
    }

    private async Task<byte[]> RunAsync(
        IReadOnlyList<string> arguments,
        byte[]? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start ImageMagick.");
        using var output = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        if (standardInput is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(standardInput, cancellationToken);
        }
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await outputTask;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            throw;
        }

        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ImageMagick exited with code {process.ExitCode}: {error.Trim()}");
        }
        return output.ToArray();
    }

    private static MonochromeBitmap ParseBinaryPbm(byte[] value)
    {
        var offset = 0;
        var magic = ReadToken(value, ref offset);
        if (magic != "P4")
        {
            throw new InvalidOperationException("ImageMagick did not return a binary PBM image.");
        }
        var width = int.Parse(ReadToken(value, ref offset), CultureInfo.InvariantCulture);
        var height = int.Parse(ReadToken(value, ref offset), CultureInfo.InvariantCulture);
        if (offset >= value.Length || !IsWhitespace(value[offset]))
        {
            throw new InvalidOperationException("Invalid PBM header.");
        }
        if (value[offset] == '\r' && offset + 1 < value.Length && value[offset + 1] == '\n')
        {
            offset += 2;
        }
        else
        {
            offset++;
        }

        var expectedLength = ((width + 7) / 8) * height;
        if (width < 1 || height < 1 || value.Length - offset < expectedLength)
        {
            throw new InvalidOperationException("PBM raster data is incomplete.");
        }
        var data = new byte[expectedLength];
        Buffer.BlockCopy(value, offset, data, 0, expectedLength);
        return new MonochromeBitmap(width, height, data);
    }

    private static string ReadToken(byte[] value, ref int offset)
    {
        while (offset < value.Length)
        {
            while (offset < value.Length && IsWhitespace(value[offset]))
            {
                offset++;
            }
            if (offset >= value.Length || value[offset] != '#')
            {
                break;
            }
            while (offset < value.Length && value[offset] != '\n')
            {
                offset++;
            }
        }

        var start = offset;
        while (offset < value.Length && !IsWhitespace(value[offset]))
        {
            offset++;
        }
        return System.Text.Encoding.ASCII.GetString(value, start, offset - start);
    }

    private static bool IsWhitespace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static string NormalizeBase64(string value)
    {
        var trimmed = value.Trim();
        var commaIndex = trimmed.IndexOf(',');
        return commaIndex >= 0 ? trimmed[(commaIndex + 1)..] : trimmed;
    }

    private static string ResolveExecutable()
    {
        if (File.Exists("/usr/bin/magick"))
        {
            return "/usr/bin/magick";
        }
        if (File.Exists("/usr/bin/convert"))
        {
            return "/usr/bin/convert";
        }
        return "magick";
    }
}
