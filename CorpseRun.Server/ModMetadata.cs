using SPTarkov.Server.Core.Models.Spt.Mod;

namespace CorpseRun.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.maschine.CorpseRun";
    public override string Name { get; init; } = "CorpseRun";
    public override string Author { get; init; } = "maschine";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.5");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/maschine34675/CorpseRun";
    public override string License { get; init; } = "MIT";
    public override bool? IsBundleMod { get; init; } = false;
}
