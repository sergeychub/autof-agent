using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Models;

namespace WorkstationAgent.Services;

internal sealed class PosTerminalService
{
    private readonly AgentSettings _settings;
    private readonly FileLogger _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _activeOperationSync = new();
    private PrivatBankPosTerminalClient? _activeClient;
    private CancellationTokenSource? _activeOperationCts;
    private string? _activeRequestId;
    private string? _cancelledRequestId;

    public PosTerminalService(AgentSettings settings, FileLogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<PosTerminalPurchaseResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var client = CreateClient();
        return await client.PingAsync(Guid.NewGuid().ToString("N"), cancellationToken);
    }

    public async Task<PosTerminalPurchaseResult> PurchaseAsync(PosTerminalPurchaseRequest request, CancellationToken cancellationToken)
    {
        if (!_settings.PosTerminal.Enabled)
        {
            return Error(request.RequestId, "POS terminal integration is disabled.");
        }

        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return Error(request.RequestId, "POS terminal is busy.");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.PosTerminal.TimeoutSeconds + 5)));
            var client = CreateClient();
            lock (_activeOperationSync)
            {
                _activeClient = client;
                _activeOperationCts = timeoutCts;
                _activeRequestId = request.RequestId;
                _cancelledRequestId = null;
            }

            return await client.PurchaseAsync(request, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (string.Equals(_cancelledRequestId, request.RequestId, StringComparison.Ordinal))
            {
                return new PosTerminalPurchaseResult
                {
                    RequestId = request.RequestId,
                    Status = "cancelled",
                    ResponseCode = "1001",
                    Message = "POS terminal payment cancelled."
                };
            }

            return new PosTerminalPurchaseResult
            {
                RequestId = request.RequestId,
                Status = "timeout",
                Message = "POS terminal timeout."
            };
        }
        catch (Exception ex)
        {
            _logger.Error("POS terminal purchase failed.", ex);
            return Error(request.RequestId, ex.Message);
        }
        finally
        {
            lock (_activeOperationSync)
            {
                if (string.Equals(_activeRequestId, request.RequestId, StringComparison.Ordinal))
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

    public async Task<PosTerminalPurchaseResult> CancelAsync(string requestId, CancellationToken cancellationToken)
    {
        PrivatBankPosTerminalClient? client;
        CancellationTokenSource? activeOperationCts;

        lock (_activeOperationSync)
        {
            if (!string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
            {
                return new PosTerminalPurchaseResult
                {
                    RequestId = requestId,
                    Status = "cancelled",
                    ResponseCode = "1001",
                    Message = "POS terminal payment cancellation requested."
                };
            }

            client = _activeClient;
            activeOperationCts = _activeOperationCts;
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
            activeOperationCts?.Cancel();
        }

        return new PosTerminalPurchaseResult
        {
            RequestId = requestId,
            Status = "cancelled",
            ResponseCode = "1001",
            Message = "POS terminal payment cancelled."
        };
    }

    private PrivatBankPosTerminalClient CreateClient()
    {
        return new PrivatBankPosTerminalClient(_settings.PosTerminal, _logger);
    }

    private static PosTerminalPurchaseResult Error(string requestId, string message)
    {
        return new PosTerminalPurchaseResult
        {
            RequestId = requestId,
            Status = "error",
            Message = message
        };
    }
}
