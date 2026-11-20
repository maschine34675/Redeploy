using BepInEx.Bootstrap;
using Comfort.Common;
using DynamicMaps;
using DynamicMaps.DynamicMarkers;
using EFT;
using Redeploy.Core;
using Redeploy.Core.Compat;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
LocalPlayer Body(GameWorld world, bool scav = false) => new() { GameWorld = world, Side = scav ? EPlayerSide.Savage : EPlayerSide.Usec };
var world = new GameWorld(); Singleton<GameWorld>.Instance = world;
var first = Body(world); world.MainPlayer = first;
DynamicMapsCompatHelper.RebindPlayerMarker(first);
Check(CoreLog.Warnings.Count == 0, "Absent plugin must be inert and silent");
var plugin = new Plugin();
var metadata = new PluginInfo { Instance = plugin };
if (args.Contains("--legacy")) metadata.Metadata.Version = new Version(1, 2, 1);
Chainloader.PluginInfos.Add("com.mpstark.dynamicmaps", metadata);
var screen = plugin.Map;
var arrow = new PlayerMarkerProvider(); arrow.Seed(first); screen.Add(arrow);
var quests = new QuestMarkerProvider(); screen.Add(quests); quests.OnShowInRaid(screen.View);
var doors = new LockedDoorMarkerMutator(); screen.Add(doors); doors.OnShowInRaid(screen.View);
Check(first.QuestController.Subscribers == 1 && first.InventoryController.Subscribers == 1, "Fixture did not capture old subscriptions");

var next = Body(world); world.MainPlayer = next;
DynamicMapsCompatHelper.RebindPlayerMarker(next);
Check(ReferenceEquals(arrow.Bound, next), "Arrow retained retired body");
if (args.Contains("--legacy"))
{
    Check(quests.Show == 1 && quests.Hide == 0 && doors.Show == 1 && doors.Hide == 0, "Legacy version received new-provider operations");
    Check(CoreLog.Warnings.Count == 0, "Legacy version warned about intentionally absent new contract");
    Console.WriteLine($"DYNAMICMAPS_BODY_CHECKS_PASSED legacy checks={checks}");
    return;
}
Check(first.QuestController.Subscribers == 0 && first.InventoryController.Subscribers == 0, "Old event handlers leaked");
Check(next.QuestController.Subscribers == 1 && next.InventoryController.Subscribers == 1, "New body did not receive subscriptions");
Check(ReferenceEquals(doors.Equipment, next.Inventory.Equipment), "Door key scope retained old equipment");
Check(quests.Calls.SequenceEqual(new[] { "show", "hide", "show" }), "Quest hide/show order is unsafe");
first.QuestController.Raise(); first.InventoryController.Raise();
Check(quests.Updates == 0 && doors.Updates == 0, "Old event still controls live markers");
next.QuestController.Raise(); next.InventoryController.Raise();
Check(quests.Updates == 1 && doors.Updates == 1, "New event does not control live markers");
DynamicMapsCompatHelper.RebindPlayerMarker(next);
Check(quests.Show == 2 && doors.Show == 2, "Repeated current-body callback restarted providers");
Check(next.QuestController.Subscribers == 1 && next.InventoryController.Subscribers == 1, "Repeated callback duplicated events");
DynamicMapsCompatHelper.RebindPlayerMarker(first);
Check(ReferenceEquals(arrow.Bound, next) && quests.Show == 2 && doors.Show == 2, "Late old-body callback stole current provider ownership");

var third = Body(world); world.MainPlayer = third;
screen.Remove<PlayerMarkerProvider>();
DynamicMapsCompatHelper.RebindPlayerMarker(third);
Check(ReferenceEquals(quests.Controller, third.QuestController) && ReferenceEquals(doors.Controller, third.InventoryController), "Disabled arrow prevented event repair");
Check(next.QuestController.Subscribers == 0 && next.InventoryController.Subscribers == 0, "Second transition leaked old handlers");

var fourth = Body(world); world.MainPlayer = fourth;
screen.Visibility(false, true);
DynamicMapsCompatHelper.RebindPlayerMarker(fourth);
Check(quests.Show == 3 && doors.Show == 3, "Hidden map received provider show");
screen.Visibility(true, false);
DynamicMapsCompatHelper.RebindPlayerMarker(fourth);
Check(quests.Show == 3 && doors.Show == 3, "Out-of-raid map received raid-provider show");
screen.Visibility(true, true); var view = screen.View; screen.SetMap(null);
DynamicMapsCompatHelper.RebindPlayerMarker(fourth);
Check(quests.Show == 3 && doors.Show == 3, "Unloaded map received show");
screen.SetMap(view);

screen.Remove<QuestMarkerProvider>();
DynamicMapsCompatHelper.RebindPlayerMarker(fourth);
Check(quests.Show == 3 && doors.Show == 4, "Disabled quest provider was reactivated or blocked doors");
quests.OnHideInRaid(view); screen.Add(quests); quests.OnShowInRaid(view);
DynamicMapsCompatHelper.RebindPlayerMarker(fourth);
Check(quests.Show == 4 && doors.Show == 4, "Naturally rebound provider restarted");

fourth.Inventory.Equipment = new object();
DynamicMapsCompatHelper.RebindPlayerMarker(fourth);
Check(doors.Show == 5 && ReferenceEquals(doors.Equipment, fourth.Inventory.Equipment), "Same controller retained replaced equipment");
Check(fourth.InventoryController.Subscribers == 1, "Equipment-only refresh duplicated inventory handler");
screen.SetMap(new DynamicMaps.UI.MapView());
DynamicMapsCompatHelper.RebindPlayerMarker(fourth);
Check(quests.Show == 5 && fourth.QuestController.Subscribers == 1, "Same controller retained another map view or duplicated quest handler");

// Own-Scav replacement: Show alone deliberately does not unsubscribe the PMC.
var scav = Body(world, true); world.MainPlayer = scav;
DynamicMapsCompatHelper.RebindPlayerMarker(scav);
Check(fourth.QuestController.Subscribers == 0 && fourth.InventoryController.Subscribers == 0, "PMC handlers leaked into own-Scav life");
Check(quests.Controller == null && scav.QuestController.Subscribers == 0, "Scav was bound to PMC quest data");
Check(scav.InventoryController.Subscribers == 1, "Scav door key updates not rebound");
DynamicMapsCompatHelper.RebindPlayerMarker(scav);
Check(quests.Show == 6 && doors.Show == 6, "Repeated Scav callback restarted providers");

var fifth = Body(world); world.MainPlayer = fifth;
quests.ThrowHide = true;
DynamicMapsCompatHelper.RebindPlayerMarker(fifth);
Check(quests.Show == 6, "Failed hide was followed by show");
Check(doors.Show == 7 && ReferenceEquals(doors.Controller, fifth.InventoryController), "One provider failure stopped the other");
Check(CoreLog.Warnings.Count == 1, "Provider failure should warn once");
DynamicMapsCompatHelper.RebindPlayerMarker(fifth);
Check(CoreLog.Warnings.Count == 1, "Repeated failure flooded warnings");
quests.ThrowHide = false;
DynamicMapsCompatHelper.RebindPlayerMarker(fifth);
Check(quests.Show == 7 && fifth.QuestController.Subscribers == 1, "Retry after failed hide did not recover");

var sixth = Body(world); world.MainPlayer = sixth;
quests.ThrowShow = true;
DynamicMapsCompatHelper.RebindPlayerMarker(sixth);
Check(fifth.QuestController.Subscribers == 0 && sixth.QuestController.Subscribers == 0, "Failed show retained old subscription");
Check(doors.Show == 8, "Failed quest show blocked door update");
quests.ThrowShow = false;
DynamicMapsCompatHelper.RebindPlayerMarker(sixth);
Check(sixth.QuestController.Subscribers == 1, "Failed show could not recover");

// Exact-world rejection, even if another world was made to point at this body.
Singleton<GameWorld>.Instance = new GameWorld { MainPlayer = sixth };
var showBefore = quests.Show;
sixth.QuestController = new Events();
DynamicMapsCompatHelper.RebindPlayerMarker(sixth);
Check(quests.Show == showBefore && sixth.QuestController.Subscribers == 0, "Another world admitted old-world body");
Singleton<GameWorld>.Instance = null;
DynamicMapsCompatHelper.RebindPlayerMarker(sixth); DynamicMapsCompatHelper.RebindPlayerMarker(null);
Check(quests.Show == showBefore, "Missing world/null body touched provider");

bool rejected = false;
try { DynamicMapsProviderRebind.Resolve(typeof(Program), typeof(Plugin).Assembly); }
catch (MissingFieldException) { rejected = true; }
Check(rejected, "Malformed visibility contract accepted");
var releasePath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
if (releasePath != null) PublishedMetadata.Verify(releasePath, Check);
Console.WriteLine($"DYNAMICMAPS_BODY_CHECKS_PASSED checks={checks}");
