namespace WorkstationAgent.Ubuntu;

internal sealed class PrinterService
{
    private readonly AgentSettings _settings;
    private readonly AgentLogger _logger;
    private readonly PrintPayloadBuilder _payloadBuilder;
    private readonly PrinterTransportClient _transportClient;
    private readonly SemaphoreSlim _printLock = new(1, 1);

    public PrinterService(
        AgentSettings settings,
        AgentLogger logger,
        PrintPayloadBuilder payloadBuilder,
        PrinterTransportClient? transportClient = null)
    {
        _settings = settings;
        _logger = logger;
        _payloadBuilder = payloadBuilder;
        _transportClient = transportClient ?? new PrinterTransportClient();
    }

    public async Task<PrinterTestResult> PrintReceiptTestAsync(string requestId, CancellationToken cancellationToken)
    {
        var endpoint = _settings.ReceiptPrinter;
        var destination = DestinationName(endpoint);
        try
        {
            await _printLock.WaitAsync(cancellationToken);
            try
            {
                EnsureEnabled(endpoint, "Receipt printer");
                await _transportClient.SendAsync(
                    endpoint,
                    _payloadBuilder.BuildReceiptTest(_settings),
                    "Avtoforward Agent Ubuntu test receipt",
                    cancellationToken);
            }
            finally
            {
                _printLock.Release();
            }

            _logger.Info($"Test receipt sent to '{destination}'.");
            return new PrinterTestResult
            {
                RequestId = requestId,
                Success = true,
                PrinterName = destination,
                PrintedAt = DateTimeOffset.UtcNow.ToString("O")
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Test receipt failed for '{destination}'.", ex);
            return new PrinterTestResult
            {
                RequestId = requestId,
                Success = false,
                PrinterName = destination,
                Error = ex.Message
            };
        }
    }

    public async Task<PrintJobResult> PrintLabelTestAsync(string requestId, CancellationToken cancellationToken)
    {
        const string documentName = "Avtoforward Agent Ubuntu test label";
        var endpoint = _settings.LabelPrinter;
        var destination = DestinationName(endpoint);
        try
        {
            await _printLock.WaitAsync(cancellationToken);
            try
            {
                EnsureEnabled(endpoint, "Label printer");
                var payload = await _payloadBuilder.BuildLabelTestAsync(_settings, cancellationToken);
                await _transportClient.SendAsync(endpoint, payload, documentName, cancellationToken);
            }
            finally
            {
                _printLock.Release();
            }

            return new PrintJobResult
            {
                RequestId = requestId,
                Success = true,
                PrinterName = destination,
                PrintedAt = DateTimeOffset.UtcNow.ToString("O"),
                DocumentName = documentName
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Test label failed for '{destination}'.", ex);
            return new PrintJobResult
            {
                RequestId = requestId,
                Success = false,
                PrinterName = destination,
                Error = ex.Message,
                DocumentName = documentName
            };
        }
    }

    public async Task<PrintJobResult> PrintAsync(PrintJobRequest request, CancellationToken cancellationToken)
    {
        var requestId = string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId;
        var endpoint = ResolveEndpoint(request);
        var destination = DestinationName(endpoint);
        var documentName = string.IsNullOrWhiteSpace(request.DocumentName)
            ? $"Avtoforward-{requestId}"
            : request.DocumentName;
        try
        {
            await _printLock.WaitAsync(cancellationToken);
            try
            {
                EnsureEnabled(endpoint, PrinterRoles.IsLabel(request.Target) ? "Label printer" : "Receipt printer");
                var payload = await _payloadBuilder.BuildAsync(_settings, endpoint, request, cancellationToken);
                await _transportClient.SendAsync(endpoint, payload, documentName, cancellationToken);
            }
            finally
            {
                _printLock.Release();
            }

            return new PrintJobResult
            {
                RequestId = requestId,
                Success = true,
                PrinterName = destination,
                PrintedAt = DateTimeOffset.UtcNow.ToString("O"),
                DocumentName = documentName
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Print job '{requestId}' failed for '{destination}'.", ex);
            return new PrintJobResult
            {
                RequestId = requestId,
                Success = false,
                PrinterName = destination,
                Error = ex.Message,
                DocumentName = documentName
            };
        }
    }

    public async Task<bool> OpenCashDrawerAsync(CancellationToken cancellationToken)
    {
        const string documentName = "Cash drawer hotkey F6";
        var endpoint = _settings.ReceiptPrinter;
        var destination = DestinationName(endpoint);
        try
        {
            await _printLock.WaitAsync(cancellationToken);
            try
            {
                EnsureEnabled(endpoint, "Receipt printer");
                await _transportClient.SendAsync(
                    endpoint,
                    [0x1B, 0x70, 0x00, 0x19, 0xFA],
                    documentName,
                    cancellationToken);
            }
            finally
            {
                _printLock.Release();
            }

            _logger.Info($"Cash drawer opened by local F6 hotkey through '{destination}'.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Cash drawer F6 hotkey failed for '{destination}'.", ex);
            return false;
        }
    }

    public string GetPrinterName(PrintJobRequest? request = null) =>
        DestinationName(request is null ? _settings.ReceiptPrinter : ResolveEndpoint(request));

    private PrinterEndpointSettings ResolveEndpoint(PrintJobRequest request) =>
        PrinterRoles.IsLabel(request.Target) ||
        (string.IsNullOrWhiteSpace(request.Target) &&
         string.Equals(request.ContentType, "tspl-label", StringComparison.OrdinalIgnoreCase))
            ? _settings.LabelPrinter
            : _settings.ReceiptPrinter;

    private static void EnsureEnabled(PrinterEndpointSettings endpoint, string displayName)
    {
        if (!endpoint.Enabled)
        {
            throw new InvalidOperationException($"{displayName} is disabled.");
        }
    }

    private static string DestinationName(PrinterEndpointSettings endpoint)
    {
        if (string.Equals(endpoint.TransportMode, PrinterTransportMode.Device, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(endpoint.DeviceSerial)
                ? $"device:serial={endpoint.DeviceSerial.Trim()}"
                : endpoint.DevicePath ?? "device:not-configured";
        }
        if (string.Equals(endpoint.TransportMode, PrinterTransportMode.Tcp, StringComparison.OrdinalIgnoreCase))
        {
            return $"tcp://{endpoint.Host ?? "not-configured"}:{endpoint.Port}";
        }
        return $"cups:{endpoint.PrinterName}";
    }

}
