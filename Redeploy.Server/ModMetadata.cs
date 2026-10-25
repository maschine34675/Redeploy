using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;

namespace Redeploy.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.maschine.Redeploy";
    public string Name { get; init; } = "Redeploy";
    public string Author { get; init; } = "maschine";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.2");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/maschine34675/Redeploy";
    public string License { get; init; } = "MIT";
}
