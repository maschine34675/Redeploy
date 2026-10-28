using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx;
using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace ScavRecoveryNativeProbe;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute : Attribute
{
    public UiBridgeActionAttribute(string name) { Name = name; }
    public string Name { get; }
    public string Description { get; set; }
    public bool Control { get; set; }
}

// Isolated development probe. No automatic seeding, activation fault or deployment.
[BepInPlugin(Id, "Scav Recovery Native Probe", "1.0.0")]
[BepInDependency("com.maschine.Redeploy")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Id = "com.maschine.RedeployRecoveryProbe";
    private const string RoubleTemplate = "5449016a4bdc2d6f028b456f";
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type _stateType, _sessionType;
    private static int _mainThread;
    private static Harmony _harmony;
    private static bool _ready;
    private static LocalGame _holdGame;
    private static GameWorld _holdWorld;
    private static int _heldAcknowledgements;
    private static LocalGame _exitGame;
    private static ExfiltrationController _exitController;
    private static ScavExfiltrationPoint[] _originalExits, _emptyExits;
    private static LocalGame _seedGame;
    private static Stash _seedStash;
    private static Grid _seedGrid;
    private static Item _seedItem;
    private static string _lastCleanup;

    private void Awake()
    {
        _mainThread = Thread.CurrentThread.ManagedThreadId;
        try
        {
            _stateType = Find("Redeploy.Services.RespawnSessionState");
            _sessionType = Find("Redeploy.Services.PmcScavSession");
            var activate = _sessionType?.GetMethod("ObserveActivation", All);
            if (_stateType == null || activate == null || !activate.IsStatic || activate.GetParameters().Length != 1)
                throw new InvalidOperationException("The expected Redeploy continuation contract is unavailable.");
            var gameEnd = AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GameEnd",
                new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });
            if (gameEnd == null)
                throw new InvalidOperationException("The native GameEnd cleanup boundary is unavailable.");
            _harmony = new Harmony(Id);
            _harmony.Patch(activate, prefix: new HarmonyMethod(typeof(Plugin), nameof(ActivationPrefix)));
            _harmony.Patch(gameEnd, prefix: new HarmonyMethod(typeof(Plugin), nameof(GameEndPrefix)) { priority = Priority.First });
            _ready = true;
            Logger.LogInfo("Development recovery probe ready; no fault or seeded data is active.");
        }
        catch (Exception ex)
        {
            _harmony?.UnpatchSelf();
            Logger.LogError("Recovery probe disabled: " + ex.Message);
        }
    }

    private void Update()
    {
        // Disarm a prior raid; this never starts a test action in a new scene.
        var game = CurrentGame;
        if (!ReferenceEquals(_holdGame, null) && !ReferenceEquals(game, _holdGame)) { _holdGame = null; _holdWorld = null; }
        if ((_exitController != null && !ReferenceEquals(game, _exitGame))
            || (_seedStash != null && !ReferenceEquals(game, _seedGame)))
        {
            try { CleanupTemporary(); }
            catch (Exception ex) { _lastCleanup = "Cleanup requires attention: " + ex.Message; }
        }
    }

    private void OnDestroy()
    {
        _ready = false;
        _holdGame = null; _holdWorld = null;
        try { CleanupTemporary(); }
        catch (Exception ex) { Logger.LogError("Probe cleanup requires attention: " + ex.Message); }
        _harmony?.UnpatchSelf();
    }

    private static Type Find(string name)
        => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name, false)).FirstOrDefault(type => type != null);

    private static object Read(Type type, string name, object instance = null)
        => type?.GetField(name, All)?.GetValue(instance) ?? type?.GetProperty(name, All)?.GetValue(instance);

    private static LocalGame CurrentGame => Read(_stateType, "CurrentLocalGame") as LocalGame;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 24);
    private static int BodyId(Player player) => player != null ? player.GetInstanceID() : 0;
    private static bool Adopted(LocalGame game)
        => game != null && (bool)(_sessionType?.GetMethod("HasSwitched", All)?.Invoke(null, new object[] { game }) ?? false);

    private static bool HasCurrentBody(LocalGame game, GameWorld world)
        => game != null && world != null && game.LocalPlayer != null && game.LocalPlayer.IsYourPlayer
            && ReferenceEquals(world.MainPlayer, game.LocalPlayer)
            && ReferenceEquals(game.LocalPlayer.GameWorld, world)
            && ReferenceEquals(game.Profile, game.LocalPlayer.Profile);

    private static (LocalGame game, GameWorld world, LocalPlayer player) RequireCurrent(bool alive = false, bool pmc = false)
    {
        if (!_ready || Thread.CurrentThread.ManagedThreadId != _mainThread)
            throw new InvalidOperationException("Probe controls require the initialized Unity main thread.");
        var game = CurrentGame;
        var world = World;
        if (game == null || game.GetType() != typeof(LocalGame) || game.GameType != EGameType.Offline
            || !ReferenceEquals(Singleton<AbstractGame>.Instance, game) || game.Status != GameStatus.Started
            || !HasCurrentBody(game, world))
            throw new InvalidOperationException("An owned single-player LocalGame with its current body is required (after Redeploy death interception).");
        if (world.RegisteredPlayers.Any(player => player != null && !player.IsAI && !player.IsYourPlayer))
            throw new InvalidOperationException("Foreign human players are present; this probe is single-player only.");
        if (alive && game.LocalPlayer.HealthController?.IsAlive != true)
            throw new InvalidOperationException("The current player must be alive for native BTR movement.");
        if (alive && !ReferenceEquals(world.GetAlivePlayerByProfileID(game.LocalPlayer.ProfileId), game.LocalPlayer))
            throw new InvalidOperationException("The native alive-player lookup does not own the current body.");
        if (pmc && (game.Profile.Info.Side == EPlayerSide.Savage || Adopted(game)))
            throw new InvalidOperationException("This pre-selection probe requires the original PMC life.");
        return (game, world, game.LocalPlayer);
    }

    private static string Run(Func<object> action)
    {
        try
        {
            if (_mainThread == 0 || Thread.CurrentThread.ManagedThreadId != _mainThread)
                throw new InvalidOperationException("Probe actions must execute on the Unity main thread.");
            return JsonConvert.SerializeObject(action());
        }
        catch (Exception ex)
        {
            var error = ex.GetBaseException().Message;
            return JsonConvert.SerializeObject(new { success = false, error = error.Length > 500 ? error.Substring(0, 500) : error });
        }
    }

    [UiBridgeAction("HoldActivation", Control = true, Description = "Explicitly suppress the current raid's own-Scav activation acknowledgment; false disarms without replaying an acknowledgment.")]
    public static string HoldActivation(bool enabled) => Run(() =>
    {
        if (!enabled)
        {
            _holdGame = null; _holdWorld = null;
            return new { success = true, held = false, heldAcknowledgements = _heldAcknowledgements };
        }
        var (game, world, _) = RequireCurrent(pmc: true);
        _holdGame = game; _holdWorld = world; _heldAcknowledgements = 0;
        return new { success = true, held = true, heldAcknowledgements = 0 };
    });

    private static bool ActivationPrefix(object __0)
    {
        if (_holdGame == null || __0 == null || !ReferenceEquals(CurrentGame, _holdGame)
            || !ReferenceEquals(World, _holdWorld) || !HasCurrentBody(_holdGame, _holdWorld))
            return true;
        var contextType = __0.GetType();
        if (!ReferenceEquals(Read(contextType, "Game", __0), _holdGame)
            || !ReferenceEquals(Read(contextType, "Scav", __0), _holdGame.Profile) || !Adopted(_holdGame))
            return true;
        _heldAcknowledgements++;
        return false;
    }

    [UiBridgeAction("SetScavExits", Control = true, Description = "false temporarily empties the exact current scene's Scav exits; true restores only the probe-owned replacement array.")]
    public static string SetScavExits(bool enabled) => Run(() =>
    {
        if (enabled) { RestoreExits(); return new { success = true, enabled = true, count = ExfiltrationController.Instance?.ScavExfiltrationPoints?.Length }; }
        var (game, _, _) = RequireCurrent(pmc: true);
        if (_exitController != null)
            throw new InvalidOperationException("Restore the previous exit probe before seeding another.");
        var controller = ExfiltrationController.Instance ?? throw new InvalidOperationException("No native extraction controller exists.");
        if (controller.ScavExfiltrationPoints == null || controller.ScavExfiltrationPoints.Length == 0)
            throw new InvalidOperationException("The native baseline has no Scav exits; nothing will be changed.");
        _exitGame = game; _exitController = controller; _originalExits = controller.ScavExfiltrationPoints;
        _emptyExits = new ScavExfiltrationPoint[0];
        controller.ScavExfiltrationPoints = _emptyExits;
        return new { success = true, enabled = false, originalCount = _originalExits.Length, count = 0 };
    });

    private static void RestoreExits()
    {
        if (_exitController == null) return;
        if (!ReferenceEquals(_exitController.ScavExfiltrationPoints, _emptyExits))
            throw new InvalidOperationException("Scav exit ownership changed; refusing to overwrite another array.");
        _exitController.ScavExfiltrationPoints = _originalExits;
        _exitGame = null; _exitController = null; _originalExits = _emptyExits = null;
    }

    [UiBridgeAction("SeedPriorDelivery", Control = true, Description = "true appends one probe-owned grid with one newly minted rouble to an existing native transit stash; false removes only that grid/item. Restore before PMC respawn.")]
    public static string SeedPriorDelivery(bool enabled) => Run(() =>
    {
        if (!enabled) { RemoveSeed(); return new { success = true, seeded = false }; }
        var (game, world, _) = RequireCurrent(pmc: true);
        if (_seedStash != null) throw new InvalidOperationException("Remove the prior delivery probe before seeding another.");
        var stash = world.TransitController?.TransferItemsController?.Stash
            ?? throw new InvalidOperationException("No existing native transit transfer stash is available.");
        if (stash.Grids == null) throw new InvalidOperationException("The native transfer grid array is unavailable.");
        var grid = new Grid(NewId(), 1, 1, false, false, Array.Empty<ItemFilter>(), stash);
        var item = MintRouble();
        AddToGrid(grid, item);
        _seedGame = game; _seedStash = stash; _seedGrid = grid; _seedItem = item;
        stash.Grids = stash.Grids.Concat(new[] { grid }).ToArray();
        return new { success = true, seeded = true, gridCount = stash.Grids.Length, ownedItemCount = grid.ItemCollection.Count };
    });

    private static Item MintRouble()
    {
        var item = Singleton<ItemFactory>.Instance.CreateItem(NewId(), RoubleTemplate, null);
        item.StackObjectsCount = 1;
        return item;
    }

    private static void AddToGrid(Grid grid, Item item)
    {
        var result = grid.Add(item, new LocationInGrid(0, 0, ItemRotation.Horizontal), simulate: false);
        if (result.Failed) throw new InvalidOperationException("The minted probe item could not enter its own grid: " + result.Error);
    }

    private static void RemoveSeed()
    {
        if (_seedStash == null) return;
        if (_seedGrid.ItemCollection.Any(pair => !ReferenceEquals(pair.Key, _seedItem)))
            throw new InvalidOperationException("The owned probe grid contains a foreign item; no data was removed.");
        if (_seedItem.Parent != null && !ReferenceEquals(_seedItem.Parent.Container, _seedGrid))
            throw new InvalidOperationException("The minted item left its owned grid; refusing to modify any foreign inventory.");
        if (_seedStash.Grids == null)
            throw new InvalidOperationException("The owned stash grid array was replaced with null; manual review is required.");
        if (_seedGrid.ItemCollection.Any())
        {
            var removed = _seedGrid.Remove(_seedItem, simulate: false);
            if (removed.Failed) throw new InvalidOperationException("The minted item could not be removed from its own grid.");
        }
        _seedStash.Grids = _seedStash.Grids.Where(grid => !ReferenceEquals(grid, _seedGrid)).ToArray();
        _seedGame = null; _seedStash = null; _seedGrid = null; _seedItem = null;
    }

    [UiBridgeAction("BtrMoveProbe", Control = true, Description = "Exercise the real patched native BTR movement on a temporary controller and minted item; report movement and clean only newly registered probe owners.")]
    public static string BtrMoveProbe() => Run(() =>
    {
        var (game, world, player) = RequireCurrent(alive: true);
        bool adopted = Adopted(game);
        int ownersBefore = world.ItemOwners.Count;
        var originalOwners = new HashSet<IItemOwner>(world.ItemOwners.Keys);
        var controller = new BtrTransferItemsController(world, null, true);
        Stash temp = null;
        Item minted = null;
        string failure = null;
        int tempBefore = -1, tempAfter = -1, deliveredBefore = -1, deliveredAfter = -1, addedOwners = -1;
        try
        {
            controller.InitItemControllerServer(NewId(), "Redeploy Recovery Probe");
            controller.InitPlayerStash(player);
            temp = controller.GetOrAddTransferContainer(player.ProfileId);
            minted = MintRouble();
            AddToGrid(temp.Grid, minted);
            var destination = controller.Stash.Grids.Single(grid => grid.ID == player.ProfileId);
            tempBefore = temp.Grid.ItemCollection.Count;
            deliveredBefore = destination.ItemCollection.Count;
            addedOwners = world.ItemOwners.Count - ownersBefore;
            controller.MoveItemsFromTempStashToTransferStash(player);
            tempAfter = temp.Grid.ItemCollection.Count;
            deliveredAfter = destination.ItemCollection.Count;
        }
        catch (Exception ex) { failure = ex.GetBaseException().Message; }
        finally
        {
            // The controller owns these roots only. Never remove a pre-existing owner,
            // or an owner merely sharing the player's profile ID or native trader ID.
            var ownedRoots = new List<Item> { controller.Stash };
            ownedRoots.AddRange(controller._transferContainers);
            foreach (var owner in world.ItemOwners.Keys.Where(owner => !originalOwners.Contains(owner)
                && ownedRoots.Any(root => root != null && ReferenceEquals(owner.RootItem, root))).ToArray())
                world.ItemOwners.Remove(owner);
        }
        bool hasBody = HasCurrentBody(game, world);
        int ownersAfter = world.ItemOwners.Count;
        bool preservedOwners = originalOwners.All(world.ItemOwners.ContainsKey);
        int moved = deliveredAfter - deliveredBefore;
        bool expected = adopted ? moved == 0 && tempAfter == 1 : moved == 1 && tempAfter == 0;
        return new { success = failure == null && expected && ownersAfter == ownersBefore && preservedOwners && hasBody,
            error = failure,
            scope = "native patched BTR item movement on temporary controller; no BTR ride or UI purchase",
            adopted, HasCurrentBody = hasBody, bodyId = BodyId(player), tempBefore, tempAfter,
            deliveredBefore, deliveredAfter, moved, addedOwners, ownersBefore, ownersAfter, preservedOwners };
    });

    private static void CleanupTemporary()
    {
        var errors = new List<string>();
        try { RemoveSeed(); } catch (Exception ex) { errors.Add(ex.Message); }
        try { RestoreExits(); } catch (Exception ex) { errors.Add(ex.Message); }
        if (errors.Count != 0)
        {
            _lastCleanup = string.Join("; ", errors);
            throw new InvalidOperationException(_lastCleanup);
        }
        _lastCleanup = "Owned temporary delivery grid and exit replacement are absent.";
    }

    private static void GameEndPrefix(object __instance)
    {
        if (ReferenceEquals(__instance, _seedGame) || ReferenceEquals(__instance, _exitGame))
            CleanupTemporary();
    }

    [UiBridgeAction("Status", Control = true, Description = "Bounded probe state: current body identity, flags and counts; no profile snapshots or continuation credentials.")]
    public static string Status() => Run(() =>
    {
        var game = CurrentGame;
        var world = World;
        return new { success = true, ready = _ready, HasCurrentBody = HasCurrentBody(game, world),
            gameType = game?.GetType().FullName, gameStatus = game?.Status.ToString(), bodyId = BodyId(game?.LocalPlayer),
            alive = game?.LocalPlayer?.HealthController?.IsAlive, side = game?.Profile?.Info?.Side.ToString(),
            adopted = Adopted(game), holdActivation = _holdGame != null && ReferenceEquals(_holdGame, game),
            heldAcknowledgements = _heldAcknowledgements, exitsOverridden = _exitController != null,
            scavExitCount = ExfiltrationController.Instance?.ScavExfiltrationPoints?.Length,
            priorDeliverySeeded = _seedStash != null, seededItemCount = _seedGrid?.ItemCollection.Count,
            itemOwnerCount = world?.ItemOwners.Count, lastCleanup = _lastCleanup };
    });
}
