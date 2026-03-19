using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;
using WorkstationAgent.Models;
using WorkstationAgent.Printing;

namespace WorkstationAgent.Services;

internal sealed class ThermalPrinterService
{
    private readonly AgentSettings _settings;
    private readonly AgentPaths _paths;
    private readonly FileLogger _logger;
    private readonly PrinterDiscoveryService _discoveryService;
    private readonly EscPosTestReceiptBuilder _receiptBuilder;
    private readonly EscPosPayloadBuilder _payloadBuilder;
    private readonly EscPosImageRenderer _imageRenderer;
    private readonly EscPosDocumentBuilder _documentBuilder;
    private readonly PrinterTransportResolver _transportResolver;

    public ThermalPrinterService(
        AgentSettings settings,
        AgentPaths paths,
        FileLogger logger,
        PrinterDiscoveryService discoveryService,
        EscPosTestReceiptBuilder receiptBuilder,
        EscPosPayloadBuilder payloadBuilder,
        EscPosImageRenderer imageRenderer,
        EscPosDocumentBuilder documentBuilder,
        PrinterTransportResolver transportResolver)
    {
        _settings = settings;
        _paths = paths;
        _logger = logger;
        _discoveryService = discoveryService;
        _receiptBuilder = receiptBuilder;
        _payloadBuilder = payloadBuilder;
        _imageRenderer = imageRenderer;
        _documentBuilder = documentBuilder;
        _transportResolver = transportResolver;
    }

    public bool IsConfigured => _settings.PrinterEnabled && !string.IsNullOrWhiteSpace(_settings.PrinterName);

    public string PrinterName => _settings.PrinterName;

    public string GetAvailabilityStatus()
    {
        if (!_settings.PrinterEnabled)
        {
            return "Printer disabled in config";
        }

        if (string.IsNullOrWhiteSpace(_settings.PrinterName))
        {
            return "Printer name is not configured";
        }

        var transport = _transportResolver.Resolve(_settings);
        var probe = transport.Probe(_settings);
        return probe.Message;
    }

    public PrinterTestResult PrintTestReceipt(string requestId)
    {
        var printerName = _settings.PrinterName;

        try
        {
            if (!_settings.PrinterEnabled)
            {
                throw new InvalidOperationException("Printer is disabled in agentsettings.json.");
            }

            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new InvalidOperationException("PrinterName is not configured in agentsettings.json.");
            }

            if (!_discoveryService.PrinterExists(printerName))
            {
                var installed = string.Join(", ", _discoveryService.GetInstalledPrinters());
                throw new InvalidOperationException($"Printer '{printerName}' was not found. Installed printers: {installed}");
            }

            var bytes = _receiptBuilder.Build(_settings);
            SendBytes(bytes, "WorkstationAgent Test Receipt");
            var printedAt = DateTimeOffset.UtcNow.ToString("O");

            _logger.Info($"Test receipt sent to printer '{printerName}'.");

            return new PrinterTestResult
            {
                RequestId = requestId,
                Success = true,
                PrinterName = printerName,
                PrintedAt = printedAt
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Test receipt failed for printer '{printerName}'.", ex);

            return new PrinterTestResult
            {
                RequestId = requestId,
                Success = false,
                PrinterName = printerName ?? string.Empty,
                Error = ex.Message
            };
        }
    }

    public PrintJobResult PrintLogoTest(string requestId)
    {
        var documentName = "WorkstationAgent Logo Test";
        var printerName = ResolveDestinationName();

        try
        {
            EnsurePrinterReady(_settings.PrinterName);

            if (!File.Exists(_paths.LogoPngPath))
            {
                throw new FileNotFoundException("Bundled logo.png was not found.", _paths.LogoPngPath);
            }

            var bytes = _imageRenderer.BuildLogoTest(_settings, _paths.LogoPngPath);
            SendBytes(bytes, documentName);
            var printedAt = DateTimeOffset.UtcNow.ToString("O");

            _logger.Info($"Logo test '{documentName}' sent to '{printerName}'.");

            return new PrintJobResult
            {
                RequestId = requestId,
                Success = true,
                PrinterName = printerName,
                PrintedAt = printedAt,
                DocumentName = documentName
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Logo test '{documentName}' failed for '{printerName}'.", ex);

            return new PrintJobResult
            {
                RequestId = requestId,
                Success = false,
                PrinterName = printerName,
                Error = ex.Message,
                DocumentName = documentName
            };
        }
    }

    public PrintJobResult PrintJob(PrintJobRequest request)
    {
        var printerName = ResolveDestinationName();
        var documentName = string.IsNullOrWhiteSpace(request.DocumentName)
            ? "WorkstationAgent Print Job"
            : request.DocumentName;

        try
        {
            EnsurePrinterReady(_settings.PrinterName);
            var bytes = BuildPayload(request);
            SendBytes(bytes, documentName);
            var printedAt = DateTimeOffset.UtcNow.ToString("O");

            _logger.Info($"Print job '{documentName}' sent to printer '{printerName}'.");

            return new PrintJobResult
            {
                RequestId = request.RequestId,
                Success = true,
                PrinterName = printerName!,
                PrintedAt = printedAt,
                DocumentName = documentName
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Print job '{documentName}' failed for printer '{printerName}'.", ex);

            return new PrintJobResult
            {
                RequestId = request.RequestId,
                Success = false,
                PrinterName = printerName ?? string.Empty,
                Error = ex.Message,
                DocumentName = documentName
            };
        }
    }

    private void EnsurePrinterReady(string? printerName)
    {
        if (!_settings.PrinterEnabled)
        {
            throw new InvalidOperationException("Printer is disabled in agentsettings.json.");
        }

        if (!PrinterTransportMode.IsDirectUsb(_settings.TransportMode) && string.IsNullOrWhiteSpace(printerName))
        {
            throw new InvalidOperationException("PrinterName is not configured in agentsettings.json.");
        }

        if (!PrinterTransportMode.IsDirectUsb(_settings.TransportMode) && !_discoveryService.PrinterExists(printerName))
        {
            var installed = string.Join(", ", _discoveryService.GetInstalledPrinters());
            throw new InvalidOperationException($"Printer '{printerName}' was not found. Installed printers: {installed}");
        }

        var transport = _transportResolver.Resolve(_settings);
        var probe = transport.Probe(_settings);
        if (!probe.IsReady)
        {
            throw new InvalidOperationException(probe.Message);
        }
    }

    private byte[] BuildPayload(PrintJobRequest request)
    {
        if (string.Equals(request.ContentType, "document", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Document is null)
            {
                throw new InvalidOperationException("Document payload is required for contentType=document.");
            }

            return _documentBuilder.Build(_settings, request.Document);
        }

        return _payloadBuilder.BuildFromRequest(_settings, request);
    }

    private void SendBytes(byte[] bytes, string documentName)
    {
        var transport = _transportResolver.Resolve(_settings);
        transport.Send(_settings, bytes, documentName);
    }

    private string ResolveDestinationName()
    {
        if (PrinterTransportMode.IsDirectUsb(_settings.TransportMode))
        {
            var probe = _transportResolver.Resolve(_settings).Probe(_settings);
            return string.IsNullOrWhiteSpace(probe.Message) ? "Direct USB" : probe.Message;
        }

        return _settings.PrinterName;
    }
}
