namespace WorkstationAgent.Updater;

internal sealed class UpdaterPaths
{
    private UpdaterPaths(string installDirectory)
    {
        InstallDirectory = installDirectory;
        BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Avtoforward",
            "Agent");
        LogsDirectory = Path.Combine(BaseDirectory, "logs");
        UpdatesDirectory = Path.Combine(BaseDirectory, "updates");
        DownloadsDirectory = Path.Combine(UpdatesDirectory, "downloads");
        StagingDirectory = Path.Combine(UpdatesDirectory, "staging");
        BackupsDirectory = Path.Combine(UpdatesDirectory, "backups");
        PendingManifestPath = Path.Combine(UpdatesDirectory, "pending-manifest.json");
        UpdateStatePath = Path.Combine(UpdatesDirectory, "state.json");
        SettingsPath = Path.Combine(BaseDirectory, "agentsettings.json");
        LogFilePath = Path.Combine(LogsDirectory, "updater.log");
    }

    public string InstallDirectory { get; }

    public string BaseDirectory { get; }

    public string LogsDirectory { get; }

    public string UpdatesDirectory { get; }

    public string DownloadsDirectory { get; }

    public string StagingDirectory { get; }

    public string BackupsDirectory { get; }

    public string PendingManifestPath { get; }

    public string UpdateStatePath { get; }

    public string SettingsPath { get; }

    public string LogFilePath { get; }

    public static UpdaterPaths Create(string[] args)
    {
        var installDirectory = ReadArg(args, "--install-dir")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Avtoforward", "Agent");

        return new UpdaterPaths(installDirectory);
    }

    private static string? ReadArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
