using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using Fixture;
namespace ContinuousLoadAmmo
{
 [BepInPlugin("com.ozen.continuousloadammo","Fixture CLA","1.2.0")]
 public class ContinuousLoadAmmo:BaseUnityPlugin {public static ConfigEntry<float> SpeedLimit {get;}=new(.45f);}
}
namespace ContinuousLoadAmmo.Controllers
{
 // Deliberately minimal, independent lifecycle fixture. No native inventory implementation is copied.
 public sealed class LoadAmmoController
 {
  private readonly Player _player;
  public Player.PlayerInventoryController PlayerInventoryController {get;}
  public bool IsActive=>PlayerInventoryController.Active;
  public Task LastStateTask;public int Disposes,Ends,Starts;public bool ThrowOnDispose;public event Action OnDispose;
  public LoadAmmoController(Player body){_player=body;PlayerInventoryController=body.InventoryController;Patches.LoadingEndPatches.OnLoadingEnd+=LoadingEnd;Patches.InventoryScreenClosePatch.OnInventoryClose+=LoadingOutsideInventory;}
  public void Dispose(){if(Hooks.Redirect(this,"Dispose"))return;Disposes++;if(ThrowOnDispose)throw new InvalidOperationException("fixture disposal failure");PlayerInventoryController.Active=false;Patches.LoadingEndPatches.OnLoadingEnd-=LoadingEnd;Patches.InventoryScreenClosePatch.OnInventoryClose-=LoadingOutsideInventory;OnDispose?.Invoke();OnDispose=null;}
  private Task SetPlayerStateAsync(bool start)=>LastStateTask=NativeState(start);
  private async Task NativeState(bool start){Starts+=start?1:0;_player.MovementContext.SprintDisabled=start;if(start){_player.MovementContext.Limit=true;return;}await Task.Delay(800);if(!Utils.MultiSelectInterop.MultiSelectLoadSerializerIsActive)_player.MovementContext.RemoveStateSpeedLimit(Player.ESpeedLimit.BarbedWire);}
  private void LoadingEnd(){Ends++;Hooks.Call(this,"SetPlayerStateAsync",false);}
  private void LoadingOutsideInventory(){Hooks.Call(this,"SetPlayerStateAsync",true);}
 }
}
namespace ContinuousLoadAmmo.Patches
{
 public static class RegisterPlayerPatch
 {
  private static Controllers.LoadAmmoController _loadAmmoController;
  public static int Registrations,StaticDisposals;public static bool ThrowAfterRegistration;
  public static Controllers.LoadAmmoController Current=>_loadAmmoController;
  public static void Postfix(GameWorld world,IPlayer player){if(Hooks.Redirect(typeof(RegisterPlayerPatch),"Postfix",world,player))return;if(world is HideoutGameWorld||!player.IsYourPlayer)return;Registrations++;_loadAmmoController=new((Player)player);_loadAmmoController.OnDispose+=()=>Hooks.Call(typeof(RegisterPlayerPatch),"OnDispose");if(ThrowAfterRegistration)throw new InvalidOperationException("fixture registration UI failure");}
  private static void OnDispose(){if(Hooks.Redirect(typeof(RegisterPlayerPatch),"OnDispose"))return;StaticDisposals++;_loadAmmoController=null;}
 }
 public static class LoadingEndPatches
 {
  public static event Action OnLoadingEnd;public static int Broadcasts;
  public static void Postfix(Task<IResult> task){_ = Complete(task);}
  private static async Task Complete(Task<IResult> task){await task;Broadcasts++;OnLoadingEnd?.Invoke();}
  public static void Broadcast(){Broadcasts++;OnLoadingEnd?.Invoke();}
 }
 public static class InventoryScreenClosePatch {public static event Action OnInventoryClose;public static void Close()=>OnInventoryClose?.Invoke();}
}
namespace ContinuousLoadAmmo.Utils {public static class MultiSelectInterop {public static bool MultiSelectLoadSerializerIsActive {get;set;} } }
