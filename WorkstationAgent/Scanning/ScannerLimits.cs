namespace WorkstationAgent.Scanning;

internal static class ScannerLimits
{
    public const int MaxPages = 500;
    public const long MaxFileBytes = 250L * 1024 * 1024;

    public static void EnsurePageCount(int count, CancellationTokenSource scanCancellation)
    {
        if (count <= MaxPages) return;
        scanCancellation.Cancel();
        throw new InvalidOperationException($"В пачке больше {MaxPages} страниц. Полученные страницы сохранены локально; разделите PDF перед загрузкой.");
    }

    public static void EnsureFileSize(long length)
    {
        if (length > MaxFileBytes)
            throw new InvalidOperationException("Пачка больше 250 МиБ. PDF сохранён на компьютере агента; загрузите его частями.");
    }
}
