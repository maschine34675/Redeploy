using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Redeploy.Core.Engine;
using Xunit;

namespace Redeploy.Tests;

public class ProfileReplacementTests
{
    private static readonly string Root = ApiContractTests.RepoRoot;

    [Fact]
    public void NoReplacementDoesNotInspectTheOldProfile()
        => ProfileReplacementPolicy.Validate(null, null);

    [Fact]
    public void IndependentProfilesAreAcceptedWithoutMutatingEitherGraph()
    {
        var original = Shape("pmc", "old-weapon", "old-rig");
        var replacement = Shape("scav", "new-weapon", "new-rig");
        var oldRoots = original.MutableRoots.ToArray();
        var newRoots = replacement.MutableRoots.ToArray();
        ProfileReplacementPolicy.Validate(original, replacement);
        Assert.Equal(oldRoots, original.MutableRoots.ToArray());
        Assert.Equal(newRoots, replacement.MutableRoots.ToArray());
        Assert.Equal(new[] { "old-weapon", "old-rig" }, original.ItemIds);
        Assert.Equal(new[] { "new-weapon", "new-rig" }, replacement.ItemIds);
    }

    [Theory]
    [InlineData("profile-reference")]
    [InlineData("profile-id")]
    [InlineData("missing-id")]
    [InlineData("inventory")]
    [InlineData("statistics")]
    [InlineData("health-part")]
    [InlineData("skills")]
    [InlineData("missing-state")]
    [InlineData("old-item-id")]
    [InlineData("duplicate-item")]
    [InlineData("empty-item-id")]
    [InlineData("empty-inventory")]
    public void RefusesCrossLifeAliasingOrInvalidIdentityBeforeMutation(string defect)
    {
        var original = Shape("pmc", "old-item");
        var replacement = Shape("scav", "new-item");
        var roots = (Dictionary<string, object>)replacement.MutableRoots;
        switch (defect)
        {
            case "profile-reference": replacement.Profile = original.Profile; break;
            case "profile-id": replacement.Id = original.Id; break;
            case "missing-id": replacement.Id = " "; break;
            case "inventory": roots["Inventory"] = original.MutableRoots["Inventory"]; break;
            case "statistics": roots["Stats"] = original.MutableRoots["Stats"]; break;
            case "health-part": roots["Health.Head"] = original.MutableRoots["Health.Head"]; break;
            case "skills": roots["Skills"] = original.MutableRoots["Skills"]; break;
            case "missing-state": roots["Inventory"] = null; break;
            case "old-item-id": replacement.ItemIds = new[] { "old-item" }; break;
            case "duplicate-item": replacement.ItemIds = new[] { "new-item", "new-item" }; break;
            case "empty-item-id": replacement.ItemIds = new[] { "" }; break;
            case "empty-inventory": replacement.ItemIds = Array.Empty<string>(); break;
        }
        Assert.Throws<InvalidOperationException>(() => ProfileReplacementPolicy.Validate(original, replacement));
        Assert.Equal("pmc", original.Id);
        Assert.Equal(new[] { "old-item" }, original.ItemIds);
    }

    [Fact]
    public void MutableAliasingIsComparedByReferenceAcrossAllRoots()
    {
        var original = Shape("pmc", "old-item");
        var replacement = Shape("scav", "new-item");
        ((Dictionary<string, object>)replacement.MutableRoots)["Health.Head"] = original.MutableRoots["Health.Chest"];
        Assert.Throws<InvalidOperationException>(() => ProfileReplacementPolicy.Validate(original, replacement));
    }

    [Fact]
    public void FreshUninitializedStatisticsAndQuestsAreAllowedButExistingAliasesAreRejected()
    {
        var original = Shape("pmc", "old-item");
        var replacement = Shape("scav", "new-item");
        original.OptionalMutableRoots = new Dictionary<string, object> { ["Stats.Eft"] = new object(), ["QuestsData"] = new object() };
        replacement.OptionalMutableRoots = new Dictionary<string, object>
        {
            ["Stats.Eft"] = null, ["Stats.SessionCounters"] = null, ["Stats.Victims"] = null, ["QuestsData"] = null
        };
        ProfileReplacementPolicy.Validate(original, replacement);
        ((Dictionary<string, object>)replacement.OptionalMutableRoots)["Stats.Eft"] = original.OptionalMutableRoots["Stats.Eft"];
        Assert.Throws<InvalidOperationException>(() => ProfileReplacementPolicy.Validate(original, replacement));
    }

    private static ProfileReplacementPolicy.Shape Shape(string id, params string[] items) => new()
    {
        Profile = new object(), Id = id, ItemIds = items,
        MutableRoots = new Dictionary<string, object>
        {
            ["Inventory"] = new object(), ["Stats"] = new object(), ["Health.Head"] = new object(),
            ["Health.Chest"] = new object(), ["Skills"] = new object()
        }
    };

    [Fact]
    public void CompatibilityAdoptsOnlyTheExactRequestedProfileBeforeAndAfterCommit()
        => RunContext(ContextSource());

    [Fact]
    public void CompatibilityProbeDetectsProfileAndGameIdentityGuardRegressions()
    {
        var source = ContextSource();
        var oldProfileOnly = source.Replace("ReferenceEquals(body.Profile, _expectedProfile)", "ReferenceEquals(body.Profile, PreviousBody.Profile)");
        Assert.NotEqual(source, oldProfileOnly);
        Assert.Throws<TargetInvocationException>(() => RunContext(oldProfileOnly));
        var foreignAccepted = source.Replace("ReferenceEquals(CurrentGame.Profile, _expectedProfile)", "true");
        Assert.NotEqual(source, foreignAccepted);
        Assert.Throws<TargetInvocationException>(() => RunContext(foreignAccepted));
    }

    [Fact]
    public void ProfileCommitFollowsBothValidationsAndPrecedesLocalPlayerCommit()
    {
        var source = EngineSource();
        Assert.True(HasSafeCommitOrder(source));
        var commit = "game.Profile = replacementProfile;";
        var moved = source.Replace(commit, "").Replace("var newPlayer = createTask.Result;", "var newPlayer = createTask.Result; " + commit);
        Assert.False(HasSafeCommitOrder(moved));
        Assert.False(HasSafeCommitOrder(source.Replace("ProfileReplacementValidation.ValidateCreated(game, deadPlayer, newPlayer, originalProfile, replacementProfile);", "")));
    }

    [Fact]
    public void LegacyMutationsUseCurrentProfileAndNewIdentityClearsVictimCarryover()
    {
        var method = ParseRun(EngineSource());
        var reset = Call(method, "ResetProfileHealth");
        var factory = Call(method, "game.vmethod_3");
        var equipment = method.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>()
            .Single(x => x.Expression.ToString() == "request.EquipmentStrategy");
        Assert.Equal("replacementProfile ?? game.Profile", reset.ArgumentList.Arguments[0].Expression.ToString());
        Assert.Equal("replacementProfile ?? game.Profile", factory.ArgumentList.Arguments[7].Expression.ToString());
        Assert.Contains("replacementProfile ?? game.Profile", equipment.ToString());
        Assert.Equal("replacementProfile != null ? null : game.Profile",
            Call(method, "RaidVictimsSnapshot.Snapshot").ArgumentList.Arguments[0].Expression.ToString());
        var purge = Call(method, "DeadBodyGuard.PurgeRecords");
        Assert.Equal("replacementProfile == null", purge.Ancestors().OfType<IfStatementSyntax>().First().Condition.ToString());
        Assert.True(Call(method, "ProfileReplacementValidation.BeforeTeardown").SpanStart
                    < Call(method, "LocalGameAccessor.UnsubscribeDeathHandler").SpanStart);
    }

    [Fact]
    public void IdentityRegistryReplacesOnlyTheOwnedOldKeyAndDoesNotChangeTheCorpse()
    {
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "GameAccess", "LocalGameAccessor.cs"))).GetRoot();
        var methods = source.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText is "ValidateReplacementIdentity" or "ReplacePlayerIdentityInDictionary")
            .Select(m => m.ToFullString());
        var implementation = "using System; using System.Collections.Generic; using EFT; public static class Registry { "
            + "private static BoundaryTraverse GameTraverse(LocalGame game) => new BoundaryTraverse(game); " + string.Join("\n", methods) + " }";
        const string boundary = """
using System;
using System.Collections.Generic;
using EFT;
namespace EFT {
 public class Player { public string ProfileId; public object Inventory = new object(); }
 public class LocalPlayer : Player { }
 public class LocalGame { public Dictionary<string, Player> Players = new(); }
}
public class BoundaryTraverse {
 private LocalGame game; public BoundaryTraverse(LocalGame value) { game = value; }
 public BoundaryTraverse Field(string name) { if (name != "_players") throw new Exception("unexpected field"); return this; }
 public T GetValue<T>() => (T)(object)game.Players;
}
public static class RegistryProbe {
 private static void Expect(bool value, string reason) { if (!value) throw new Exception(reason); }
 public static void Run() {
  var game = new LocalGame(); var old = new LocalPlayer { ProfileId = "pmc" };
  var corpseGear = old.Inventory; var fresh = new LocalPlayer { ProfileId = "scav" };
  var unrelated = new Player { ProfileId = "bot" };
  game.Players.Add(old.ProfileId, old); game.Players.Add(unrelated.ProfileId, unrelated);
  Registry.ValidateReplacementIdentity(game, old, fresh.ProfileId);
  Expect(game.Players.Count == 2 && ReferenceEquals(game.Players["pmc"], old), "preflight mutated registry");
  game.Players.Add("scav", unrelated);
  bool refused = false; try { Registry.ReplacePlayerIdentityInDictionary(game, old, fresh); } catch (InvalidOperationException) { refused = true; }
  Expect(refused && ReferenceEquals(game.Players["pmc"], old) && ReferenceEquals(game.Players["scav"], unrelated), "occupied identity was overwritten");
  game.Players.Remove("scav"); game.Players["pmc"] = unrelated;
  refused = false; try { Registry.ReplacePlayerIdentityInDictionary(game, old, fresh); } catch (InvalidOperationException) { refused = true; }
  Expect(refused && ReferenceEquals(game.Players["pmc"], unrelated), "foreign old entry was removed");
  game.Players["pmc"] = old;
  Registry.ReplacePlayerIdentityInDictionary(game, old, fresh);
  Expect(game.Players.Count == 2 && !game.Players.ContainsKey("pmc") && ReferenceEquals(game.Players["scav"], fresh), "old identity key retained");
  Expect(ReferenceEquals(game.Players["bot"], unrelated) && ReferenceEquals(old.Inventory, corpseGear) && old.ProfileId == "pmc", "corpse or unrelated player mutated");
 }
}
""";
        CompileAndRun(new[] { implementation, boundary }, "RegistryProbe");
    }

    [Fact]
    public void ExplicitInfilProfileUsesNewFactionBeforeCommitAndRefusesFallback()
    {
        var source = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "Spawning", "SpawnSelectors.cs"))).GetRoot();
        var declarations = source.DescendantNodes().Where(n => n is InterfaceDeclarationSyntax i && i.Identifier.ValueText == "ISpawnSelector"
            || n is ClassDeclarationSyntax c && c.Identifier.ValueText is "InfilSpawnSelector" or "GameSpawnPoints");
        var implementation = "using System; using EFT; using EFT.Game.Spawning; using UnityEngine; namespace Redeploy.Core.Spawning { "
            + string.Join("\n", declarations.Select(n => n.ToFullString())) + " }";
        const string boundary = """
using System;
using EFT;
using Redeploy.Core.Spawning;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 { public int Value; public static Vector3 zero => new(); public static Vector3 up => new() { Value = 1 };
  public static Vector3 operator +(Vector3 a, Vector3 b) => new() { Value = a.Value + b.Value };
  public static Vector3 operator *(Vector3 a, float value) => new() { Value = (int)(a.Value * value) }; }
 public struct Quaternion { public static Quaternion identity => new(); }
}
namespace EFT {
 public enum Side { Pmc, Savage }
 public class ProfileInfo { public Side Side; }
 public class Profile { public string Id; public ProfileInfo Info = new(); }
 public class LocalGame { public Profile Profile; }
}
namespace EFT.Game.Spawning { public enum ESpawnCategory { Player } }
namespace Redeploy.Core.Spawning {
 public class Point { public Vector3 Position = new() { Value = 12 }; public Quaternion Rotation; }
 public class SpawnSystem {
  public Side ChosenSide; public string ChosenId; public Point Point = new();
  public Point SelectSpawnPoint(EFT.Game.Spawning.ESpawnCategory category, Side side, object a, object b, object c, object d, string id) {
   ChosenSide = side; ChosenId = id; return Point;
  }
 }
 public static class SpawnSystemCache { public static SpawnSystem Current = new(); public static SpawnSystem GetOrCreate(LocalGame game) => Current; }
 public class Fallback : ISpawnSelector { public int Calls; public (Vector3 position, Quaternion rotation) Resolve(LocalGame game) { Calls++; return (default, default); } }
}
public static class SpawnProbe {
 private static void Expect(bool value, string reason) { if (!value) throw new Exception(reason); }
 public static void Run() {
  var pmc = new Profile { Id = "pmc" }; var scav = new Profile { Id = "scav", Info = new() { Side = Side.Savage } };
  var game = new LocalGame { Profile = pmc }; var fallback = new Fallback();
  var selector = new InfilSpawnSelector(fallback, scav); var result = selector.Resolve(game);
  Expect(SpawnSystemCache.Current.ChosenSide == Side.Savage && SpawnSystemCache.Current.ChosenId == "scav", "spawn used original PMC identity");
  Expect(ReferenceEquals(game.Profile, pmc) && result.position.Value == 12, "spawn resolution committed profile or changed native position");
  SpawnSystemCache.Current.Point = null;
  bool refused = false; try { selector.Resolve(game); } catch (InvalidOperationException) { refused = true; }
  Expect(refused && fallback.Calls == 0, "explicit identity used unsafe fallback");
  new InfilSpawnSelector(fallback).Resolve(game);
  Expect(fallback.Calls == 1 && SpawnSystemCache.Current.ChosenSide == Side.Pmc, "ordinary selector fallback changed");
  Expect(new InfilSpawnSelector().Resolve(game).position.Value == 2, "ordinary origin fallback changed");
 }
}
""";
        CompileAndRun(new[] { implementation, boundary }, "SpawnProbe");
    }

    private static string EngineSource() => File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "Engine", "RespawnEngine.cs"));
    private static string ContextSource() => File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "Compat", "BodyReplacementContext.cs"));
    private static MethodDeclarationSyntax ParseRun(string source) => CSharpSyntaxTree.ParseText(source).GetRoot()
        .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(x => x.Identifier.ValueText == "RunRespawn");
    private static InvocationExpressionSyntax Call(SyntaxNode root, string name) => root.DescendantNodes()
        .OfType<InvocationExpressionSyntax>().Single(x => x.Expression.ToString() == name);
    private static bool HasSafeCommitOrder(string source)
    {
        var method = ParseRun(source);
        var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var validate = calls.SingleOrDefault(x => x.Expression.ToString() == "ProfileReplacementValidation.ValidateCreated");
        var compatibility = calls.SingleOrDefault(x => x.Expression.ToString() == "BodyReplacementCompatibility.ValidateCreated");
        var assignments = method.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToArray();
        var commit = assignments.Single(x => x.Left.ToString() == "game.Profile");
        var body = assignments.Single(x => x.Left.ToString() == "game.LocalPlayer");
        return validate != null && compatibility != null && validate.SpanStart < commit.SpanStart
               && compatibility.SpanStart < commit.SpanStart && commit.SpanStart < body.SpanStart
               && commit.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault()?.Condition.ToString() == "replacementProfile != null";
    }

    private static void RunContext(string source)
    {
        const string boundary = """
using System;
using EFT;
using Comfort.Common;
using Redeploy.Core.Compat;
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT {
 public enum GameStatus { Running, Starting, Started, Stopping }
 public class Profile { public string Id; }
 public class AbstractGame { }
 public class GameWorld { public Player MainPlayer; }
 public class HideoutGameWorld : GameWorld { }
 public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public Profile Profile; public GameStatus Status = GameStatus.Running; }
 public class Player { public bool IsYourPlayer; public GameWorld GameWorld; public Profile Profile; }
 public class LocalPlayer : Player { }
}
public static class ContextProbe {
 private static void Expect(bool value, string reason) { if (!value) throw new Exception(reason); }
 public static void Run() {
  var oldProfile = new Profile { Id = "pmc" };
  var target = new Profile { Id = "scav" };
  var world = new GameWorld();
  var old = new LocalPlayer { Profile = oldProfile, GameWorld = world, IsYourPlayer = true };
  var game = new LocalGame { Profile = oldProfile, LocalPlayer = old, GameWorld = world };
  world.MainPlayer = old; Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
  BodyReplacementContext.Begin(game, old, target);
  var candidate = new LocalPlayer { Profile = target, GameWorld = world, IsYourPlayer = true };
  world.MainPlayer = candidate;
  Expect(BodyReplacementContext.TryOwnCandidate(candidate), "expected target before commit");
  game.Profile = target; game.LocalPlayer = candidate;
  Expect(BodyReplacementContext.TryOwnCandidate(candidate), "expected target after commit");
  game.Profile = new Profile { Id = target.Id };
  Expect(!BodyReplacementContext.TryOwnCandidate(candidate), "foreign game profile must not be accepted even with same ID");
  game.Profile = target;
  candidate.Profile = new Profile { Id = target.Id };
  Expect(!BodyReplacementContext.TryOwnCandidate(candidate), "candidate must be exact requested profile, not same ID clone");
  candidate.Profile = oldProfile;
  Expect(!BodyReplacementContext.TryOwnCandidate(candidate), "candidate must not reuse previous profile");
  candidate.Profile = target; candidate.GameWorld = new GameWorld();
  Expect(!BodyReplacementContext.TryOwnCandidate(candidate), "foreign world");
  candidate.GameWorld = world;
  BodyReplacementContext.EndAttempt();
  Expect(!BodyReplacementContext.TryOwnCandidate(candidate), "closed attempt cannot adopt body");
  // Repeat the original same-profile path, including refusal of another profile after creation.
  game.Profile = oldProfile; game.LocalPlayer = old; world.MainPlayer = old;
  BodyReplacementContext.Begin(game, old);
  candidate.Profile = oldProfile; world.MainPlayer = candidate;
  Expect(BodyReplacementContext.TryOwnCandidate(candidate), "normal same-profile replacement remains valid");
  candidate.Profile = target;
  Expect(!BodyReplacementContext.TryOwnCandidate(candidate), "normal operation cannot adopt a new identity");
  BodyReplacementContext.Reset();
 }
}
""";
        CompileAndRun(new[] { source, boundary }, "ContextProbe");
    }

    private static void CompileAndRun(IEnumerable<string> sources, string probe)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ProfileContext_" + Guid.NewGuid().ToString("N"),
            sources.Select(source => CSharpSyntaxTree.ParseText(source)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        Assembly.Load(output.ToArray()).GetType(probe).GetMethod("Run").Invoke(null, null);
    }
}
