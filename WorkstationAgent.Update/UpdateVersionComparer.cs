namespace WorkstationAgent.Update;

public static class UpdateVersionComparer
{
    public static bool IsUpdateAvailable(string latestVersion, string currentVersion)
    {
        return Compare(latestVersion, currentVersion) > 0;
    }

    public static int Compare(string? left, string? right)
    {
        var leftParsed = TryParse(left, out var leftVersion);
        var rightParsed = TryParse(right, out var rightVersion);

        if (leftParsed && rightParsed)
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(
            left ?? string.Empty,
            right ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Split('+', 2)[0].Split('-', 2)[0];
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 4)
        {
            return false;
        }

        var numeric = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numeric[i]))
            {
                return false;
            }
        }

        version = new Version(numeric[0], numeric[1], numeric[2], numeric[3]);
        return true;
    }
}
