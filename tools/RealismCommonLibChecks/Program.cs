using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Animations;
using HarmonyLib;
using RealismCommonLib;
using RealismCommonLib.Events;
using RealismCommonLib.Patches;
using RealismCommonLib.StateControllers.InstanceState;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using UnityEngine;

internal static class Program
{
    static int checks;
    static void Check(bool value,string text) { if(!value) throw new Exception(text); checks++; }
    static Plugin plugin;
    static (LocalGame game,LocalPlayer body) World()
    {
        var world=new GameWorld(); var body=new LocalPlayer{GameWorld=world};
        body.HandsController=new Player.FirearmController{_player=body};
        var game=new LocalGame{GameWorld=world,LocalPlayer=body,Profile=body.Profile}; world.MainPlayer=body;
        Singleton<GameWorld>.Instance=world; Singleton<AbstractGame>.Instance=game;
        PlayerInitPatch.Run(body); return(game,body);
    }
    static Behaviour[] Bundle() => new Behaviour[]{Plugin.PlayerStateInstance,Plugin.WeaponStateInstance,Plugin.AimStateInstance,Plugin.HealthStateInstance,
        Plugin.HazardsStateInstance,Plugin.GearStateInstance,Plugin.FiringStateInstance,Plugin.SkillStateInstance,Plugin.ReloadStateInstance};
    static LocalPlayer Candidate(LocalGame game,LocalPlayer old,Profile profile=null)
    {
        var next=new LocalPlayer{GameWorld=game.GameWorld,Profile=profile??old.Profile};
        next.HandsController=new Player.FirearmController{_player=next}; game.GameWorld.MainPlayer=next;
        PlayerInitPatch.Run(next); return next;
    }
    static void Prepare(LocalGame game,LocalPlayer old,Profile profile=null)
    { BodyReplacementContext.Begin(game,old,profile); RealismCommonLibBodyCompatibility.Prepare(game,old); }
    static void Finish() { RealismCommonLibBodyCompatibility.EndAttempt(); BodyReplacementContext.EndAttempt(); }
    static void NoWrite(Action action,string label) { int before=Plugin.Writes; action(); Check(before==Plugin.Writes,label); }
    static void Writes(Action action,string label) { int before=Plugin.Writes; action(); Check(Plugin.Writes>before,label); }
    static bool Targets(Delegate handlers,object target) => handlers?.GetInvocationList().Any(d=>ReferenceEquals(d.Target,target))==true;
    static ProceduralWeaponAnimation Pwa(Player p)=>new(){_firearmController=(Player.FirearmController)p.HandsController};
    static void Main(string[] args)
    {
        if(args.Length>0 && args[0]=="published") { Published.Verify(args[1]); return; }
        Check(RealismCommonLibBodyCompatibility.GetRejectionReason()==null,"absence allowed");
        Check(!Harmony.GetAllPatchedMethods().Any(),"absence installs no Harmony hooks");
        plugin=new Plugin(); plugin.Attach();
        Chainloader.PluginInfos[RealismCommonLibContract.PluginGuid]=new PluginInfo{Instance=plugin,Metadata=typeof(Plugin).GetCustomAttribute<BepInPlugin>()};
        Check(RealismCommonLibBodyCompatibility.GetRejectionReason()==null,"fixture admission");
        var (game,old)=World(); var oldBundle=Bundle(); var oldReload=Plugin.ReloadStateInstance; var oldGear=Plugin.GearStateInstance; var oldWeapon=Plugin.WeaponStateInstance;
        oldReload.ReloadPhase=8; oldBundle[7].enabled=false;
        if(args.Length>0 && args[0]=="partial")
        {
            Prepare(game,old); Plugin.ThrowAfterWeapon=true; LocalPlayer partial=null;
            try { partial=Candidate(game,old); } catch(InvalidOperationException) { partial=(LocalPlayer)game.GameWorld.MainPlayer; }
            var partialState=Plugin.PlayerStateInstance; var partialWeapon=Plugin.WeaponStateInstance;
            Check(RealismCommonLibBodyCompatibility.GetRejectionReason()?.Contains("injected foreign init fault")==true,"partial failure refuses further teardown");
            game.GameWorld.MainPlayer=old; Finish();
            Check(oldBundle.SequenceEqual(Bundle()) && oldBundle[0].enabled && !oldBundle[7].enabled,"partial init rollback restores exact old state");
            Check(!Targets(PlayerEvents.OnPlayerInit,partialState) && !Targets(PWAEvents.OnUpdateWeaponVariablesWithFc,partialWeapon),"partial init handlers detached");
            Check(!partialState.enabled && !partialWeapon.enabled,"partial init controllers disabled");
            NoWrite(()=>partialState.Update(),"partial init late callbacks fenced");
            Console.WriteLine($"COMMONLIB_PARTIAL_CHECKS_PASSED {checks}"); return;
        }
        Writes(()=>oldReload.Update(),"ordinary raid callbacks unchanged");
        oldReload.ReloadPhase=8;
        Prepare(game,old);
        Check(oldBundle.SequenceEqual(Bundle()) && oldBundle[0].enabled && !oldBundle[7].enabled && oldReload.ReloadPhase==8,"Prepare leaves foreign state unchanged");
        RealismCommonLibBodyCompatibility.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Writes(()=>ShootPatch.Run(Pwa(old)),"cancelled admission permits initial body");
        Plugin.PlayerStateInstance.OnDestroy();
        Check(oldBundle[1].enabled && oldBundle[8].enabled && Targets(oldWeapon.OnWeaponStateChanged,oldGear),"active component destroy after cancelled prepare preserves siblings");
        Plugin.PlayerStateInstance.Awake();
        Prepare(game,old); Finish();
        Writes(()=>ShootPatch.Run(Pwa(old)),"failed creation without candidate preserves old callbacks");
        Prepare(game,old);
        var stranger=new LocalPlayer{GameWorld=game.GameWorld,Profile=new Profile()};
        PlayerInitPatch.Run(stranger);
        Check(oldBundle.SequenceEqual(Bundle()),"wrong-profile init cannot hijack prepared globals");
        var next=Candidate(game,old); var newBundle=Bundle(); var newGear=Plugin.GearStateInstance; var newWeapon=Plugin.WeaponStateInstance;
        Check(ReferenceEquals(Plugin.PlayerStateInstance.Player,next) && newBundle.All(c=>ReferenceEquals(c.gameObject,next.gameObject)),"fresh nine-controller candidate bundle");
        Check(oldBundle.All(c=>!c.enabled),"old bundle disabled at native init");
        NoWrite(()=>oldReload.Update(),"suspended old reload cannot write new animator");
        NoWrite(()=>oldGear.CheckGear(),"suspended old event target fenced");
        NoWrite(()=>ShootPatch.Run(Pwa(old)),"old body source cannot emit new firing event");
        var oldFc=(Player.FirearmController)old.HandsController;
        NoWrite(()=>OnMagInsertedPatch.Run(ref oldFc),"old byref firearm callback fenced");
        Check(ToggleAimPatch.Run(oldFc),"old foreign boolean prefix allows native EFT operation");
        Writes(()=>ShootPatch.Run(Pwa(next)),"candidate physical callbacks remain active after native init");
        RespawnApi.Publish(old,next);
        Check(Targets(PlayerEvents.OnPlayerInit,oldGear),"uncommitted event does not retire old listeners");
        game.LocalPlayer=next; RespawnApi.Publish(old,next);
        Check(!Targets(PlayerEvents.OnPlayerInit,oldGear) && !Targets(PlayerEvents.OnItemAddedOrRemoved,oldGear)
            && !Targets(InputEvents.ChangeScopeInput,oldGear) && !Targets(PWAEvents.OnUpdateWeaponVariablesWithFc,oldWeapon),"retirement removes exact old static delegate targets");
        Check(!Targets(oldWeapon.OnWeaponStateChanged,oldGear),"retirement detaches from captured old publisher");
        Check(Targets(newWeapon.OnWeaponStateChanged,newGear),"new publisher listener survives retirement");
        int externalCalls=0; PlayerEvents.OnItemAddedOrRemoved+=()=>externalCalls++;
        oldGear.OnDestroy(); oldWeapon.OnDestroy();
        Check(Targets(newWeapon.OnWeaponStateChanged,newGear),"late old destroy preserves new publisher");
        PlayerEvents.OnItemAddedOrRemoved?.Invoke(); Check(externalCalls==1,"unrelated event subscriptions preserved");
        Finish();
        NoWrite(()=>oldReload.ResetMagazineReloadState(),"retired old reload remains fenced after EndAttempt");
        Writes(()=>Plugin.ReloadStateInstance.ResetMagazineReloadState(),"current reload remains active");
        newBundle[7].enabled=false; // A consumer may legitimately disable a controller after the previous commit.
        Prepare(game,next); var failed=Candidate(game,next); var failedBundle=Bundle();
        game.GameWorld.MainPlayer=next; Finish();
        Check(newBundle.SequenceEqual(Bundle()) && newBundle.Where((c,i)=>i!=7).All(c=>c.enabled) && !newBundle[7].enabled,
            "failed candidate restores previous globals and enabled flags");
        Check(failedBundle.All(c=>!c.enabled),"failed candidate disabled");
        NoWrite(()=>((ReloadState)failedBundle[8]).Update(),"failed candidate direct callback fenced");
        NoWrite(()=>ShootPatch.Run(Pwa(failed)),"failed candidate native source fenced");
        Writes(()=>ShootPatch.Run(Pwa(next)),"rollback permits restored physical body");
        Prepare(game,next); var third=Candidate(game,next); game.LocalPlayer=third; Finish();
        Check(ReferenceEquals(Plugin.PlayerStateInstance.Player,third) && newBundle.All(c=>!c.enabled),"failed rewire retains committed new body");
        var latest=Bundle(); RealismCommonLibBodyCompatibility.RetireWorld(new GameWorld());
        Check(latest.SequenceEqual(Bundle()),"unrelated retirement unchanged");
        Prepare(game,third,new Profile()); var scav=Candidate(game,third,BodyReplacementContext.ExpectedProfile);
        game.LocalPlayer=scav; game.Profile=scav.Profile; RespawnApi.Publish(third,scav); Finish();
        Check(ReferenceEquals(Plugin.PlayerStateInstance.Player,scav),"explicit profile replacement follows exact candidate");
        RealismCommonLibBodyCompatibility.RetireWorld(game.GameWorld); BodyReplacementContext.Reset();
        Check(Bundle().All(c=>ReferenceEquals(c,null)),"world retirement clears only owned globals");
        NoWrite(()=>((ReloadState)latest[8]).ResetMagazineReloadState(),"late old-world callback fenced");
        var (nextGame,fresh)=World(); Writes(()=>ShootPatch.Run(Pwa(fresh)),"next ordinary raid remains unchanged");
        Console.WriteLine($"COMMONLIB_CHECKS_PASSED {checks}");
    }
}
