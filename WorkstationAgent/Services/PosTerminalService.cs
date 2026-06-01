using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Models;

namespace WorkstationAgent.Services;

internal sealed class PosTerminalService
{
    private readonly AgentSettings _settings;
    private readonly FileLogger _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

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
            return await client.PurchaseAsync(request, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
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
            _operationLock.Release();
        }
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
