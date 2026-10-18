// Shared source for the compiled Core drivers (Redeploy.Client, ArenaMode.Client); each links
// it into its own assembly. Keep this free of Core types: it runs BEFORE any
// Core-dependent method is compiled or any hooks/components are initialized.
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
        /// A blocking startup error, or null for a Core at least as new as the
        /// build-time minimum. Unknown versions are not a safe admission.
        /// </summary>
        internal static string Evaluate(string pluginName, Version installedCore, string builtAgainst)
        {
            Version expected;
            if (string.IsNullOrEmpty(builtAgainst) || !Version.TryParse(builtAgainst, out expected))
                return Disabled(pluginName, "the required Redeploy.Core version could not be determined");
            if (installedCore == null)
                return Disabled(pluginName, "Redeploy.Core is missing or its version could not be determined (requires at least " + expected + ")");

            if (Normalize(installedCore) >= Normalize(expected))
                return null;

            return Disabled(pluginName, "installed Redeploy.Core " + installedCore
                + " is older than the required minimum " + expected);
        }

        internal static string Check(string pluginName, Assembly driverAssembly)
        {
            try { return Check(pluginName, ReadBuiltAgainst(driverAssembly), ResolveCoreIdentity); }
            catch (Exception ex)
            {
                return Disabled(pluginName, "the Core requirement could not be read (" + ex.GetType().Name + ")");
            }
        }

        internal static string Check(string pluginName, string builtAgainst, Func<AssemblyName> resolveCore)
        {
            Version required;
            if (!Version.TryParse(builtAgainst, out required))
                return Evaluate(pluginName, null, builtAgainst);
            try
            {
                AssemblyName core = resolveCore();
                if (core == null || core.Name != "maschine-Redeploy.Core")
                    return Disabled(pluginName, "the resolved assembly is not Redeploy.Core");
                return Evaluate(pluginName, core.Version, builtAgainst);
            }
            catch (Exception ex)
            {
                return Disabled(pluginName, "Redeploy.Core could not be loaded (requires at least "
                    + required + "; " + ex.GetType().Name + ")");
            }
        }

        private static AssemblyName ResolveCoreIdentity()
        {
            // The loaded assembly wins over a newer file copied over it mid-game.
            AssemblyName found = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AssemblyName identity = assembly.GetName();
                if (identity.Name != "maschine-Redeploy.Core") continue;
                if (found != null) throw new InvalidOperationException("Multiple loaded Core assemblies");
                found = identity;
            }
            return found ?? Assembly.Load(new AssemblyName("maschine-Redeploy.Core")).GetName();
        }

        private static string Disabled(string pluginName, string reason)
            => pluginName + ": disabled because " + reason
                + ". Install the matching maschine-Redeploy.Core.dll in BepInEx/plugins, keep only one copy,"
                + " and restart the game. No respawn hooks from this mod were initialized.";

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
