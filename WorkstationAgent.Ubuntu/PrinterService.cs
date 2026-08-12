using System.Diagnostics;
using System.Net.Sockets;

namespace WorkstationAgent.Ubuntu;

internal sealed class PrinterService
{
    private readonly AgentSettings _settings;
    private readonly AgentLogger _logger;
    private readonly PrintPayloadBuilder _payloadBuilder;
    private readonly SemaphoreSlim _printLock = new(1, 1);

    public PrinterService(AgentSettings settings, AgentLogger logger, PrintPayloadBuilder payloadBuilder)
    {
        _settings = settings;
        _logger = logger;
        _payloadBuilder = payloadBuilder;
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
                await SendAsync(
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
                await SendAsync(endpoint, payload, documentName, cancellationToken);
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
                await SendAsync(endpoint, payload, documentName, cancellationToken);
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
            return endpoint.DevicePath ?? "device:not-configured";
        }
        if (string.Equals(endpoint.TransportMode, PrinterTransportMode.Tcp, StringComparison.OrdinalIgnoreCase))
        {
            return $"tcp://{endpoint.Host ?? "not-configured"}:{endpoint.Port}";
        }
        return $"cups:{endpoint.PrinterName}";
    }

    private static Task SendAsync(
        PrinterEndpointSettings endpoint,
        byte[] payload,
        string documentName,
        CancellationToken cancellationToken)
    {
        if (string.Equals(endpoint.TransportMode, PrinterTransportMode.Device, StringComparison.OrdinalIgnoreCase))
        {
            return SendToDeviceAsync(endpoint, payload, cancellationToken);
        }
        if (string.Equals(endpoint.TransportMode, PrinterTransportMode.Tcp, StringComparison.OrdinalIgnoreCase))
        {
            return SendToTcpAsync(endpoint, payload, cancellationToken);
        }
        return SendToCupsAsync(endpoint, payload, documentName, cancellationToken);
    }

    private static async Task SendToDeviceAsync(
        PrinterEndpointSettings endpoint,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var path = endpoint.DevicePath
            ?? throw new InvalidOperationException("devicePath is required for device printing.");
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite,
            4096,
            FileOptions.Asynchronous);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SendToTcpAsync(
        PrinterEndpointSettings endpoint,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, endpoint.ConnectTimeoutSeconds)));
        using var client = new TcpClient();
        await client.ConnectAsync(
            endpoint.Host ?? throw new InvalidOperationException("host is required for TCP printing."),
            endpoint.Port,
            timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(payload, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }

    private static async Task SendToCupsAsync(
        PrinterEndpointSettings endpoint,
        byte[] payload,
        string documentName,
        CancellationToken cancellationToken)
    {
        var executable = File.Exists("/usr/bin/lp") ? "/usr/bin/lp" : "lp";
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(endpoint.PrinterName);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("raw");
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(documentName.Length <= 120 ? documentName : documentName[..120]);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the CUPS lp command.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.BaseStream.WriteAsync(payload, cancellationToken);
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"CUPS rejected the print job with exit code {process.ExitCode}: {stderr.Trim()}");
        }
        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new InvalidOperationException("CUPS did not return a print job identifier.");
        }
    }
}
