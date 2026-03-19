using System.Text.Json.Serialization;

namespace WorkstationAgent.Models;

internal sealed class PrintDocument
{
    [JsonPropertyName("blocks")]
    public List<PrintDocumentBlock> Blocks { get; init; } = [];
}

internal sealed class PrintDocumentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("align")]
    public string? Align { get; init; }

    [JsonPropertyName("emphasis")]
    public bool? Emphasis { get; init; }

    [JsonPropertyName("font")]
    public string? Font { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }

    [JsonPropertyName("base64Image")]
    public string? Base64Image { get; init; }

    [JsonPropertyName("imageFormat")]
    public string? ImageFormat { get; init; }

    [JsonPropertyName("maxWidthDots")]
    public int? MaxWidthDots { get; init; }

    [JsonPropertyName("threshold")]
    public byte? Threshold { get; init; }

    [JsonPropertyName("dither")]
    public string? Dither { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("barcodeType")]
    public string? BarcodeType { get; init; }

    [JsonPropertyName("moduleSize")]
    public int? ModuleSize { get; init; }

    [JsonPropertyName("errorCorrection")]
    public int? ErrorCorrection { get; init; }

    [JsonPropertyName("lines")]
    public int? Lines { get; init; }

    [JsonPropertyName("char")]
    public string? Character { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }
}
