using NAPS2.Images;
using NAPS2.Images.Gdi;
using NAPS2.Pdf;
using NAPS2.Scan;
using System.Security.Cryptography;
using System.Text;

namespace WorkstationAgent.Scanning;

internal sealed record ScannerCapability(string scannerKey, string name, bool available, bool busy);

/// <summary>TWAIN runs in the isolated 32-bit NAPS2 worker, independently of the agent UI.</summary>
internal interface IDocumentScanner : IDisposable
{
    Task<ScannerCapability[]> Discover(CancellationToken cancellationToken = default);
    Task<int> Scan(string scannerKey, string mode, string source, bool singlePage,
        string outputFile, Action<int>? progress, CancellationToken cancellationToken);
}

internal sealed class DocumentScanner : IDocumentScanner
{
    private readonly ScanningContext _context;
    private readonly ScanController _controller;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, ScanDevice> _devices = new();
    private string? _busyKey;
    private DateTime _lastDiscovery = DateTime.MinValue;

    public DocumentScanner(string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        _context = new ScanningContext(new GdiImageContext())
        {
            FileStorageManager = new FileStorageManager(storageDirectory)
        };
        _context.SetUpWin32Worker();
        _controller = new ScanController(_context);
    }

    public async Task<ScannerCapability[]> Discover(CancellationToken cancellationToken = default)
    {
        if (await _gate.WaitAsync(0, cancellationToken))
        {
            try
            {
                if (DateTime.UtcNow - _lastDiscovery > TimeSpan.FromSeconds(25))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var found = new Dictionary<string, ScanDevice>();
                    await foreach (var device in _controller.GetDevices(Driver.Twain, timeout.Token))
                    {
                        var key = "twain:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(device.ID))).ToLowerInvariant();
                        found[key] = device;
                    }
                    _devices = found;
                    _lastDiscovery = DateTime.UtcNow;
                }
            }
            finally { _gate.Release(); }
        }
        return _devices.Select(pair => new ScannerCapability(pair.Key, pair.Value.Name, true, pair.Key == _busyKey)).ToArray();
    }

    public async Task<int> Scan(string scannerKey, string mode, string source, bool singlePage,
        string outputFile, Action<int>? progress, CancellationToken cancellationToken)
    {
        await Discover(cancellationToken);
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Сканер уже занят.");
        var images = new List<ProcessedImage>();
        try
        {
            if (!_devices.TryGetValue(scannerKey, out var device)) throw new InvalidOperationException("Сканер не найден. Проверьте USB и питание.");
            _busyKey = scannerKey;
            using var scanCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var options = new ScanOptions
            {
                Device = device,
                PaperSource = source switch
                {
                    "Flatbed" => PaperSource.Flatbed,
                    "Duplex" when !singlePage => PaperSource.Duplex,
                    _ => PaperSource.Feeder
                },
                PageSize = PageSize.A4,
                Dpi = 300,
                BitDepth = mode == "Grayscale" ? BitDepth.Grayscale : BitDepth.Color,
                Quality = 85,
                UseNativeUI = false
            };
            await foreach (var image in _controller.Scan(options, scanCancel.Token))
            {
                images.Add(image);
                progress?.Invoke(images.Count);
                if (images.Count > 100)
                {
                    scanCancel.Cancel();
                    throw new InvalidOperationException("В пачке больше 100 страниц. Полученные страницы сохранены локально; разделите PDF перед загрузкой.");
                }
                if (singlePage)
                {
                    scanCancel.Cancel();
                    break;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (images.Count == 0) throw new InvalidOperationException("Нет отсканированных страниц. Положите документы в автоподатчик.");
            await Save(images, outputFile, singlePage);
            return images.Count;
        }
        catch
        {
            // A jam or disconnect must not destroy pages already taken from the feeder.
            if (images.Count > 0)
            {
                try { await Save(images, Path.ChangeExtension(outputFile, "partial.pdf"), false); }
                catch { /* Preserve the original scanner error. */ }
            }
            throw;
        }
        finally
        {
            foreach (var image in images) image.Dispose();
            _busyKey = null;
            _lastDiscovery = DateTime.MinValue;
            _gate.Release();
        }
    }

    private async Task Save(List<ProcessedImage> images, string path, bool singlePage)
    {
        if (singlePage) images[0].Save(path);
        else if (!await new PdfExporter(_context).Export(path, images)) throw new IOException("Не удалось сохранить скан в PDF.");
    }

    public void Dispose() { _context.Dispose(); _gate.Dispose(); }
}
