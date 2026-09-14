using BepInEx.Configuration;

namespace Redeploy.Config;

public static class Settings
{
    public static ConfigEntry<bool> Enabled { get; private set; }
    public static ConfigEntry<int> MaxRespawnsPerRaid { get; private set; }
    public static ConfigEntry<float> RespawnHealthPercent { get; private set; }
    public static ConfigEntry<float> RespawnCooldownSeconds { get; private set; }
    public static ConfigEntry<float> CorpseSpawnDistanceMeters { get; private set; }
    public static ConfigEntry<float> SpawnProtectionSeconds { get; private set; }
    public static ConfigEntry<string> RequiredRespawnItem { get; private set; }

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
        Enabled = config.Bind(
            "General",
            "Enabled",
            true,
            "Enable respawn after death instead of immediately ending the raid.");

        MaxRespawnsPerRaid = config.Bind(
            "General",
            "MaxRespawnsPerRaid",
            1,
            "How many respawns are allowed per raid. 0 = unlimited.");

        RespawnHealthPercent = config.Bind(
            "Respawn",
            "RespawnHealthPercent",
            100f,
            "Health percentage restored after respawn (1-100).");

        RespawnCooldownSeconds = config.Bind(
            "Respawn",
            "RespawnCooldownSeconds",
            5f,
            "Minimum seconds between death and respawn, counted from the death: time spent watching a death replay counts, so choosing Respawn after it spawns you right away.");

        CorpseSpawnDistanceMeters = config.Bind(
            "Respawn",
            "CorpseSpawnDistanceMeters",
            20f,
            "Preferred distance from the corpse when respawning near the corpse (chosen in the death prompt). Only spots you could have walked to count; closer ones are tried if needed, then the corpse's own spot, then a map spawn point.");

        SpawnProtectionSeconds = config.Bind(
            "Respawn",
            "SpawnProtectionSeconds",
            10f,
            "Invulnerability duration after respawn (0 = disabled).");

        RequiredRespawnItem = config.Bind(
            "Respawn",
            "RequiredRespawnItem",
            "None",
            new ConfigDescription(
                "Item that must be in the inventory to respawn; it is consumed on respawn.",
                new AcceptableValueList<string>(System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(RespawnItems, entry => entry.Name)))));
    }

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
