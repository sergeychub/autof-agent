using System.Reflection;

namespace WorkstationAgent.Infrastructure;

internal static class AgentVersionProvider
{
    public static string CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AgentVersionProvider).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "0.0.0";
        }
    }
}
