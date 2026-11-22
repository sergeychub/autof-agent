using System.Text;
using WorkstationAgent.Forms;
using WorkstationAgent.Infrastructure;

namespace WorkstationAgent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        var paths = new AgentPaths();
        paths.EnsureDirectories();
        var settingsStore = new AgentSettingsStore(paths);
        var settings = settingsStore.Load();
        settings.LogFilePath ??= paths.LogFilePath;

        if (settingsStore.RequiresSetup(settings))
        {
            var setupWizard = new SetupWizardForm(settings, settingsStore, paths, isFirstRun: true);
            var result = setupWizard.ShowDialog();
            if (result != true || setupWizard.SavedSettings is null)
            {
                return;
            }

            settings = setupWizard.SavedSettings;
        }

        using var agentContext = new AgentApplicationContext(settingsStore, paths, settings);
#if DEBUG
        if (paths.IsDevelopment && args.Contains("--settings", StringComparer.Ordinal))
            agentContext.OpenSettingsWhenIdle();
#endif
        Application.Run(agentContext);
    }
}
