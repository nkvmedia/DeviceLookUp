using System.Reflection;

namespace DeviceLookup.Core.Services;

public static class AppInfo
{
    public const string ProjectUrl = "https://github.com/nkvmedia/DeviceLookUp";
    public const string ProductName = "Device Lookup";

    /// <summary>Version comes from the assembly, so a release only bumps it in one place.</summary>
    public static string Version
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }
}
