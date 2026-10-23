using System.Reflection;
using Redeploy.Core.Compat;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using ContinuousLoadAmmo.Controllers;
using ContinuousLoadAmmo.Patches;
using ContinuousLoadAmmo.Utils;
using Fixture;

internal static class Program
{
 static int checks;
 internal static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("ASSERT: "+name);}
 static MethodInfo Method(Type t,string n)=>t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
 static object Invoke(object instance,MethodInfo method,object[] args)
 {
  object state=null,result=null;Exception error=null;bool run=true;var patches=Harmony.GetPatchInfo(method);
  object Hook(MethodInfo hook)
  {
   var p=hook.GetParameters();var hargs=new object[p.Length];
   for(int i=0;i<p.Length;i++){string n=p[i].Name;hargs[i]=n switch {"__instance"=>instance,"__state"=>state,"__result"=>result,"__exception"=>error,"__runOriginal"=>run,_=>args[int.Parse(n[2..])]};}
   object r=hook.Invoke(null,hargs);
   for(int i=0;i<p.Length;i++){if(p[i].Name=="__state")state=hargs[i];if(p[i].Name=="__result")result=hargs[i];}
   return r;
  }
  try
  {
   if(patches!=null)foreach(var p in patches.Prefixes.OrderByDescending(p=>p.priority)){if(p.PatchMethod.ReturnType==typeof(bool)){if(!(bool)Hook(p.PatchMethod))run=false;}else Hook(p.PatchMethod);}
   if(run){var previous=Hooks.RawMethod;Hooks.RawMethod=method;try{result=method.Invoke(instance,args);}finally{Hooks.RawMethod=previous;}}
   if(patches!=null)foreach(var p in patches.Postfixes.OrderByDescending(p=>p.priority))Hook(p.PatchMethod);
  }
  catch(Exception ex){error=ex.GetBaseException();}
  finally{if(patches!=null)foreach(var p in patches.Finalizers)error=(Exception)Hook(p.PatchMethod);}
  if(error!=null)throw error;return result;
 }
 static void Receipt(MethodInfo target,MethodInfo foreign){Harmony.Bound[target]=new();Harmony.Bound[target].Postfixes.Add(new(){owner="fixture",PatchMethod=foreign});}
 static void Install()
 {
  ContinuousLoadAmmoCompat.Shutdown();Harmony.Bound.Clear();Chainloader.PluginInfos.Clear();
  var type=typeof(ContinuousLoadAmmo.ContinuousLoadAmmo);Chainloader.PluginInfos[ContinuousLoadAmmoContract.PluginGuid]=new(){Instance=new ContinuousLoadAmmo.ContinuousLoadAmmo(),Metadata=type.GetCustomAttribute<BepInPlugin>()};
  Receipt(Method(typeof(GameWorld),"RegisterPlayer"),Method(typeof(RegisterPlayerPatch),"Postfix"));
  foreach(var t in new[]{typeof(Player.PlayerInventoryController.LoadMagazineProcess),typeof(Player.PlayerInventoryController.UnloadMagazineProcess)})Receipt(Method(t,"Start"),Method(typeof(LoadingEndPatches),"Postfix"));
  ContinuousLoadAmmoCompat.Initialize();Check(ContinuousLoadAmmoCompat.GetRejectionReason()==null,"published API fixture admitted");
 }
 static LocalPlayer World()
 {
  BodyReplacementContext.Reset();
  var world=new GameWorld();var body=new LocalPlayer{GameWorld=world};world.MainPlayer=body;
  var game=new LocalGame{GameWorld=world,LocalPlayer=body,Profile=body.Profile};
  Singleton<GameWorld>.Instance=world;Singleton<AbstractGame>.Instance=game;Singleton<IBotGame>.Instance=game;
  return body;
 }
 static LocalGame Game=>(LocalGame)Singleton<AbstractGame>.Instance;
 static LoadAmmoController Register(Player p){Hooks.Call(typeof(RegisterPlayerPatch),"Postfix",p.GameWorld,p);return RegisterPlayerPatch.Current;}
 static void Begin(LocalPlayer old){BodyReplacementContext.Begin(Game,old);ContinuousLoadAmmoCompat.Prepare(Game,old);}
 static LocalPlayer Candidate(LocalPlayer old){var p=new LocalPlayer{GameWorld=old.GameWorld,Profile=old.Profile};p.GameWorld.MainPlayer=p;return p;}
 static void Finish(LocalPlayer p){Game.LocalPlayer=p;BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();}
 static (LocalPlayer,LoadAmmoController) Owned()
 {
  var old=World();Register(old);Begin(old);var p=Candidate(old);var c=Register(p);Finish(p);return(p,c);
 }
 static LocalPlayer Replacement(LocalPlayer old){Begin(old);var p=Candidate(old);return p;}
 static Task State(LoadAmmoController c,bool start)=>(Task)Hooks.Call(c,"SetPlayerStateAsync",start);
 static void Dispose(LoadAmmoController c)=>Hooks.Call(c,"Dispose");
 static void Start(LocalPlayer p,Task<IResult> task,bool unload=false)
 {
  object process=unload?new Player.PlayerInventoryController.UnloadMagazineProcess{_inventoryController=p.InventoryController,Task=task}:new Player.PlayerInventoryController.LoadMagazineProcess{_inventoryController=p.InventoryController,Task=task};
  // Native Start owns its native result; foreign Harmony postfix is itself intercepted.
  InvokeNative(process,Method(process.GetType(),"Start"));
 }
 static void InvokeNative(object process,MethodInfo method)
 {
  var result=(Task<IResult>)method.Invoke(process,null);
  foreach(var p in Harmony.GetPatchInfo(method).Postfixes.OrderByDescending(p=>p.priority))
   if(p.PatchMethod.DeclaringType==typeof(ContinuousLoadAmmoCompat))p.PatchMethod.Invoke(null,new object[]{process,result});
   else Invoke(null,p.PatchMethod,new object[]{result});
 }
 static async Task Until(Func<bool> condition)
 {
  var end=DateTime.UtcNow.AddSeconds(15);
  while(!condition()&&DateTime.UtcNow<end)await Task.Delay(20);
 }
 static async Task Main(string[] args)
 {
  Hooks.Invoke=Invoke;PublishedMetadata.Verify(args[0],Check);PureScope();ObservedLimitScope();Install();
  var ordinary=World();var oc=Register(ordinary);Check(oc!=null,"ordinary controller created");await State(oc,true);Check(oc.Starts==1,"ordinary native start retained");
  var ordinaryTask=new TaskCompletionSource<IResult>();Start(ordinary,ordinaryTask.Task);ordinary.InventoryController.Active=false;ordinaryTask.SetResult(new Result());await Until(()=>oc.Ends!=0);Check(oc.Ends==1,"ordinary completion broadcast retained");Dispose(oc);Check(RegisterPlayerPatch.Current==null,"ordinary disposal unchanged");

  var entry=World();var entryController=Register(entry);await State(entryController,true);
  Begin(entry);Check(entryController.Disposes==0&&ReferenceEquals(RegisterPlayerPatch.Current,entryController),"prepare does not dispose entry controller");
  LoadingEndPatches.Broadcast();Check(entryController.Ends==1,"prepare preserves original subscription before all preflights pass");
  BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();Check(entryController.Disposes==0,"failed preflight leaves old controller usable");
  var initialTask=new TaskCompletionSource<IResult>();Start(entry,initialTask.Task);Begin(entry);var a=Candidate(entry);var ac=Register(a);Finish(a);initialTask.SetResult(new Result());await Task.Delay(30);Check(ac.Ends==0,"pre-adoption task cannot broadcast onto successor");Check(entryController.Disposes==1&&ac!=entryController,"standalone live entry controller adopted and retired");
  int count=RegisterPlayerPatch.Registrations;Check(ReferenceEquals(Register(a),ac)&&RegisterPlayerPatch.Registrations==count,"duplicate registration does not leak controller");
  await State(ac,true);Check(ac.Starts==1&&a.MovementContext.Limit,"replacement original load start preserved");
  var delayed=State(ac,false);await State(ac,true);await delayed;Check(a.MovementContext.Limit&&a.Equips==0,"new same-body load cancels delayed unlock");
  var oldTask=new TaskCompletionSource<IResult>();Start(a,oldTask.Task);var b=Replacement(a);var bc=Register(b);Finish(b);Check(ac.Disposes==1&&ReferenceEquals(RegisterPlayerPatch.Current,bc),"live kit swap disposes before rebinding");
  int staticCount=RegisterPlayerPatch.StaticDisposals;Dispose(ac);Check(RegisterPlayerPatch.StaticDisposals==staticCount&&ReferenceEquals(RegisterPlayerPatch.Current,bc),"late duplicate disposal preserves successor");
  await State(bc,true);oldTask.SetResult(new Result());await Task.Delay(25);Check(ac.Ends==0&&bc.Ends==0&&b.MovementContext.SprintDisabled,"old task cannot notify successor");
  int broadcasts=LoadingEndPatches.Broadcasts;LoadingEndPatches.Broadcast();Check(bc.Ends==0&&b.MovementContext.SprintDisabled,"unrelated global completion cannot notify replacement");
  b.InventoryController.Active=true;var currentTask=new TaskCompletionSource<IResult>();Start(b,currentTask.Task,true);b.InventoryController.Active=false;currentTask.SetResult(new Result());await Until(()=>bc.Ends!=0);Check(bc.Ends==1,"current unload reaches exact controller");await Until(()=>b.Equips!=0&&!b.MovementContext.Limit);Check(!b.MovementContext.Limit&&b.Equips==1,"current completion restores weapon and limit");Check(LoadingEndPatches.Broadcasts==broadcasts+1,"owned task does not broadcast to ordinary subscribers");
  b.HandsIsEmpty=true;await State(bc,true);var nextEnd=State(bc,false);b.HealthController.IsAlive=false;Dispose(bc);var c=Replacement(b);var cc=Register(c);Finish(c);await nextEnd;Check(b.Equips==1&&c.Equips==0,"retired delayed restore touches neither body");Check(bc.Disposes==1,"death controller disposed exactly once before repeated respawn");
  await State(cc,true);var activeEnd=State(cc,false);c.InventoryController.Active=true;await activeEnd;Check(c.MovementContext.Limit,"active load prevents delayed restore");c.InventoryController.Active=false;
  var multi=State(cc,false);MultiSelectInterop.MultiSelectLoadSerializerIsActive=true;await multi;Check(c.MovementContext.Limit,"MultiSelect pending load preserves limit");MultiSelectInterop.MultiSelectLoadSerializerIsActive=false;
  c.HandsIsEmpty=true;await State(cc,true);var worldEnd=State(cc,false);var abandonedTask=new TaskCompletionSource<IResult>();Start(c,abandonedTask.Task);int oldEnds=cc.Ends;var following=World();var fc=Register(following);abandonedTask.SetResult(new Result());await Task.Delay(30);Check(cc.Ends==oldEnds,"old task cannot notify following world");await worldEnd;Check(c.Equips==0&&following.Equips==0,"following ordinary world cannot inherit delayed work");Dispose(cc);Check(ReferenceEquals(RegisterPlayerPatch.Current,fc),"old owned dispose cannot clear ordinary successor");await State(fc,true);Check(fc.Starts==1,"following ordinary start retained");Dispose(fc);
  await InitialLiveRollback();await Failures();Initial();await Immediate();await Revocation();Admission();
  Console.WriteLine($"CONTINUOUS_LOAD_AMMO_COMPAT_PASSED ({checks})");
 }
 static async Task InitialLiveRollback()
 {
  var old=World();var native=Register(old);var originalTask=State(native,true);Check(ReferenceEquals(originalTask,native.LastStateTask),"ordinary observed start preserves original task");await originalTask;Check(old.MovementContext.Limit&&old.MovementContext.SprintDisabled,"ordinary initial loading is unchanged by observation");
  Begin(old);ContinuousLoadAmmoCompat.AbortPreparation();BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();
  Check(native.Disposes==0&&old.MovementContext.Limit&&old.MovementContext.SprintDisabled,"rejected preflight retains native initial loading restriction");
  Begin(old);var candidate=Candidate(old);Register(candidate);old.GameWorld.MainPlayer=old;Game.LocalPlayer=old;BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();
  Check(RegisterPlayerPatch.Current!=native&&!old.MovementContext.SprintDisabled&&!old.MovementContext.Limit&&old.MovementContext.Removes==1,"initial unowned live load rollback releases only observed native limit");Dispose(RegisterPlayerPatch.Current);
  old=World();native=Register(old);await State(native,true);originalTask=State(native,false);Check(ReferenceEquals(originalTask,native.LastStateTask),"ordinary observed completion preserves original task");await originalTask;Check(!old.MovementContext.Limit,"ordinary native completion still removes its own restriction");
  old.MovementContext.Limit=true;old.MovementContext.SprintDisabled=true;int removes=old.MovementContext.Removes;Begin(old);candidate=Candidate(old);Register(candidate);old.GameWorld.MainPlayer=old;Game.LocalPlayer=old;BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();
  Check(old.MovementContext.Limit&&old.MovementContext.SprintDisabled&&old.MovementContext.Removes==removes,"completed initial load does not claim later unrelated restriction");Dispose(RegisterPlayerPatch.Current);
  old=World();native=Register(old);old.MovementContext.Limit=true;old.MovementContext.SprintDisabled=true;Begin(old);candidate=Candidate(old);Register(candidate);old.GameWorld.MainPlayer=old;Game.LocalPlayer=old;BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();
  Check(old.MovementContext.Limit&&old.MovementContext.SprintDisabled&&old.MovementContext.Removes==0,"unobserved unrelated restriction survives initial rollback");Dispose(RegisterPlayerPatch.Current);
  old=World();native=Register(old);old.MovementContext.Limit=true;await State(native,true);bool rejected=false;
  try{Begin(old);}catch(InvalidOperationException){rejected=true;}ContinuousLoadAmmoCompat.AbortPreparation();BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();
  Check(rejected&&native.Disposes==0&&old.MovementContext.Limit,"pre-existing shared restriction rejects safely without removal");
  await State(native,false);Begin(old);candidate=Candidate(old);Register(candidate);Finish(candidate);Check(ContinuousLoadAmmoCompat.GetRejectionReason()==null,"actual native removal clears ambiguous observation");Dispose(RegisterPlayerPatch.Current);
 }
 static async Task Failures()
 {
  var (old,oldController)=Owned();var staleFault=new TaskCompletionSource<IResult>();Start(old,staleFault.Task);await State(oldController,true);Begin(old);var candidate=Candidate(old);var abandoned=Register(candidate);
  staleFault.SetException(new InvalidOperationException("retired task failure"));await Task.Delay(30);Check(ContinuousLoadAmmoCompat.GetRejectionReason()==null,"retired task fault cannot poison successor admission");
  old.GameWorld.MainPlayer=old;Game.LocalPlayer=old;BodyReplacementContext.EndAttempt();ContinuousLoadAmmoCompat.Tick();
  var restored=RegisterPlayerPatch.Current;Check(!old.MovementContext.SprintDisabled&&!old.MovementContext.Limit,"rollback body no longer carries owned loading restriction");Check(abandoned.Disposes==1&&restored!=oldController&&restored!=abandoned,"failed creation cleans candidate and restores old controller");
  await State(restored,true);Check(restored.Starts==1,"restored live body controller usable");Dispose(abandoned);Check(ReferenceEquals(RegisterPlayerPatch.Current,restored),"failed candidate late disposal cannot clear restored controller");
  var faulted=new TaskCompletionSource<IResult>();Start(old,faulted.Task);faulted.SetException(new InvalidOperationException("fixture native task failure"));await Until(()=>ContinuousLoadAmmoCompat.GetRejectionReason()!=null);
  Check(ContinuousLoadAmmoCompat.GetRejectionReason()!=null&&restored.Ends==0,"faulted native task reports failure without notifying current controller");
  Dispose(restored);Install();
  (old,oldController)=Owned();Begin(old);oldController.ThrowOnDispose=true;candidate=Candidate(old);Register(candidate);
  Check(ContinuousLoadAmmoCompat.GetRejectionReason()!=null&&RegisterPlayerPatch.Current==null,"disposal failure rejects replacement and clears only retired current slot");
  oldController.ThrowOnDispose=false;BodyReplacementContext.EndAttempt();Install();
  (old,oldController)=Owned();Begin(old);candidate=Candidate(old);RegisterPlayerPatch.ThrowAfterRegistration=true;bool registerFailed=false;
  try{Register(candidate);}catch(InvalidOperationException){registerFailed=true;}finally{RegisterPlayerPatch.ThrowAfterRegistration=false;}
  Check(registerFailed&&RegisterPlayerPatch.Current==null&&ContinuousLoadAmmoCompat.GetRejectionReason()!=null,"partial registration failure disposes exact candidate controller");BodyReplacementContext.EndAttempt();Install();
  (old,oldController)=Owned();var cancelled=new TaskCompletionSource<IResult>();Start(old,cancelled.Task);cancelled.SetCanceled();await Task.Delay(30);
  Check(ContinuousLoadAmmoCompat.GetRejectionReason()==null&&oldController.Ends==0,"cancelled native task is inert and not a compatibility failure");Dispose(oldController);
  var p=World();var wrong=Register(p);var expected=new LocalPlayer{GameWorld=p.GameWorld,Profile=p.Profile};p.GameWorld.MainPlayer=expected;Game.LocalPlayer=expected;
  BodyReplacementContext.Begin(Game,expected);bool rejected=false;try{ContinuousLoadAmmoCompat.Prepare(Game,expected);}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&wrong.Disposes==0,"unrelated entry controller rejected without teardown");BodyReplacementContext.EndAttempt();Dispose(wrong);
 }
 static void ObservedLimitScope()
 {
  var s=new ContinuousLoadAmmoScope();object ctl=new(),body=new(),world=new(),inv=new(),movement=new();
  var call=s.ObserveLimitStart(ctl,body,world,inv,movement,false);s.FinishLimitStart(call,true,true);
  var lease=s.BindController(ctl,body,world,inv,world,body,true,true,movement);Check(lease.HasLimit,"exact successful upstream limit is transferred");
  s.ObserveLimitRemoved(new object());Check(lease.HasLimit,"unrelated movement removal cannot revoke proof");s.ObserveLimitRemoved(movement);Check(!lease.HasLimit,"actual removal revokes transferred proof");
  foreach(var mismatch in new[]{"body","world","inventory","movement"})
  {
   s=new();call=s.ObserveLimitStart(ctl,body,world,inv,movement,false);s.FinishLimitStart(call,true,true);
   var b=mismatch=="body"?new object():body;var w=mismatch=="world"?new object():world;var i=mismatch=="inventory"?new object():inv;
   lease=s.BindController(ctl,b,w,i,w,b,true,true,mismatch=="movement"?new object():movement);Check(!lease.HasLimit,"observed limit requires exact "+mismatch);
  }
  s=new();call=s.ObserveLimitStart(ctl,body,world,inv,movement,false);s.FinishLimitStart(call,false,true);
  Check(s.HasUncertainLimit(ctl),"pending or failed upstream state remains uncertain");
  s.ObserveLimitRemoved(movement);Check(!s.HasUncertainLimit(ctl),"actual removal clears uncertain observation");
  s=new();call=s.ObserveLimitStart(ctl,body,world,inv,movement,false);s.ObserveLimitRemoved(movement);s.FinishLimitStart(call,true,true);
  lease=s.BindController(ctl,body,world,inv,world,body,true,true,movement);Check(!lease.HasLimit,"superseded observation cannot reclaim removed limit");
  s=new();call=s.ObserveLimitStart(ctl,body,world,inv,movement,false);s.Clear();s.FinishLimitStart(call,true,true);
  lease=s.BindController(ctl,body,world,inv,world,body,true,true,movement);Check(!lease.HasLimit,"cleared observation cannot reclaim limit");
 }
 static void PureScope()
 {
  var s=new ContinuousLoadAmmoScope();object ctl=new(),body=new(),world=new(),inv=new();
  Check(s.BindController(ctl,body,world,inv,world,body,false,true)==null,"ordinary scope is not captured");
  Check(s.BindController(ctl,body,world,inv,new object(),body,true,true)==null,"different physical world is not captured");
  Check(s.BindController(ctl,body,world,inv,world,new object(),true,true)==null,"non-main body is not captured");
  Check(s.BindController(ctl,body,world,inv,world,body,true,false)==null,"bot scope is not captured");
  var lease=s.BindController(ctl,body,world,inv,world,body,true,true);Check(lease!=null,"exact physical scope captured");
  object otherBody=new();Check(s.BindController(ctl,otherBody,world,inv,world,otherBody,true,true)==null,"reused controller body rejected");
  bool Current(object ct,object w,object b,object i,bool alive=true,bool exact=true)=>ContinuousLoadAmmoScope.IsCurrent(lease,ct,w,b,i,alive,exact);
  Check(Current(ctl,world,body,inv),"exact current scope");Check(!Current(new object(),world,body,inv),"controller identity check");Check(!Current(ctl,new object(),body,inv),"world identity check");Check(!Current(ctl,world,new object(),inv),"body identity check");Check(!Current(ctl,world,body,new object()),"inventory identity check");Check(!Current(ctl,world,body,inv,false),"dead current denied");Check(!Current(ctl,world,body,inv,true,false),"unarmed current denied");
  var task1=s.CaptureTask(new object(),lease);var task2=s.CaptureTask(new object(),lease);Check(!ContinuousLoadAmmoScope.IsLatest(task1)&&ContinuousLoadAmmoScope.IsLatest(task2),"old same-body task epoch rejected");
  Check(ContinuousLoadAmmoScope.Retire(lease)&&!ContinuousLoadAmmoScope.Retire(lease),"retire exactly once");Check(!Current(ctl,world,body,inv),"retired lease denied");
  s.Clear();Check(s.Find(ctl)==null&&s.ForInventory(inv)==null,"scope cleanup releases leases");
 }
 static void Initial()
 {
  var dead=World();var deadController=Register(dead);dead.HealthController.IsAlive=false;Dispose(deadController);Begin(dead);var born=Candidate(dead);var bornController=Register(born);Finish(born);Check(bornController!=null&&deadController.Disposes==1,"initial standalone death creates fresh controller after native disposal");Dispose(bornController);
  var p=World();var ctl=Register(p);int n=RegisterPlayerPatch.Registrations;Register(p);
  Check(RegisterPlayerPatch.Registrations==n+1,"ordinary initial registration remains unmodified");Dispose(RegisterPlayerPatch.Current);Dispose(ctl);
  var old=World();var original=Register(old);Begin(old);var unrelated=Candidate(old);unrelated.Profile=new object();Register(unrelated);
  Check(original.Disposes==0,"unrelated physical candidate is not claimed");BodyReplacementContext.EndAttempt();Dispose(RegisterPlayerPatch.Current);Dispose(original);
 }
 static async Task Immediate()
 {
  var (p,c)=Owned();await State(c,true);int broadcasts=LoadingEndPatches.Broadcasts;Start(p,Task.FromResult<IResult>(new Result()));await Until(()=>c.Ends!=0);Check(c.Ends==1&&LoadingEndPatches.Broadcasts==broadcasts,"immediate completion captured before foreign broadcast");await Until(()=>p.Equips!=0&&!p.MovementContext.Limit);Check(p.Equips==1&&!p.MovementContext.Limit,"immediate completion restores current controller");Dispose(c);
 }
 static async Task Revocation()
 {
  var (p,c)=Owned();await State(c,true);var pending=State(c,false);ContinuousLoadAmmoCompat.Shutdown();ContinuousLoadAmmoCompat.Initialize();await pending;Check(p.Equips==0&&p.MovementContext.Limit,"revoked binding cannot resume after reinitialization");Dispose(c);
 }
 static void Admission()
 {
  var target=Method(typeof(Player.PlayerInventoryController.LoadMagazineProcess),"Start");var info=Harmony.GetPatchInfo(target);var receipt=info.Postfixes.Single(p=>p.PatchMethod.DeclaringType==typeof(LoadingEndPatches));info.Postfixes.Remove(receipt);Check(ContinuousLoadAmmoCompat.GetRejectionReason()!=null,"missing foreign receipt rejects replacement");Install();
  info=Harmony.GetPatchInfo(target);info.ILManipulators.Add(new(){owner="foreign-il",PatchMethod=Method(typeof(RegisterPlayerPatch),"OnDispose")});Check(ContinuousLoadAmmoCompat.GetRejectionReason()!=null,"unknown foreign IL hook rejects replacement");Install();
  info=Harmony.GetPatchInfo(target);info.Postfixes.Single(p=>p.PatchMethod.DeclaringType==typeof(LoadingEndPatches)).priority=Priority.First+1;Check(ContinuousLoadAmmoCompat.GetRejectionReason()!=null,"foreign broadcast before capture rejects replacement");Install();
  var owned=Harmony.GetPatchInfo(Method(typeof(RegisterPlayerPatch),"Postfix"));owned.Prefixes.Clear();Check(ContinuousLoadAmmoCompat.GetRejectionReason()!=null,"missing own receipt rejects replacement");
  ContinuousLoadAmmoCompat.Shutdown();Chainloader.PluginInfos.Clear();Check(ContinuousLoadAmmoCompat.GetRejectionReason()==null,"absent mod does not reject replacement");
 }
}
