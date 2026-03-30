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
    private readonly TsplPayloadBuilder _tsplPayloadBuilder;
    private readonly TsplTestLabelBuilder _tsplTestLabelBuilder;
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
        TsplPayloadBuilder tsplPayloadBuilder,
        TsplTestLabelBuilder tsplTestLabelBuilder,
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
        _tsplPayloadBuilder = tsplPayloadBuilder;
        _tsplTestLabelBuilder = tsplTestLabelBuilder;
        _transportResolver = transportResolver;
    }

    public bool IsConfigured => IsEndpointConfigured(_settings.ReceiptPrinter) || IsEndpointConfigured(_settings.LabelPrinter);

    public string PrinterName => _settings.ReceiptPrinter.PrinterName;

    public string GetAvailabilityStatus()
    {
        return $"Receipt: {GetAvailabilityStatus(_settings.ReceiptPrinter, "Receipt printer")} | Label: {GetAvailabilityStatus(_settings.LabelPrinter, "Label printer")}";
    }

    public string GetPrinterNameForRole(string? role)
    {
        return ResolveDestinationName(GetEndpoint(PrinterRoles.IsLabel(role) ? PrinterRoles.Label : PrinterRoles.Receipt));
    }

    public string GetPrinterNameForRequest(PrintJobRequest? request)
    {
        return request is null
            ? GetPrinterNameForRole(null)
            : ResolveDestinationName(GetEndpoint(ResolvePrintRole(request)));
    }

    public PrinterTestResult PrintTestReceipt(string requestId)
    {
        var endpoint = _settings.ReceiptPrinter;
        var printerName = ResolveDestinationName(endpoint);

        try
        {
            EnsurePrinterReady(endpoint, "Receipt printer");

            var bytes = _receiptBuilder.Build(_settings);
            SendBytes(endpoint, bytes, "WorkstationAgent Test Receipt");
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
                PrinterName = printerName,
                Error = ex.Message
            };
        }
    }

    public PrintJobResult PrintTsplTestLabel(string requestId)
    {
        const string documentName = "WorkstationAgent TSPL Test Label";
        var endpoint = _settings.LabelPrinter;
        var printerName = ResolveDestinationName(endpoint);

        try
        {
            EnsurePrinterReady(endpoint, "Label printer");
            var bytes = _tsplTestLabelBuilder.Build(_settings);
            SendBytes(endpoint, bytes, documentName);
            var printedAt = DateTimeOffset.UtcNow.ToString("O");

            _logger.Info($"TSPL test label sent to '{printerName}'.");

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
            _logger.Error($"TSPL test label failed for '{printerName}'.", ex);

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

    public PrintJobResult PrintLogoTest(string requestId)
    {
        const string documentName = "WorkstationAgent Logo Test";
        var endpoint = _settings.ReceiptPrinter;
        var printerName = ResolveDestinationName(endpoint);

        try
        {
            EnsurePrinterReady(endpoint, "Receipt printer");

            if (!File.Exists(_paths.LogoPngPath))
            {
                throw new FileNotFoundException("Bundled logo.png was not found.", _paths.LogoPngPath);
            }

            var bytes = _imageRenderer.BuildLogoTest(_settings, _paths.LogoPngPath);
            SendBytes(endpoint, bytes, documentName);
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
        var role = ResolvePrintRole(request);
        var endpoint = GetEndpoint(role);
        var printerName = ResolveDestinationName(endpoint);
        var documentName = string.IsNullOrWhiteSpace(request.DocumentName)
            ? "WorkstationAgent Print Job"
            : request.DocumentName;

        try
        {
            EnsurePrinterReady(endpoint, PrinterRoles.IsLabel(role) ? "Label printer" : "Receipt printer");
            var bytes = BuildPayload(request, role);
            SendBytes(endpoint, bytes, documentName);
            var printedAt = DateTimeOffset.UtcNow.ToString("O");

            _logger.Info($"Print job '{documentName}' sent to printer '{printerName}'.");

            return new PrintJobResult
            {
                RequestId = request.RequestId,
                Success = true,
                PrinterName = printerName,
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
                PrinterName = printerName,
                Error = ex.Message,
                DocumentName = documentName
            };
        }
    }

    private void EnsurePrinterReady(PrinterEndpointSettings endpoint, string endpointName)
    {
        if (!endpoint.Enabled)
        {
            throw new InvalidOperationException($"{endpointName} is disabled in agentsettings.json.");
        }

        if (!PrinterTransportMode.IsDirectUsb(endpoint.TransportMode) && string.IsNullOrWhiteSpace(endpoint.PrinterName))
        {
            throw new InvalidOperationException($"{endpointName} name is not configured in agentsettings.json.");
        }

        if (!PrinterTransportMode.IsDirectUsb(endpoint.TransportMode) && !_discoveryService.PrinterExists(endpoint.PrinterName))
        {
            var installed = string.Join(", ", _discoveryService.GetInstalledPrinters());
            throw new InvalidOperationException($"Printer '{endpoint.PrinterName}' was not found. Installed printers: {installed}");
        }

        var transport = _transportResolver.Resolve(endpoint);
        var probe = transport.Probe(endpoint);
        if (!probe.IsReady)
        {
            throw new InvalidOperationException(probe.Message);
        }
    }

    private byte[] BuildPayload(PrintJobRequest request, string role)
    {
        if (string.Equals(request.ContentType, "document", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Document is null)
            {
                throw new InvalidOperationException("Document payload is required for contentType=document.");
            }

            if (PrinterRoles.IsLabel(role))
            {
                throw new InvalidOperationException("Label printer does not support contentType=document. Use contentType=tspl-label or raw-base64.");
            }

            return _documentBuilder.Build(_settings, request.Document);
        }

        if (string.Equals(request.ContentType, "tspl-label", StringComparison.OrdinalIgnoreCase))
        {
            if (request.TsplLabel is null)
            {
                throw new InvalidOperationException("TsplLabel payload is required for contentType=tspl-label.");
            }

            return _tsplPayloadBuilder.Build(_settings, request.TsplLabel);
        }

        if (PrinterRoles.IsLabel(role) && !string.Equals(request.ContentType, "raw-base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Label printer accepts only contentType=tspl-label or raw-base64.");
        }

        return _payloadBuilder.BuildFromRequest(_settings, request);
    }

    private void SendBytes(PrinterEndpointSettings endpoint, byte[] bytes, string documentName)
    {
        var transport = _transportResolver.Resolve(endpoint);
        transport.Send(endpoint, bytes, documentName);
    }

    private string ResolveDestinationName(PrinterEndpointSettings endpoint)
    {
        if (PrinterTransportMode.IsDirectUsb(endpoint.TransportMode))
        {
            try
            {
                var probe = _transportResolver.Resolve(endpoint).Probe(endpoint);
                return string.IsNullOrWhiteSpace(probe.Message) ? "Direct USB" : probe.Message;
            }
            catch
            {
                return "Direct USB";
            }
        }

        return endpoint.PrinterName;
    }

    private PrinterEndpointSettings GetEndpoint(string role)
    {
        return PrinterRoles.IsLabel(role) ? _settings.LabelPrinter : _settings.ReceiptPrinter;
    }

    private string ResolvePrintRole(PrintJobRequest request)
    {
        if (PrinterRoles.IsLabel(request.Target))
        {
            return PrinterRoles.Label;
        }

        if (PrinterRoles.IsReceipt(request.Target))
        {
            return PrinterRoles.Receipt;
        }

        return string.Equals(request.ContentType, "tspl-label", StringComparison.OrdinalIgnoreCase)
            ? PrinterRoles.Label
            : PrinterRoles.Receipt;
    }

    private string GetAvailabilityStatus(PrinterEndpointSettings endpoint, string endpointName)
    {
        if (!endpoint.Enabled)
        {
            return $"{endpointName} disabled";
        }

        if (!PrinterTransportMode.IsDirectUsb(endpoint.TransportMode) && string.IsNullOrWhiteSpace(endpoint.PrinterName))
        {
            return $"{endpointName} name is not configured";
        }

        var transport = _transportResolver.Resolve(endpoint);
        var probe = transport.Probe(endpoint);
        return probe.Message;
    }

    private static bool IsEndpointConfigured(PrinterEndpointSettings endpoint)
    {
        return endpoint.Enabled
            && (PrinterTransportMode.IsDirectUsb(endpoint.TransportMode)
                || !string.IsNullOrWhiteSpace(endpoint.PrinterName));
    }
}
