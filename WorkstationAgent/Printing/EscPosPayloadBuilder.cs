using System.Text;
using WorkstationAgent.Configuration;
using WorkstationAgent.Models;

namespace WorkstationAgent.Printing;

internal sealed class EscPosPayloadBuilder
{
    public byte[] BuildFromRequest(AgentSettings settings, PrintJobRequest request)
    {
        var receipt = settings.ReceiptPrinter;

        if (string.Equals(request.ContentType, "raw-base64", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Base64Payload))
            {
                throw new InvalidOperationException("Base64Payload is required for raw-base64 print jobs.");
            }

            return Convert.FromBase64String(request.Base64Payload);
        }

        if (string.Equals(request.ContentType, "text", StringComparison.OrdinalIgnoreCase))
        {
            var encodingName = string.IsNullOrWhiteSpace(request.Encoding)
                ? receipt.CharacterEncoding
                : request.Encoding;
            var encoding = Encoding.GetEncoding(encodingName);
            var feedLines = request.FeedLinesAfterPrint ?? receipt.FeedLinesAfterPrint;
            var text = request.Text ?? string.Empty;

            using var stream = new MemoryStream();
            Write(stream, 0x1B, 0x40);

            var bytes = encoding.GetBytes(text.Replace("\r\n", "\n").Replace('\n', '\r').Replace("\r", Environment.NewLine));
            stream.Write(bytes, 0, bytes.Length);

            if (!text.EndsWith('\n') && !text.EndsWith('\r'))
            {
                WriteText(stream, encoding, Environment.NewLine);
            }

            for (var i = 0; i < Math.Max(0, feedLines); i++)
            {
                WriteText(stream, encoding, Environment.NewLine);
            }

            return stream.ToArray();
        }

        throw new InvalidOperationException($"Unsupported print content type '{request.ContentType}'.");
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
