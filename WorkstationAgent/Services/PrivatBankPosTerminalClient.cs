using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Models;

namespace WorkstationAgent.Services;

internal sealed class PrivatBankPosTerminalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PosTerminalSettings _settings;
    private readonly FileLogger _logger;

    public PrivatBankPosTerminalClient(PosTerminalSettings settings, FileLogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<PosTerminalPurchaseResult> PingAsync(string requestId, CancellationToken cancellationToken)
    {
        var response = await SendHandshakeAsync(cancellationToken);
        return BuildResult(requestId, response);
    }

    public async Task<PosTerminalPurchaseResult> PurchaseAsync(PosTerminalPurchaseRequest request, CancellationToken cancellationToken)
    {
        var amount = NormalizeAmount(request.Amount);
        var response = await SendAsync(new
        {
            method = "Purchase",
            step = 0,
            @params = new
            {
                amount,
                discount = string.Empty,
                merchantId = _settings.MerchantId,
                facepay = "false",
                subMerchant = string.Empty
            }
        }, cancellationToken);

        return BuildResult(request.RequestId, response);
    }

    public async Task<PosTerminalPurchaseResult> GetMerchantListAsync(string requestId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(new
        {
            method = "ServiceMessage",
            step = 0,
            @params = new
            {
                msgType = "getMerchantList"
            }
        }, cancellationToken);

        return BuildResult(requestId, response);
    }

    private async Task<JsonElement> SendAsync(object request, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));

        using var client = new TcpClient();
        await client.ConnectAsync(_settings.Host, _settings.Port, timeoutCts.Token);
        await using var stream = client.GetStream();

        await SendHandshakeAsync(stream, timeoutCts.Token);
        await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token);
        return await SendAndReadAsync(stream, request, leadingNull: false, timeoutCts.Token);
    }

    private async Task<JsonElement> SendHandshakeAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));

        using var client = new TcpClient();
        await client.ConnectAsync(_settings.Host, _settings.Port, timeoutCts.Token);
        await using var stream = client.GetStream();

        return await SendHandshakeAsync(stream, timeoutCts.Token);
    }

    private async Task<JsonElement> SendHandshakeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        return await SendAndReadAsync(stream, new
        {
            method = "PingDevice",
            step = 0
        }, leadingNull: true, cancellationToken);
    }

    private async Task<JsonElement> SendAndReadAsync(
        NetworkStream stream,
        object request,
        bool leadingNull,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        _logger.Info($"POS terminal send: {payload}");
        var bytes = BuildDatagram(payload, leadingNull);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return await ReadDatagramAsync(stream, cancellationToken);
    }

    private async Task<JsonElement> ReadDatagramAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var responseBytes = new List<byte>();
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != 0)
                {
                    responseBytes.Add(buffer[i]);
                    continue;
                }

                if (responseBytes.Count == 0)
                {
                    continue;
                }

                return ParseResponse(responseBytes);
            }
        }

        if (responseBytes.Count > 0)
        {
            return ParseResponse(responseBytes);
        }

        throw new TimeoutException("POS terminal did not return a complete JSON response.");
    }

    private JsonElement ParseResponse(List<byte> responseBytes)
    {
        var value = Encoding.UTF8.GetString(responseBytes.ToArray());
        if (!TryParseJson(value, out var response))
        {
            throw new JsonException("POS terminal returned an invalid JSON response.");
        }

        _logger.Info($"POS terminal recv: {response.GetRawText()}");
        return response;
    }

    private static byte[] BuildDatagram(string payload, bool leadingNull)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var bytes = new byte[payloadBytes.Length + (leadingNull ? 2 : 1)];
        var offset = 0;
        if (leadingNull)
        {
            bytes[offset++] = 0;
        }

        Array.Copy(payloadBytes, 0, bytes, offset, payloadBytes.Length);
        bytes[^1] = 0;
        return bytes;
    }

    private static bool TryParseJson(string value, out JsonElement response)
    {
        response = default;
        var normalized = value.Trim('\0', '\r', '\n', ' ', '\t');
        var start = normalized.IndexOf('{');
        var end = normalized.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var json = normalized[start..(end + 1)];
        try
        {
            using var document = JsonDocument.Parse(json);
            response = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private PosTerminalPurchaseResult BuildResult(string requestId, JsonElement response)
    {
        var parameters = response.TryGetProperty("params", out var paramsElement)
            ? paramsElement
            : default;
        var responseCode =
            ReadString(parameters, "responseCode") ??
            ReadString(parameters, "code") ??
            ReadString(response, "responseCode") ??
            ReadString(response, "code");
        var message =
            ReadString(parameters, "message") ??
            ReadString(parameters, "msg") ??
            ReadString(parameters, "description") ??
            ReadString(parameters, "errorDescription") ??
            ReadString(response, "message") ??
            ReadString(response, "errorDescription") ??
            ReadString(response, "errorMessage");

        return new PosTerminalPurchaseResult
        {
            RequestId = requestId,
            Status = ResolveStatus(responseCode, message),
            ResponseCode = responseCode,
            Message = message,
            MerchantId = ReadString(parameters, "merchantId") ?? _settings.MerchantId,
            SessionId = ReadString(parameters, "SessionId") ?? ReadString(parameters, "sessionId"),
            Rrn = ReadString(parameters, "RRN") ?? ReadString(parameters, "rrn"),
            AuthCode = ReadString(parameters, "authCode") ?? ReadString(parameters, "approvalCode"),
            MaskedPan = ReadString(parameters, "maskedPan") ?? ReadString(parameters, "pan"),
            RawResponse = JsonSerializer.Deserialize<Dictionary<string, object?>>(response.GetRawText(), JsonOptions)
        };
    }

    private static string ResolveStatus(string? responseCode, string? message)
    {
        if (string.Equals(responseCode, "0000", StringComparison.OrdinalIgnoreCase))
        {
            return "approved";
        }

        if (string.Equals(responseCode, "1001", StringComparison.OrdinalIgnoreCase) ||
            (message?.Contains("cancel", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return "cancelled";
        }

        if (!string.IsNullOrWhiteSpace(responseCode))
        {
            return "declined";
        }

        return "error";
    }

    private static string NormalizeAmount(string value)
    {
        var normalized = value.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            throw new InvalidOperationException("POS terminal amount must be greater than zero.");
        }

        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
