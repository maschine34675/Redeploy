using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Redeploy.Core.Api;
using Redeploy.Core.Engine;
using Redeploy.Core.Spawning;
using UnityEngine;

namespace WttArmoryNativeProbe;
[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute:Attribute
{
 public UiBridgeActionAttribute(string name){Name=name;} public string Name{get;} public string Description{get;set;} public bool Control{get;set;}
}
[BepInPlugin("com.maschine.WttArmoryNativeProbe","WTT Armory Native Probe","1.0.0")]
public sealed class Plugin:BaseUnityPlugin
{
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 const string GunTpl="678fe4a4906c7bd23722c71f";
 static readonly List<object> Samples=new(); static readonly List<LocalPlayer> Retained=new();
 static Player Previous; static float WatchUntil; static string LastState; static int Frames; static Plugin Self;
 static object Armory=>Chainloader.PluginInfos.TryGetValue("com.wtt.armory",out var p)?p.Instance:null;
 static object Read(object target,string name){var t=target as Type??target?.GetType();var o=target is Type?null:target;var f=t?.GetField(name,All);if(f!=null)return f.GetValue(o);return t?.GetProperty(name,All)?.GetValue(o);}
 static object A(string name)=>Read(Armory,name);
 static int Id(object v)=>ReferenceEquals(v,null)?0:v is UnityEngine.Object u?u.GetInstanceID():RuntimeHelpers.GetHashCode(v);
 static Player Current=>Singleton<GameWorld>.Instantiated?Singleton<GameWorld>.Instance.MainPlayer:null;
 static string BurstState()=>string.Join("/",new[]{A("IsFiring"),A("ROFShotCount"),A("RecoilShotCount"),A("GunHasHyperburst"),Read(Current?.HandsController,"autoFireOn")}.Select(x=>x?.ToString()??"null"));
 void Awake(){Self=this;RespawnApi.Replaced+=Replaced;RespawnApi.RaidEnded+=Ended;}
 void OnDestroy(){RespawnApi.Replaced-=Replaced;RespawnApi.RaidEnded-=Ended;}
 void Update(){if(Time.time>WatchUntil)return;Frames++;var state=BurstState();if(state!=LastState){LastState=state;Sample("frame-change");}}
 static void Replaced(Player oldBody,Player newBody,string reason)=>Sample("replaced:"+reason);
 static void Ended(){Sample("raid-ended");WatchUntil=0;foreach(var body in Retained)if(body!=null&&!ReferenceEquals(body,Current))UnityEngine.Object.Destroy(body.gameObject);Retained.Clear();}
 static void Sample(string stage){if(Samples.Count>=120)Samples.RemoveAt(0);Samples.Add(new{stage,state=Snapshot()});}
 static object Snapshot()
 {
  var p=Current;var fc=p?.HandsController as Player.FirearmController;var gun=fc?.Item;
  var you=A("You") as Player;var oldFc=Previous?.HandsController as Player.FirearmController;
  return new{frame=Time.frameCount,time=Time.time,body=Id(p),previous=Id(Previous),previousRetained=Previous!=null,previousAlive=Previous?.HealthController?.IsAlive,previousHands=Id(oldFc),previousAuto=oldFc?.autoFireOn,
   alive=p?.HealthController?.IsAlive,hands=fc?.GetType().Name,handsId=Id(fc),weapon=gun?.TemplateId,fireMode=gun?.SelectedFireMode.ToString(),rounds=gun?.GetCurrentMagazine()?.Count,chamber=gun?.ChamberAmmoCount,
   armory=Armory!=null,you=Id(you),matches=ReferenceEquals(you,p),youHands=Id(you?.HandsController),auto=fc?.autoFireOn,hyper=A("GunHasHyperburst"),firing=A("IsFiring"),timer=A("ShotTimer"),rof=A("ROFShotCount"),recoil=A("RecoilShotCount"),replacing=RespawnApi.IsReplacing};
 }
 static Player Require(){var p=Current;if(p==null||!p.HealthController.IsAlive||!p.Profile.Info.Nickname.StartsWith("ArmoryTest",StringComparison.Ordinal))throw new InvalidOperationException("Living disposable ArmoryTest profile required");return p;}
 [UiBridgeAction("observe",Description="Reads current/outgoing bodies and native Armory counters. watchSeconds temporarily samples counter changes; never writes foreign state.")]
 public static string Observe(bool remember=false,int sample=-1,float watchSeconds=0)
 {
  if(remember){Previous=Current;Samples.Clear();}
  if(watchSeconds>0){Require();Frames=0;Samples.Clear();LastState=null;WatchUntil=Time.time+Math.Min(watchSeconds,20);Sample("watch-start");}
  return JsonConvert.SerializeObject(new{current=sample<0?Snapshot():sample<Samples.Count?Samples[sample]:null,sampleCount=Samples.Count,frames=Frames});
 }
 [UiBridgeAction("history",Description="Compact bounded native burst-state trace; observation only.")]
 public static string History()
 {
  return JsonConvert.SerializeObject(Samples.Skip(Math.Max(0,Samples.Count-25)).Select(x=>{
   var state=Read(x,"state");return new{stage=Read(x,"stage"),frame=Read(state,"frame"),time=Read(state,"time"),body=Read(state,"body"),you=Read(state,"you"),auto=Read(state,"auto"),firing=Read(state,"firing"),timer=Read(state,"timer"),rof=Read(state,"rof"),recoil=Read(state,"recoil"),rounds=Read(state,"rounds")};}));
 }
 [UiBridgeAction("transition",Control=true,Description="Disposable-profile only: real native death or actual Core live-body replacement; keeps old live body until raid end.")]
 public static string Transition(string kind="death")
 {
  var p=Require();var game=Singleton<AbstractGame>.Instance as LocalGame;if(game==null||RespawnApi.IsReplacing)throw new InvalidOperationException("No idle local game");
  if(kind!="death"&&kind!="swap")throw new ArgumentException("kind: death or swap");if(Retained.Count>=3)throw new InvalidOperationException("Retained-body limit");Previous=p;Samples.Clear();Sample("before-transition");
  if(kind=="death")p.ActiveHealthController.Kill(EDamageType.Landmine);
  else{var origin=p.Position;Retained.Add((LocalPlayer)p);game.StartCoroutine(RespawnEngine.RunRespawn(new RespawnRequest{Game=game,DeadPlayer=(LocalPlayer)p,SpawnSelector=new NearPositionSpawnSelector(()=>origin,()=>3f),HealthPercent=100f,OnBeforeProfileMutation=_=>Sample("before-mutation")},ex=>Samples.Add(new{stage="failure",error=ex.ToString()})));}
  return JsonConvert.SerializeObject(new{started=true,current=Snapshot(),sampleCount=Samples.Count});
 }
 [UiBridgeAction("supply",Control=true,Description="Disposable profile only: supplies an official AN94 Standard preset and loaded AK74 magazine into an empty primary slot, draws through native Player.SetInHands. Never changes Armory fields.")]
 public static string Supply()
 {
  var p=Require();var f=Singleton<ItemFactory>.Instance;var equip=p.Inventory.Equipment;
  var slot=equip.Slots.FirstOrDefault(s=>(s.ID=="FirstPrimaryWeapon"||s.ID=="SecondPrimaryWeapon")&&s.ContainedItem==null);
  if(slot==null)throw new InvalidOperationException("An empty primary slot is required");
  var w=(Weapon)f.CreateItem(f.NextId,GunTpl,null);
  foreach(var pair in new[]{("mod_magazine","55d480c04bdc2d1d4e8b456a"),("mod_pistol_grip","5649ade84bdc2d1b2b8b4587"),("mod_stock","678fe67f710e3e53145bc5e0")})
  {var child=f.CreateItem(f.NextId,pair.Item2,null);var result=w.Slots.First(s=>s.ID==pair.Item1).AddWithoutRestrictions(child);if(result.Failed)throw new InvalidOperationException(result.Error.ToString());}
  var mag=w.GetCurrentMagazine();var ammo=f.CreateItem(f.NextId,"56dff3afd2720bba668b4567",null);ammo.StackObjectsCount=mag.MaxCount;var added=mag.Cartridges.Add(ammo,false);if(added.Failed)throw new InvalidOperationException(added.Error.ToString());
  foreach(var item in w.GetAllItems())p.Profile.Encyclopedia[item.TemplateId]=true;
  var res=slot.AddWithoutRestrictions(w);if(res.Failed)throw new InvalidOperationException(res.Error.ToString());
  p.SetInHands(w,result=>{Self.Logger.LogInfo("Armory probe native draw: "+result);Sample("supplied-native-draw");});
  return JsonConvert.SerializeObject(new{supplied=w.TemplateId,slot=slot.ID,rounds=mag.Count});
 }
}
