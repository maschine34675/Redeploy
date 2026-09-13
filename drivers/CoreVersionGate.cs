// Shared source for the compiled Core drivers (Redeploy.Client, ArenaMode.Client); each links
// it into its own assembly. It must stay free of Core types: it runs in Awake against
// whatever maschine-Redeploy.Core.dll is installed, including one older than this build.
// C# 7.3, no Unity or BepInEx types, so Redeploy.Tests can compile it as is.
using System;
using System.Reflection;

namespace Redeploy.Drivers
{
    /// <summary>
    /// Skew check from docs/RESPAWN-API.md 3.9 ("Treiber-Check"): ApiVersion does not move on
    /// fix releases, so a driver compares the installed Core's assembly version with the Core
    /// version it was built against. Every mod zip carries the same flat Core DLL and the last
    /// one unpacked wins, so an older zip can silently downgrade Core under a newer driver.
    /// </summary>
    internal static class CoreVersionGate
    {
        /// <summary>AssemblyMetadata key the driver csproj stamps (Redeploy.Core/CoreVersion.props).</summary>
        internal const string BuiltAgainstKey = "RedeployCoreBuiltAgainst";

        /// <summary>The Core version stamped into <paramref name="driverAssembly"/> at build time, or null.</summary>
        internal static string ReadBuiltAgainst(Assembly driverAssembly)
        {
            if (driverAssembly == null) return null;
            foreach (AssemblyMetadataAttribute attribute in driverAssembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                if (attribute.Key == BuiltAgainstKey) return attribute.Value;
            }
            return null;
        }

        /// <summary>
        /// The warning to log when the installed Core is older than the build-time one; null when
        /// it is the same or newer, or when either version is unknown (the check fails open: a
        /// missing stamp must never keep a driver from loading).
        /// </summary>
        internal static string Evaluate(string pluginName, Version installedCore, string builtAgainst)
        {
            Version expected;
            if (installedCore == null || string.IsNullOrEmpty(builtAgainst) || !Version.TryParse(builtAgainst, out expected))
                return null;

            if (Normalize(installedCore) >= Normalize(expected))
                return null;

            return pluginName + ": the installed maschine-Redeploy.Core " + installedCore
                   + " is older than the " + expected + " this build expects. Nothing failed to load: respawns keep"
                   + " running on the old Core, without its fixes. Another mod's zip probably overwrote"
                   + " BepInEx/plugins/maschine-Redeploy.Core.dll; reinstall the latest Redeploy or ArenaMode.";
        }

        // "1.0.0" parses with Revision -1 and an assembly version reads "1.0.0.0": compare all four parts.
        private static Version Normalize(Version version)
        {
            return new Version(
                Math.Max(version.Major, 0),
                Math.Max(version.Minor, 0),
                Math.Max(version.Build, 0),
                Math.Max(version.Revision, 0));
        }
    }
}
