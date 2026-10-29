using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            // Never a pass: a variable left set in a shell or on a build agent would otherwise bless
            // every later change to the order without anyone looking at it.
            Assert.Fail($"{GoldenPath} was rewritten. Review its diff, then run the tests again without {UpdateVariable}.");
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

        var commentsAndLayout = Replace(source, "ResetProfileHealth(replacementProfile ?? game.Profile, request.HealthPercent);",
            "// a comment\n            ResetProfileHealth(\n                replacementProfile ?? game.Profile,   request.HealthPercent);");
        Assert.True(golden.SequenceEqual(SourceGuards.RunRespawnOutline(commentsAndLayout)), "comments or layout changed the order");
    }

    [Fact]
    public void BotDeathSubscribersRunBeforeFinalStopDisposesTheirRegistry()
    {
        var source = File.ReadAllText(EnginePath);
        Assert.Equal(new[] { "bot-death", "stop" }, DispatchDeathInProductionSubscriptionOrder(source, finalDeath: true));
        Assert.Equal(new[] { "bot-death", "keep-alive" }, DispatchDeathInProductionSubscriptionOrder(source, finalDeath: false));

        // The previous order reproduces PFT's disposed-singleton failure even though Stop
        // unsubscribes OnDead: the multicast invocation already captured that delegate.
        const string subscribe = "LocalGameAccessor.SubscribeDeathHandler(game, newPlayer);";
        const string add = "bots.AddActivePLayer(newPlayer);";
        var regressed = Replace(Replace(source, subscribe, ""), add, subscribe + " " + add);
        Assert.Throws<NullReferenceException>(() => DispatchDeathInProductionSubscriptionOrder(regressed, finalDeath: true));
    }

    [Fact]
    public void FailedBotRewireStillInstallsTheNativeDeathHandler()
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(EnginePath)).GetRoot();
        var subscribe = tree.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression.ToString() == "LocalGameAccessor.SubscribeDeathHandler");
        var cleanup = subscribe.Ancestors().OfType<FinallyClauseSyntax>().Single();
        var guarded = Assert.IsType<TryStatementSyntax>(cleanup.Parent);
        var calls = guarded.Block.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression.ToString()).ToArray();
        Assert.Contains("LocalGameAccessor.GetBotsController", calls);
        Assert.Contains("bots.DestroyInfo", calls);
        Assert.Contains("bots.AddActivePLayer", calls);
        Assert.Empty(subscribe.Ancestors().TakeWhile(node => node != cleanup).OfType<IfStatementSyntax>());
    }

    private static string[] DispatchDeathInProductionSubscriptionOrder(string source, bool finalDeath)
    {
        // Model only the event boundary and PFT's registry lifetime, taking the actual
        // production subscription order from RunRespawn. This is not a native raid test.
        var steps = SourceGuards.RunRespawnOutline(source).Select(line => line.Trim()).Where(line =>
            line == "LocalGameAccessor.SubscribeDeathHandler(game, newPlayer);"
            || line == "bots.AddActivePLayer(newPlayer);").ToArray();
        Assert.Equal(2, steps.Length);
        var calls = new List<string>();
        Action died = null;
        var registry = calls;
        Action onBotDeath = () => registry.Add("bot-death");
        foreach (var step in steps)
        {
            if (step == "bots.AddActivePLayer(newPlayer);")
                died += onBotDeath;
            else
                died += () =>
                {
                    calls.Add(finalDeath ? "stop" : "keep-alive");
                    if (!finalDeath) return;
                    registry = null;
                    died -= onBotDeath;
                };
        }
        died();
        return calls.ToArray();
    }

    [Fact]
    public void TimingStatementsAreLeftOutOnlyWhileTheyAreInert()
    {
        var source = File.ReadAllText(EnginePath);
        var golden = SourceGuards.RunRespawnOutline(source);
        const string anchor = "ResetProfileHealth(replacementProfile ?? game.Profile, request.HealthPercent);";

        var timed = Replace(source, "var game = request.Game;",
                "var game = request.Game; var timing = RespawnPipelineTiming.Begin(game, 3, \"death\", 2.5f, null);");
        timed = Replace(timed, anchor, "timing.Mark(\"teardown\"); " + anchor + " timing.Mark(\"profile\", deadPlayer, request);");
        Assert.True(golden.SequenceEqual(SourceGuards.RunRespawnOutline(timed)), "inert timing statements changed the order");

        foreach (var (argument, why) in new[]
                 {
                     ("bots.AddActivePLayer(newPlayer)", "a call"),
                     ("game = null", "an assignment"),
                     ("RespawnEngineState.LastRespawnedPlayer", "a property getter behind a member access"),
                     ("() => RaidUiHelper.ClearBlackScreen()", "a lambda"),
                     ("request?.Game", "a conditional access"),
                     ("undeclaredName", "a name that is not one of RunRespawn's locals"),
                     ("x: deadPlayer", "a named argument"),
                 })
        {
            var hidden = Replace(source, anchor, anchor + " timing.Mark(\"x\", " + argument + ");");
            Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(hidden)), $"{why} hidden in a timing argument was ignored");
        }

        var otherLocal = Replace(source, anchor, anchor + " measure.Mark(\"x\");");
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(otherLocal)), "a call on another local was ignored");

        var otherType = Replace(source, "var game = request.Game;", "var game = request.Game; var timing = RaidUiHelper.Begin(game);");
        Assert.False(golden.SequenceEqual(SourceGuards.RunRespawnOutline(otherType)), "a timing local of another type was ignored");
    }

    // ---------------------------------------------------------------- events

    [Fact]
    public void CoreRaisesItsPublicEventsOnlyThroughTheDispatcher()
    {
        var violations = SourceGuards.EventDelegateUsesOutsideDispatcher(CoreSources());
        Assert.True(violations.Count == 0, "Raise these through CoreSignals (SignalDispatcher):\n" + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("RespawnEngine.cs", "OnPlayerRespawned?.Invoke(player)")]
    [InlineData("RespawnEngine.cs", "OnPlayerRespawned.Invoke(player)")]
    [InlineData("RespawnEngine.cs", "OnPlayerRespawned(player)")]
    [InlineData("RespawnEngine.cs", "RespawnEngine.OnPlayerRespawned?.Invoke(player)")]
    [InlineData("RespawnEngine.cs", "this.OnPlayerRespawned.DynamicInvoke(player)")]
    [InlineData("RespawnEngine.cs", "OnPlayerRespawned.GetInvocationList()")]
    [InlineData("RespawnEngine.cs", "System.Threading.Volatile.Read(ref OnPlayerRespawned)?.Invoke(player)")]
    [InlineData("RespawnEngine.cs", "Helper(OnPlayerRespawned)")]
    [InlineData("RespawnEngine.cs", "CoreSignals.Respawn.Raise(h => h(player), OnPlayerRespawned, nameof(OnPlayerRespawned))")]
    [InlineData("RespawnEngine.cs", "Other.Raise(OnPlayerRespawned, h => h(player), nameof(OnPlayerRespawned))")]
    public void ADelegateUseAroundTheDispatcherIsCaught(string file, string fault)
    {
        const string original = "CoreSignals.Respawn.Raise(OnPlayerRespawned, h => h(player), nameof(OnPlayerRespawned))";
        var sources = CoreSources();
        Assert.Contains(sources, source => Path.GetFileName(source.Path) == file);
        var faulted = sources.Select(source => Path.GetFileName(source.Path) == file ? (source.Path, Replace(source.Source, original, fault)) : source);
        Assert.NotEmpty(SourceGuards.EventDelegateUsesOutsideDispatcher(faulted));
    }

    [Fact]
    public void ALocalCopyOfTheDelegateIsCaught()
    {
        const string original = "        => CoreSignals.Respawn.Raise(OnPlayerRespawned, h => h(player), nameof(OnPlayerRespawned));";
        const string fault = "    { var handler = OnPlayerRespawned; handler?.Invoke(player); }";
        var faulted = CoreSources().Select(source => Path.GetFileName(source.Path) == "RespawnEngine.cs" ? (source.Path, Replace(source.Source, original, fault)) : source);
        Assert.NotEmpty(SourceGuards.EventDelegateUsesOutsideDispatcher(faulted));

        const string otherFile = "CoreSignals.Respawn.Raise(Replaced, h => h(oldBody, newBody, reason), nameof(Replaced));";
        var api = CoreSources().Select(source => Path.GetFileName(source.Path) == "RespawnApi.cs" ? (source.Path, Replace(source.Source, otherFile, "Replaced?.Invoke(oldBody, newBody, reason);")) : source);
        Assert.NotEmpty(SourceGuards.EventDelegateUsesOutsideDispatcher(api));
    }

    [Fact]
    public void AMethodSharingAnEventsNameAndSubscriptionsAreNotRaises()
    {
        const string sample = "static class RaidLifecycle { public static event Action OnRaidEnded; "
                              + "static void End(IDriver driver) { driver.OnRaidEnded(); OnRaidEnded += Log; RaidLifecycle.OnRaidEnded -= Log; OnRaidEnded = null; } }";
        Assert.Empty(SourceGuards.EventDelegateUsesOutsideDispatcher(new[] { ("sample.cs", sample) }));
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
    [InlineData("FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}", "FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\nthrow;\n}")]
    [InlineData("FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch (Exception ex)\n{\n}", "FreecamCompatHelper.EnablePatchesIfPresent();\n}\ncatch\n{\nif (ex != null) throw new InvalidOperationException();\n}")]
    public void AnUnguardedFreecamEnableIsCaught(string original, string fault)
    {
        var sample = "class RaidLifecycle { static void Postfix() { try\n{\n" + original + " } }";
        var (guarded, unguarded) = SourceGuards.FreecamEnableSites(new[] { ("sample.cs", sample) });
        Assert.Equal((1, 0), (guarded, unguarded.Count));

        var faulted = "class RaidLifecycle { static void Postfix() { try\n{\n" + fault + " } }";
        Assert.NotEmpty(SourceGuards.FreecamEnableSites(new[] { ("faulted.cs", faulted) }).Unguarded);
    }

    // ---------------------------------------------------------------- replacement profile

    [Fact]
    public void AdaptersNeverTreatTheGamesProfileAsTheOnlyProfileOfABodyUnderConstruction()
    {
        var violations = SourceGuards.GameProfileReadsOutsideTheReplacementRule(CoreSources());
        Assert.True(violations.Count == 0,
            "An own-Scav body is built with the replacement profile before game.Profile changes. Use ReplacementProfileRule.CarriesRaidProfile:\n"
            + string.Join("\n", violations));
        // A guard over zero reads proves nothing: the rule and the audited post-commit readers must still be there.
        var sources = CoreSources();
        Assert.Contains(sources, s => s.Path.EndsWith("RecoilLegacyCompat.cs") && s.Source.Contains("ReplacementProfileRule.CarriesRaidProfile("));
        Assert.Contains(sources, s => s.Path.EndsWith("FovFixCompat.cs") && s.Source.Contains("ReplacementProfileRule.CarriesRaidProfile("));
        // A stale whitelist entry (renamed method, removed read) would silently allow a future read there.
        Assert.Empty(SourceGuards.StaleCommittedGameProfileReaders(sources));
    }

    [Fact]
    public void ARenamedPostCommitReaderMakesItsWhitelistEntryStale()
    {
        var sources = CoreSources();
        var renamed = sources.Select(s => Path.GetFileName(s.Path) == "HealingAutoCancelCompat.cs"
            ? (s.Path, Replace(s.Source, " Current", " CurrentRenamed")) : s);
        Assert.NotEmpty(SourceGuards.StaleCommittedGameProfileReaders(renamed));
    }

    [Theory]
    [InlineData("RecoilLegacyCompat.cs", "ReplacementProfileRule.CarriesRaidProfile(body.Profile, game.Profile, BodyReplacementContext.ExpectedProfile)", "ReferenceEquals(body.Profile, game.Profile)")]
    [InlineData("FovFixCompat.cs", "ReplacementProfileRule.CarriesRaidProfile(body.Profile, Game?.Profile, BodyReplacementContext.ExpectedProfile)", "ReferenceEquals(body.Profile, Game?.Profile)")]
    public void ReintroducingTheGameProfileComparisonIsCaught(string file, string original, string fault)
    {
        var sources = CoreSources();
        Assert.Contains(sources, source => Path.GetFileName(source.Path) == file);
        var faulted = sources.Select(source => Path.GetFileName(source.Path) == file ? (source.Path, Replace(source.Source, original, fault)) : source);
        Assert.NotEmpty(SourceGuards.GameProfileReadsOutsideTheReplacementRule(faulted));
    }

    [Fact]
    public void ANewAdapterReadingTheGamesProfileIsCaughtButTheContextAndRuleCallAreNot()
    {
        var folder = Path.Combine("Redeploy.Core", "Compat");
        const string reads = "class Sample { bool M() => ReferenceEquals(body.Profile, _game.Profile); }";
        const string chained = "class Sample { bool M() => ReferenceEquals(body.Profile, CurrentGame?.Profile); }";
        const string viaRule = "class Sample { bool M() => ReplacementProfileRule.CarriesRaidProfile(body.Profile, game.Profile, BodyReplacementContext.ExpectedProfile); }";
        const string unrelated = "class Sample { bool M() => body.Profile != null && other.Profile == body.Profile; }";
        Assert.NotEmpty(SourceGuards.GameProfileReadsOutsideTheReplacementRule(new[] { (Path.Combine(folder, "Sample.cs"), reads) }));
        Assert.NotEmpty(SourceGuards.GameProfileReadsOutsideTheReplacementRule(new[] { (Path.Combine(folder, "Sample.cs"), chained) }));
        Assert.Empty(SourceGuards.GameProfileReadsOutsideTheReplacementRule(new[] { (Path.Combine(folder, "Sample.cs"), viaRule) }));
        Assert.Empty(SourceGuards.GameProfileReadsOutsideTheReplacementRule(new[] { (Path.Combine(folder, "Sample.cs"), unrelated) }));
        Assert.Empty(SourceGuards.GameProfileReadsOutsideTheReplacementRule(new[] { (Path.Combine(folder, "BodyReplacementContext.cs"), reads) }));
        // Outside Compat (engine, view) the engine's own ordering is its business.
        Assert.Empty(SourceGuards.GameProfileReadsOutsideTheReplacementRule(new[] { (Path.Combine("Redeploy.Core", "Engine", "Sample.cs"), reads) }));
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
