using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace WorkstationAgent.Ubuntu;

internal sealed class PosTerminalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AgentSettings _settings;
    private readonly AgentLogger _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _activeOperationSync = new();
    private PrivatBankPosTerminalClient? _activeClient;
    private CancellationTokenSource? _activeOperationCts;
    private string? _activeRequestId;
    private string? _cancelledRequestId;

    public PosTerminalService(AgentSettings settings, AgentLogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task<PosTerminalResult> TestConnectionAsync(CancellationToken cancellationToken) =>
        new PrivatBankPosTerminalClient(_settings.PosTerminal, _logger)
            .PingAsync(Guid.NewGuid().ToString("N"), cancellationToken);

    public async Task<PosTerminalResult> PurchaseAsync(
        PosTerminalPurchaseRequest request,
        CancellationToken cancellationToken)
    {
        var requestId = string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId;
        if (!_settings.PosTerminal.Enabled)
        {
            return Error(requestId, "POS terminal integration is disabled.");
        }
        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return Error(requestId, "POS terminal is busy.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.PosTerminal.TimeoutSeconds + 5)));
            var client = new PrivatBankPosTerminalClient(_settings.PosTerminal, _logger);
            lock (_activeOperationSync)
            {
                _activeClient = client;
                _activeOperationCts = timeout;
                _activeRequestId = requestId;
                _cancelledRequestId = null;
            }
            return await client.PurchaseAsync(requestId, request.Amount, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return string.Equals(_cancelledRequestId, requestId, StringComparison.Ordinal)
                ? new PosTerminalResult
                {
                    RequestId = requestId,
                    Status = "cancelled",
                    ResponseCode = "1001",
                    Message = "POS terminal payment cancelled."
                }
                : new PosTerminalResult
                {
                    RequestId = requestId,
                    Status = "timeout",
                    Message = "POS terminal timeout."
                };
        }
        catch (Exception ex)
        {
            _logger.Error("POS terminal purchase failed.", ex);
            return Error(requestId, ex.Message);
        }
        finally
        {
            lock (_activeOperationSync)
            {
                if (string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
                {
                    _activeClient = null;
                    _activeOperationCts = null;
                    _activeRequestId = null;
                    _cancelledRequestId = null;
                }
            }
            _operationLock.Release();
        }
    }

    public async Task<PosTerminalResult> CancelAsync(string requestId, CancellationToken cancellationToken)
    {
        PrivatBankPosTerminalClient? client;
        CancellationTokenSource? activeOperation;
        lock (_activeOperationSync)
        {
            if (!string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
            {
                return new PosTerminalResult
                {
                    RequestId = requestId,
                    Status = "cancelled",
                    ResponseCode = "1001",
                    Message = "POS terminal payment cancellation requested."
                };
            }
            client = _activeClient;
            activeOperation = _activeOperationCts;
            _cancelledRequestId = requestId;
        }

        try
        {
            if (client is not null)
            {
                await client.InterruptAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("POS terminal interrupt failed.", ex);
        }
        finally
        {
            activeOperation?.Cancel();
        }

        return new PosTerminalResult
        {
            RequestId = requestId,
            Status = "cancelled",
            ResponseCode = "1001",
            Message = "POS terminal payment cancelled."
        };
    }

    private static PosTerminalResult Error(string requestId, string message) => new()
    {
        RequestId = requestId,
        Status = "error",
        Message = message
    };

    private sealed class PrivatBankPosTerminalClient
    {
        private readonly PosTerminalSettings _settings;
        private readonly AgentLogger _logger;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private NetworkStream? _activeStream;

        public PrivatBankPosTerminalClient(PosTerminalSettings settings, AgentLogger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public async Task<PosTerminalResult> PingAsync(string requestId, CancellationToken cancellationToken)
        {
            var response = await SendHandshakeAsync(cancellationToken);
            return BuildResult(requestId, response);
        }

        public async Task<PosTerminalResult> PurchaseAsync(
            string requestId,
            string amountValue,
            CancellationToken cancellationToken)
        {
            var amount = NormalizeAmount(amountValue);
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
            return BuildResult(requestId, response);
        }

        public async Task InterruptAsync(CancellationToken cancellationToken)
        {
            var request = new
            {
                method = "ServiceMessage",
                step = 0,
                @params = new { msgType = "interrupt" }
            };
            var stream = _activeStream;
            if (stream is not null)
            {
                await WriteDatagramAsync(stream, request, false, cancellationToken);
                return;
            }
            await SendAsync(request, cancellationToken);
        }

        private async Task<JsonElement> SendAsync(object request, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));
            using var client = new TcpClient();
            await client.ConnectAsync(_settings.Host, _settings.Port, timeout.Token);
            await using var stream = client.GetStream();
            _activeStream = stream;
            try
            {
                await SendAndReadAsync(stream, new { method = "PingDevice", step = 0 }, true, timeout.Token);
                await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
                return await SendAndReadAsync(stream, request, false, timeout.Token);
            }
            finally
            {
                if (ReferenceEquals(_activeStream, stream))
                {
                    _activeStream = null;
                }
            }
        }

        private async Task<JsonElement> SendHandshakeAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));
            using var client = new TcpClient();
            await client.ConnectAsync(_settings.Host, _settings.Port, timeout.Token);
            await using var stream = client.GetStream();
            return await SendAndReadAsync(stream, new { method = "PingDevice", step = 0 }, true, timeout.Token);
        }

        private async Task<JsonElement> SendAndReadAsync(
            NetworkStream stream,
            object request,
            bool leadingNull,
            CancellationToken cancellationToken)
        {
            await WriteDatagramAsync(stream, request, leadingNull, cancellationToken);
            return await ReadDatagramAsync(stream, cancellationToken);
        }

        private async Task WriteDatagramAsync(
            NetworkStream stream,
            object request,
            bool leadingNull,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(request, JsonOptions);
            _logger.Info($"POS terminal send: {payload}");
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var datagram = new byte[payloadBytes.Length + (leadingNull ? 2 : 1)];
            var offset = leadingNull ? 1 : 0;
            Buffer.BlockCopy(payloadBytes, 0, datagram, offset, payloadBytes.Length);

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await stream.WriteAsync(datagram, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
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
                    }
                    else if (responseBytes.Count > 0)
                    {
                        return ParseResponse(responseBytes);
                    }
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
            var raw = Encoding.UTF8.GetString(responseBytes.ToArray()).Trim('\0', '\r', '\n', ' ', '\t');
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                throw new JsonException("POS terminal returned an invalid JSON response.");
            }
            using var document = JsonDocument.Parse(raw[start..(end + 1)]);
            var response = document.RootElement.Clone();
            _logger.Info($"POS terminal recv: {response.GetRawText()}");
            return response;
        }

        private PosTerminalResult BuildResult(string requestId, JsonElement response)
        {
            var parameters = response.TryGetProperty("params", out var value) ? value : default;
            var responseCode = ReadString(parameters, "responseCode") ?? ReadString(parameters, "code") ??
                ReadString(response, "responseCode") ?? ReadString(response, "code");
            var message = ReadString(parameters, "message") ?? ReadString(parameters, "msg") ??
                ReadString(parameters, "description") ?? ReadString(parameters, "errorDescription") ??
                ReadString(response, "message") ?? ReadString(response, "errorDescription") ??
                ReadString(response, "errorMessage");
            return new PosTerminalResult
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
            return string.IsNullOrWhiteSpace(responseCode) ? "error" : "declined";
        }

        private static string NormalizeAmount(string value)
        {
            if (!decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
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
}
