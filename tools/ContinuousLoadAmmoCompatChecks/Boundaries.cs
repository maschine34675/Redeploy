using System.Reflection;
namespace BepInEx
{
 public class BaseUnityPlugin { }
 [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin(string guid,string name,string version):Attribute { public string GUID=>guid;public string Name=>name; public Version Version=>new(version); }
 public sealed class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Configuration { public sealed class ConfigEntry<T>(T value) { public T Value=value; } }
namespace BepInEx.Bootstrap { public static class Chainloader { public static Dictionary<string,BepInEx.PluginInfo> PluginInfos=new(); } }
namespace HarmonyLib
{
 public static class Priority { public const int First=800; }
 public sealed class HarmonyMethod(Type type,string name) { public int priority; public MethodInfo Method=type.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static); }
 public sealed class Patch { public string owner; public MethodInfo PatchMethod; public int priority; }
 public sealed class PatchInfo { public List<Patch> Prefixes=new(),Postfixes=new(),Transpilers=new(),Finalizers=new(),ILManipulators=new(); }
 public static class PatchProcessor { public static List<MethodInfo> GetSortedPatchMethods(MethodBase m,Patch[] p)=>p.OrderByDescending(x=>x.priority).Select(x=>x.PatchMethod).ToList(); }
 public sealed class Harmony(string id)
 {
  public string Id=>id; public static Dictionary<MethodBase,PatchInfo> Bound=new(); public static bool DropReceipt;
  public void Patch(MethodInfo m,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod transpiler=null,HarmonyMethod finalizer=null)
  { if(!Bound.TryGetValue(m,out var info))Bound[m]=info=new(); if(DropReceipt)return; void Add(List<Patch> list,HarmonyMethod h){if(h!=null)list.Add(new(){owner=id,PatchMethod=h.Method,priority=h.priority});} Add(info.Prefixes,prefix);Add(info.Postfixes,postfix);Add(info.Transpilers,transpiler);Add(info.Finalizers,finalizer); }
  public static PatchInfo GetPatchInfo(MethodBase m)=>Bound.GetValueOrDefault(m);
  public static IEnumerable<MethodBase> GetAllPatchedMethods()=>Bound.Keys;
  public void UnpatchSelf(){foreach(var i in Bound.Values){i.Prefixes.RemoveAll(p=>p.owner==id);i.Postfixes.RemoveAll(p=>p.owner==id);i.Finalizers.RemoveAll(p=>p.owner==id);}}
 }
}
namespace Comfort.Common
{
 public interface IResult { } public sealed class Result:IResult { }
 public static class Singleton<T> where T:class { public static T Instance;public static bool Instantiated=>Instance!=null; }
}
public enum GameStatus { Running,Starting,Started,Stopping,Stopped }
public enum EPhysicalCondition { SprintDisabled }
namespace JsonType { public enum ESideType { Pmc,Savage } public sealed class LocalRaidSettings { public string location="factory4_day",serverId; public ESideType playerSide=ESideType.Pmc; } }
namespace EFT.InventoryLogic { public class InventoryController { } public interface IMagazineLoadingProcess { Task<Comfort.Common.IResult> Start(); } }
namespace EFT
{
 public interface IPlayer { bool IsYourPlayer {get;} }
 public interface IBotGame { }
 public class AbstractGame { }
 public class LocalGame:AbstractGame,IBotGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public object Profile; public GameStatus Status=GameStatus.Started;public JsonType.LocalRaidSettings _raidSettings=new(); }
 public class GameWorld { public string LocationId="factory4_day";public Player MainPlayer;public void RegisterPlayer(IPlayer p){MainPlayer=(Player)p;} }
 public class HideoutGameWorld:GameWorld { }
 public class Health { public bool IsAlive=true; }
 public class MovementContext
 {
  public bool SprintDisabled;public int Removes;
  public Dictionary<Player.ESpeedLimit,float> _speedLimits=new();
  public bool Limit {get=>_speedLimits.ContainsKey(Player.ESpeedLimit.BarbedWire);set{if(value)_speedLimits[Player.ESpeedLimit.BarbedWire]=.45f;else _speedLimits.Remove(Player.ESpeedLimit.BarbedWire);}}
  public void SetPhysicalCondition(EPhysicalCondition condition,bool value){SprintDisabled=value;}
  public void RemoveStateSpeedLimit(Player.ESpeedLimit limit){if(Fixture.Hooks.Redirect(this,"RemoveStateSpeedLimit",limit))return;_speedLimits.Remove(limit);Removes++;}
 }
 public class Player:IPlayer
 {
  public enum ESpeedLimit {BarbedWire}
  public bool IsYourPlayer {get;set;}=true;public GameWorld GameWorld;public object Profile=new();public PlayerInventoryController InventoryController=new();public MovementContext MovementContext=new();public Health HealthController=new();public bool HandsIsEmpty=true;public int Equips;
  public void TrySetLastEquippedWeapon(){Equips++;HandsIsEmpty=false;}
  public class PlayerInventoryController:EFT.InventoryLogic.InventoryController
  {
   public bool Active;
   public class LoadMagazineProcess:EFT.InventoryLogic.IMagazineLoadingProcess { public EFT.InventoryLogic.InventoryController _inventoryController;public Task<Comfort.Common.IResult> Task;public Task<Comfort.Common.IResult> Start()=>Task; }
   public class UnloadMagazineProcess:EFT.InventoryLogic.IMagazineLoadingProcess { public EFT.InventoryLogic.InventoryController _inventoryController;public Task<Comfort.Common.IResult> Task;public Task<Comfort.Common.IResult> Start()=>Task; }
  }
 }
 public class LocalPlayer:Player { }
}
namespace Redeploy.Core { public static class CoreLog {public static List<string> Warnings=new();public static void LogDebug(string s){}public static void LogWarning(string s)=>Warnings.Add(s);} }
namespace Fixture
{
 public static class Hooks
 {
  public static Func<object,MethodInfo,object[],object> Invoke;
  [ThreadStatic] public static MethodInfo RawMethod;
  public static bool Redirect(object target,string name,params object[] args)
  {
   var t=target as Type??target.GetType();var method=t.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);
   if(method==RawMethod)return false;
   Invoke(target is Type?null:target,method,args);return true;
  }
  public static object Call(object target,string name,params object[] args)=>Invoke(target,target.GetType().GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance),args);
  public static object Call(Type target,string name,params object[] args)=>Invoke(null,target.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static),args);
 }
}
