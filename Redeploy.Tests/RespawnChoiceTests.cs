using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Redeploy.Tests;

public sealed class RespawnChoiceTests
{
    [Theory]
    [InlineData("pmc-enabled-near")]
    [InlineData("pmc-enabled-infil")]
    [InlineData("pmc-unavailable")]
    [InlineData("pmc-disabled")]
    [InlineData("ordinary-scav")]
    [InlineData("scav-ready")]
    [InlineData("scav-disabled")]
    [InlineData("scav-cooldown")]
    [InlineData("scav-patch-unavailable")]
    [InlineData("scav-from-scav")]
    [InlineData("captured-pmc-choice")]
    [InlineData("captured-scav-choice")]
    public void ExplicitChoiceUsesTheProductionEntryPointsAndCoroutine(string scenario)
        => Assert.Equal("PASS", Run(Harness.Value, scenario, false));

    [Theory]
    [InlineData("missing-game", false)] [InlineData("missing-game", true)]
    [InlineData("mod-disabled", false)] [InlineData("mod-disabled", true)]
    [InlineData("no-pending-choice", false)] [InlineData("no-pending-choice", true)]
    [InlineData("outside-raid", false)] [InlineData("outside-raid", true)]
    [InlineData("given-up", false)] [InlineData("given-up", true)]
    [InlineData("already-running", false)] [InlineData("already-running", true)]
    [InlineData("stopping", false)] [InlineData("stopping", true)]
    [InlineData("alive", false)] [InlineData("alive", true)]
    [InlineData("missing-body", false)] [InlineData("missing-body", true)]
    [InlineData("missing-health", false)] [InlineData("missing-health", true)]
    [InlineData("switched", false)] [InlineData("switched", true)]
    [InlineData("limit", false)] [InlineData("limit", true)]
    [InlineData("missing-item", false)] [InlineData("missing-item", true)]
    [InlineData("raid-ended-while-waiting", false)] [InlineData("raid-ended-while-waiting", true)]
    public void InvalidChoicesDoNotStartPreparationOrBodyReplacement(string scenario, bool scav)
        => Assert.Equal("PASS", Run(Harness.Value, scenario, scav));

    [Fact]
    public void AdoptedScavDeathIsNotInterceptedEvenWithUnlimitedRespawns()
        => Assert.Equal("PASS", Run(Harness.Value, "driver-switched", false));

    [Fact]
    public void NegativeControlRejectsRoutingOrdinaryChoiceByScavAvailability()
    {
        var mutant = BuildHarness(source => source.Replace(
            "BeginRespawn(spawnMode, continueAsScav: false)",
            "BeginRespawn(spawnMode, continueAsScav: PmcScavSession.IsOffered)"));
        Assert.NotEqual("PASS", Run(mutant, "pmc-enabled-near", false));
    }

    [Fact]
    public void NegativeControlRejectsClaimingAnotherDeathAfterScavSwitch()
    {
        var mutant = BuildHarness(source => source.Replace(
            "if (PmcScavSession.HasSwitched(game))\r\n            return false;", "")
            .Replace("if (PmcScavSession.HasSwitched(game))\n            return false;", ""));
        Assert.NotEqual("PASS", Run(mutant, "driver-switched", false));
    }

    private static readonly Lazy<Type> Harness = new(() => BuildHarness());
    private static readonly object HarnessLock = new();

    private static string Run(Type harness, string scenario, bool scav)
    {
        // The compiled boundary models one game; xUnit may run test cases concurrently.
        lock (HarnessLock)
            return (string)harness.GetMethod("Run")!.Invoke(null, [scenario, scav]);
    }

    private static Type BuildHarness(Func<string, string> mutate = null)
    {
        var service = Root("Services/RespawnService.cs");
        var methods = service.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is "BeginRespawn" or "BeginScavRespawn"
                or "RespawnCoroutine" or "BuildSpawnSelector").ToArray();
        Assert.Equal(5, methods.Length);
        var offered = Root("Services/PmcScavSession.cs").DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(property => property.Identifier.ValueText == "IsOffered");
        var driver = Root("Services/RedeployDriver.cs").DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "ShouldIntercept");
        // Compile the real selection, cooldown, request-composition and driver methods
        // unchanged. Only EFT/Unity, inventory, networking and engine boundaries are stubs.
        string production = "using System; using System.Collections; namespace Redeploy.Services { "
            + "public static partial class RespawnService { "
            + string.Join(Environment.NewLine, methods.Select(method => method.ToFullString())) + " } "
            + "public static partial class PmcScavSession { " + offered.ToFullString() + " } "
            + "public static class DriverProbe { " + driver.ToFullString()
            + " public static bool Invoke(LocalGame game) => ShouldIntercept(game); } }";
        if (mutate != null)
        {
            var changed = mutate(production);
            Assert.NotEqual(production, changed);
            production = changed;
        }
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("RespawnChoiceHarness_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(production), CSharpSyntaxTree.ParseText(Boundary)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("ChoiceHarness", throwOnError: true)!;
    }

    private static SyntaxNode Root(string relative)
        => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot,
            "Redeploy.Client", relative))).GetRoot();

    private const string Boundary = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using Redeploy.Services;
        namespace Redeploy.Services {
            public enum RespawnLocationMode { NearCorpse, InfilSpawn }
            public enum GameStatus { Started, Stopping }
            public enum EPlayerSide { Bear, Savage }
            public sealed class Profile { public Info Info = new Info(); }
            public sealed class Info { public EPlayerSide Side; }
            public sealed class Health { public bool IsAlive; }
            public sealed class LocalPlayer { public Health HealthController = new Health(); public bool IsYourPlayer = true; }
            public sealed class Location { public string Id = "factory4_day"; }
            public sealed class LocalGame {
                public GameStatus Status = GameStatus.Started;
                public LocalPlayer LocalPlayer = new LocalPlayer();
                public Profile Profile = new Profile();
                public Location Location = new Location();
                private readonly Queue<IEnumerator> pending = new Queue<IEnumerator>();
                public int Starts;
                public object StartCoroutine(IEnumerator coroutine) {
                    Starts++;
                    if (coroutine.MoveNext()) pending.Enqueue(coroutine);
                    return new object();
                }
                public void Pump() {
                    int steps = 0;
                    while (pending.Count > 0) {
                        if (++steps > 50) throw new Exception("Coroutines did not complete.");
                        var next = pending.Dequeue();
                        if (next.MoveNext()) pending.Enqueue(next);
                    }
                }
            }
            public sealed class Entry<T> { public T Value; public Entry(T value) { Value = value; } }
            public static class Settings {
                public static Entry<bool> Enabled = new Entry<bool>(true), RespawnAsScav = new Entry<bool>(true), EnablePipelineTiming = new Entry<bool>(false);
                public static Entry<float> RespawnCooldownSeconds = new Entry<float>(5), RespawnHealthPercent = new Entry<float>(100), SpawnProtectionSeconds = new Entry<float>(10), CorpseSpawnDistanceMeters = new Entry<float>(20);
                public static int Maximum = 5;
                public static bool HasRespawnsRemaining(int used) => Maximum <= 0 || used < Maximum;
            }
            public static class RespawnSessionState {
                public static LocalGame CurrentLocalGame;
                public static bool PendingRespawnChoice, IsInRaid, HasGivenUp, IsRespawnInProgress, RespawnItemAvailable;
                public static int RespawnsUsed;
                public static float LastDeathTime, LastCorpsePosition;
                public static RespawnLocationMode SelectedSpawnMode;
                public static void RegisterRespawnUsed() { RespawnsUsed++; PendingRespawnChoice = false; }
            }
            public sealed class RespawnPromptPanel {
                public static RespawnPromptPanel Instance = new RespawnPromptPanel();
                public int Hides;
                public void Hide() { Hides++; }
            }
            public static class Time { public static float time = 100; }
            public sealed class WaitForSeconds { public WaitForSeconds(float seconds) { } }
            public static class RespawnCooldown { public static float Remaining(float configured, float death, float now) => configured; }
            public static class Plugin { public static Logger ModLogger = new Logger(); }
            public sealed class Logger { public void LogDebug(object value) { } public void LogInfo(object value) { } public void LogWarning(object value) { } public void LogError(object value) { } }
            public interface ISpawnSelector { }
            public sealed class NearPositionSpawnSelector : ISpawnSelector { public NearPositionSpawnSelector(Func<float> position, Func<float> distance) { } }
            public sealed class InfilSpawnSelector : ISpawnSelector { public InfilSpawnSelector(ISpawnSelector fallback) { } }
            public sealed class ScavSpawnSelector : ISpawnSelector { }
            public sealed class SpawnProtectionOptions { public float DurationSeconds; public bool EndEarlyOnWeapon, StowAutoDrawnWeapon, ShowToasts; }
            public sealed class RespawnRequest {
                public LocalGame Game; public LocalPlayer DeadPlayer; public Profile ReplacementProfile;
                public ISpawnSelector SpawnSelector; public Action<LocalPlayer> OnBeforeProfileMutation;
                public Action<Profile, LocalPlayer> EquipmentStrategy; public float HealthPercent;
                public bool EnablePipelineTiming; public SpawnProtectionOptions Protection;
            }
            public static class EquipmentSurgeon { public static void ReplaceEquipment(Profile profile, LocalPlayer player) { } }
            public static class RespawnEngine {
                public static RespawnRequest Request;
                public static IEnumerator RunRespawn(RespawnRequest request, Action<Exception> completed) {
                    Request = request;
                    if (request.ReplacementProfile != null) request.Game.Profile = request.ReplacementProfile;
                    completed(null); yield break;
                }
            }
            public static partial class PmcScavSession {
                public static bool ForcedSwitched; public static string UnavailableReason;
                public static int Preparations, Completions, Cancellations;
                private static Profile selected;
                public static bool HasSwitched(LocalGame game) => ForcedSwitched || (selected != null && ReferenceEquals(game.Profile, selected));
                public static IEnumerator Prepare(LocalGame game, Action<Profile, ISpawnSelector, Exception> completed) {
                    Preparations++; selected = new Profile(); selected.Info.Side = EPlayerSide.Savage;
                    completed(selected, new ScavSpawnSelector(), null); yield break;
                }
                public static void CompleteSwitch(LocalGame game) { Completions++; }
                public static void CancelPrepared() { Cancellations++; selected = null; }
                public static void NotifyUnavailable(string message) { }
                public static void Reset() { ForcedSwitched = false; selected = null; UnavailableReason = null; Preparations = Completions = Cancellations = 0; }
            }
            public static partial class RespawnService {
                public static bool RequiredItem = true;
                private static bool HasRequiredRespawnItem(LocalPlayer player) => RequiredItem;
                private static void ConsumeRespawnItem(LocalPlayer player) { }
                public static void GiveUpRaid() { RespawnSessionState.HasGivenUp = true; }
            }
            public static class BodyReplacementCompatibility {
                public static string CachedRejectionReason => null;
                public static string GetRejectionReason() => null;
            }
        }
        public static class ChoiceHarness {
            private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
            public static string Run(string scenario, bool scav) {
                try { Execute(scenario, scav); return "PASS"; }
                catch (Exception ex) { return ex.Message; }
            }
            private static void Execute(string scenario, bool scav) {
                var game = new LocalGame();
                Settings.Enabled.Value = Settings.RespawnAsScav.Value = true; Settings.Maximum = 5;
                RespawnSessionState.CurrentLocalGame = game; RespawnSessionState.PendingRespawnChoice = RespawnSessionState.IsInRaid = true;
                RespawnSessionState.HasGivenUp = RespawnSessionState.IsRespawnInProgress = false;
                RespawnSessionState.RespawnsUsed = 0; RespawnSessionState.LastDeathTime = 99;
                RespawnSessionState.RespawnItemAvailable = true; RespawnSessionState.SelectedSpawnMode = RespawnLocationMode.NearCorpse;
                RespawnPromptPanel.Instance.Hides = 0; RespawnService.RequiredItem = true;
                RespawnEngine.Request = null; PmcScavSession.Reset();
                bool reject = false; var location = RespawnLocationMode.NearCorpse;
                switch (scenario) {
                    case "pmc-enabled-near": break;
                    case "pmc-enabled-infil": location = RespawnLocationMode.InfilSpawn; break;
                    case "pmc-unavailable": PmcScavSession.UnavailableReason = "cooldown"; break;
                    case "pmc-disabled": Settings.RespawnAsScav.Value = false; break;
                    case "ordinary-scav": game.Profile.Info.Side = EPlayerSide.Savage; break;
                    case "scav-ready": scav = true; break;
                    case "scav-disabled": scav = true; Settings.RespawnAsScav.Value = false; reject = true; break;
                    case "scav-cooldown": scav = true; PmcScavSession.UnavailableReason = "cooldown"; reject = true; break;
                    case "scav-patch-unavailable": scav = true; PmcScavSession.UnavailableReason = "unsupported contract"; reject = true; break;
                    case "scav-from-scav": scav = true; game.Profile.Info.Side = EPlayerSide.Savage; reject = true; break;
                    case "captured-pmc-choice": Settings.RespawnAsScav.Value = false; break;
                    case "captured-scav-choice": scav = true; break;
                    case "missing-game": RespawnSessionState.CurrentLocalGame = null; reject = true; break;
                    case "mod-disabled": Settings.Enabled.Value = false; reject = true; break;
                    case "no-pending-choice": RespawnSessionState.PendingRespawnChoice = false; reject = true; break;
                    case "outside-raid": RespawnSessionState.IsInRaid = false; reject = true; break;
                    case "given-up": RespawnSessionState.HasGivenUp = true; reject = true; break;
                    case "already-running": RespawnSessionState.IsRespawnInProgress = true; reject = true; break;
                    case "stopping": game.Status = GameStatus.Stopping; reject = true; break;
                    case "alive": game.LocalPlayer.HealthController.IsAlive = true; reject = true; break;
                    case "missing-body": game.LocalPlayer = null; reject = true; break;
                    case "missing-health": game.LocalPlayer.HealthController = null; reject = true; break;
                    case "switched": PmcScavSession.ForcedSwitched = true; reject = true; break;
                    case "limit": RespawnSessionState.RespawnsUsed = Settings.Maximum; reject = true; break;
                    case "missing-item": RespawnService.RequiredItem = false; reject = true; break;
                    case "raid-ended-while-waiting": break;
                    case "driver-switched":
                        Settings.Maximum = 0; Check(DriverProbe.Invoke(game), "Baseline death not eligible.");
                        PmcScavSession.ForcedSwitched = true;
                        Check(!DriverProbe.Invoke(game), "Adopted Scav death was claimed again.");
                        Settings.RespawnAsScav.Value = false;
                        Check(!DriverProbe.Invoke(game), "Disabling Scav option allowed another life."); return;
                    default: throw new Exception("Unknown scenario " + scenario);
                }
                if (scav) RespawnService.BeginScavRespawn(); else RespawnService.BeginRespawn(location);
                if (reject) {
                    Check(game.Starts == 0 && RespawnEngine.Request == null && PmcScavSession.Preparations == 0, "Rejected choice started work.");
                    Check(RespawnPromptPanel.Instance.Hides == 0, "Rejected choice hid the prompt.");
                    if (scenario == "missing-item") Check(!RespawnSessionState.RespawnItemAvailable, "Missing cost item was not refreshed.");
                    return;
                }
                Check(RespawnSessionState.IsRespawnInProgress && RespawnPromptPanel.Instance.Hides == 1, "Accepted choice did not begin once.");
                Check(RespawnEngine.Request == null && PmcScavSession.Preparations == 0, "Preparation ran inside the selection click.");
                if (scenario == "captured-pmc-choice") Settings.RespawnAsScav.Value = true;
                if (scenario == "captured-scav-choice") Settings.RespawnAsScav.Value = false;
                if (scenario == "raid-ended-while-waiting") { game.Status = GameStatus.Stopping; RespawnSessionState.IsInRaid = false; }
                game.Pump();
                if (scenario == "raid-ended-while-waiting") {
                    Check(RespawnEngine.Request == null && PmcScavSession.Preparations == 0, "Stopped raid still replaced the body."); return;
                }
                var request = RespawnEngine.Request;
                Check(request != null && RespawnSessionState.RespawnsUsed == 1, "Choice did not finish exactly one replacement.");
                if (scav) {
                    Check(PmcScavSession.Preparations == 1 && PmcScavSession.Completions == 1, "Scav preparation/switch was not used exactly once.");
                    Check(request.ReplacementProfile?.Info.Side == EPlayerSide.Savage && request.SpawnSelector is ScavSpawnSelector, "Scav identity/spawn missing.");
                    Check(RespawnSessionState.SelectedSpawnMode == RespawnLocationMode.InfilSpawn, "Scav did not select infil.");
                    Check(request.EquipmentStrategy == null && request.OnBeforeProfileMutation == null, "Scav also ran naked-PMC equipment or cost mutation.");
                } else {
                    Check(PmcScavSession.Preparations == 0 && request.ReplacementProfile == null, "Ordinary selection was routed to Scav.");
                    Check(request.EquipmentStrategy != null && request.OnBeforeProfileMutation != null, "Ordinary inventory/cost behavior missing.");
                    Check(RespawnSessionState.SelectedSpawnMode == location, "Ordinary selected location changed.");
                    Check(location == RespawnLocationMode.NearCorpse ? request.SpawnSelector is NearPositionSpawnSelector : request.SpawnSelector is InfilSpawnSelector,
                        "Ordinary spawn selector changed.");
                }
                Check(!RespawnSessionState.PendingRespawnChoice && !RespawnSessionState.IsRespawnInProgress, "Completed choice left stale prompt state.");
            }
        }
        """;
}
