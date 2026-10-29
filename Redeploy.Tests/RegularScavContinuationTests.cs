using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Redeploy.Services;
using Xunit;

namespace Redeploy.Tests;

public sealed class RegularScavContinuationTests
{
    [Theory]
    [InlineData(999, 1000, 0)]
    [InlineData(1000, 1000, 1)]
    [InlineData(1001, 1000, 2)]
    [InlineData(1000.5, 1000, 1)]
    [InlineData(0, 1000, 0)]
    public void CooldownFollowsNativeStrictExpiry(double lockedUntil, double now, double seconds)
        => Assert.Equal(seconds, RegularScavPolicy.CooldownSeconds(lockedUntil, now));

    [Theory]
    [InlineData(double.NaN, 1000)]
    [InlineData(double.PositiveInfinity, 1000)]
    [InlineData(double.NegativeInfinity, 1000)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    public void InvalidCooldownCannotUnlockScav(double lockedUntil, double now)
        => Assert.Equal(double.PositiveInfinity, RegularScavPolicy.CooldownSeconds(lockedUntil, now));

    [Theory]
    [InlineData("pmc", "scav", "scav", true)]
    [InlineData("pmc", "scav", "generated", false)]
    [InlineData("pmc", "pmc", "pmc", false)]
    [InlineData("pmc", null, null, false)]
    [InlineData(null, "scav", "scav", false)]
    public void OnlyTheAccountScavMayReplaceThePmc(string pmc, string account, string candidate, bool expected)
        => Assert.Equal(expected, RegularScavPolicy.IsAccountScav(pmc, account, candidate));

    [Theory]
    [InlineData(true, true, "pmc", "scav", "pmc", true)]
    [InlineData(true, true, "pmc", "scav", "scav", true)]
    [InlineData(false, true, "pmc", "scav", "scav", false)]
    [InlineData(true, false, "pmc", "scav", "scav", false)]
    [InlineData(true, true, "pmc", "scav", "other", false)]
    [InlineData(true, true, "pmc", "pmc", "pmc", false)]
    [InlineData(true, true, null, "scav", "scav", false)]
    public void ResultFenceRequiresTheOwnedEndingSession(bool ending, bool sameSession,
        string pmc, string scav, string result, bool expected)
        => Assert.Equal(expected, RegularScavPolicy.OwnsResult(ending, sameSession, pmc, scav, result));

    [Theory]
    [InlineData("snapshot-restores")]
    [InlineData("serializer-failure-restores")]
    [InlineData("exact-item-commit")]
    [InlineData("lost-item-rejects")]
    [InlineData("failed-removal-rejects")]
    [InlineData("broken-rollback-rejects")]
    [InlineData("duplicate-commit-rejects")]
    [InlineData("taken-while-waiting")]
    [InlineData("no-cost")]
    public void CostReservationUsesRealClientAlgorithmAgainstInventoryBoundary(string scenario)
    {
        // The production source is compiled unchanged. The boundary only replaces EFT's
        // container operations, so these tests do not claim to exercise native containers.
        var result = (string)CostHarness.Value.GetMethod("Run")!.Invoke(null, new object[] { scenario });
        Assert.Equal("PASS", result);
    }

    private static readonly Lazy<Type> CostHarness = new(BuildCostHarness);

    [Fact]
    public void ScavClockExcludesPmcLifeAndDeathPromptPreload()
    {
        var body = new object();
        var clock = new RegularScavLifeClock();
        // PMC died at 300s; prompt/preload took until 800s. Only the new life counts.
        Assert.True(clock.TryStart(body, 800));
        Assert.True(clock.TryFreeze(body, 850.75, out var seconds));
        Assert.Equal(50.75f, seconds);
        Assert.Equal(50, (int)seconds); // Native request stores integral seconds.
        Assert.Equal(TimeSpan.FromSeconds(50.75), TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void ClockRequiresExactPhysicalBodyAndCannotBeResetOrExtendedByRetries()
    {
        var body = new object();
        var other = new object();
        var clock = new RegularScavLifeClock();
        Assert.True(clock.TryStart(body, 500));
        Assert.False(clock.TryStart(other, 700));
        Assert.True(clock.TryStart(body, 700)); // Same-body repeat preserves the first start.
        Assert.False(clock.TryFreeze(other, 900, out _));
        Assert.False(clock.TryRead(body, out _));
        Assert.True(clock.TryFreeze(body, 800, out var first));
        Assert.Equal(300f, first);
        Assert.True(clock.TryFreeze(body, 999, out var retry));
        Assert.Equal(first, retry);
        Assert.False(clock.TryRead(other, out _));
        Assert.True(clock.TryRead(body, out var read));
        Assert.Equal(first, read);
    }

    [Fact]
    public void NativeScavTimeIsRewrittenBeforeLifetimeAndExitConditionCounters()
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot,
            "Redeploy.Client", "Patches", "PmcScavPatches.cs"));
        Assert.True(HasBeforeNativeStatisticsHook(source));
        // A postfix would leave already-mutated stats/quest counters wrong even if
        // the outgoing playTime were later corrected. The guard must reject it.
        var mutant = source.Replace("[PatchPrefix]\r\n        private static void Prefix(LocalPlayer __instance, ref float pastTime)",
                "[PatchPostfix]\r\n        private static void Prefix(LocalPlayer __instance, ref float pastTime)")
            .Replace("[PatchPrefix]\n        private static void Prefix(LocalPlayer __instance, ref float pastTime)",
                "[PatchPostfix]\n        private static void Prefix(LocalPlayer __instance, ref float pastTime)");
        Assert.NotEqual(source, mutant);
        Assert.False(HasBeforeNativeStatisticsHook(mutant));
    }

    private static bool HasBeforeNativeStatisticsHook(string source)
    {
        var patch = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "PmcScavStatisticsEndPatch");
        var prefix = patch.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Prefix");
        return prefix.AttributeLists.SelectMany(list => list.Attributes).Any(attribute => attribute.Name.ToString() == "PatchPrefix")
            && prefix.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "pastTime"
                && parameter.Modifiers.Any(token => token.ValueText == "ref"))
            && prefix.ToFullString().Contains("RewriteScavSessionTime(__instance, ref pastTime)", StringComparison.Ordinal)
            && patch.ToFullString().Contains("typeof(LocalPlayer), nameof(LocalPlayer.OnGameSessionEnd)", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(100, 99)]
    [InlineData(100, double.NaN)]
    [InlineData(100, double.PositiveInfinity)]
    [InlineData(100, 86501)]
    public void InvalidClockNeverInventsScavPlayTime(double start, double end)
    {
        var body = new object();
        var clock = new RegularScavLifeClock();
        bool started = clock.TryStart(body, start);
        bool ended = clock.TryFreeze(body, end, out _);
        Assert.False(started && ended);
        Assert.False(clock.TryRead(body, out _));
    }

    [Fact]
    public void NativeTimerCleanupAllowsRebuildAndPreservesForeignTimers()
    {
        var eventTimer = new object();
        var btrTimer = new object();
        var foreignTimer = new object();
        var timers = new Dictionary<string, object> {
            ["EventTimer"] = eventTimer, ["BtrTimer"] = btrTimer, ["OtherModTimer"] = foreignTimer
        };
        // Negative control: native Close leaves this registry intact, so the next
        // native SetTime's EventTimer Add throws without our selective cleanup.
        Assert.Throws<ArgumentException>(() => timers.Add("EventTimer", new object()));
        RegularScavPolicy.RemoveOwnedTimer(timers, "EventTimer", eventTimer);
        RegularScavPolicy.RemoveOwnedTimer(timers, "BtrTimer", btrTimer);
        timers.Add("EventTimer", new object());
        timers.Add("BtrTimer", new object());
        Assert.Same(foreignTimer, timers["OtherModTimer"]);
        Assert.Equal(3, timers.Count);
        Assert.NotSame(eventTimer, timers["EventTimer"]);
        Assert.NotSame(btrTimer, timers["BtrTimer"]);
    }

    [Fact]
    public void ReservedTimerKeyOwnedByAnotherObjectIsNotDeleted()
    {
        var native = new object();
        var foreign = new object();
        var timers = new Dictionary<string, object> { ["EventTimer"] = foreign };
        Assert.Throws<InvalidOperationException>(() => RegularScavPolicy.RemoveOwnedTimer(timers, "EventTimer", native));
        Assert.Same(foreign, timers["EventTimer"]);
        RegularScavPolicy.RemoveOwnedTimer(timers, "BtrTimer", null); // Absent native timers are allowed.
        Assert.Single(timers);
    }

    [Fact]
    public void TimerResetFollowsCloseAndPrecedesSetTime()
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot,
            "Redeploy.Client", "Services", "PmcScavSession.cs"));
        Assert.True(HasSafeTimerRebuildOrder(source));
        var mutant = source.Replace("        ScavExtractionUi.RetireNativeTimersAfterClose(timer);", "");
        Assert.False(HasSafeTimerRebuildOrder(mutant));
    }

    private static bool HasSafeTimerRebuildOrder(string source)
    {
        var method = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "BindScavExits").ToFullString();
        var validate = method.IndexOf("ScavExtractionUi.ValidateOwnedTimers(timer)", StringComparison.Ordinal);
        var close = method.IndexOf("timer.Close()", StringComparison.Ordinal);
        var retire = method.IndexOf("ScavExtractionUi.RetireNativeTimersAfterClose(timer)", StringComparison.Ordinal);
        var setTime = method.IndexOf("timer.SetTime(", StringComparison.Ordinal);
        return validate >= 0 && close > validate && retire > close && setTime > retire;
    }

    [Fact]
    public void RealCostCommitFollowsCooldownReservationAndPreload()
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot,
            "Redeploy.Client", "Services", "PmcScavSession.cs"));
        Assert.True(HasSafePrepareOrder(source));
        // Negative control: charging before the server response must fail this guard.
        var mutant = source.Replace("                cost.Commit();", "")
            .Replace("            task = Post(\"prepare\", body);", "            cost.Commit();\n            task = Post(\"prepare\", body);");
        Assert.False(HasSafePrepareOrder(mutant));
    }

    private static bool HasSafePrepareOrder(string source)
    {
        var method = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Prepare").ToFullString();
        var fragments = new[] {
            "if (UnavailableReason != null)",
            "cost = new ScavRespawnItemCost(game.LocalPlayer)",
            "Post(\"probe\"",
            "cost.CaptureWithoutCostItem(",
            "task = Post(\"prepare\", body)",
            "RequireSuccess(response)",
            "preload.GetAwaiter().GetResult()",
            "cost.Commit()"
        };
        int last = -1;
        foreach (var fragment in fragments)
        {
            int next = method.IndexOf(fragment, StringComparison.Ordinal);
            if (next <= last) return false;
            last = next;
        }
        return true;
    }

    private static Type BuildCostHarness()
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot,
            "Redeploy.Client", "Services", "ScavRespawnItemCost.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("RegularScavCostHarness_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(CostBoundary) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("CostHarness", throwOnError: true)!;
    }

    private const string CostBoundary = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        namespace EFT {
            public sealed class LocalPlayer {
                public Profile Profile = new Profile();
                public object InventoryController = new object();
            }
            public sealed class Profile { public Inventory Inventory = new Inventory(); }
            public sealed class Inventory {
                public readonly List<EFT.InventoryLogic.Item> Items = new List<EFT.InventoryLogic.Item>();
                public IEnumerable<EFT.InventoryLogic.Item> GetPlayerItems(EFT.InventoryLogic.EPlayerItems mode) => Items;
            }
        }
        namespace EFT.InventoryLogic {
            public enum EPlayerItems { Equipment }
            public sealed class Item {
                public string TemplateId = "med";
                public object Parent = new object();
                public EFT.Inventory Owner;
            }
            public sealed class RemoveResult {
                private Item item;
                public RemoveResult(Item value) { item = value; }
                public void RollBack() { if (!ItemManipulator.BrokenRollback) item.Owner.Items.Add(item); }
            }
            public sealed class Result {
                public bool Failed; public string Error; public RemoveResult Value;
            }
            public static class ItemManipulator {
                public static bool FailRemoval;
                public static bool BrokenRollback;
                public static Result RemoveWithoutRestrictions(Item item, object controller) {
                    if (FailRemoval) return new Result { Failed = true, Error = "blocked" };
                    if (!item.Owner.Items.Remove(item)) throw new Exception("not present");
                    return new Result { Value = new RemoveResult(item) };
                }
            }
        }
        namespace Redeploy.Config {
            public sealed class Setting { public string Value = "Salewa"; }
            public static class Settings {
                public static Setting RequiredRespawnItem = new Setting();
                public static string Template = "med";
                public static string GetRequiredRespawnItemTemplateId() => Template;
            }
        }
        namespace Redeploy {
            public static class Plugin { public static Logger ModLogger = new Logger(); }
            public sealed class Logger { public void LogInfo(string value) {} }
        }
        public static class CostHarness {
            public static string Run(string scenario) {
                Redeploy.Config.Settings.Template = "med";
                EFT.InventoryLogic.ItemManipulator.FailRemoval = false;
                EFT.InventoryLogic.ItemManipulator.BrokenRollback = false;
                var player = new EFT.LocalPlayer();
                var original = new EFT.InventoryLogic.Item { Owner = player.Profile.Inventory };
                player.Profile.Inventory.Items.Add(original);
                if (scenario == "no-cost") Redeploy.Config.Settings.Template = "";
                var cost = new Redeploy.Services.ScavRespawnItemCost(player);
                bool rejected = false;
                switch (scenario) {
                    case "snapshot-restores":
                        if (cost.CaptureWithoutCostItem(() => player.Profile.Inventory.Items.Contains(original))) return "snapshot included cost";
                        if (!player.Profile.Inventory.Items.Contains(original)) return "live cost lost";
                        break;
                    case "serializer-failure-restores":
                        try { cost.CaptureWithoutCostItem<string>(() => throw new Exception("serializer failed")); }
                        catch (Exception) { rejected = true; }
                        if (!rejected || !player.Profile.Inventory.Items.Contains(original)) return "failure lost cost";
                        break;
                    case "exact-item-commit":
                        cost.CaptureWithoutCostItem(() => true);
                        var other = new EFT.InventoryLogic.Item { Owner = player.Profile.Inventory };
                        player.Profile.Inventory.Items.Insert(0, other);
                        cost.Commit();
                        if (player.Profile.Inventory.Items.Contains(original) || !player.Profile.Inventory.Items.Contains(other)) return "wrong item consumed";
                        break;
                    case "lost-item-rejects":
                        player.Profile.Inventory.Items.Clear();
                        player.Profile.Inventory.Items.Add(new EFT.InventoryLogic.Item { Owner = player.Profile.Inventory });
                        try { cost.Commit(); } catch (InvalidOperationException) { rejected = true; }
                        if (!rejected || player.Profile.Inventory.Items.Count != 1) return "substituted cost item";
                        break;
                    case "failed-removal-rejects":
                        EFT.InventoryLogic.ItemManipulator.FailRemoval = true;
                        try { cost.CaptureWithoutCostItem(() => true); } catch (InvalidOperationException) { rejected = true; }
                        if (!rejected || !player.Profile.Inventory.Items.Contains(original)) return "failed removal accepted";
                        break;
                    case "broken-rollback-rejects":
                        EFT.InventoryLogic.ItemManipulator.BrokenRollback = true;
                        try { cost.CaptureWithoutCostItem(() => true); } catch (InvalidOperationException) { rejected = true; }
                        if (!rejected) return "broken rollback accepted";
                        break;
                    case "duplicate-commit-rejects":
                        cost.Commit();
                        try { cost.Commit(); } catch (InvalidOperationException) { rejected = true; }
                        if (!rejected) return "double commit accepted";
                        break;
                    case "taken-while-waiting":
                        if (!cost.IsStillHeld) return "held cost reported missing";
                        player.Profile.Inventory.Items.Clear();
                        player.Profile.Inventory.Items.Add(new EFT.InventoryLogic.Item { Owner = player.Profile.Inventory });
                        if (cost.IsStillHeld) return "substituted cost item counted as held";
                        break;
                    case "no-cost":
                        if (!cost.IsStillHeld) return "unconfigured cost reported missing";
                        if (!cost.CaptureWithoutCostItem(() => player.Profile.Inventory.Items.Contains(original))) return "unconfigured cost removed";
                        cost.Commit();
                        if (!player.Profile.Inventory.Items.Contains(original)) return "unconfigured cost committed";
                        break;
                    default: return "unknown scenario";
                }
                return "PASS";
            }
        }
        """;
}
