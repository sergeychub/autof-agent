using System.Text.Json.Serialization;

namespace WorkstationAgent.Ubuntu;

internal sealed class RegisterAgentRequest
{
    [JsonPropertyName("registrationToken")]
    public required string RegistrationToken { get; init; }

    [JsonPropertyName("proposedAgentName")]
    public required string ProposedAgentName { get; init; }

    [JsonPropertyName("machineName")]
    public required string MachineName { get; init; }

    [JsonPropertyName("userName")]
    public required string UserName { get; init; }

    [JsonPropertyName("deviceFingerprint")]
    public required string DeviceFingerprint { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }
}

internal sealed class RegisterAgentResponse
{
    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("agentName")]
    public required string AgentName { get; init; }

    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; init; }

    [JsonPropertyName("socketIoUrl")]
    public required string SocketIoUrl { get; init; }
}

internal sealed class PrinterTestRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }
}

internal sealed class PrintJobRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("contentType")]
    public string ContentType { get; init; } = "text";

    [JsonPropertyName("target")]
    public string? Target { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("base64Payload")]
    public string? Base64Payload { get; init; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; init; }

    [JsonPropertyName("documentName")]
    public string? DocumentName { get; init; }

    [JsonPropertyName("feedLinesAfterPrint")]
    public int? FeedLinesAfterPrint { get; init; }

    [JsonPropertyName("document")]
    public PrintDocument? Document { get; init; }

    [JsonPropertyName("tsplLabel")]
    public TsplLabel? TsplLabel { get; init; }
}

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

    [JsonPropertyName("base64Image")]
    public string? Base64Image { get; init; }

    [JsonPropertyName("maxWidthDots")]
    public int? MaxWidthDots { get; init; }

    [JsonPropertyName("threshold")]
    public int? Threshold { get; init; }

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

    [JsonPropertyName("ecc")]
    public string? Ecc { get; init; }

    [JsonPropertyName("cellWidth")]
    public int? CellWidth { get; init; }

    [JsonPropertyName("x2")]
    public int? X2 { get; init; }

    [JsonPropertyName("y2")]
    public int? Y2 { get; init; }

    [JsonPropertyName("lineWidth")]
    public int? LineWidth { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }
}

internal sealed class PrinterTestResult
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("printerName")]
    public required string PrinterName { get; init; }

    [JsonPropertyName("printedAt")]
    public string? PrintedAt { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed class PrintJobResult
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("printerName")]
    public required string PrinterName { get; init; }

    [JsonPropertyName("printedAt")]
    public string? PrintedAt { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("documentName")]
    public string? DocumentName { get; init; }
}

internal sealed class PosTerminalPurchaseRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "UAH";
}

internal sealed class PosTerminalCancelRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }
}

internal sealed class PosTerminalResult
{
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("responseCode")]
    public string? ResponseCode { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("merchantId")]
    public string? MerchantId { get; init; }

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("rrn")]
    public string? Rrn { get; init; }

    [JsonPropertyName("authCode")]
    public string? AuthCode { get; init; }

    [JsonPropertyName("maskedPan")]
    public string? MaskedPan { get; init; }

    [JsonPropertyName("rawResponse")]
    public Dictionary<string, object?>? RawResponse { get; init; }
}
