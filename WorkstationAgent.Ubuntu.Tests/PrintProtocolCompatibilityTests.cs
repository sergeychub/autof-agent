using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkstationAgent.Ubuntu.Tests;

[TestClass]
public sealed class PrintProtocolCompatibilityTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [TestMethod]
    public async Task RawBase64PayloadFromAutofIsPreserved()
    {
        var expected = new byte[] { 0x1B, 0x40, 0x1D, 0x56, 0x00 };
        var request = JsonSerializer.Deserialize<PrintJobRequest>(
            $$"""
            {
              "requestId": "raw-1",
              "contentType": "raw-base64",
              "target": "receipt",
              "base64Payload": "{{Convert.ToBase64String(expected)}}",
              "documentName": "raw receipt"
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var actual = await CreateBuilder().BuildAsync(
            CreateSettings(),
            CreateSettings().ReceiptPrinter,
            request!,
            CancellationToken.None);

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task StructuredReceiptProducesEscPosCommandsExpectedByAutof()
    {
        var request = JsonSerializer.Deserialize<PrintJobRequest>(
            """
            {
              "requestId": "document-1",
              "contentType": "document",
              "target": "receipt",
              "document": {
                "blocks": [
                  { "type": "text", "text": "Autof", "align": "center", "emphasis": true },
                  { "type": "qr", "content": "order:123", "moduleSize": 4 },
                  { "type": "barcode", "content": "ABC123", "barcodeType": "code39" },
                  { "type": "rule", "char": "-", "width": 8 },
                  { "type": "feed", "lines": 2 }
                ]
              }
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var settings = CreateSettings();

        var payload = await CreateBuilder().BuildAsync(
            settings,
            settings.ReceiptPrinter,
            request!,
            CancellationToken.None);

        CollectionAssert.AreEqual(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 23 }, payload[..5]);
        Assert.IsTrue(Contains(payload, [0x1B, 0x61, 0x01]));
        Assert.IsTrue(Contains(payload, [0x1D, 0x28, 0x6B]));
        Assert.IsTrue(Contains(payload, [0x1D, 0x6B, 0x45]));
    }

    [TestMethod]
    public async Task LabelPayloadProducesTsplCommandsExpectedByAutof()
    {
        var request = JsonSerializer.Deserialize<PrintJobRequest>(
            """
            {
              "requestId": "label-1",
              "contentType": "tspl-label",
              "target": "label",
              "tsplLabel": {
                "widthMm": 30,
                "heightMm": 20,
                "gapMm": 2,
                "direction": 0,
                "copies": 2,
                "speed": 2,
                "density": 8,
                "elements": [
                  { "type": "text", "x": 10, "y": 10, "text": "AUTOF", "font": "2" },
                  { "type": "barcode", "x": 10, "y": 40, "content": "12345", "barcodeType": "128" },
                  { "type": "qr", "x": 140, "y": 10, "content": "product:123" },
                  { "type": "box", "x": 2, "y": 2, "x2": 230, "y2": 150, "lineWidth": 2 }
                ]
              }
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var settings = CreateSettings();

        var payload = await CreateBuilder().BuildAsync(
            settings,
            settings.LabelPrinter,
            request!,
            CancellationToken.None);
        var commands = Encoding.ASCII.GetString(payload);

        StringAssert.Contains(commands, "SIZE 30 mm,20 mm\r\n");
        StringAssert.Contains(commands, "GAP 2 mm,0 mm\r\n");
        StringAssert.Contains(commands, "REFERENCE 0,0\r\n");
        StringAssert.Contains(commands, "OFFSET 0 mm\r\n");
        StringAssert.Contains(commands, "TEXT 10,10,\"2\",0,1,1,\"AUTOF\"\r\n");
        StringAssert.Contains(commands, "BARCODE 10,40,\"128\"");
        StringAssert.Contains(commands, "QRCODE 140,10,M,4,A,0,\"product:123\"");
        StringAssert.Contains(commands, "BOX 2,2,230,150,2");
        StringAssert.EndsWith(commands, "PRINT 2,1\r\n");
    }

    [TestMethod]
    public async Task LabelTestLayoutUsesConfiguredMediaSize()
    {
        var settings = CreateSettings();
        settings.LabelPrinter.LabelWidthMm = 58;
        settings.LabelPrinter.LabelHeightMm = 40;

        var payload = await CreateBuilder().BuildLabelTestAsync(settings, CancellationToken.None);
        var commands = Encoding.ASCII.GetString(payload);

        StringAssert.Contains(commands, "SIZE 58 mm,40 mm\r\n");
        StringAssert.Contains(commands, "BOX 10,10,453,309,2\r\n");
        StringAssert.Contains(commands, "TEXT 20,30,\"2\",0,2,2,\"UBUNTU AGENT OK\"\r\n");
        StringAssert.Contains(commands, "BARCODE 20,96,\"128\",90,1,0,3,3,\"TEST-123456\"\r\n");
        StringAssert.Contains(commands, "TEXT 20,240,\"1\",0,1,1,\"58x40 mm ");
        StringAssert.EndsWith(commands, "PRINT 1,1\r\n");
    }

    [TestMethod]
    public void EscPosBitmapUsesCompatibleEscStarVerticalSlices()
    {
        var bitmap = new MonochromeBitmap(8, 1, [0b1000_0001]);
        using var escPos = new MemoryStream();
        PrintPayloadBuilder.WriteEscPosBitmap(escPos, bitmap);
        CollectionAssert.AreEqual(
            new byte[]
            {
                0x1B, 0x61, 0x01,
                0x1B, 0x33, 24,
                0x1B, 0x2A, 33, 0x08, 0x00,
                0x80, 0x00, 0x00,
                0x00, 0x00, 0x00,
                0x00, 0x00, 0x00,
                0x00, 0x00, 0x00,
                0x00, 0x00, 0x00,
                0x00, 0x00, 0x00,
                0x00, 0x00, 0x00,
                0x80, 0x00, 0x00,
                0x0A,
                0x1B, 0x32,
                0x1B, 0x61, 0x00
            },
            escPos.ToArray());
    }

    [TestMethod]
    public void EscPosBitmapContinuesInTwentyFourDotBands()
    {
        var data = new byte[25];
        data[0] = 0x80;
        data[8] = 0x80;
        data[16] = 0x80;
        data[24] = 0x80;
        using var stream = new MemoryStream();

        PrintPayloadBuilder.WriteEscPosBitmap(stream, new MonochromeBitmap(1, 25, data));

        CollectionAssert.AreEqual(
            new byte[]
            {
                0x1B, 0x61, 0x01,
                0x1B, 0x33, 24,
                0x1B, 0x2A, 33, 0x01, 0x00,
                0x80, 0x80, 0x80,
                0x0A,
                0x1B, 0x2A, 33, 0x01, 0x00,
                0x80, 0x00, 0x00,
                0x0A,
                0x1B, 0x32,
                0x1B, 0x61, 0x00
            },
            stream.ToArray());
    }

    [TestMethod]
    public void TsplBitmapUsesZeroForPrintedDots()
    {
        var bitmap = new MonochromeBitmap(8, 1, [0b1000_0001]);

        using var tspl = new MemoryStream();
        PrintPayloadBuilder.WriteTsplBitmap(tspl, new TsplElement { X = 3, Y = 4 }, bitmap);
        CollectionAssert.AreEqual(
            Encoding.ASCII.GetBytes("BITMAP 3,4,1,1,0,").Concat(new byte[] { 0x7E, 0x0D, 0x0A }).ToArray(),
            tspl.ToArray());
    }

    [TestMethod]
    public void ResultJsonMatchesWorkstationAgentGatewayContract()
    {
        var json = JsonSerializer.Serialize(new PrintJobResult
        {
            RequestId = "request-1",
            Success = true,
            PrinterName = "cups:receipt",
            PrintedAt = "2026-08-12T00:00:00Z",
            DocumentName = "receipt"
        });

        StringAssert.Contains(json, "\"requestId\":\"request-1\"");
        StringAssert.Contains(json, "\"success\":true");
        StringAssert.Contains(json, "\"printerName\":\"cups:receipt\"");
        StringAssert.Contains(json, "\"printedAt\":\"2026-08-12T00:00:00Z\"");
        StringAssert.Contains(json, "\"documentName\":\"receipt\"");
    }

    private static AgentSettings CreateSettings() => new()
    {
        ReceiptPrinter = new PrinterEndpointSettings
        {
            CharacterEncoding = "cp866",
            MaxImageWidthDots = 384
        },
        LabelPrinter = new PrinterEndpointSettings
        {
            Enabled = true,
            CharacterEncoding = "ascii",
            LabelWidthMm = 30,
            LabelHeightMm = 20,
            GapMm = 2,
            Speed = 2,
            Density = 8
        }
    };

    private static PrintPayloadBuilder CreateBuilder() => new(new ImageMagickRasterizer());

    private static bool Contains(byte[] value, byte[] sequence) =>
        value.AsSpan().IndexOf(sequence) >= 0;
}
