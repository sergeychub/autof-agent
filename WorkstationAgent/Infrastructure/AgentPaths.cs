namespace WorkstationAgent.Infrastructure;

internal sealed class AgentPaths
{
    public AgentPaths()
    {
        BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Avtoforward",
            "Agent");

#if DEBUG
        var developmentDataDirectory = Environment.GetEnvironmentVariable("AUTOF_AGENT_DEV_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(developmentDataDirectory))
        {
            BaseDirectory = Path.GetFullPath(developmentDataDirectory);
            IsDevelopment = true;
        }
#endif

        LogsDirectory = Path.Combine(BaseDirectory, "logs");
        ConfigPath = Path.Combine(BaseDirectory, "agentsettings.json");
        LogFilePath = Path.Combine(LogsDirectory, "agent.log");
        UpdatesDirectory = Path.Combine(BaseDirectory, "updates");
        PendingUpdateManifestPath = Path.Combine(UpdatesDirectory, "pending-manifest.json");
        UpdateStatePath = Path.Combine(UpdatesDirectory, "state.json");
        BundledAssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        LogoPath = Path.Combine(BundledAssetsDirectory, "logo.svg");
        LogoPngPath = Path.Combine(BundledAssetsDirectory, "logo.png");
    }

    public bool IsDevelopment { get; }
    public string BaseDirectory { get; }

    public string LogsDirectory { get; }

    public string ConfigPath { get; }

    public string LogFilePath { get; }

    public string UpdatesDirectory { get; }

    public string PendingUpdateManifestPath { get; }

    public string UpdateStatePath { get; }

    public string BundledAssetsDirectory { get; }

    public string LogoPath { get; }

    public string LogoPngPath { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(UpdatesDirectory);
    }
}
