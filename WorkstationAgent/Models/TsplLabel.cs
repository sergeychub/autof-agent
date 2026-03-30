using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class TsplLabel
{
    [JsonPropertyName("widthMm")]
    public double? WidthMm { get; init; }

    [JsonPropertyName("heightMm")]
    public double? HeightMm { get; init; }

    [JsonPropertyName("gapMm")]
    public double? GapMm { get; init; }

    [JsonPropertyName("direction")]
    public int? Direction { get; init; }

    [JsonPropertyName("copies")]
    public int? Copies { get; init; }

    [JsonPropertyName("speed")]
    public int? Speed { get; init; }

    [JsonPropertyName("density")]
    public int? Density { get; init; }

    [JsonPropertyName("characterEncoding")]
    public string? CharacterEncoding { get; init; }

    [JsonPropertyName("codePage")]
    public string? CodePage { get; init; }

    [JsonPropertyName("elements")]
    public List<TsplElement> Elements { get; init; } = [];
}

internal sealed class TsplElement
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    // Text
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("base64Image")]
    public string? Base64Image { get; init; }

    [JsonPropertyName("font")]
    public string? Font { get; init; }

    [JsonPropertyName("rotation")]
    public int Rotation { get; init; }

    [JsonPropertyName("xMultiplier")]
    public int XMultiplier { get; init; } = 1;

    [JsonPropertyName("yMultiplier")]
    public int YMultiplier { get; init; } = 1;

    [JsonPropertyName("fontSize")]
    public int? FontSize { get; init; }

    [JsonPropertyName("bold")]
    public bool? Bold { get; init; }

    // Barcode / QR
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("barcodeType")]
    public string? BarcodeType { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("readable")]
    public int? Readable { get; init; }

    [JsonPropertyName("narrow")]
    public int? Narrow { get; init; }

    [JsonPropertyName("wide")]
    public int? Wide { get; init; }

    [JsonPropertyName("bitmapMode")]
    public int? BitmapMode { get; init; }

    // QR specific
    [JsonPropertyName("ecc")]
    public string? Ecc { get; init; }

    [JsonPropertyName("cellWidth")]
    public int? CellWidth { get; init; }

    // Box
    [JsonPropertyName("x2")]
    public int? X2 { get; init; }

    [JsonPropertyName("y2")]
    public int? Y2 { get; init; }

    [JsonPropertyName("lineWidth")]
    public int? LineWidth { get; init; }

    // Bar
    [JsonPropertyName("width")]
    public int? Width { get; init; }
}
