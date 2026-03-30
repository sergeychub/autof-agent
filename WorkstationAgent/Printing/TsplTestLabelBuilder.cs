using WorkstationAgent.Configuration;
using WorkstationAgent.Models;

namespace WorkstationAgent.Printing;

internal sealed class TsplTestLabelBuilder
{
    private readonly TsplPayloadBuilder _builder;

    public TsplTestLabelBuilder(TsplPayloadBuilder builder)
    {
        _builder = builder;
    }

    public byte[] Build(AgentSettings settings)
    {
        var labelSettings = settings.LabelPrinter;
        const double dotsPerMm = 8.0; // 203 DPI
        var widthDots = (int)Math.Round(labelSettings.TsplLabelWidthMm * dotsPerMm);
        var heightDots = (int)Math.Round(labelSettings.TsplLabelHeightMm * dotsPerMm);
        var isCompact = widthDots <= 260 || heightDots <= 180;
        var agentName = string.IsNullOrWhiteSpace(settings.AgentName) ? "AGENT" : settings.AgentName;
        if (agentName.Length > 16)
        {
            agentName = agentName[..16];
        }

        var label = new TsplLabel
        {
            WidthMm = labelSettings.TsplLabelWidthMm,
            HeightMm = labelSettings.TsplLabelHeightMm,
            GapMm = labelSettings.TsplLabelGapMm,
            Direction = labelSettings.TsplDirection,
            Speed = labelSettings.TsplSpeed,
            Density = labelSettings.TsplDensity,
            Copies = 1
        };

        label.Elements.AddRange(isCompact
            ? BuildCompactLayout(widthDots, heightDots, agentName, labelSettings)
            : BuildStandardLayout(widthDots, heightDots, agentName, labelSettings));

        return _builder.Build(settings, label);
    }

    private static IEnumerable<TsplElement> BuildCompactLayout(int widthDots, int heightDots, string agentName, LabelPrinterSettings settings)
    {
        var boxInset = Math.Clamp(widthDots / 30, 6, 10);
        var margin = Math.Clamp(widthDots / 18, 12, 16);
        var titleTop = 10;
        var agentTop = 28;
        var barcodeTop = 50;
        var barcodeHeight = Math.Clamp(heightDots / 5, 24, 32);
        var footerTop = Math.Max(barcodeTop + barcodeHeight + 10, heightDots - 22);
        var footerText = $"{settings.TsplLabelWidthMm:F0}x{settings.TsplLabelHeightMm:F0} S{settings.TsplSpeed} D{settings.TsplDensity}";

        return
        [
            new TsplElement { Type = "box", X = boxInset, Y = boxInset, X2 = widthDots - boxInset, Y2 = heightDots - boxInset, LineWidth = 1 },
            new TsplElement { Type = "text", X = margin, Y = titleTop, Text = "TSPL OK", Font = "2" },
            new TsplElement { Type = "text", X = margin, Y = agentTop, Text = agentName, Font = "1" },
            new TsplElement { Type = "barcode", X = margin, Y = barcodeTop, BarcodeType = "128", Content = "TSPL365", Height = barcodeHeight, Readable = 0, Narrow = 1, Wide = 2 },
            new TsplElement { Type = "text", X = margin, Y = footerTop, Text = footerText, Font = "1" }
        ];
    }

    private static IEnumerable<TsplElement> BuildStandardLayout(int widthDots, int heightDots, string agentName, LabelPrinterSettings settings)
    {
        var margin = 12;
        var rightEdge = widthDots - margin;
        var bottomEdge = heightDots - margin;

        return
        [
            new TsplElement { Type = "box", X = 4, Y = 4, X2 = widthDots - 4, Y2 = heightDots - 4, LineWidth = 2 },
            new TsplElement { Type = "text", X = margin, Y = margin, Text = "TSPL Test Label", Font = "3", XMultiplier = 1, YMultiplier = 1 },
            new TsplElement { Type = "text", X = margin, Y = margin + 26, Text = $"Agent: {agentName}", Font = "2" },
            new TsplElement { Type = "text", X = margin, Y = margin + 46, Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Font = "1" },
            new TsplElement { Type = "bar", X = margin, Y = margin + 66, Width = Math.Max(40, rightEdge - margin), Height = 2 },
            new TsplElement { Type = "barcode", X = margin, Y = margin + 76, BarcodeType = "128", Content = "TEST-123456", Height = 44, Readable = 1, Narrow = 2, Wide = 2 },
            new TsplElement { Type = "qr", X = Math.Max(margin, rightEdge - 78), Y = margin + 8, Content = $"WorkstationAgent/{agentName}", CellWidth = 3, Ecc = "M" },
            new TsplElement { Type = "text", X = margin, Y = bottomEdge - 16, Text = $"Label {settings.TsplLabelWidthMm:F0}x{settings.TsplLabelHeightMm:F0}  S{settings.TsplSpeed} D{settings.TsplDensity}", Font = "1" }
        ];
    }
}
