using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using Mono.Cecil;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using Softwyx.LootInVicinity;
using Softwyx.LootInVicinity.LivPlayer;
using Softwyx.LootInVicinity.Raid;
using Softwyx.LootInVicinity.Session;
using Softwyx.LootInVicinity.Ui;
using Softwyx.LootInVicinity.Ui.Handlers;

internal static class Program
{
    private static int checks;
    private static readonly GameWorld World = new();
    private static readonly LocalGame Game = new() { GameWorld = World };
    private static LocalPlayer Body;

    private static void Check(bool outcome, string reason)
    {
        if (!outcome) throw new Exception(reason);
        checks++;
    }

    private static void ValidateRelease(string file)
    {
        using var release = AssemblyDefinition.ReadAssembly(file);
        var plugin = new LootInVicinityPlugin();
        var meta = plugin.GetType().GetCustomAttribute<BepInPlugin>();
        var contract = LootInVicinityContract.Resolve(plugin.GetType(), meta.GUID, meta.Version);
        var module = release.MainModule;
        var type = module.GetType(plugin.GetType().FullName);
        var attribute = type.CustomAttributes.Single(a => a.AttributeType.FullName == typeof(BepInPlugin).FullName);
        Check(release.Name.Name == plugin.GetType().Assembly.GetName().Name && release.Name.Version == LootInVicinityContract.Reviewed
            && (string)attribute.ConstructorArguments[0].Value == LootInVicinityCompat.PluginGuid
            && (string)attribute.ConstructorArguments[2].Value == "3.0.6", "released plugin identity");
        foreach (var field in contract.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var member = (MemberInfo)field.GetValue(contract);
            var owner = module.GetType(member.DeclaringType.FullName);
            if (member is FieldInfo f)
            {
                var real = owner.Fields.SingleOrDefault(r => r.Name == f.Name && r.IsStatic && r.IsInitOnly == f.IsInitOnly);
                Check(real?.FieldType.FullName == TypeName(f.FieldType), "released field: " + f.Name);
            }
            else
            {
                var method = (MethodInfo)member;
                var args = method.GetParameters().Select(a => TypeName(a.ParameterType));
                Check(owner.Methods.Any(m => m.Name == method.Name && m.IsStatic && m.ReturnType.FullName == TypeName(method.ReturnType)
                    && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args)), "released method: " + method.Name);
            }
        }
        var open = module.GetType("Softwyx.LootInVicinity.Ui.Handlers.VicinityItemsPanelOpenHandler").NestedTypes
            .Single(t => t.Name.StartsWith("<OpenAfterItemsPanelShow>")).Methods.Single(m => m.Name == "MoveNext");
        Check(open.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_openGeneration")
            && open.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "IsLocalRaidInventory"),
            "released pending attach no longer checks generation and controller");
        var close = module.GetType("Softwyx.LootInVicinity.Ui.Handlers.VicinityInventoryCloseHandler").Methods
            .Single(m => m.Name == "OnInventoryScreenClose");
        Check(close.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "HideIfActive"),
            "released inventory-close handler no longer retires its visible vicinity panel");
    }

    private static string TypeName(Type type) => type.IsGenericType
        ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">"
        : type.FullName;

    private static void Prepare()
    {
        BodyReplacementContext.Begin(Game, Body);
        LootInVicinityCompat.Prepare(Game, Body);
    }

    private static void Refused(Action action)
    {
        int generation = VicinityItemsPanelOpenHandler._openGeneration;
        int closes = VicinityPanelPresenter.Closed, cancels = VicinityPanelPresenter.Cancelled;
        try { action(); }
        catch (InvalidOperationException)
        {
            Check(generation == VicinityItemsPanelOpenHandler._openGeneration && closes == VicinityPanelPresenter.Closed
                && cancels == VicinityPanelPresenter.Cancelled, "refusal touched panel/rows");
            LootInVicinityCompat.AbortPreparation();
            BodyReplacementContext.CancelPreparation();
            return;
        }
        throw new Exception("unsafe handoff was accepted");
    }

    private static LocalPlayer NewBody(bool scav = false)
    {
        var next = new LocalPlayer { GameWorld = World, Profile = scav ? new Profile { Id = "own-scav" } : Body.Profile };
        World.RegisterPlayer(next);
        Check(BodyReplacementContext.TryOwnCandidate(next), "candidate physical lease");
        Game.LocalPlayer = next;
        GamePlayerOwner.MyPlayer = next;
        Game.Profile = next.Profile;
        return next;
    }

    public static void Main(string[] args)
    {
        ValidateRelease(args.Single());
        Singleton<GameWorld>.Instance = World;
        Singleton<AbstractGame>.Instance = Game;
        Body = new LocalPlayer { GameWorld = World };
        Game.Profile = Body.Profile;
        GamePlayerOwner.MyPlayer = Game.LocalPlayer = Body;
        World.RegisterPlayer(Body);
        Check(LootInVicinityCompat.GetRejectionReason() == null, "absent mod refused");
        var plugin = new LootInVicinityPlugin();
        Chainloader.PluginInfos[LootInVicinityCompat.PluginGuid] = new PluginInfo { Instance = plugin, Metadata = plugin.GetType().GetCustomAttribute<BepInPlugin>() };
        Check(LootInVicinityCompat.GetRejectionReason() == null, "reviewed release not admitted");
        Check(VicinityLocalPlayer.TryBind() && VicinityLocalPlayer.Find() == Body, "normal local bind");

        var stash = VicinityRaidServices.RadiusStash;
        var grid = stash.Grids[0];
        var staged = new Item { Id = "actual player item", CurrentAddress = new Address { Owner = stash } };
        grid.Items.Add(staged);
        VicinityStagingRegistry.StagedItemIds.Add(staged.Id);
        Refused(Prepare);
        Check(grid.Items.Single() == staged && staged.CurrentAddress.Owner == stash && VicinityStagingRegistry.StagedItemIds.Contains(staged.Id),
            "staged item was removed, dropped or orphaned");
        VicinityStagingRegistry.StagedItemIds.Clear();
        Refused(Prepare); // even an unregistered real item must never enter ClearStashContents
        grid.Items.Clear();
        VicinityTakeCleanup.PendingTakeIds.Add("pending");
        Refused(Prepare);
        Check(VicinityTakeCleanup.PendingTakeIds.Contains("pending"), "lost deferred pickup");
        VicinityTakeCleanup.PendingTakeIds.Clear();

        var worldOwner = new object();
        var ground = new Item { Id = "ground", CurrentAddress = new Address { Owner = worldOwner } };
        var pile = new LootItem { Item = ground, ItemOwner = worldOwner };
        grid.Items.Add(ground);
        World.ItemOwners.Add(worldOwner, pile);
        VicinityListedLootRegistry.ListedToWorld.Add(ground.Id, pile);
        VicinityPanelPresenter.HasActiveVicinityWork = true;
        VicinityPanelPresenter.BoundItemsPanel = new ItemsPanel { Inventory = Body.InventoryController.Inventory };
        VicinityPanelPresenter._attachHost = VicinityPanelPresenter.BoundItemsPanel;
        ground.CurrentAddress.Owner = Body.InventoryController;
        Refused(Prepare); // move has begun, pending IDs still empty
        ground.CurrentAddress.Owner = worldOwner;
        ground.Obsolete = true;
        Refused(Prepare); // the upstream deferred removal still needs the listing
        ground.Obsolete = false;
        VicinityPanelPresenter.BoundItemsPanel.Inventory = new Inventory();
        Refused(Prepare);
        VicinityPanelPresenter.BoundItemsPanel.Inventory = Body.InventoryController.Inventory;
        Prepare();
        Check(grid.Items.Single() == ground && VicinityPanelPresenter.HasActiveVicinityWork, "Prepare mutated old panel");
        LootInVicinityCompat.CompletePreparation();
        Check(VicinityItemsPanelOpenHandler._openGeneration == 1 && !VicinityPanelPresenter.HasActiveVicinityWork &&
            VicinityPanelPresenter._attachHost == null && VicinityPanelPresenter.Closed == 1 && grid.Items.Count == 0, "old listing/attach not closed");
        Check(ReferenceEquals(World.ItemOwners[worldOwner], pile) && pile.Item == ground && ground.CurrentAddress.Owner == worldOwner
            && ReferenceEquals(stash, VicinityRaidServices.RadiusStash), "real ground loot or raid stash changed");
        Check(VicinityLocalPlayer.Find() == null && !VicinityLocalPlayer.TryBind(), "rebound old player in preparation");
        LootInVicinityCompat.AbortPreparation();
        BodyReplacementContext.CancelPreparation();
        Check(VicinityLocalPlayer.Find() == Body && VicinityLocalPlayer._cachedController == Body.InventoryController, "abort failed to restore old cache");

        Prepare();
        LootInVicinityCompat.CompletePreparation();
        var next = NewBody();
        Check(!VicinityLocalPlayer.TryBind() && VicinityLocalPlayer.Find() == null, "uncommitted candidate bound");
        RespawnApi.Publish(Body, next);
        BodyReplacementContext.EndAttempt();
        Body = next;
        Check(VicinityLocalPlayer._raidPlayer == Body && VicinityLocalPlayer._cachedController == Body.InventoryController,
            "same-profile cache did not change both physical owners");
        grid.Items.Add(ground);
        VicinityListedLootRegistry.ListedToWorld.Add(ground.Id, pile);
        VicinityPanelPresenter.HasActiveVicinityWork = true;
        VicinityPanelPresenter.BoundItemsPanel = new ItemsPanel { Inventory = Body.InventoryController.Inventory };
        VicinityPanelPresenter.HideIfActive(null); // the original synchronous InventoryScreen.Close path
        Check(VicinityPanelPresenter.Closed == 2 && grid.Items.Count == 0 && VicinityLocalPlayer.Find() == next
            && ground.CurrentAddress.Owner == worldOwner && ReferenceEquals(World.ItemOwners[worldOwner], pile),
            "successor's real close was suppressed or altered world ownership");

        Prepare();
        LootInVicinityCompat.CompletePreparation();
        next = NewBody();
        GamePlayerOwner.MyPlayer = null;
        RespawnApi.Publish(Body, next);
        BodyReplacementContext.EndAttempt();
        LootInVicinityCompat.Tick();
        Check(LootInVicinityCompat.GetRejectionReason() == null && VicinityLocalPlayer.Find() == null,
            "published body with delayed input owner must wait without permanently refusing");
        GamePlayerOwner.MyPlayer = next;
        LootInVicinityCompat.Tick();
        Check(VicinityLocalPlayer._raidPlayer == next && VicinityLocalPlayer._cachedController == next.InventoryController
            && VicinityLocalPlayer.TryBind(), "delayed owner did not release and bind the published physical body");
        Body = next;

        Prepare();
        LootInVicinityCompat.CompletePreparation();
        BodyReplacementContext.EndAttempt();
        RespawnApi.Fail(Body);
        Check(!VicinityLocalPlayer.TryBind(), "failed handoff rebound before recovery");
        LootInVicinityCompat.Tick();
        Check(VicinityLocalPlayer.Find() == Body, "old survivor not restored");

        BodyReplacementContext.Begin(Game, Body, new Profile { Id = "own-scav" });
        LootInVicinityCompat.Prepare(Game, Body);
        LootInVicinityCompat.CompletePreparation();
        next = new LocalPlayer { GameWorld = World, Profile = BodyReplacementContext.ExpectedProfile };
        World.RegisterPlayer(next);
        Check(BodyReplacementContext.TryOwnCandidate(next), "Scav lease");
        GamePlayerOwner.MyPlayer = Game.LocalPlayer = next;
        Game.Profile = next.Profile;
        BodyReplacementContext.EndAttempt();
        RespawnApi.Fail(Body);
        LootInVicinityCompat.Tick();
        Body = next;
        Check(VicinityLocalPlayer.Find() == next && VicinityLocalPlayer._cachedController == next.InventoryController, "Scav recovery");
        LootInVicinityCompat.RetireWorld(World);
        BodyReplacementContext.Reset();
        VicinityLocalPlayer._raidPlayer = null;
        VicinityLocalPlayer._cachedController = null;
        Prepare();
        LootInVicinityCompat.CompletePreparation();
        next = NewBody();
        RespawnApi.Publish(Body, next);
        BodyReplacementContext.EndAttempt();
        Check(VicinityLocalPlayer._raidPlayer == null && VicinityLocalPlayer._cachedController == null
            && VicinityLocalPlayer.Find() == next, "uninitialized native cache did not stay lazy");
        LootInVicinityCompat.RetireWorld(new GameWorld());
        LootInVicinityCompat.RetireWorld(World);
        Chainloader.PluginInfos["com.fika.core"] = new PluginInfo();
        Check(LootInVicinityCompat.GetRejectionReason() == null, "Fika refusal");
        Check(HarmonyLib.Harmony.GetPatchInfo(typeof(VicinityLocalPlayer).GetMethod(nameof(VicinityLocalPlayer.TryBind)))?.Prefixes
            .Any(p => p.owner == "com.maschine.Redeploy.Core.LootInVicinity") != true, "Fika retained the cache fence");
        Console.WriteLine("VICINITY_BODY_CHECKS_PASSED assertions=" + checks);
    }
}
