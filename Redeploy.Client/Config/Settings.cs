using BepInEx.Configuration;

namespace Redeploy.Config;

public static class Settings
{
    public static ConfigEntry<bool> Enabled { get; private set; }
    public static ConfigEntry<bool> RespawnAsScav { get; private set; }
    public static ConfigEntry<int> MaxRespawnsPerRaid { get; private set; }
    public static ConfigEntry<float> RespawnHealthPercent { get; private set; }
    public static ConfigEntry<float> RespawnCooldownSeconds { get; private set; }
    public static ConfigEntry<float> CorpseSpawnDistanceMeters { get; private set; }
    public static ConfigEntry<float> SpawnProtectionSeconds { get; private set; }
    public static ConfigEntry<string> RequiredRespawnItem { get; private set; }
    public static ConfigEntry<bool> EnablePipelineTiming { get; private set; }

    private static readonly (string Name, string TemplateId)[] RespawnItems =
    {
        ("None", ""),
        ("Defibrillator", "5c052e6986f7746b207bc3c9"),
        ("Grizzly", "590c657e86f77412b013051d"),
        ("Salewa", "544fb45d4bdc2dee738b4568"),
        ("IFAK", "590c678286f77426c9660122"),
        ("AFAK", "60098ad7c2240c0fe85c570a"),
        ("CMS", "5d02778e86f774203e7dedbe"),
        ("Surv12", "5d02797c86f774203f38e30a"),
        ("CarFirstAidKit", "590c661e86f7741e566b646a"),
        ("Adrenaline", "5c10c8fd86f7743d7d706df3"),
        ("Propital", "5c0e530286f7747fa1419862"),
    };

    public static void Init(ConfigFile config)
    {
        // Keep stored section/key identities stable. ConfigurationManager reads
        // display names/order from tags and section order from the bind order.
        Enabled = config.Bind(
            "General",
            "Enabled",
            true,
            Describe("Enable respawning", "Enable respawn choices after death instead of immediately ending the raid.", 100));

        RespawnAsScav = config.Bind(
            "General",
            "RespawnAsScav",
            true,
            Describe("Offer own Scav", "Also offer your regular Scav after a PMC death, alongside the normal respawn without equipment. Requires the normal Scav cooldown to have expired and uses that Scav's gear. Your PMC remains dead. Extract to use the normal loot transfer screen; dying as the Scav ends the raid. The next Scav receives the normal cooldown.", 90));

        MaxRespawnsPerRaid = config.Bind(
            "General",
            "MaxRespawnsPerRaid",
            1,
            Describe("Respawn limit", "How many respawns are allowed per raid. 0 = unlimited.", 80));

        RespawnHealthPercent = config.Bind(
            "Respawn",
            "RespawnHealthPercent",
            100f,
            Describe("Respawn health (%)", "Health percentage restored after respawn (1-100).", 50));

        RespawnCooldownSeconds = config.Bind(
            "Respawn",
            "RespawnCooldownSeconds",
            5f,
            Describe("Respawn delay (seconds)", "Minimum seconds between death and respawn, counted from the death: time spent watching a death replay counts, so choosing Respawn after it spawns you right away.", 40));

        CorpseSpawnDistanceMeters = config.Bind(
            "Respawn",
            "CorpseSpawnDistanceMeters",
            20f,
            Describe("Distance from corpse (meters)", "Preferred distance from the corpse when respawning near the corpse (chosen in the death prompt). Only spots you could have walked to count; closer ones are tried if needed, then the corpse's own spot, then a map spawn point.", 30));

        SpawnProtectionSeconds = config.Bind(
            "Respawn",
            "SpawnProtectionSeconds",
            10f,
            Describe("Spawn protection (seconds)", "Invulnerability duration after respawn (0 = disabled).", 20));

        RequiredRespawnItem = config.Bind(
            "Respawn",
            "RequiredRespawnItem",
            "None",
            Describe("Required respawn item",
                "Item that must be in the inventory to respawn; it is consumed on respawn.",
                10,
                new AcceptableValueList<string>(System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(RespawnItems, entry => entry.Name)))));

        EnablePipelineTiming = config.Bind(
            "Diagnostics",
            "EnablePipelineTiming",
            false,
            Describe("Detailed respawn measurements", "Collect detailed respawn timing, state counters and following-frame measurements. Enable only when diagnosing slow respawns; this adds measurement work and technical log output.", 10, advanced: true));
    }

    private static ConfigDescription Describe(string name, string description, int order,
        AcceptableValueBase range = null, bool advanced = false) =>
        new ConfigDescription(description, range, new ConfigurationManagerAttributes
        {
            DispName = name,
            Order = order,
            IsAdvanced = advanced,
            ShowRangeAsPercent = false
        });

    public static string GetRequiredRespawnItemTemplateId()
    {
        foreach (var (name, templateId) in RespawnItems)
        {
            if (name == RequiredRespawnItem.Value)
                return templateId;
        }

        return "";
    }

    public static bool HasRespawnsRemaining(int used)
    {
        if (MaxRespawnsPerRaid.Value <= 0)
            return true;

        return used < MaxRespawnsPerRaid.Value;
    }
}

// Read by ConfigurationManager through its type name and public fields.
internal sealed class ConfigurationManagerAttributes
{
    public string DispName;
    public int? Order;
    public bool? IsAdvanced;
    public bool? ShowRangeAsPercent;
}
