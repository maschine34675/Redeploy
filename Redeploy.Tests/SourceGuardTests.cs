using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// Golden-order source guard (docs/RESPAWN-API.md, section 8.4): RunRespawn keeps its checked-in
/// statement order, Core raises its public events only through the SignalDispatcher, and the
/// Freecam patches are enabled only inside a try. Each guard also gets the fault it exists for,
/// so a guard that stopped seeing anything fails here rather than passing quietly.
/// </summary>
public class SourceGuardTests
{
    private const string UpdateVariable = "REDEPLOY_UPDATE_GOLDEN";

    private static readonly string RepoRoot = ApiContractTests.RepoRoot;
    private static readonly string EnginePath = Path.Combine(RepoRoot, "Redeploy.Core", "Engine", "RespawnEngine.cs");
    private static readonly string GoldenPath = Path.Combine(RepoRoot, "Redeploy.Tests", "Golden", "RunRespawn.order.txt");

    // ---------------------------------------------------------------- golden order

    [Fact]
    public void RunRespawnKeepsItsGoldenOrder()
    {
        var actual = SourceGuards.RunRespawnOutline(File.ReadAllText(EnginePath));

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            var header = new[]
            {
                "# RespawnEngine.RunRespawn, one statement per line (Redeploy.Tests/SourceGuards.cs).",
                "# The order is the respawn's contract with every driver and consumer (docs/RESPAWN-API.md 3.2, 8.4).",
                $"# Intended change: set {UpdateVariable}=1, run dotnet test once, and review this file's diff.",
                "# Inert timing statements (RespawnPipelineTiming) are not listed.",
            };
            File.WriteAllLines(GoldenPath, header.Concat(actual));
        }

        Assert.True(File.Exists(GoldenPath), $"{GoldenPath} is missing; create it with {UpdateVariable}=1");
        var expected = File.ReadAllLines(GoldenPath).Where(line => !line.StartsWith("#")).ToList();
        Assert.True(expected.SequenceEqual(actual), Describe(expected, actual));
    }

    [Fact]
    public void TheGoldenOrderNoticesAReorderARemovalAndAnAddition()
    {
        var source = File.ReadAllText(EnginePath);
        var golden = SourceGuards.RunRespawnOutline(source);

        const string unregister = "UnregisterDeadPlayerFromWorld(deadPlayer);";
        const string mutation = "request.OnBeforeProfileMutation?.Invoke(deadPlayer);";
        var reordered = Replace(Replace(source, unregister, "__A__"), mutation, unregister).Replace("__A__", mutation);
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(reordered)), "a swapped teardown step went unnoticed");

        var removed = Replace(source, "bots.AddActivePLayer(newPlayer);", "");
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(removed)), "a removed rewire step went unnoticed");

        var added = Replace(source, "RespawnEngineState.LastRespawnedPlayer = newPlayer;",
            "RespawnEngineState.LastRespawnedPlayer = newPlayer; RaidUiHelper.ClearBlackScreen();");
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(added)), "an added step went unnoticed");

        var commentsAndLayout = Replace(source, "ResetProfileHealth(game.Profile, request.HealthPercent);",
            "// a comment\n            ResetProfileHealth(\n                game.Profile,   request.HealthPercent);");
        Assert.True(golden.SequenceEqual(SourceGuards.RunRespawnOutline(commentsAndLayout)), "comments or layout changed the order");
    }

    [Fact]
    public void TimingStatementsAreLeftOutOnlyWhileTheyAreInert()
    {
        var source = File.ReadAllText(EnginePath);
        var golden = SourceGuards.RunRespawnOutline(source);
        const string anchor = "ResetProfileHealth(game.Profile, request.HealthPercent);";

        var timed = Replace(source, "var game = request.Game;",
                "var timing = RespawnPipelineTiming.Begin(request.Game, 3, \"death\"); var game = request.Game;");
        timed = Replace(timed, anchor, "timing.Mark(\"teardown\"); " + anchor + " timing.Mark(RespawnApi.StagePrepare, deadPlayer);");
        Assert.True(golden.SequenceEqual(SourceGuards.RunRespawnOutline(timed)), "inert timing statements changed the order");

        var hiddenWork = Replace(source, anchor, anchor + " timing.Mark(\"x\", bots.AddActivePLayer(newPlayer));");
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(hiddenWork)), "a call hidden in a timing argument was ignored");

        var assignment = Replace(source, anchor, anchor + " timing.Mark(game = null);");
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(assignment)), "an assignment hidden in a timing argument was ignored");

        var otherLocal = Replace(source, anchor, anchor + " measure.Mark(\"x\");");
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(otherLocal)), "a call on another local was ignored");
    }

    // ---------------------------------------------------------------- events

    [Fact]
    public void CoreRaisesItsPublicEventsOnlyThroughTheDispatcher()
    {
        var violations = SourceGuards.DirectEventRaises(CoreSources());
        Assert.True(violations.Count == 0, "Raise these through CoreSignals (SignalDispatcher):\n" + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("RespawnEngine.cs", "RaiseRespawned(newPlayer);", "OnPlayerRespawned?.Invoke(newPlayer);")]
    [InlineData("RespawnEngine.cs", "RaiseRespawned(newPlayer);", "OnPlayerRespawned.Invoke(newPlayer);")]
    [InlineData("RespawnEngine.cs", "RaiseRespawned(newPlayer);", "OnPlayerRespawned(newPlayer);")]
    [InlineData("RespawnEngine.cs", "RaiseRespawned(newPlayer);", "RespawnEngine.OnPlayerRespawned?.Invoke(newPlayer);")]
    [InlineData("RespawnEngine.cs", "RaiseRespawned(newPlayer);", "foreach (var h in OnPlayerRespawned.GetInvocationList()) { }")]
    [InlineData("RespawnApi.cs", "CoreSignals.Respawn.Raise(Replaced, h => h(oldBody, newBody, reason), nameof(Replaced));", "Replaced?.Invoke(oldBody, newBody, reason);")]
    [InlineData("RaidLifecycle.cs", "RaiseRaidStarted(__instance);", "RaidLifecycle.OnRaidStarted?.Invoke(__instance);")]
    public void ADirectRaiseIsCaught(string file, string original, string fault)
    {
        var sources = CoreSources();
        Assert.Contains(sources, source => Path.GetFileName(source.Path) == file);
        var faulted = sources.Select(source => Path.GetFileName(source.Path) == file ? (source.Path, Replace(source.Source, original, fault)) : source);
        Assert.NotEmpty(SourceGuards.DirectEventRaises(faulted));
    }

    [Fact]
    public void AMethodSharingAnEventsNameIsNotARaise()
    {
        const string sample = "static class RaidLifecycle { public static event Action OnRaidEnded; static void End(IDriver driver) { driver.OnRaidEnded(); } }";
        Assert.Empty(SourceGuards.DirectEventRaises(new[] { ("sample.cs", sample) }));
    }

    // ---------------------------------------------------------------- Freecam

    [Fact]
    public void FreecamPatchesAreEnabledOnlyInsideATry()
    {
        var (guarded, unguarded) = SourceGuards.FreecamEnableSites(CoreSources());
        Assert.True(unguarded.Count == 0, "EnablePatchesIfPresent outside a catch-all try: " + string.Join(", ", unguarded));
        // A guard over zero calls proves nothing: if the call moved or was renamed, say so.
        Assert.True(guarded > 0, "no FreecamCompatHelper.EnablePatchesIfPresent call found in Core; update this guard");
    }

    [Theory]
    [InlineData("FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}", "}\ncatch (Exception ex)\n{\n}\nFreecamCompatHelper.EnablePatchesIfPresent();")]
    [InlineData("FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}", "}\ncatch (Exception ex)\n{\nFreecamCompatHelper.EnablePatchesIfPresent();\n}")]
    [InlineData("FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}", "Action later = () => FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}\nlater();")]
    [InlineData("FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}", "FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (InvalidOperationException ex)\n{\n}")]
    [InlineData("FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}", "FreecamCompatHelper.EnablePatchesIfPresent();\n}\nfinally\n{\n}")]
    public void AnUnguardedFreecamEnableIsCaught(string original, string fault)
    {
        var sample = "class RaidLifecycle { static void Postfix() { try\n{\n" + original + " } }";
        var (guarded, unguarded) = SourceGuards.FreecamEnableSites(new[] { ("sample.cs", sample) });
        Assert.Equal((1, 0), (guarded, unguarded.Count));

        var faulted = "class RaidLifecycle { static void Postfix() { try\n{\n" + fault + " } }";
        Assert.NotEmpty(SourceGuards.FreecamEnableSites(new[] { ("faulted.cs", faulted) }).Unguarded);
    }

    // ---------------------------------------------------------------- helpers

    private static List<(string Path, string Source)> CoreSources()
        => Directory.GetFiles(Path.Combine(RepoRoot, "Redeploy.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                           && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(path => (path, File.ReadAllText(path)))
            .ToList();

    private static string Replace(string source, string anchor, string replacement)
    {
        Assert.True(source.Contains(anchor), "fault anchor missing from the source: " + anchor);
        return source.Replace(anchor, replacement);
    }

    private static string Describe(List<string> expected, List<string> actual)
    {
        var index = 0;
        while (index < expected.Count && index < actual.Count && expected[index] == actual[index])
            index++;
        return $"RunRespawn no longer matches {GoldenPath}.\n"
               + $"First difference at statement {index + 1}:\n"
               + $"  golden: {(index < expected.Count ? expected[index] : "<end>")}\n"
               + $"  source: {(index < actual.Count ? actual[index] : "<end>")}\n"
               + $"If the change is intended, run dotnet test once with {UpdateVariable}=1 and review the diff.";
    }
}
