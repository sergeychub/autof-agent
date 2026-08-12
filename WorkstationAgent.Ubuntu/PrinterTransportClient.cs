using System.Diagnostics;
using System.Net.Sockets;

namespace WorkstationAgent.Ubuntu;

internal sealed class PrinterTransportClient
{
    private readonly string _cupsExecutable;

    public PrinterTransportClient(string? cupsExecutable = null)
    {
        _cupsExecutable = string.IsNullOrWhiteSpace(cupsExecutable)
            ? ResolveCupsExecutable()
            : cupsExecutable;
    }

    public Task SendAsync(
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
        if (string.Equals(endpoint.TransportMode, PrinterTransportMode.Cups, StringComparison.OrdinalIgnoreCase))
        {
            return SendToCupsAsync(endpoint, payload, documentName, cancellationToken);
        }
        throw new InvalidOperationException($"Unsupported printer transport '{endpoint.TransportMode}'.");
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

    private async Task SendToCupsAsync(
        PrinterEndpointSettings endpoint,
        byte[] payload,
        string documentName,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _cupsExecutable,
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
        startInfo.ArgumentList.Add("-");

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

    private static string ResolveCupsExecutable() =>
        File.Exists("/usr/bin/lp") ? "/usr/bin/lp" : "lp";
}
