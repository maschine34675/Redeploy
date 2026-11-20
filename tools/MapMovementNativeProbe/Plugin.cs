using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using Redeploy.Core.Api;
using Redeploy.Core.Engine;
using Redeploy.Core.Spawning;
using UnityEngine;

namespace MapMovementNativeProbe;
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute : Attribute
{
 public UiBridgeActionAttribute(string name) { Name=name; }
 public string Name {get;}
 public string Description {get;set;}
 public bool Control {get;set;}
}

// Test-only observer and Core-driver scenario. Never included in a release.
[BepInPlugin("com.maschine.MapMovementNativeProbe", "Map Movement Native Probe", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
 private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 private static readonly List<object> Samples=new();
 private static Player Previous;
 private static readonly List<LocalPlayer> Retained=new();
 private static object WatchedMap;
 private static FieldInfo ActiveField,RaidField;
 private static float WatchUntil;
 private static int WatchedFrames,InactiveFrames,NotRaidFrames,WatchStart,WatchLast;
 private static void BeginWatch()
 {
  var type=Find("DynamicMaps.Plugin");WatchedMap=Read(type,"Map",Read(type,"Instance"));
  ActiveField=WatchedMap?.GetType().GetField("_providersActive",All);RaidField=WatchedMap?.GetType().GetField("_providersInRaid",All);
  WatchedFrames=InactiveFrames=NotRaidFrames=0;WatchStart=Time.frameCount;WatchLast=0;WatchUntil=Time.time+3f;
 }
 private void Update()
 {
  if(WatchedMap==null||Time.time>WatchUntil||ActiveField==null||RaidField==null)return;
  WatchedFrames++;WatchLast=Time.frameCount;
  if(!(bool)ActiveField.GetValue(WatchedMap))InactiveFrames++;
  if(!(bool)RaidField.GetValue(WatchedMap))NotRaidFrames++;
 }

 private static Type Find(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null);
 private static object Read(Type t,string name,object o=null)
 {
  var f=t?.GetField(name,All); if(f!=null)return f.IsStatic||o!=null?f.GetValue(o):null;
  var p=t?.GetProperty(name,All);return p?.GetMethod!=null&&(p.GetMethod.IsStatic||o!=null)?p.GetValue(o):null;
 }
 private static int Id(object value)=>ReferenceEquals(value,null)?0:value is UnityEngine.Object u?u.GetInstanceID():RuntimeHelpers.GetHashCode(value);
 private static object Member(object o,string name)=>Read(o?.GetType(),name,o);
 private static int Count(object o)=>o is ICollection c?c.Count:o is IEnumerable e?e.Cast<object>().Count():0;
 private void Awake(){RespawnApi.Replaced+=Replaced;RespawnApi.RaidEnded+=Ended;}
 private void OnDestroy(){RespawnApi.Replaced-=Replaced;RespawnApi.RaidEnded-=Ended;}
 private static void Replaced(Player oldBody,Player newBody,string reason)=>Sample("replaced:"+reason);
 private static void Ended(){Sample("raid-ended");Retire();}
 private static void Retire()
 {
  var current=Singleton<GameWorld>.Instantiated?Singleton<GameWorld>.Instance.MainPlayer:null;
  foreach(var body in Retained) if(body!=null&&!ReferenceEquals(body,current)) UnityEngine.Object.Destroy(body.gameObject);
  Retained.Clear();
 }
 private static int DelegateTargets(object owner,object target)
 {
  if(owner==null||target==null)return 0;int count=0;
  for(var type=owner.GetType();type!=null;type=type.BaseType)
   foreach(var field in type.GetFields(All|BindingFlags.DeclaredOnly))
    if(!field.IsStatic&&typeof(Delegate).IsAssignableFrom(field.FieldType)&&field.GetValue(owner) is Delegate d)
     count+=d.GetInvocationList().Count(x=>ReferenceEquals(x.Target,target));
  return count;
 }
 private static void Sample(string stage){if(Samples.Count>=40)Samples.RemoveAt(0);Samples.Add(new {stage,observation=Snapshot()});}
 private static object Snapshot()
 {
  var world=Singleton<GameWorld>.Instantiated?Singleton<GameWorld>.Instance:null;var body=world?.MainPlayer;
  var mapPlugin=Find("DynamicMaps.Plugin");var map=Read(mapPlugin,"Map",Read(mapPlugin,"Instance"));
  var providers=Member(map,"_dynamicMarkerProviders") as IDictionary;
  object quest=null,door=null,marker=null;
  if(providers!=null)foreach(DictionaryEntry entry in providers)
  {
   var name=entry.Value?.GetType().FullName;
   if(name=="DynamicMaps.QuestMarkerProvider")quest=entry.Value;
   if(name=="DynamicMaps.DynamicMarkers.LockedDoorMarkerMutator")door=entry.Value;
   if(name=="DynamicMaps.DynamicMarkers.PlayerMarkerProvider")marker=Member(entry.Value,"_playerMarker");
  }
  var move=Find("AdvancedMovement.Moves.MoveController");var air=Find("AdvancedMovement.Moves.AirSlide");
  var bodyPlayer=Find("AdvancedMovement.Body.BodyPlayer");var animator=Read(bodyPlayer,"_local");
  var gesture=Find("AdvancedMovement.Viewmodel.GesturePlayer");var pin=Find("AdvancedMovement.Viewmodel.WeaponPin");var audio=Find("AdvancedMovement.Audio.SlideAudio");
  return new {
   frame=Time.frameCount,world=Id(world),body=Id(body),previousBody=Id(Previous),bodyChanged=!ReferenceEquals(Previous,null)&&!ReferenceEquals(Previous,body),
   previousRetained=Previous!=null,previousAlive=Previous?.HealthController?.IsAlive,alive=body?.HealthController?.IsAlive,
   side=body?.Side.ToString(),hands=body?.HandsController?.GetType().Name,position=body==null?null:body.Position.ToString(),
   state=body?.MovementContext?.CurrentState?.GetType().Name,overrideState=body?.MovementContext?.OverridenState?.GetType().Name,
   replacing=RespawnApi.IsReplacing,available=RespawnApi.IsAvailable,visibilityWatch=new {frames=WatchedFrames,inactive=InactiveFrames,notRaid=NotRaidFrames,start=WatchStart,last=WatchLast},
   maps=new {present=map!=null,mode=Member(map,"_mode")?.ToString(),active=Member(map,"_providersActive"),inRaid=Member(map,"_providersInRaid"),
    questRegistered=quest!=null,questController=Id(Member(quest,"_questController")),currentQuest=Id(body?.QuestController),questMatches=body!=null&&ReferenceEquals(Member(quest,"_questController"),body.QuestController),
    subscribedQuests=Count(Member(quest,"_subscribedQuests")),questSubscriptions=DelegateTargets(body?.QuestController,quest),oldQuestSubscriptions=DelegateTargets(Previous?.QuestController,quest),doorRegistered=door!=null,inventoryController=Id(Member(door,"_inventoryController")),currentInventory=Id(body?.InventoryController),
    inventoryMatches=body!=null&&ReferenceEquals(Member(door,"_inventoryController"),body.InventoryController),equipmentMatches=body!=null&&ReferenceEquals(Member(door,"_equipment"),body.Inventory.Equipment),
    inventorySubscriptions=DelegateTargets(body?.InventoryController,door),oldInventorySubscriptions=DelegateTargets(Previous?.InventoryController,door),trackedKeys=Count(Member(door,"_trackedKeys")),keyCounts=Member(door,"_keyCounts"),doorKeyGroups=Count(Member(door,"_doorMarkersByKey")),markerPlayer=Id(Member(marker,"Player")),markerMatches=body!=null&&ReferenceEquals(Member(marker,"Player"),body)},
   movement=new {present=move!=null,owner=Id(Read(move,"_player")),busy=Read(move,"Busy"),slide=Read(move,"_move")?.GetType().Name,dive=Read(move,"_dive")?.GetType().Name,
    hold=Read(move,"HoldStill"),settle=Read(move,"_settleUntil"),airOwner=Id(Read(air,"_player")),airQueued=Read(air,"_queued"),
    animator=Id(animator),gesturePlayer=Id(Read(gesture,"_player")),gesturePhase=Read(gesture,"_phase")?.ToString(),pin=Read(pin,"_pinned"),pinFirearm=Id(Read(pin,"_firearm")),audioOwner=Id(Read(audio,"_player"))}
  };
 }
 [UiBridgeAction("observe",Description="Read map-provider controllers and exact movement ownership; remember only stores comparison references.")]
 public static string Observe(bool remember=false,int sample=-1){if(remember){Previous=Singleton<GameWorld>.Instantiated?Singleton<GameWorld>.Instance.MainPlayer:null;Samples.Clear();}return JsonConvert.SerializeObject(new {current=sample<0?Snapshot():sample<Samples.Count?Samples[sample]:null,sampleCount=Samples.Count});}
 [UiBridgeAction("transition",Control=true,Description="Test profile only: begin a real Slide/Dive while sprinting, then kill or replace via Core in the same frame. Old live body is retained until raid end.")]
 public static string Transition(string move="none",string transition="swap")
 {
  if(move!="none"&&move!="slide"&&move!="dive")throw new ArgumentException("move: none, slide or dive");
  if(transition!="swap"&&transition!="death"&&transition!="none")throw new ArgumentException("transition: swap, death or none");
  var game=Singleton<AbstractGame>.Instantiated?Singleton<AbstractGame>.Instance as LocalGame:null;var player=game?.LocalPlayer;
  if(game==null||player==null||!player.HealthController.IsAlive||RespawnApi.IsReplacing)throw new InvalidOperationException("No living idle local game");
  if(!player.Profile.Info.Nickname.StartsWith("MapMoveTest",StringComparison.Ordinal))throw new InvalidOperationException("Disposable MapMoveTest profile required");
  if(Retained.Count>=5)throw new InvalidOperationException("Retained body limit; finish raid first");
  Previous=player;Samples.Clear();BeginWatch();Sample("before-move");
  if(move!="none")
  {
   var type=Find("AdvancedMovement.Moves.MoveController");var method=type?.GetMethod(move=="slide"?"TryStartSlide":"TryStartDive",All);
   if(method==null||!(bool)method.Invoke(null,move=="slide"?new object[]{player,-1f}:new object[]{player}))return JsonConvert.SerializeObject(new {started=false,current=Snapshot()});
  }
  Sample("move-started");
  if(transition=="death")player.ActiveHealthController.Kill(EDamageType.Landmine);
  else if(transition=="swap")
  {
   var origin=player.Position;
   var request=new RespawnRequest{Game=game,DeadPlayer=player,SpawnSelector=new NearPositionSpawnSelector(()=>origin,()=>3f),HealthPercent=100f,
     OnBeforeProfileMutation=_=>Sample("before-profile-mutation")};
   Retained.Add(player);game.StartCoroutine(RespawnEngine.RunRespawn(request,ex=>Samples.Add(new {stage="failure",error=ex.ToString()})));
  }
  Sample("action-returned");return JsonConvert.SerializeObject(new {started=true,current=Snapshot(),sampleCount=Samples.Count});
 }
}
