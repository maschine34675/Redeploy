using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using PlayerEncumbranceBar;
using PlayerEncumbranceBar.Utils;
using Redeploy.Core.Compat;

internal static class Program
{
    private static int _checks;
    private static void Require(bool ok, string message)
    { if (!ok) throw new Exception(message); _checks++; Console.WriteLine("PASS " + message); }
    private static void Main()
    {
        Require(PlayerEncumbranceBarCompat.GetRejectionReason() == null, "absent plugin is inert");
        var plugin = new Plugin();
        Chainloader.PluginInfos.Add(PlayerEncumbranceBarCompat.PluginGuid, new PluginInfo { Instance = plugin, Metadata = typeof(Plugin).GetCustomAttribute<BepInPlugin>() });
        Require(PlayerEncumbranceBarCompat.GetRejectionReason() == null, "released private binding contract accepted");
        for (int i = 0; i < 100; i++) Require(PlayerEncumbranceBarCompat.GetRejectionReason() == null, "cached admission remains stable");
        var world = Singleton<GameWorld>.Instance = new();
        var game = new LocalGame { GameWorld = world, Profile = GameUtils.Backend };
        Singleton<AbstractGame>.Instance = game;
        var old = new LocalPlayer { GameWorld = world, Profile = game.Profile, IsYourPlayer = true };
        world.MainPlayer = game.LocalPlayer = old; game.PlayerOwner.Player = old;
        var bar = plugin.PlayerEncumbranceBar;
        bar.Show(old.HealthController);

        old.Profile.Inventory.Set(10);
        Require(bar.DisplayedWeight == 10, "ordinary pre-replacement weight remains native");
        var replacement = new Profile(); replacement.Inventory.Value = 35;
        BodyReplacementContext.Begin(game, old, replacement);
        PlayerEncumbranceBarCompat.Prepare(game, old);
        PlayerEncumbranceBarCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        old.Profile.Inventory.Set(15);
        Require(bar.DisplayedWeight == 15, "refused preparation retains original subscription");

        void BeginPreparation()
        { BodyReplacementContext.Begin(game, old, replacement); PlayerEncumbranceBarCompat.Prepare(game, old); }
        void CancelPreparation()
        { PlayerEncumbranceBarCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation(); }

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation();
        // A later preparer (for example ladder dismount) throws before teardown.
        try { throw new InvalidOperationException("fixture downstream prepare failure"); }
        catch (InvalidOperationException) { CancelPreparation(); }
        old.Profile.Inventory.Set(17);
        Require(bar.DisplayedWeight == 17 && old.Profile.Inventory.Subscribers == 1 && bar.GameObject.activeSelf,
            "completed preparation rollback restores ordinary live inventory callbacks after context cancellation");
        int shows = bar.Shows;
        PlayerEncumbranceBarCompat.AbortPreparation();
        Require(bar.Shows == shows, "cancelled rollback is one-shot");

        BeginPreparation(); bar.Show(old.HealthController); shows = bar.Shows;
        PlayerEncumbranceBarCompat.CompletePreparation(); CancelPreparation(); old.Profile.Inventory.Set(171);
        Require(bar.Shows == shows && bar.DisplayedWeight == 171 && old.Profile.Inventory.Subscribers == 1 && bar.GameObject.activeSelf,
            "new same-body Show between capture and cleanup is not disposed");

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); bar.Show(old.HealthController); shows = bar.Shows;
        CancelPreparation(); old.Profile.Inventory.Set(172);
        Require(bar.Shows == shows && bar.DisplayedWeight == 172 && old.Profile.Inventory.Subscribers == 1,
            "new same-body Show after cleanup owns its subscriptions without rollback replay");

        BeginPreparation(); bar.ThrowAfterDispose = true;
        try { PlayerEncumbranceBarCompat.CompletePreparation(); throw new Exception("dispose fixture did not throw"); }
        catch (InvalidOperationException) { CancelPreparation(); }
        old.Profile.Inventory.Set(18);
        Require(bar.DisplayedWeight == 18 && old.Profile.Inventory.Subscribers == 1,
            "partial foreign disposal is rolled back without duplicate subscriptions");

        bar.Dispose(); shows = bar.Shows;
        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); CancelPreparation();
        Require(bar.Shows == shows && !bar.GameObject.activeSelf && ReferenceEquals(PlayerEncumbranceBarComponent.Health, old.HealthController)
            && old.Profile.Inventory.Subscribers == 0, "cancel does not reopen a previously hidden panel");
        bar.Show(old.HealthController);

        bar.HideGameObject(); shows = bar.Shows;
        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); CancelPreparation(); old.Profile.Inventory.Set(181);
        Require(bar.Shows == shows + 1 && !bar.GameObject.activeSelf && bar.DisplayedWeight == 181
            && old.Profile.Inventory.Subscribers == 1, "hidden subscribed display restores subscriptions independently of visibility");
        bar.ShowGameObject();

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); shows = bar.Shows;
        var menuHealth = new EFT.HealthSystem.Health(); bar.Show(menuHealth);
        CancelPreparation();
        Require(bar.Shows == shows + 1 && ReferenceEquals(PlayerEncumbranceBarComponent.Health, menuHealth),
            "cancel cannot overwrite a later menu Show");
        bar.Show(old.HealthController);

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); shows = bar.Shows;
        var otherWorld = new GameWorld(); Singleton<GameWorld>.Instance = otherWorld;
        CancelPreparation();
        Require(bar.Shows == shows, "cancel does not restore into another world");
        Singleton<GameWorld>.Instance = world; bar.Show(old.HealthController);

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); shows = bar.Shows;
        game.PlayerOwner.Player = new LocalPlayer { Profile = old.Profile, GameWorld = world, IsYourPlayer = true };
        CancelPreparation();
        Require(bar.Shows == shows, "cancel cannot replace a newer physical UI owner even with the same profile");
        game.PlayerOwner.Player = old; bar.Show(old.HealthController);

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); shows = bar.Shows;
        game.Profile = new Profile(); CancelPreparation();
        Require(bar.Shows == shows, "cancel cannot bind a changed game profile to the old body");
        game.Profile = old.Profile; bar.Show(old.HealthController);

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); shows = bar.Shows;
        var newerBar = new PlayerEncumbranceBarComponent(); plugin.PlayerEncumbranceBar = newerBar; newerBar.Show(old.HealthController);
        CancelPreparation(); old.Profile.Inventory.Set(19);
        Require(bar.Shows == shows && newerBar.DisplayedWeight == 19,
            "cancel preserves a newer native bar and its ordinary inventory callbacks");
        newerBar.Dispose(); plugin.PlayerEncumbranceBar = bar; bar.Show(old.HealthController);

        BeginPreparation(); PlayerEncumbranceBarCompat.CompletePreparation(); bar.ThrowOnShow = true;
        CancelPreparation();
        Require(!BodyReplacementContext.IsCreating, "foreign rollback failure cannot prevent context cancellation");
        bar.Show(old.HealthController);
        BodyReplacementContext.Begin(game, old, replacement);
        PlayerEncumbranceBarCompat.Prepare(game, old); PlayerEncumbranceBarCompat.CompletePreparation();
        int updates = bar.Updates;
        old.Profile.Inventory.Set(20);
        Require(bar.Updates == updates && old.Profile.Inventory.Subscribers == 0 && PlayerEncumbranceBarComponent.Health == null, "outgoing inventory detached before private health is cleared");
        var next = new LocalPlayer { GameWorld = world, Profile = replacement, IsYourPlayer = true };
        world.MainPlayer = next;
        Require(BodyReplacementContext.TryOwnCandidate(next), "replacement acquired by exact Core operation");
        game.Profile = replacement; game.LocalPlayer = next; game.PlayerOwner.Player = next;
        bar.Show(next.HealthController);
        Require(bar.DisplayedWeight == 35, "replacement panel reads new physical profile");
        shows = bar.Shows;
        PlayerEncumbranceBarCompat.EndAttempt();
        BodyReplacementContext.EndAttempt();
        PlayerEncumbranceBarCompat.AbortPreparation();
        Require(bar.Shows == shows && ReferenceEquals(PlayerEncumbranceBarComponent.Health, next.HealthController),
            "successful replacement forgets rollback without another Show or restoring the old health");
        next.Profile.Inventory.Set(60);
        Require(bar.DisplayedWeight == 60, "replacement weight event updates visible bar");
        BodyReplacementContext.Begin(game, next); PlayerEncumbranceBarCompat.Prepare(game, next);
        PlayerEncumbranceBarCompat.CompletePreparation(); CancelPreparation(); next.Profile.Inventory.Set(61);
        Require(bar.DisplayedWeight == 61 && next.Profile.Inventory.Subscribers == 1,
            "cancellation after an earlier successful swap restores prior owned bar lease");
        updates = bar.Updates; old.Profile.Inventory.Set(99);
        Require(bar.Updates == updates && bar.DisplayedWeight == 61, "retired profile cannot refresh replacement panel");
        var unrelated = new LocalPlayer { GameWorld = world, Profile = replacement, IsYourPlayer = true };
        game.PlayerOwner.Player = unrelated;
        bar.Refresh(); Require(bar.Updates == updates, "same-profile unrelated owner cannot refresh tracked bar");
        game.PlayerOwner.Player = next;
        PlayerEncumbranceBarCompat.RetireWorld(world); BodyReplacementContext.Reset();
        updates = bar.Updates; next.Profile.Inventory.Set(80);
        Require(bar.Updates == updates && next.Profile.Inventory.Subscribers == 0 && PlayerEncumbranceBarComponent.Health == null, "world retirement releases replacement subscriptions and private health");
        bar.Refresh(); Require(bar.Updates == updates, "late callback to closed raid panel is inert");
        bar.Show(new EFT.HealthSystem.Health()); GameUtils.Backend.Inventory.Set(7);
        Require(bar.DisplayedWeight == 7, "menu backend profile resumes after raid");
        Console.WriteLine("PLAYER_ENCUMBRANCE_BAR_CHECKS_PASSED " + _checks);
    }
}
