using System.Reflection;
using BepInEx.Configuration;
using Redeploy.Config;

var root = Path.Combine(Path.GetTempPath(), "RedeployConfig-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
ConfigFile Open(string name, string contents = null)
{
    var directory = Path.Combine(root, name);
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "com.maschine.Redeploy.cfg");
    if (contents != null) File.WriteAllText(path, contents);
    var config = new ConfigFile(path, false);
    Settings.Init(config);
    return config;
}

var fresh = Open("fresh");
Check(Settings.RespawnAsScav.Value && Equals(Settings.RespawnAsScav.DefaultValue, true),
    "The own-Scav choice must be enabled by default");
Check(fresh.Count == 9, "All existing settings must remain available");
Check(fresh.Select(pair => pair.Key.Section).Distinct().SequenceEqual(new[] { "General", "Respawn", "Diagnostics" }),
    "Player-facing sections must precede diagnostics");
var identities = new[]
{
    "General/Enabled", "General/RespawnAsScav", "General/MaxRespawnsPerRaid",
    "Respawn/RespawnHealthPercent", "Respawn/RespawnCooldownSeconds", "Respawn/CorpseSpawnDistanceMeters",
    "Respawn/SpawnProtectionSeconds", "Respawn/RequiredRespawnItem", "Diagnostics/EnablePipelineTiming"
};
Check(fresh.Select(pair => pair.Key.Section + "/" + pair.Key.Key).Order().SequenceEqual(identities.Order()),
    "Stored section/key identities must stay stable");

// Exercise the installed reader: correct-looking anonymous tags are not accepted.
var menuType = typeof(ConfigurationManager.ConfigurationManager).Assembly.GetType("ConfigurationManager.ConfigSettingEntry", true);
var previousOrders = new Dictionary<string, int>();
foreach (var pair in fresh)
{
    var entry = pair.Value;
    var menuEntry = Activator.CreateInstance(menuType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, new object[] { entry, null }, null);
    object Get(string property) => menuType.GetProperty(property).GetValue(menuEntry);
    Check(Get("DispName") is string label && label.Contains(' ') && label != entry.Definition.Key,
        $"Readable name not consumed by ConfigurationManager: {entry.Definition}");
    Check(Get("Order") is int order && order > 0, $"Missing menu order: {entry.Definition}");
    int currentOrder = (int)Get("Order");
    Check(!previousOrders.TryGetValue(entry.Definition.Section, out var previous) || previous > currentOrder,
        $"Menu/bind order disagree in {entry.Definition.Section}");
    previousOrders[entry.Definition.Section] = currentOrder;
    Check(Equals(Get("IsAdvanced"), entry == Settings.EnablePipelineTiming), $"Wrong advanced visibility: {entry.Definition}");
    Check(Equals(Get("ShowRangeAsPercent"), false), $"Units must remain literal: {entry.Definition}");
}

Check(Settings.RequiredRespawnItem.Description.AcceptableValues is AcceptableValueList<string> items
    && items.AcceptableValues.Contains("None") && items.AcceptableValues.Contains("Surv12"),
    "Respawn item choices must remain intact");
Check(Settings.MaxRespawnsPerRaid.Description.AcceptableValues == null,
    "Menu changes must not clamp previously valid respawn limits");

const string legacy = "[General]\nEnabled = false\nRespawnAsScav = false\nMaxRespawnsPerRaid = 0\n"
    + "[Respawn]\nRespawnHealthPercent = 63\nRespawnCooldownSeconds = 17.25\nCorpseSpawnDistanceMeters = 42\n"
    + "SpawnProtectionSeconds = 3.5\nRequiredRespawnItem = Surv12\n[Diagnostics]\nEnablePipelineTiming = true\n";
void CheckPreferences(bool expectedScav)
{
    Check(!Settings.Enabled.Value && Settings.RespawnAsScav.Value == expectedScav && Settings.MaxRespawnsPerRaid.Value == 0,
        "Explicit general choices must survive the new default");
    Check(Settings.RespawnHealthPercent.Value == 63 && Settings.RespawnCooldownSeconds.Value == 17.25f,
        "Health or delay changed");
    Check(Settings.CorpseSpawnDistanceMeters.Value == 42 && Settings.SpawnProtectionSeconds.Value == 3.5f,
        "Distance or protection changed");
    Check(Settings.RequiredRespawnItem.Value == "Surv12" && Settings.GetRequiredRespawnItemTemplateId() == "5d02797c86f774203f38e30a",
        "Required item changed");
    Check(Settings.EnablePipelineTiming.Value, "Advanced diagnostics preference must remain enabled");
}
var upgraded = Open("upgrade", legacy);
CheckPreferences(false);
upgraded.Save();
upgraded = Open("upgrade");
CheckPreferences(false);
Settings.RespawnAsScav.Value = true;
upgraded.Save();
upgraded = Open("upgrade");
CheckPreferences(true);

var withoutScav = Open("old-release", legacy.Replace("RespawnAsScav = false\n", ""));
CheckPreferences(true);
withoutScav.Save();
Open("old-release");
CheckPreferences(true);

Console.WriteLine($"REDEPLOY_CONFIG_CHECKS_PASSED checks={checks}; fixtures: {root}");
