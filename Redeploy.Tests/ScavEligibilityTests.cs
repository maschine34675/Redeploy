using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Redeploy.Tests;

public sealed class ScavEligibilityTests
{
    private static readonly Lazy<Type> Harness = new(() => BuildHarness());
    private static readonly object HarnessLock = new();

    [Theory]
    [InlineData("ready")]
    [InlineData("empty-native-roots")]
    [InlineData("missing-exits")]
    [InlineData("null-exits")]
    [InlineData("btr-delivery")]
    [InlineData("transit-delivery")]
    [InlineData("missing-identity")]
    [InlineData("missing-authority")]
    [InlineData("old-death-body")]
    [InlineData("insurance-failed")]
    [InlineData("prepared")]
    [InlineData("cooldown")]
    [InlineData("transit-arrival")]
    [InlineData("transit-count-only")]
    [InlineData("transit-visited-only")]
    [InlineData("transit-missing-history")]
    [InlineData("fresh-transition-data")]
    [InlineData("no-transition-data")]
    [InlineData("server-refused")]
    public void ActualAvailabilityRejectsKnownFailuresAndPreservesPmcChoices(string scenario)
        => Assert.Equal("PASS", Run(Harness.Value, "eligibility", scenario));

    [Theory]
    [InlineData("owned")]
    [InlineData("ordinary-pmc")]
    [InlineData("ordinary-scav")]
    [InlineData("prepared")]
    [InlineData("ending")]
    [InlineData("old-game")]
    [InlineData("old-body")]
    [InlineData("other-profile")]
    [InlineData("outside-raid")]
    [InlineData("taxi")]
    [InlineData("cover")]
    [InlineData("transit")]
    public void ActualNativePrefixesBlockOnlyOwnedBtrDelivery(string scenario)
        => Assert.Equal("PASS", Run(Harness.Value, "btr", scenario));

    [Theory]
    [InlineData("owned-result")]
    [InlineData("owned-result-runner")]
    [InlineData("owned-recovery")]
    [InlineData("ordinary-scav")]
    [InlineData("stale-profile")]
    [InlineData("stale-profile-id")]
    [InlineData("stale-equipment-reference")]
    [InlineData("stale-equipment-id")]
    [InlineData("stale-session")]
    [InlineData("not-ending")]
    [InlineData("not-acknowledged")]
    [InlineData("scav-died")]
    [InlineData("recovery-stale-controller")]
    [InlineData("recovery-stale-session")]
    [InlineData("recovery-stale-screen")]
    [InlineData("recovery-in-raid")]
    public void ActualSellAllPrefixProtectsOwnedTransferWithoutBlockingOrdinaryScav(string scenario)
        => Assert.Equal("PASS", Run(Harness.Value, "sell-all", scenario));

    [Fact]
    public void SellAllProfileScopeNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("&& ReferenceEquals(controller.Profile, accountScav)", ""));
        Assert.NotEqual("PASS", Run(mutant, "sell-all", "stale-profile"));
    }

    [Fact]
    public void SellAllRecoveryControllerScopeNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("&& ReferenceEquals(_transfer.ScavController, controller)", ""));
        Assert.NotEqual("PASS", Run(mutant, "sell-all", "recovery-stale-controller"));
    }

    [Theory]
    [InlineData("missing-exits")]
    [InlineData("btr-delivery")]
    [InlineData("serialized-delivery")]
    [InlineData("prepared")]
    [InlineData("unsafe")]
    [InlineData("transit-arrival")]
    public void ActualPreparationRevalidatesBeforeMutationAndReservation(string scenario)
        => Assert.Equal("PASS", Run(Harness.Value, "recheck", scenario));

    [Theory]
    [InlineData("refused")]
    [InlineData("committed")]
    [InlineData("server-refusal-after-body")]
    [InlineData("raid-ended")]
    [InlineData("no-respawns-left")]
    public void ActualPreparationFailureReturnsToPmcChoicesOnlyWhenNothingWasChanged(string scenario)
        => Assert.Equal("PASS", Run(Harness.Value, "prepare-failure", scenario));

    [Theory]
    [InlineData("codes")]
    [InlineData("transit-rule")]
    [InlineData("decision")]
    public void PolicyTablesMatchTheServerRulesAndKeepRawCodesOutOfPlayerText(string scenario)
        => Assert.Equal("PASS", Run(Harness.Value, "policy", scenario));

    [Fact]
    public void EveryServerContinuationCodeHasReadableClientText()
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot, "Redeploy.Server", "Scav", "ScavContinuationService.cs"));
        var codes = System.Text.RegularExpressions.Regex.Matches(source, "(?:Rejected\\(|Error = )\"([a-z][a-z-]+)\"")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        // Guards the scan itself: the codes the client has to explain today.
        Assert.Contains("no-issued-pmc-raid", codes);
        Assert.Contains("scav-cooldown", codes);
        Assert.True(codes.Length >= 15, string.Join(",", codes));
        foreach (var code in codes)
            Assert.Equal("PASS", Run(Harness.Value, "code", code));
    }

    [Fact]
    public void ClientAsksTheServerBeforeItEndsTheDeadPmcSessionAndOnlyThenFailsClosed()
    {
        Assert.True(ProbePrecedesSessionEndAndLaterFailuresStayClosed(PrepareSource()));
        Assert.Contains("/redeploy/pmc-scav/probe", File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot,
            "Redeploy.Server", "Scav", "ScavRouter.cs")));
    }

    [Theory]
    [InlineData("probe-missing")]
    [InlineData("probe-after-session-end")]
    [InlineData("refusal-after-session-end")]
    public void PreSessionEndOrderNegativeControlsAreDetected(string mutation)
    {
        var source = PrepareSource();
        var mutant = mutation switch
        {
            "probe-missing" => source.Replace("Post(\"probe\"", "Post(\"probe-renamed\""),
            "probe-after-session-end" => source.Replace("Post(\"probe\"", "Post(\"probe-renamed\"")
                .Replace("dead.OnGameSessionEnd(ExitStatus.Killed, _deathTime, game.Location.Id, null);",
                    "dead.OnGameSessionEnd(ExitStatus.Killed, _deathTime, game.Location.Id, null); var late = Post(\"probe\", null);"),
            _ => source.Replace("catch (ScavCostRestorationException ex)",
                "catch (ScavCostRestorationException ex) when (ex is ScavPreparationRefused)"),
        };
        Assert.NotEqual(source, mutant);
        Assert.False(ProbePrecedesSessionEndAndLaterFailuresStayClosed(mutant));
    }

    // The probe is the last thing that may end in a PMC re-offer; the dead PMC's statistics session
    // cannot be reopened, so no failure after it may produce one.
    private static bool ProbePrecedesSessionEndAndLaterFailuresStayClosed(string prepare)
    {
        int probe = prepare.IndexOf("Post(\"probe\"", StringComparison.Ordinal);
        int sessionEnd = prepare.IndexOf("dead.OnGameSessionEnd(", StringComparison.Ordinal);
        int refusal = prepare.IndexOf("new ScavPreparationRefused(", StringComparison.Ordinal);
        if (probe < 0 || sessionEnd < 0 || probe > sessionEnd || refusal < 0 || refusal > sessionEnd)
            return false;
        return !prepare[sessionEnd..].Contains("ScavPreparationRefused") && !prepare[sessionEnd..].Contains("Block(");
    }

    private static string PrepareSource()
    {
        var root = CSharpSyntaxTree.ParseText(ClientSource("Services/PmcScavSession.cs")).GetRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Prepare").ToFullString();
    }

    [Fact]
    public void DeathInsuranceIsCapturedOncePerBodyAndNeverOnPromptRepaint()
        => Assert.Equal("PASS", Run(Harness.Value, "insurance", ""));

    [Fact]
    public void NativeInsuranceDeduplicationPrefersParentedRecordAndIgnoresUninsuredItems()
        => Assert.Equal("PASS", Run(Harness.Value, "insurance-dedup", ""));

    [Fact]
    public void MissingExitNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("if (!hasScavExits)", "if (false)"));
        Assert.NotEqual("PASS", Run(mutant, "eligibility", "missing-exits"));
    }

    [Fact]
    public void ProfileScopeNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("|| !ReferenceEquals(profile, context.Scav)", ""));
        Assert.NotEqual("PASS", Run(mutant, "btr", "other-profile"));
    }

    [Fact]
    public void DeliveryNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("return hasDeliveredItems ? PriorDeliveryReason : null;", "return null;"));
        Assert.NotEqual("PASS", Run(mutant, "eligibility", "btr-delivery"));
    }

    [Fact]
    public void TransitArrivalNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("if (isTransitArrival)", "if (false)"));
        Assert.NotEqual("PASS", Run(mutant, "eligibility", "transit-arrival"));
    }

    [Theory]
    [InlineData("transition.transitionCount != 0 ||", "transit-count-only")]
    [InlineData("|| transition.visitedLocations is not { Length: 0 }", "transit-visited-only")]
    [InlineData("transition.visitedLocations is not { Length: 0 }", "transit-missing-history", "transition.visitedLocations is { Length: > 0 }")]
    public void TransitRuleNegativeControlsAreDetected(string original, string scenario, string replacement = "")
    {
        var mutant = BuildHarness(source => source.Replace(original, replacement));
        Assert.NotEqual("PASS", Run(mutant, "eligibility", scenario));
    }

    [Fact]
    public void ServerRefusalMustStickNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("if (_refusedReason != null)", "if (false)"));
        Assert.NotEqual("PASS", Run(mutant, "prepare-failure", "refused"));
    }

    [Fact]
    public void AlwaysEndRaidNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("refusedBeforeAnyChange && pmcChoiceStillPossible", "false"));
        Assert.NotEqual("PASS", Run(mutant, "prepare-failure", "refused"));
    }

    [Fact]
    public void ReofferAfterChangeNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("refusedBeforeAnyChange && pmcChoiceStillPossible", "pmcChoiceStillPossible"));
        Assert.NotEqual("PASS", Run(mutant, "prepare-failure", "committed"));
    }

    [Fact]
    public void HeldContinuationMustNotBeReturnedFromNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("continuationHeld ? new InvalidOperationException(reason) : new ScavPreparationRefused(reason)",
            "new ScavPreparationRefused(reason)"));
        Assert.NotEqual("PASS", Run(mutant, "recheck", "prepared"));
    }

    [Fact]
    public void PromptNotShownNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace("RespawnPromptPanel.Instance?.Show();", ""));
        Assert.NotEqual("PASS", Run(mutant, "prepare-failure", "refused"));
    }

    [Fact]
    public void RawServerCodeInPlayerTextNegativeControlIsDetected()
    {
        var mutant = BuildHarness(source => source.Replace(": base(ScavEligibilityPolicy.DescribeServerCode(code))", ": base(code)"));
        Assert.NotEqual("PASS", Run(mutant, "policy", "codes"));
    }

    [Fact]
    public void PrepareWireUsesVerifiedDeathInsuranceWithoutReplacingFinalCapture()
    {
        var source = ClientSource("Services/PmcScavSession.cs");
        Assert.Contains("[\"pmcInsuranceCaptured\"] = true", source);
        Assert.Contains("[\"lostInsuredItems\"] = JArray.FromObject(_deathInsurance)", source);
        Assert.Contains("_context.FinalInsurance = CaptureOriginalInsurance(game)", source);
    }

    private static string Run(Type harness, string mode, string scenario)
    {
        lock (HarnessLock)
            return (string)harness.GetMethod("Run")!.Invoke(null, [mode, scenario]);
    }

    private static string ClientSource(string relative)
        => File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot, "Redeploy.Client", relative));

    private static Type BuildHarness(Func<string, string> mutate = null)
    {
        var root = CSharpSyntaxTree.ParseText(ClientSource("Services/PmcScavSession.cs")).GetRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is "ReadOnlyUnavailableReason" or "ScavUnavailableReason"
                or "HasSwitched" or "CanContinue" or "RecordDeath" or "ResetForRaidStart" or "BlocksBtrItemDelivery" or "CaptureDeathInsurance" or "BlocksScavSellAll"
                or "RecordRefusal" or "NotifyRefused" or "NotifyUnavailable");
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(property => property.Identifier.ValueText is "UnavailableReason" or "IsOffered");
        var prepare = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Prepare");
        var firstTry = prepare.Body!.Statements.OfType<TryStatementSyntax>().First();
        // Compile the actual pre-mutation boundary, stopping at the first body mutation.
        var preflight = firstTry.Block.Statements.TakeWhile(statement => statement is not LocalDeclarationStatementSyntax local
            || !local.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "dead"));
        var choice = CSharpSyntaxTree.ParseText(ClientSource("Services/RespawnService.cs")).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is "BeginRespawn" or "BeginScavRespawn" or "HandleScavPreparationFailure");
        var patches = CSharpSyntaxTree.ParseText(ClientSource("Patches/PmcScavPatches.cs")).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(type => type.Identifier.ValueText.StartsWith("PmcScavBtr", StringComparison.Ordinal)
                || type.Identifier.ValueText == "PmcScavSellAllPatch");
        Assert.Equal(5, patches.Count());
        var recoveryOwnership = CSharpSyntaxTree.ParseText(ClientSource("Services/ScavRecoveryClient.cs")).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "OwnsTransfer");
        string production = "using System; using System.Linq; using System.Collections; using System.Reflection; using System.Threading.Tasks; "
            + "using Comfort.Common; using EFT; using EFT.Dialogs; using EFT.InventoryLogic; using EFT.Interactive; using EFT.Quests; using EFT.UI; using EFT.UI.Screens; using EFT.Vehicle; using JsonType; "
            + "using HarmonyLib; using SPT.Reflection.Patching; using Redeploy.Patches; using Redeploy.Services; namespace Redeploy.Services { "
            + "public static partial class PmcScavSession { " + string.Join("\n", methods.Select(method => method.ToFullString()))
            + string.Join("\n", properties.Select(property => property.ToFullString()))
            + " public static void Preflight(LocalGame game) { " + string.Join("\n", preflight.Select(statement => statement.ToFullString())) + " } } "
            + "public static partial class ScavRecoveryClient { " + recoveryOwnership.ToFullString() + " } "
            + "public static partial class RespawnService { " + string.Join("\n", choice.Select(method => method.ToFullString())) + " } } "
            + "namespace Redeploy.Patches { public static partial class PmcScavPatches { "
            + string.Join("\n", patches.Select(type => type.ToFullString())) + " } }";
        var policy = ClientSource("Services/ScavEligibilityPolicy.cs");
        if (mutate != null)
        {
            string changedProduction = mutate(production), changedPolicy = mutate(policy);
            Assert.True(changedProduction != production || changedPolicy != policy, "Mutation did not reach production code.");
            production = changedProduction; policy = changedPolicy;
        }
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ScavEligibility_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(production), CSharpSyntaxTree.ParseText(policy),
                CSharpSyntaxTree.ParseText(ClientSource("Services/RegularScavPolicy.cs")), CSharpSyntaxTree.ParseText(Boundary)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("EligibilityHarness", throwOnError: true)!;
    }

    private const string Boundary = """
        using System;
        using System.Linq;
        using System.Collections;
        using System.Collections.Generic;
        using System.Reflection;
        using System.Threading.Tasks;
        using EFT;
        using EFT.InventoryLogic;
        using JsonType;
        using Redeploy.Services;
        public struct MongoID {
            private string value;
            public static implicit operator MongoID(string value) => new MongoID { value = value };
            public static implicit operator string(MongoID id) => id.value;
            public override string ToString() => value;
        }
        namespace JsonType {
            public sealed class FlatItem { public MongoID _id; public MongoID? parentId; }
            public sealed class TransitSettings { public int transitionCount; public string[] visitedLocations; }
        }
        namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
        namespace EFT {
            public enum EPlayerSide { Bear, Savage }
            public enum GameStatus { Started, Stopping }
            public enum ExitStatus { Killed, Survived, Runner }
            public static class InGameStatus { public static bool InRaid; }
            public enum ETraderServiceType { BtrItemsDelivery, PlayerTaxi, BtrBotCover, TransitItemsDelivery }
            public sealed class Info { public EPlayerSide Side; public double SavageLockTime; }
            public sealed class Profile {
                public string Id = "pmc"; public Info Info = new Info(); public Inventory Inventory = new Inventory();
                public Insured[] InsuredItems = new[] { new Insured { ItemId = "carried" }, new Insured { ItemId = "dropped" } };
            }
            public sealed class Inventory { public Item Equipment = new Item { Id = "equipment", Children = new[] { new Item { Id = "carried" } } }; }
            public sealed class Insured { public string ItemId; }
            public sealed class Health { public bool IsAlive; }
            public class Player { public Profile Profile = new Profile(); }
            public class LocalPlayer : Player { public Health HealthController = new Health(); }
            public sealed class LocalGame {
                public Profile Profile = new Profile(); public LocalPlayer LocalPlayer = new LocalPlayer();
                public GameStatus Status = GameStatus.Started; public float PastTime;
                public int InsuranceReads, TransferReads, Starts; public bool InsuranceFails, SerializedDelivery;
                public Dictionary<string, FlatItem[]> GetTransferItems() {
                    TransferReads++; return new Dictionary<string, FlatItem[]> { ["root_btr"] = new FlatItem[SerializedDelivery ? 2 : 1] };
                }
                public object StartCoroutine(IEnumerator work) { Starts++; work.MoveNext(); return null; }
            }
            public sealed class GameWorld {
                public Controller BtrController = new Controller(), TransitController = new Controller();
                public List<object> LootList = new List<object> { new EFT.Interactive.LootItem { ItemOwner = new EFT.Interactive.Owner { RootItem = new Item { Id = "dropped" } } } };
            }
            public sealed class ItemFactory {
                public FlatItem[] TreeToFlatItems(IEnumerable<Item> items) {
                    var game = RespawnSessionState.CurrentLocalGame; game.InsuranceReads++;
                    if (game.InsuranceFails) throw new Exception("insurance unavailable");
                    var result = new List<FlatItem>();
                    void Add(Item item, string parent) { result.Add(new FlatItem { _id = item.Id, parentId = parent == null ? null : (MongoID?)parent }); foreach (var child in item.Children) Add(child, item.Id); }
                    foreach (var item in items) Add(item, null);
                    return result.ToArray();
                }
            }
            public sealed class Controller { public TransferItemsController TransferItemsController = new TransferItemsController(); }
            public class TransferItemsController {
                public Stash Stash = new Stash(); public ETraderServiceType ServiceType;
                public void MoveItemsFromTempStashToTransferStash(Player player) { }
            }
            public static class GamePlayerOwner { public static Player MyPlayer; }
            public static class DateTimeExtensions { public static double UtcNowUnixInt = 1000; }
        }
        namespace EFT.InventoryLogic {
            public class Item { public string Id; public Item[] Children = Array.Empty<Item>(); }
            public class ContainerCollection : Item { }
            public static class CollectionExtensions { public static IEnumerable<Item> GetAllItemsFromCollections(this IEnumerable<ContainerCollection> roots) => roots.SelectMany(root => root.Children); }
            public sealed class Stash { public Grid[] Grids = Array.Empty<Grid>(); }
            public sealed class Grid { public List<object> ItemCollection = new List<object>(); }
            public class InventoryController {
                public object Profile; public Inventory Inventory;
                public Task<bool> TryPurchaseTraderService(ETraderServiceType serviceType, EFT.Quests.QuestController questController, string subServiceId = null) => Task.FromResult(true);
            }
            public class BackEndInventoryController : InventoryController { public object _backendSession; }
            public sealed class OfflineInventoryController : BackEndInventoryController { }
        }
        namespace EFT.Interactive {
            public sealed class Owner { public Item RootItem; }
            public class LootItem { public Owner ItemOwner; }
            public sealed class LootableContainer { public Owner ItemOwner; }
        }
        namespace EFT.Dialogs {
            public class DialogAction { }
            public sealed class DialogPurchaseServiceAction : DialogAction { public ETraderServiceType ServiceType; }
        }
        namespace EFT.Quests { public sealed class QuestController { } }
        namespace EFT.UI {
            public class TraderDialogScreen { public void HandlePurchaseService(EFT.Dialogs.DialogAction action) { } }
            public class ScavengerInventoryScreen {
                public Task SellAll() => Task.CompletedTask;
                public sealed class ScavengerInventoryScreenController {
                    public OfflineInventoryController ScavController; public object Session;
                }
            }
        }
        namespace EFT.UI.Screens {
            public sealed class EftScreenManager {
                public static EftScreenManager Instance = new EftScreenManager(); public object CurrentScreenController;
            }
        }
        namespace EFT.Vehicle { public class BTRView { public void OpenTransferItemsScreen() { } } }
        namespace HarmonyLib {
            public static class AccessTools { public static MethodBase Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters); }
        }
        namespace SPT.Reflection.Patching {
            public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
            public sealed class PatchPrefixAttribute : Attribute { }
        }
        namespace Redeploy.Patches {
            public static partial class PmcScavPatches {
                public static string UnavailableReason;
                public static bool Invoke(string type, object[] args) {
                    var nested = typeof(PmcScavPatches).GetNestedType(type, BindingFlags.NonPublic);
                    var patch = Activator.CreateInstance(nested, true);
                    if (nested.GetMethod("GetTargetMethod", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(patch, null) == null)
                        throw new Exception("Native target signature did not resolve.");
                    return (bool)nested.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
                }
            }
        }
        namespace Redeploy.Services {
            public enum RespawnLocationMode { NearCorpse, InfilSpawn }
            public sealed class Setting { public bool Value = true; }
            public static class Settings {
                public static Setting RespawnAsScav = new Setting(), Enabled = new Setting();
                public static bool RespawnsLeft = true;
                public static bool HasRespawnsRemaining(int used) => RespawnsLeft;
            }
            public static class RespawnSessionState {
                public static LocalGame CurrentLocalGame;
                public static bool IsInRaid, HasGivenUp, IsRespawnInProgress, RespawnItemAvailable, PendingRespawnChoice;
                public static int RespawnsUsed; public static RespawnLocationMode SelectedSpawnMode;
            }
            public sealed class Session { public Profile ProfileOfPet = new Profile { Id = "scav", Info = new Info { Side = EPlayerSide.Savage } }; }
            public sealed class RaidSettings {
                public string serverId = "raid";
                // A fresh raid, as the server's start response describes it.
                public TransitSettings transition = new TransitSettings { transitionCount = 0, visitedLocations = new string[0] };
            }
            public static class LocalGameAccessor {
                public static Session Session = new Session(); public static RaidSettings Settings = new RaidSettings();
                public static Session GetSession(LocalGame game) => Session;
                public static RaidSettings GetLocalRaidSettings(LocalGame game) => Settings;
            }
            public sealed class ExfiltrationController {
                public static ExfiltrationController Instance = new ExfiltrationController();
                public object[] ScavExfiltrationPoints = new[] { new object() };
            }
            public static class Plugin { public static Logger ModLogger = new Logger(); }
            public sealed class Logger { public void LogWarning(string message) { } }
            public sealed class RespawnPromptPanel {
                public static RespawnPromptPanel Instance = new RespawnPromptPanel(); public int Hides, Shows;
                public void Hide() { Hides++; }
                public void Show() { Shows++; }
            }
            public static partial class RespawnService {
                public static bool? LastScavChoice; public static int GiveUps;
                public static void GiveUpRaid() { GiveUps++; RespawnSessionState.HasGivenUp = true; RespawnPromptPanel.Instance.Hide(); }
                public static void RunFailure(LocalGame game, Exception failure) => HandleScavPreparationFailure(game, failure);
                public static bool HasRequiredRespawnItem(LocalPlayer player) => true;
                private static IEnumerator RespawnCoroutine(LocalGame game, bool continueAsScav) { LastScavChoice = continueAsScav; yield break; }
            }
            public static partial class PmcScavSession {
                private sealed class Context {
                    public LocalGame Game; public Profile Scav, OriginalPmc; public bool Ending, BackendAcknowledged;
                    public Session Session; public ExitStatus ScavOutcome;
                }
                private static Context _context;
                private static LocalGame _deathGame; private static LocalPlayer _deathPlayer;
                private static float _deathTime; private static FlatItem[] _deathInsurance; private static string _deathInsuranceFailure;
                private static string _unsafePreparationServerId, _unsafePreparationPmcId, _unsafePreparationReason;
                private static string _refusedReason;
                public static void MarkUnsafe() { _unsafePreparationReason = "unsafe"; }
                public static object ExitAuthority = new object(); public static int Notices; public static string LastNotice;
                private static void ShowNotice(string text, bool warning = false) { Notices++; LastNotice = text; }
                public static FlatItem[] DeathInsurance() => _deathInsurance;
                public static void SetResultState(string scenario) {
                    _context.Ending = scenario != "not-ending";
                    _context.BackendAcknowledged = scenario != "not-acknowledged";
                    _context.ScavOutcome = scenario == "scav-died" ? ExitStatus.Killed : scenario == "owned-result-runner" ? ExitStatus.Runner : ExitStatus.Survived;
                }
                public static void Adopt(LocalGame game, bool adopted = true, bool ending = false) {
                    var scav = LocalGameAccessor.Session.ProfileOfPet;
                    _context = new Context { Game = game, Scav = scav, Ending = ending, OriginalPmc = game.Profile, Session = LocalGameAccessor.Session };
                    if (adopted) { game.Profile = scav; game.LocalPlayer.Profile = scav; }
                }
            }
            public sealed class ScavRecoveryFlow { public object Session; }
            public static partial class ScavRecoveryClient {
                public static ScavRecoveryFlow Flow = new ScavRecoveryFlow();
                public static EFT.UI.ScavengerInventoryScreen.ScavengerInventoryScreenController _transfer;
            }
        }
        public static class EligibilityHarness {
            private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
            public static string Run(string mode, string scenario) {
                try { Execute(mode, scenario); return "PASS"; } catch (Exception ex) { return ex.GetBaseException().Message; }
            }
            private static LocalGame Reset() {
                PmcScavSession.ResetForRaidStart(); PmcScavSession.ExitAuthority = new object();
                Settings.RespawnAsScav.Value = Settings.Enabled.Value = true;
                var game = new LocalGame(); game.LocalPlayer.Profile = game.Profile;
                RespawnSessionState.CurrentLocalGame = game; RespawnSessionState.IsInRaid = RespawnSessionState.PendingRespawnChoice = true;
                RespawnSessionState.HasGivenUp = RespawnSessionState.IsRespawnInProgress = false;
                RespawnPromptPanel.Instance.Hides = RespawnPromptPanel.Instance.Shows = 0;
                RespawnService.LastScavChoice = null; RespawnService.GiveUps = 0; Settings.RespawnsLeft = true;
                LocalGameAccessor.Session = new Session(); LocalGameAccessor.Settings = new RaidSettings();
                ExfiltrationController.Instance = new ExfiltrationController();
                Comfort.Common.Singleton<GameWorld>.Instance = new GameWorld();
                Comfort.Common.Singleton<ItemFactory>.Instance = new ItemFactory();
                GamePlayerOwner.MyPlayer = game.LocalPlayer;
                PmcScavSession.Notices = 0; PmcScavSession.LastNotice = null;
                Redeploy.Patches.PmcScavPatches.UnavailableReason = null;
                ScavRecoveryClient._transfer = null; ScavRecoveryClient.Flow = new ScavRecoveryFlow();
                EFT.UI.Screens.EftScreenManager.Instance.CurrentScreenController = null; InGameStatus.InRaid = false;
                PmcScavSession.RecordDeath(game); return game;
            }
            private static void SetFailure(LocalGame game, string scenario) {
                var world = Comfort.Common.Singleton<GameWorld>.Instance;
                switch (scenario) {
                    case "ready": break;
                    case "empty-native-roots": world.BtrController.TransferItemsController.Stash.Grids = new[] { new Grid(), new Grid() }; break;
                    case "missing-exits": ExfiltrationController.Instance.ScavExfiltrationPoints = Array.Empty<object>(); break;
                    case "null-exits": ExfiltrationController.Instance.ScavExfiltrationPoints = new object[] { null }; break;
                    case "btr-delivery": world.BtrController.TransferItemsController.Stash.Grids = new[] { new Grid { ItemCollection = new List<object> { new object() } } }; break;
                    case "transit-delivery": world.TransitController.TransferItemsController.Stash.Grids = new[] { new Grid { ItemCollection = new List<object> { new object() } } }; break;
                    case "serialized-delivery": game.SerializedDelivery = true; break;
                    case "missing-identity": LocalGameAccessor.Settings.serverId = null; break;
                    case "missing-authority": PmcScavSession.ExitAuthority = null; break;
                    case "old-death-body": game.LocalPlayer = new LocalPlayer { Profile = game.Profile }; break;
                    case "insurance-failed": game.InsuranceFails = true; PmcScavSession.RecordDeath(game); break;
                    case "prepared": PmcScavSession.Adopt(game, adopted: false); break;
                    case "cooldown": LocalGameAccessor.Session.ProfileOfPet.Info.SavageLockTime = 1200; break;
                    case "transit-arrival": LocalGameAccessor.Settings.transition = new TransitSettings { transitionCount = 1, visitedLocations = new[] { "bigmap" } }; break;
                    case "transit-count-only": LocalGameAccessor.Settings.transition = new TransitSettings { transitionCount = 2, visitedLocations = new string[0] }; break;
                    case "transit-visited-only": LocalGameAccessor.Settings.transition = new TransitSettings { transitionCount = 0, visitedLocations = new[] { "bigmap" } }; break;
                    case "transit-missing-history": LocalGameAccessor.Settings.transition = new TransitSettings { transitionCount = 0, visitedLocations = null }; break;
                    case "fresh-transition-data": LocalGameAccessor.Settings.transition = new TransitSettings { transitionCount = 0, visitedLocations = new string[0] }; break;
                    case "no-transition-data": LocalGameAccessor.Settings.transition = null; break;
                    case "server-refused": PmcScavSession.RecordRefusal("The server did not register this raid."); break;
                    case "unsafe": PmcScavSession.MarkUnsafe(); break;
                    default: throw new Exception("Unknown scenario: " + scenario);
                }
            }
            private static void Execute(string mode, string scenario) {
                var game = Reset();
                if (mode == "sell-all") {
                    var account = new Profile { Id = "scav", Info = new Info { Side = EPlayerSide.Savage } };
                    var controller = new OfflineInventoryController { Profile = account, Inventory = account.Inventory, _backendSession = LocalGameAccessor.Session };
                    bool recovery = scenario.Contains("recovery");
                    if (recovery) {
                        ScavRecoveryClient.Flow.Session = LocalGameAccessor.Session;
                        ScavRecoveryClient._transfer = new EFT.UI.ScavengerInventoryScreen.ScavengerInventoryScreenController { ScavController = controller, Session = LocalGameAccessor.Session };
                        EFT.UI.Screens.EftScreenManager.Instance.CurrentScreenController = ScavRecoveryClient._transfer;
                        if (scenario == "recovery-stale-controller") controller = new OfflineInventoryController { Profile = account, Inventory = account.Inventory, _backendSession = LocalGameAccessor.Session };
                        if (scenario == "recovery-stale-session") ScavRecoveryClient.Flow.Session = new Session();
                        if (scenario == "recovery-stale-screen") EFT.UI.Screens.EftScreenManager.Instance.CurrentScreenController = new object();
                        if (scenario == "recovery-in-raid") InGameStatus.InRaid = true;
                    } else if (scenario != "ordinary-scav") {
                        PmcScavSession.Adopt(game); PmcScavSession.SetResultState(scenario);
                        LocalGameAccessor.Session.ProfileOfPet = account; // Native GetProfiles refreshes the transfer profile.
                        if (scenario == "stale-profile") controller.Profile = new Profile { Id = account.Id, Info = account.Info, Inventory = account.Inventory };
                        if (scenario == "stale-profile-id") account.Id = "other-scav";
                        if (scenario == "stale-equipment-reference") controller.Inventory = new Inventory();
                        if (scenario == "stale-equipment-id") account.Inventory.Equipment.Id = "new-generation";
                        if (scenario == "stale-session") controller._backendSession = new Session();
                    }
                    bool sellBlocked = scenario is "owned-result" or "owned-result-runner" or "owned-recovery";
                    var args = new object[] { controller, null };
                    Check(Redeploy.Patches.PmcScavPatches.Invoke("PmcScavSellAllPatch", args) == !sellBlocked, "SellAll scope mismatch: " + scenario);
                    Check(sellBlocked ? args[1] is Task sellResult && sellResult.IsCompletedSuccessfully : args[1] == null, "SellAll returned the wrong task boundary.");
                    Check(sellBlocked ? PmcScavSession.Notices == 1 && PmcScavSession.LastNotice.Contains("Move loot to your stash first") : PmcScavSession.Notices == 0, "SellAll visibility or notice scope mismatch.");
                    return;
                }
                if (mode == "insurance-dedup") {
                    var root = new FlatItem { _id = "carried" };
                    var parented = new FlatItem { _id = "carried", parentId = (MongoID)"equipment" };
                    var extra = new FlatItem { _id = "uninsured" };
                    foreach (var order in new[] { new[] { root, parented, root, extra }, new[] { parented, root, extra } }) {
                        var saved = ScavEligibilityPolicy.SelectInsuredSnapshot(order, new[] { "carried" });
                        Check(saved.Length == 1 && ReferenceEquals(saved[0], parented), "Insurance deduplication changed ownership or retained uninsured items.");
                    }
                    return;
                }
                if (mode == "insurance") {
                    for (int i = 0; i < 50; i++) Check(PmcScavSession.UnavailableReason == null, "Fresh snapshot unavailable.");
                    Check(game.InsuranceReads == 1 && game.TransferReads == 0, "Prompt reserialized inventory or insurance.");
                    var snapshot = PmcScavSession.DeathInsurance();
                    Check(snapshot.Length == 2 && snapshot.Any(item => (string)item._id == "carried") && snapshot.Any(item => (string)item._id == "dropped"), "Snapshot lost carried or earlier dropped insurance.");
                    Check(game.Profile.Inventory.Equipment.Children.Length == 1 && Comfort.Common.Singleton<GameWorld>.Instance.LootList.Count == 1, "Read-only snapshot changed inventory or world.");
                    PmcScavSession.RecordDeath(game); Check(game.InsuranceReads == 2, "New death did not capture new insurance.");
                    Settings.RespawnAsScav.Value = false; PmcScavSession.RecordDeath(game);
                    Check(game.InsuranceReads == 2, "Disabled feature captured insurance."); return;
                }
                if (mode == "recheck") {
                    Check(PmcScavSession.UnavailableReason == null, "Baseline was unavailable.");
                    SetFailure(game, scenario); InvalidOperationException rejected = null;
                    try { PmcScavSession.Preflight(game); } catch (InvalidOperationException ex) { rejected = ex; }
                    Check(rejected != null, "Preparation ignored a changed prerequisite.");
                    // A held (prepared or unsafe) continuation is never a clean state to return from.
                    bool held = scenario is "prepared" or "unsafe";
                    Check((rejected is ScavPreparationRefused) == !held, "Pre-change refusal classification mismatch: " + scenario); return;
                }
                if (mode == "prepare-failure") {
                    RespawnSessionState.IsRespawnInProgress = true; RespawnPromptPanel.Instance.Hide();
                    int hidesBefore = RespawnPromptPanel.Instance.Hides;
                    Exception failure = scenario switch {
                        "refused" or "raid-ended" or "no-respawns-left" => new ScavPreparationRefused("Reason shown to the player.", "no-issued-pmc-raid"),
                        "committed" => new InvalidOperationException("late failure"),
                        "server-refusal-after-body" => new ScavServerRefusal("invalid-frozen-pmc-death"),
                        _ => throw new Exception("Unknown scenario: " + scenario) };
                    if (scenario == "raid-ended") game.Status = GameStatus.Stopping;
                    if (scenario == "no-respawns-left") Settings.RespawnsLeft = false;
                    RespawnService.RunFailure(game, failure);
                    bool reopen = scenario == "refused", end = scenario is "committed" or "server-refusal-after-body" or "no-respawns-left";
                    Check(RespawnPromptPanel.Instance.Shows == (reopen ? 1 : 0), "Prompt visibility mismatch: " + scenario);
                    Check(RespawnService.GiveUps == (end ? 1 : 0), "End-raid mismatch: " + scenario);
                    Check(!RespawnSessionState.IsRespawnInProgress, "Respawn flag left set.");
                    Check(PmcScavSession.Notices == 1, "Missing player notice.");
                    Check(!PmcScavSession.LastNotice.Contains("no-issued-pmc-raid"), "Raw server code reached the player.");
                    if (end || scenario == "raid-ended") Check(PmcScavSession.LastNotice.Contains("will end normally"), "Wrong ending notice.");
                    if (!reopen) { Check(RespawnPromptPanel.Instance.Hides >= hidesBefore, "Prompt state corrupted."); return; }
                    Check(RespawnSessionState.PendingRespawnChoice && !RespawnSessionState.HasGivenUp, "PMC choice state lost.");
                    Check(PmcScavSession.LastNotice.Contains("PMC respawn choices"), "Notice does not say the PMC choices remain.");
                    Check(PmcScavSession.UnavailableReason == "Reason shown to the player.", "Scav choice not disabled with the reason.");
                    RespawnService.BeginScavRespawn(); Check(game.Starts == 0, "Refused Scav choice could be retried.");
                    RespawnService.BeginRespawn(RespawnLocationMode.NearCorpse);
                    Check(game.Starts == 1 && RespawnService.LastScavChoice == false, "PMC respawn cannot run after the refusal.");
                    return;
                }
                if (mode == "code") {
                    var text = ScavEligibilityPolicy.DescribeServerCode(scenario);
                    Check(text != ScavEligibilityPolicy.DescribeServerCode("code-from-a-future-server") && !text.Contains(scenario), "No readable text for server code " + scenario);
                    return;
                }
                if (mode == "policy") {
                    if (scenario == "codes") {
                        Check(ScavEligibilityPolicy.DescribeServerCode(null) == ScavEligibilityPolicy.DescribeServerCode("code-from-a-future-server"), "Missing code is not generic.");
                        var refusal = new ScavServerRefusal("no-issued-pmc-raid");
                        Check(!refusal.Message.Contains("no-issued-pmc-raid"), "Raw server code in the player text.");
                        Check(ScavEligibilityPolicy.DescribeForLog(refusal).Contains("no-issued-pmc-raid"), "Log lost the raw server code.");
                        var wrapped = new ScavPreparationRefused(refusal.Message, refusal.Code, refusal);
                        Check(ScavEligibilityPolicy.RefusalCode(wrapped) == "no-issued-pmc-raid" && ScavEligibilityPolicy.RefusalCode(new Exception("x", wrapped)) == "no-issued-pmc-raid", "Code not recoverable from the wrapper chain.");
                        Check(ScavEligibilityPolicy.RefusalCode(new InvalidOperationException("plain")) == null, "Plain failure invented a code.");
                        return;
                    }
                    if (scenario == "transit-rule") {
                        Check(!ScavEligibilityPolicy.IsTransitArrival(null), "No transition data is a fresh raid.");
                        Check(!ScavEligibilityPolicy.IsTransitArrival(new TransitSettings { transitionCount = 0, visitedLocations = new string[0] }), "Fresh raid flagged.");
                        Check(ScavEligibilityPolicy.IsTransitArrival(new TransitSettings { transitionCount = 1, visitedLocations = new string[0] }), "Count missed.");
                        Check(ScavEligibilityPolicy.IsTransitArrival(new TransitSettings { transitionCount = 0, visitedLocations = new[] { "bigmap" } }), "History missed.");
                        Check(ScavEligibilityPolicy.IsTransitArrival(new TransitSettings { transitionCount = 0, visitedLocations = null }), "Missing history is non-fresh on the server.");
                        Check(ScavEligibilityPolicy.IsTransitArrival(new TransitSettings { transitionCount = -1, visitedLocations = new string[0] }), "Negative count is non-fresh on the server.");
                        return;
                    }
                    if (scenario == "decision") {
                        var none = ScavPrepareFailureAction.Nothing; var reopen = ScavPrepareFailureAction.ReopenPmcChoices; var end = ScavPrepareFailureAction.EndRaid;
                        Check(ScavEligibilityPolicy.AfterPrepareFailure(true, true, true) == reopen, "Clean refusal must reopen.");
                        Check(ScavEligibilityPolicy.AfterPrepareFailure(true, true, false) == end, "No PMC choice left must end.");
                        Check(ScavEligibilityPolicy.AfterPrepareFailure(false, true, true) == end, "A changed state must end.");
                        Check(ScavEligibilityPolicy.AfterPrepareFailure(false, true, false) == end, "A changed state must end.");
                        foreach (var refused in new[] { true, false }) foreach (var possible in new[] { true, false })
                            Check(ScavEligibilityPolicy.AfterPrepareFailure(refused, false, possible) == none, "A finished raid owns its own ending.");
                        return;
                    }
                    throw new Exception("Unknown policy scenario");
                }
                if (mode == "eligibility") {
                    SetFailure(game, scenario); bool ready = scenario is "ready" or "empty-native-roots" or "fresh-transition-data" or "no-transition-data";
                    for (int i = 0; i < 5; i++) Check((PmcScavSession.UnavailableReason == null) == ready, "Eligibility mismatch: " + scenario);
                    Check(game.TransferReads == 0, "Prompt flattened native transfers.");
                    if (!ready) {
                        RespawnService.BeginScavRespawn();
                        Check(game.Starts == 0 && RespawnPromptPanel.Instance.Hides == 0, "Unavailable Scav hid prompt or started work.");
                    }
                    if (scenario == "prepared") {
                        // The real coroutine owns the choice until success or safe raid
                        // shutdown; a prepared transaction must not regain a PMC choice.
                        RespawnSessionState.IsRespawnInProgress = true;
                        RespawnService.BeginRespawn(RespawnLocationMode.NearCorpse);
                        Check(game.Starts == 0, "Prepared transaction reopened PMC respawn."); return;
                    }
                    foreach (var location in new[] { RespawnLocationMode.NearCorpse, RespawnLocationMode.InfilSpawn }) {
                        int before = game.Starts; RespawnService.BeginRespawn(location);
                        Check(game.Starts == before + 1 && RespawnService.LastScavChoice == false, "PMC fallback unavailable.");
                        Check(RespawnSessionState.SelectedSpawnMode == location, "PMC fallback changed spawn mode.");
                    }
                    return;
                }
                if (mode != "btr") throw new Exception("Unknown mode");
                if (scenario == "ordinary-scav") { game.Profile.Info.Side = EPlayerSide.Savage; }
                else if (scenario != "ordinary-pmc") PmcScavSession.Adopt(game, scenario != "prepared", scenario == "ending");
                object profile = game.Profile;
                if (scenario == "old-game") RespawnSessionState.CurrentLocalGame = new LocalGame();
                if (scenario == "old-body") game.LocalPlayer = new LocalPlayer();
                if (scenario == "other-profile") profile = new Profile { Id = game.Profile.Id, Info = game.Profile.Info };
                if (scenario == "outside-raid") RespawnSessionState.IsInRaid = false;
                var service = scenario switch { "taxi" => ETraderServiceType.PlayerTaxi, "cover" => ETraderServiceType.BtrBotCover,
                    "transit" => ETraderServiceType.TransitItemsDelivery, _ => ETraderServiceType.BtrItemsDelivery };
                bool blocked = scenario is "owned" or "ending";
                var action = new EFT.Dialogs.DialogPurchaseServiceAction { ServiceType = service };
                Check(Redeploy.Patches.PmcScavPatches.Invoke("PmcScavBtrDialogPatch", new object[] { action, profile }) == !blocked, "Dialog scope mismatch.");
                var purchaseArgs = new object[] { new InventoryController { Profile = profile }, service, null };
                Check(Redeploy.Patches.PmcScavPatches.Invoke("PmcScavBtrPurchasePatch", purchaseArgs) == !blocked, "Purchase scope mismatch.");
                Check(!blocked || purchaseArgs[2] is Task<bool> result && result.IsCompleted && !result.Result, "Blocked purchase did not return false.");
                Check(Redeploy.Patches.PmcScavPatches.Invoke("PmcScavBtrTransferPatch", new object[] { new TransferItemsController { ServiceType = service }, new Player { Profile = (Profile)profile } }) == !blocked, "Transfer scope mismatch.");
                if (service == ETraderServiceType.BtrItemsDelivery) {
                    GamePlayerOwner.MyPlayer = new Player { Profile = (Profile)profile };
                    Check(Redeploy.Patches.PmcScavPatches.Invoke("PmcScavBtrScreenPatch", Array.Empty<object>()) == !blocked, "Direct screen scope mismatch.");
                }
                Check(blocked ? PmcScavSession.Notices >= 3 && PmcScavSession.LastNotice.Contains("Extract") : PmcScavSession.Notices == 0, "Visibility or notification scope mismatch.");
            }
        }
        """;
}
