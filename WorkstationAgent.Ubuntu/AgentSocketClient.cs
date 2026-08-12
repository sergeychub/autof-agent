using System.Text.Json;
using SocketIOClient;
using SocketIOClient.Transport;

namespace WorkstationAgent.Ubuntu;

internal sealed class AgentSocketClient : IAsyncDisposable
{
    private readonly AgentSettings _settings;
    private readonly AgentIdentity _identity;
    private readonly AgentLogger _logger;
    private readonly PrinterService _printerService;
    private readonly PosTerminalService _posTerminalService;
    private SocketIOClient.SocketIO? _activeSocket;
    private TaskCompletionSource _connectionAcknowledged = NewCompletionSource();
    private CancellationToken _lifetimeToken;

    public AgentSocketClient(
        AgentSettings settings,
        AgentIdentity identity,
        AgentLogger logger,
        PrinterService printerService,
        PosTerminalService posTerminalService)
    {
        _settings = settings;
        _identity = identity;
        _logger = logger;
        _printerService = printerService;
        _posTerminalService = posTerminalService;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _lifetimeToken = cancellationToken;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndProcessAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Socket.IO loop failed; reconnect scheduled.", ex);
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(1, _settings.ReconnectDelaySeconds)),
                    cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_activeSocket is null)
        {
            return;
        }
        await _activeSocket.DisconnectAsync();
        _activeSocket.Dispose();
        _activeSocket = null;
    }

    private async Task ConnectAndProcessAsync(CancellationToken cancellationToken)
    {
        _connectionAcknowledged = NewCompletionSource();
        using var socket = CreateSocket();
        _activeSocket = socket;
        try
        {
            _logger.Info($"Connecting to {_identity.SocketIoUrl}.");
            await socket.ConnectAsync();

            using var acknowledgmentTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acknowledgmentTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _connectionAcknowledged.Task.WaitAsync(acknowledgmentTimeout.Token);

            using var connectedLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeatTask = HeartbeatLoopAsync(socket, connectedLifetime.Token);
            while (!cancellationToken.IsCancellationRequested && socket.Connected)
            {
                await Task.Delay(500, cancellationToken);
            }
            connectedLifetime.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
            _logger.Info("Socket.IO disconnected.");
        }
        finally
        {
            if (ReferenceEquals(_activeSocket, socket))
            {
                _activeSocket = null;
            }
        }
    }

    private SocketIOClient.SocketIO CreateSocket()
    {
        var socket = new SocketIOClient.SocketIO(_identity.SocketIoUrl, new SocketIOOptions
        {
            Transport = TransportProtocol.WebSocket,
            Reconnection = false,
            Auth = new
            {
                deviceId = _identity.DeviceId,
                agentName = _identity.AgentName,
                machineName = Environment.MachineName,
                userName = AgentRuntime.UserName(_settings),
                apiKey = _identity.ApiKey,
                agentVersion = AgentRuntime.Version,
                updateChannel = _settings.UpdateChannel,
                lastUpdateStatus = "manual",
                runtime = "linux-x64"
            }
        });

        socket.OnConnected += (_, _) => _logger.Info("Socket.IO transport connected.");
        socket.OnDisconnected += (_, reason) =>
        {
            _logger.Info($"Socket.IO transport disconnected: {reason}");
            _connectionAcknowledged.TrySetException(
                new InvalidOperationException($"Socket.IO disconnected before agent acknowledgement: {reason}"));
        };
        socket.OnError += (_, error) => _logger.Error($"Socket.IO error: {error}");

        socket.On("agent:connected", response =>
        {
            var payload = response.GetValue<JsonElement>();
            _logger.Info($"Agent connection accepted: {payload.GetRawText()}");
            _connectionAcknowledged.TrySetResult();
        });
        socket.On("agent:error", response =>
        {
            var payload = response.GetValue<JsonElement>();
            var message = payload.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString()
                : payload.GetRawText();
            _connectionAcknowledged.TrySetException(
                new UnauthorizedAccessException(message ?? "Agent authentication failed."));
        });
        socket.On("printer:test", response => _ = HandlePrinterTestAsync(socket, response));
        socket.On("printer:job", response => _ = HandlePrintJobAsync(socket, response));
        socket.On("pos:terminal:purchase", response => _ = HandlePosPurchaseAsync(socket, response));
        socket.On("pos:terminal:cancel", response => _ = HandlePosCancelAsync(socket, response));
        return socket;
    }

    private async Task HeartbeatLoopAsync(SocketIOClient.SocketIO socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.Connected)
        {
            await socket.EmitAsync("agent:heartbeat", new
            {
                agentName = _identity.AgentName,
                machineName = Environment.MachineName,
                userName = AgentRuntime.UserName(_settings),
                agentVersion = AgentRuntime.Version,
                updateChannel = _settings.UpdateChannel,
                lastUpdateStatus = "manual",
                timestamp = DateTimeOffset.UtcNow
            });
            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, _settings.HeartbeatIntervalSeconds)),
                cancellationToken);
        }
    }

    private async Task HandlePrinterTestAsync(SocketIOClient.SocketIO socket, SocketIOResponse response)
    {
        PrinterTestRequest? request = null;
        try
        {
            request = response.GetValue<PrinterTestRequest>();
            var requestId = string.IsNullOrWhiteSpace(request.RequestId)
                ? Guid.NewGuid().ToString("N")
                : request.RequestId;
            _logger.Info($"Received printer:test. RequestId={requestId}");
            var result = await _printerService.PrintReceiptTestAsync(requestId, _lifetimeToken);
            await socket.EmitAsync("printer:test:result", result);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to process printer:test.", ex);
            await socket.EmitAsync("printer:test:result", new PrinterTestResult
            {
                RequestId = request?.RequestId ?? Guid.NewGuid().ToString("N"),
                Success = false,
                PrinterName = _printerService.GetPrinterName(),
                Error = ex.Message
            });
        }
    }

    private async Task HandlePrintJobAsync(SocketIOClient.SocketIO socket, SocketIOResponse response)
    {
        PrintJobRequest? request = null;
        try
        {
            request = response.GetValue<PrintJobRequest>();
            _logger.Info(
                $"Received printer:job. RequestId={request.RequestId ?? "n/a"}, Target={request.Target ?? "auto"}, ContentType={request.ContentType}, Document={request.DocumentName ?? "n/a"}");
            var result = await _printerService.PrintAsync(request, _lifetimeToken);
            await socket.EmitAsync("printer:job:result", result);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to process printer:job.", ex);
            await socket.EmitAsync("printer:job:result", new PrintJobResult
            {
                RequestId = request?.RequestId ?? Guid.NewGuid().ToString("N"),
                Success = false,
                PrinterName = _printerService.GetPrinterName(request),
                Error = ex.Message,
                DocumentName = request?.DocumentName
            });
        }
    }

    private async Task HandlePosPurchaseAsync(SocketIOClient.SocketIO socket, SocketIOResponse response)
    {
        PosTerminalPurchaseRequest? request = null;
        try
        {
            request = response.GetValue<PosTerminalPurchaseRequest>();
            _logger.Info($"Received pos:terminal:purchase. RequestId={request.RequestId ?? "n/a"}");
            var result = await _posTerminalService.PurchaseAsync(request, _lifetimeToken);
            await socket.EmitAsync("pos:terminal:result", result);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to process pos:terminal:purchase.", ex);
            await socket.EmitAsync("pos:terminal:result", new PosTerminalResult
            {
                RequestId = request?.RequestId ?? Guid.NewGuid().ToString("N"),
                Status = "error",
                Message = ex.Message
            });
        }
    }

    private async Task HandlePosCancelAsync(SocketIOClient.SocketIO socket, SocketIOResponse response)
    {
        PosTerminalCancelRequest? request = null;
        try
        {
            request = response.GetValue<PosTerminalCancelRequest>();
            var requestId = string.IsNullOrWhiteSpace(request.RequestId)
                ? Guid.NewGuid().ToString("N")
                : request.RequestId;
            _logger.Info($"Received pos:terminal:cancel. RequestId={requestId}");
            var result = await _posTerminalService.CancelAsync(requestId, _lifetimeToken);
            await socket.EmitAsync("pos:terminal:result", result);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to process pos:terminal:cancel.", ex);
            await socket.EmitAsync("pos:terminal:result", new PosTerminalResult
            {
                RequestId = request?.RequestId ?? Guid.NewGuid().ToString("N"),
                Status = "error",
                Message = ex.Message
            });
        }
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
