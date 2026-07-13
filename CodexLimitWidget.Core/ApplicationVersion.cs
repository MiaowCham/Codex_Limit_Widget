using System.Reflection;

namespace CodexLimitWidget.Core;

public static class ApplicationVersion
{
    public static string FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "?.?.?";
    }
}
