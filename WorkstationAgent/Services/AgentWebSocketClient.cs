using System.Text.Json;
using SocketIOClient;
using SocketIOClient.Transport;
using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Models;

namespace WorkstationAgent.Services;

internal sealed class AgentWebSocketClient : IAsyncDisposable
{
    private readonly AgentSettings _settings;
    private readonly FileLogger _logger;
    private readonly ThermalPrinterService _thermalPrinterService;
    private readonly Func<string> _lastUpdateStatusProvider;
    private readonly Func<string> _agentVersionProvider;
    private SocketIOClient.SocketIO? _socket;
    private TaskCompletionSource _connectedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AgentWebSocketClient(
        AgentSettings settings,
        FileLogger logger,
        ThermalPrinterService thermalPrinterService,
        Func<string> lastUpdateStatusProvider,
        Func<string> agentVersionProvider)
    {
        _settings = settings;
        _logger = logger;
        _thermalPrinterService = thermalPrinterService;
        _lastUpdateStatusProvider = lastUpdateStatusProvider;
        _agentVersionProvider = agentVersionProvider;
    }

    public event Action<string>? StatusChanged;
    public event Action<string>? MessageReceived;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
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
                UpdateStatus("Reconnect scheduled");
                _logger.Error("WebSocket loop failed.", ex);
                await Task.Delay(TimeSpan.FromSeconds(_settings.ReconnectDelaySeconds), cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is null)
        {
            return;
        }

        await _socket.DisconnectAsync();
        _socket.Dispose();
        _socket = null;
    }

    private async Task ConnectAndProcessAsync(CancellationToken cancellationToken)
    {
        _connectedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var socket = CreateSocket();
        _socket = socket;

        UpdateStatus("Connecting");
        await socket.ConnectAsync();
        await WaitForConnectionAckAsync(cancellationToken);

        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pingTask = PingLoopAsync(socket, pingCts.Token);
        await WaitForDisconnectAsync(socket, pingCts.Token);
        pingCts.Cancel();

        try
        {
            await pingTask;
        }
        catch (OperationCanceledException)
        {
        }

        UpdateStatus("Disconnected");
        _logger.Info("Socket.IO disconnected.");
    }

    private SocketIOClient.SocketIO CreateSocket()
    {
        var socket = new SocketIOClient.SocketIO(_settings.SocketIoUrl, new SocketIOOptions
        {
            Transport = TransportProtocol.WebSocket,
            Reconnection = false,
            Auth = new
            {
                deviceId = _settings.DeviceId,
                agentName = _settings.AgentName,
                machineName = Environment.MachineName,
                userName = Environment.UserName,
                apiKey = _settings.ApiKey,
                agentVersion = _agentVersionProvider(),
                updateChannel = _settings.UpdateChannel,
                lastUpdateStatus = _lastUpdateStatusProvider()
            }
        });

        socket.OnConnected += (_, _) =>
        {
            _logger.Info($"Socket.IO transport connected to {_settings.SocketIoUrl}.");
        };

        socket.OnDisconnected += (_, reason) =>
        {
            _logger.Info($"Socket.IO transport disconnected: {reason}");
        };

        socket.OnError += (_, error) =>
        {
            _logger.Error($"Socket.IO error: {error}");
        };

        socket.On("agent:connected", response =>
        {
            var payload = response.GetValue<JsonElement>();
            var message = payload.GetRawText();
            _logger.Info($"Received agent:connected: {message}");
            MessageReceived?.Invoke(message);
            UpdateStatus("Connected");
            _connectedSignal.TrySetResult();
        });

        socket.On("printer:test", response =>
        {
            _ = HandlePrinterTestAsync(socket, response);
        });

        socket.On("printer:job", response =>
        {
            _ = HandlePrintJobAsync(socket, response);
        });

        return socket;
    }

    private async Task PingLoopAsync(SocketIOClient.SocketIO socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.Connected)
        {
            var payload = new
            {
                agentName = _settings.AgentName,
                machineName = Environment.MachineName,
                userName = Environment.UserName,
                agentVersion = _agentVersionProvider(),
                updateChannel = _settings.UpdateChannel,
                lastUpdateStatus = _lastUpdateStatusProvider(),
                timestamp = DateTimeOffset.UtcNow
            };

            await socket.EmitAsync("agent:heartbeat", payload);
            _logger.Info($"Heartbeat sent: {JsonSerializer.Serialize(payload)}");
            await Task.Delay(TimeSpan.FromSeconds(_settings.PingIntervalSeconds), cancellationToken);
        }
    }

    private async Task WaitForConnectionAckAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => _connectedSignal.TrySetCanceled(cancellationToken));
        await _connectedSignal.Task;
        _logger.Info($"Connected to {_settings.SocketIoUrl}.");
    }

    private static async Task WaitForDisconnectAsync(SocketIOClient.SocketIO socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.Connected)
        {
            await Task.Delay(500, cancellationToken);
        }
    }

    private void UpdateStatus(string status)
    {
        StatusChanged?.Invoke(status);
        _logger.Info(status);
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

            _logger.Info($"Received printer:test command. RequestId={requestId}");
            var result = _thermalPrinterService.PrintTestReceipt(requestId);
            await socket.EmitAsync("printer:test:result", result);
            _logger.Info($"printer:test:result sent. Success={result.Success}, Printer={result.PrinterName}");
        }
        catch (Exception ex)
        {
            var fallbackRequestId = request?.RequestId;
            if (string.IsNullOrWhiteSpace(fallbackRequestId))
            {
                fallbackRequestId = Guid.NewGuid().ToString("N");
            }

            _logger.Error("Failed to process printer:test command.", ex);

            await socket.EmitAsync("printer:test:result", new PrinterTestResult
            {
                RequestId = fallbackRequestId,
                Success = false,
                PrinterName = _thermalPrinterService.GetPrinterNameForRole(PrinterRoles.Receipt),
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
            var requestId = string.IsNullOrWhiteSpace(request.RequestId)
                ? Guid.NewGuid().ToString("N")
                : request.RequestId;

            request = new PrintJobRequest
            {
                RequestId = requestId,
                ContentType = request.ContentType,
                Target = request.Target,
                Text = request.Text,
                Base64Payload = request.Base64Payload,
                Encoding = request.Encoding,
                DocumentName = request.DocumentName,
                FeedLinesAfterPrint = request.FeedLinesAfterPrint,
                Document = request.Document,
                TsplLabel = request.TsplLabel
            };

            _logger.Info(
                $"Received printer:job command. RequestId={requestId}, Target={request.Target ?? "auto"}, ContentType={request.ContentType}, TextLength={request.Text?.Length ?? 0}, Base64Length={request.Base64Payload?.Length ?? 0}, Blocks={request.Document?.Blocks.Count ?? 0}, Document={request.DocumentName ?? "n/a"}");
            var result = _thermalPrinterService.PrintJob(request);
            await socket.EmitAsync("printer:job:result", result);
            _logger.Info($"printer:job:result sent. Success={result.Success}, Printer={result.PrinterName}, Document={result.DocumentName}");
        }
        catch (Exception ex)
        {
            var fallbackRequestId = request?.RequestId;
            if (string.IsNullOrWhiteSpace(fallbackRequestId))
            {
                fallbackRequestId = Guid.NewGuid().ToString("N");
            }

            _logger.Error("Failed to process printer:job command.", ex);

            await socket.EmitAsync("printer:job:result", new PrintJobResult
            {
                RequestId = fallbackRequestId,
                Success = false,
                PrinterName = _thermalPrinterService.GetPrinterNameForRequest(request),
                Error = ex.Message,
                DocumentName = request?.DocumentName
            });
        }
    }
}
