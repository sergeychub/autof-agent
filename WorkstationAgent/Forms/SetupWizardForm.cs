using WorkstationAgent.Configuration;
using WorkstationAgent.Infrastructure;

namespace WorkstationAgent.Forms;

internal sealed class SetupWizardForm : SetupWizardFormCore
{
    public SetupWizardForm(AgentSettings initialSettings, AgentSettingsStore settingsStore, AgentPaths paths, bool isFirstRun)
        : base(initialSettings, settingsStore, paths, isFirstRun)
    {
    }
}
