using System.Text;
using WorkstationAgent.Branding;
using WorkstationAgent.Configuration;
using WorkstationAgent.Forms;
using WorkstationAgent.Infrastructure;

namespace WorkstationAgent;

internal static class Program
{
    [STAThread]
    private static void Main()
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
            using var setupWizard = new SetupWizardForm(settings, settingsStore, paths, isFirstRun: true);
            var result = setupWizard.ShowDialog();
            if (result != DialogResult.OK || setupWizard.SavedSettings is null)
            {
                return;
            }

            settings = setupWizard.SavedSettings;
        }

        using var agentContext = new AgentApplicationContext(settingsStore, paths, settings);
        Application.Run(agentContext);
    }
}
