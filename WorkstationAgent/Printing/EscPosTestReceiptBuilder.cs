using System.Text;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;

namespace WorkstationAgent.Printing;

internal sealed class EscPosTestReceiptBuilder
{
    public byte[] Build(AgentSettings settings)
    {
        var receipt = settings.ReceiptPrinter;
        var encoding = Encoding.GetEncoding(receipt.CharacterEncoding);
        using var stream = new MemoryStream();

        Write(stream, 0x1B, 0x40);
        Write(stream, 0x1B, 0x61, 0x01);
        WriteLine(stream, encoding, "XPrinter XP-58IIH");
        WriteLine(stream, encoding, AvtoforwardBranding.AppName);
        Write(stream, 0x1B, 0x61, 0x00);
        WriteLine(stream, encoding, "------------------------------");
        WriteLine(stream, encoding, $"Agent: {settings.AgentName}");
        WriteLine(stream, encoding, $"Machine: {Environment.MachineName}");
        WriteLine(stream, encoding, $"User: {Environment.UserName}");
        WriteLine(stream, encoding, $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteLine(stream, encoding, string.Empty);
        WriteLine(stream, encoding, "Test thermal print");
        WriteLine(stream, encoding, "USB printer channel is ready");
        WriteLine(stream, encoding, "ESC/POS RAW mode");

        for (var i = 0; i < Math.Max(1, receipt.FeedLinesAfterPrint); i++)
        {
            WriteLine(stream, encoding, string.Empty);
        }

        return stream.ToArray();
    }

    private static void WriteLine(Stream stream, Encoding encoding, string text)
    {
        var bytes = encoding.GetBytes(text + Environment.NewLine);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void Write(Stream stream, params byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
    }
}
