using System.Reflection;
using Redeploy.Core.Compat;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;

public static class Program
{
    static int checks;
    static void Need(bool b,string s){checks++;if(!b)throw new InvalidOperationException("ASSERT: "+s);}
    static MethodInfo Hook(string n)=>typeof(RecoilLegacyCompat).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Static);
    static void Init(Player body,bool adapter=true)
    {
        object[] a={body,null};bool run=!adapter||(bool)Hook("BeforeInit").Invoke(null,a);Exception fault=null;
        try{if(run)ForeignBehavior.Fixture.Init.Invoke(null,new object[]{body});}
        catch(Exception e){fault=e;}
        if(adapter)fault=(Exception)Hook("AfterInit").Invoke(null,new[]{fault,body,a[1]});
        if(fault!=null)throw fault;
    }
    static void Calculate(Player body,Player.FirearmController hands)
    {
        var effect=body.ProceduralWeaponAnimation.Shootingg.NewShotRecoil;
        object[] a={effect,hands,null};bool run=(bool)Hook("BeforeRecalculate").Invoke(null,a);Exception fault=null;
        try
        {
            if(run)ForeignBehavior.Fixture.Calc.Invoke(null,new object[]{effect,hands.Template,new GlobalConfiguration.AimingConfiguration(),hands,0f,0f,0f,0f});
        }
        catch(Exception e){fault=e;}
        fault=(Exception)Hook("AfterRecalculate").Invoke(null,new[]{fault,a[2]});
        if(fault!=null)throw fault;
    }
    static void Update(object component)
    {
        if((bool)Hook("BeforeComponentUpdate").Invoke(null,new[]{component}))ForeignBehavior.Update(component);
    }
    static Fixture Reset(string version="2.0.0",bool wrong=false,bool native=true)
    {
        RecoilLegacyCompat.Shutdown();BodyReplacementContext.Reset();
        Harmony.Bound.Clear();Harmony.DropReceipt=false;Chainloader.PluginInfos.Clear();
        Singleton<AbstractGame>.Instance=null;Singleton<IBotGame>.Instance=null;Singleton<GameWorld>.Instance=null;
        RespawnApi.IsReplacing=false;ForeignBehavior.FailCamera=ForeignBehavior.FailCalc=ForeignBehavior.StaleCalc=false;ForeignBehavior.Calculations=0;
        var f=new Fixture(version,wrong);ForeignBehavior.Fixture=f;
        if(native)f.Bind();
        Chainloader.PluginInfos[RecoilLegacyContract.PluginGuid]=new(){Instance=(BaseUnityPlugin)Activator.CreateInstance(f.Plugin),
            Metadata=f.Plugin.GetCustomAttribute<BepInPlugin>()};
        RecoilLegacyCompat.Initialize();return f;
    }
    static LocalGame Game(bool loading=false)
    {
        var g=new LocalGame{GameWorld=new(),Status=loading?GameStatus.Running:GameStatus.Started};
        Singleton<AbstractGame>.Instance=g;Singleton<GameWorld>.Instance=g.GameWorld;
        Singleton<IBotGame>.Instance=loading?null:g;
        return g;
    }
    static LocalPlayer Body(LocalGame g,float weapon=5,Profile profile=null)
    {
        var b=new GameObject().Attach(new LocalPlayer{GameWorld=g.GameWorld,Profile=profile??g.Profile});
        g.GameWorld.MainPlayer=b; // Native Init registers before the foreign postfix.
        var hands=b.gameObject.Attach(new Player.FirearmController{Template=new(){Value=weapon}});
        b.HandsController=hands;
        b.ProceduralWeaponAnimation.Shootingg.Calculate=()=>Calculate(b,hands);
        return b;
    }
    static LocalPlayer Initial(LocalGame g)
    {
        g.Status=GameStatus.Running;var b=Body(g);Init(b);g.LocalPlayer=b;g.Status=GameStatus.Started;
        Singleton<IBotGame>.Instance=g;
        Calculate(b,(Player.FirearmController)b.HandsController); // The ordinary original weapon draw.
        return b;
    }
    static void Prepare(LocalGame g,Profile replacement=null)
    {
        BodyReplacementContext.Begin(g,g.LocalPlayer,replacement);
        RecoilLegacyCompat.Prepare(g,g.LocalPlayer);
        RecoilLegacyCompat.CompletePreparation();
        RespawnApi.IsReplacing=true;
    }
    static void Complete(LocalGame g,LocalPlayer old,LocalPlayer next,Profile replacement=null)
    {
        if(replacement!=null)g.Profile=replacement; // The engine commits the profile right before LocalPlayer.
        g.LocalPlayer=next;RespawnApi.Replace(old,next);RespawnApi.IsReplacing=false;BodyReplacementContext.EndAttempt();
    }
    static void Fail(Player old)
    {
        BodyReplacementContext.EndAttempt();RespawnApi.Fail(old);RespawnApi.IsReplacing=false;
    }
    static void End(){ RespawnApi.End(); BodyReplacementContext.Reset(); }
    static void Rejects(Action work,string message)
    {
        bool rejected=false;try{work();}catch(InvalidOperationException){rejected=true;}
        Need(rejected,message);
    }
    static void Main(string[] args)
    {
        var f=Reset();Need(RecoilLegacyCompat.GetRejectionReason()==null,"known identity admitted");
        var g=Game();var first=Initial(g);
        Need(!first.gameObject.Destroyed,"ordinary initial whole player survives");
        var firstR=first.GetComponent(f.Recoil);var firstC=first.GetComponent(f.Camera);
        Need(ReferenceEquals(f.Instance(f.Recoil),firstR)&&ReferenceEquals(f.Instance(f.Camera),firstC),"both ordinary initial owners");
        Need(((Behaviour)firstR).enabled&&((Behaviour)firstC).enabled,"ordinary initialized components enabled");
        Need(ForeignBehavior.Calculations==1,"ordinary initial physical draw unchanged");
        RecoilLegacyCompat.Tick();Need(ForeignBehavior.Calculations==1,"no pre-operation recoil calculation");
        foreach (bool alive in new[] { true, false })
        {
            f=Reset();g=Game();first=Initial(g);first.HealthController.IsAlive=alive;
            firstR=first.GetComponent(f.Recoil);firstC=first.GetComponent(f.Camera);
            var before=f.Cache("CurrentFirearmController");var beforeValues=(Vector2)f.Cache("CurrentRecoilVals");
            BodyReplacementContext.Begin(g,first);RecoilLegacyCompat.Prepare(g,first);
            RecoilLegacyCompat.AbortPreparation();BodyReplacementContext.Reset();
            Update(firstR);Need(first.Rotations==1,"canceled adoption leaves ordinary component updates untouched "+alive);
            Need(ReferenceEquals(f.Instance(f.Recoil),firstR)&&ReferenceEquals(f.Instance(f.Camera),firstC)
                &&ReferenceEquals(f.Cache("CurrentFirearmController"),before)&&((Vector2)f.Cache("CurrentRecoilVals")).x==beforeValues.x,
                "canceled adoption preserves original singleton pair and cache "+alive);
            End();Need(!firstR.Destroyed&&!firstC.Destroyed,"canceled adoption does not retire original components at raid end "+alive);
        }
        f=Reset();g=Game();first=Initial(g);
        for(int i=0;i<5;i++)
        {
            var old=g.LocalPlayer;var oldR=old.GetComponent(f.Recoil);var oldC=old.GetComponent(f.Camera);
            old.HealthController.IsAlive=i%2!=0; // First and later death replacement; alternate with live swaps.
            var cache=f.Cache("CurrentFirearmController");
            Prepare(g);
            Need(ReferenceEquals(f.Instance(f.Recoil),oldR)&&ReferenceEquals(f.Instance(f.Camera),oldC)
                &&ReferenceEquals(f.Cache("CurrentFirearmController"),cache)&&((Behaviour)oldR).enabled&&((Behaviour)oldC).enabled,
                "preparation leaves foreign state untouched "+i);
            var next=Body(g,10+i);
            try{Init(next);}catch(Exception){Need(false,"replacement initialized without singleton destruction "+i);}
            Need(!next.gameObject.Destroyed&&!old.gameObject.Destroyed,"replacement and old body survive "+i);
            Need(!((Behaviour)oldR).enabled&&!((Behaviour)oldC).enabled,"old pair disabled pending "+i);
            int rotations=old.Rotations;Update(oldR);Need(old.Rotations==rotations,"retired body cannot rotate "+i);
            var nr=next.GetComponent(f.Recoil);Update(nr);Need(next.Rotations==0,"uncommitted body cannot rotate "+i);
            Calculate(next,(Player.FirearmController)next.HandsController);
            Need(f.Cache("CurrentFirearmController")==null,"uncommitted cache cannot publish "+i);
            Complete(g,old,next);
            Need(oldR.Destroyed&&oldC.Destroyed&&!old.gameObject.Destroyed,"only old components destroyed "+i);
            Need(ReferenceEquals(f.Instance(f.Recoil),nr),"singleton current "+i);
            Need(((Vector2)f.Cache("CurrentRecoilVals")).x==10+i,"recalculation uses incoming weapon "+i);
            int calculations=ForeignBehavior.Calculations;RecoilLegacyCompat.Tick();
            Calculate(old,(Player.FirearmController)old.HandsController);
            Need(ForeignBehavior.Calculations==calculations,"retired controller remains guarded after component release "+i);
            Update(nr);Need(next.Rotations==1,"current recoil active "+i);
            Init(next);Need(next.gameObject.Components.Count(c=>c.GetType()==f.Recoil&&!c.Destroyed)==1,"duplicate owned init idempotent "+i);
        }
        var body=g.LocalPlayer;var previous=(Player.FirearmController)body.HandsController;
        var newHands=body.gameObject.Attach(new Player.FirearmController{Template=new(){Value=77}});
        body.HandsController=newHands;Calculate(body,newHands);
        Need(((Vector2)f.Cache("CurrentRecoilVals")).x==77,"weapon switch does not use prior weapon");
        f.Cache("CurrentFirearmController",previous);f.Cache("CurrentRecoilVals",new Vector2(123,456));
        ForeignBehavior.FailCalc=true;try{Calculate(body,newHands);Need(false,"exception propagated");}catch(TargetInvocationException){}
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),previous)&&((Vector2)f.Cache("CurrentRecoilVals")).x==123,"exception restores complete cache");
        ForeignBehavior.FailCalc=false;ForeignBehavior.StaleCalc=true;Calculate(body,newHands);
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),previous),"stale recalc restores previous controller");
        g.GameWorld.MainPlayer=body;ForeignBehavior.StaleCalc=false;

        f=Reset();g=Game();body=Initial(g);
        var oldRecoil=body.GetComponent(f.Recoil);var oldCamera=body.GetComponent(f.Camera);
        Prepare(g);var failed=Body(g);Init(failed);BodyReplacementContext.EndAttempt();RespawnApi.Fail(body);
        RecoilLegacyCompat.Tick();Need(!oldRecoil.Destroyed,"failure notification does not destroy recoverable old component");
        g.GameWorld.MainPlayer=body;RespawnApi.IsReplacing=false;RecoilLegacyCompat.Tick();
        Need(ReferenceEquals(f.Instance(f.Recoil),oldRecoil)&&ReferenceEquals(f.Instance(f.Camera),oldCamera),"old singleton pair restored");
        Need(((Behaviour)oldRecoil).enabled&&!body.gameObject.Destroyed&&!failed.gameObject.Destroyed,"failed replacement preserves both native bodies");
        Need(failed.GetComponent(f.Recoil)==null&&failed.GetComponent(f.Camera)==null,"failed components retired");
        Update(oldRecoil);Need(body.Rotations==1,"recovered old body recoil active");

        f=Reset();g=Game();body=Initial(g);body.HealthController.IsAlive=false;
        oldRecoil=body.GetComponent(f.Recoil);oldCamera=body.GetComponent(f.Camera);
        Prepare(g);failed=Body(g);Init(failed);g.GameWorld.MainPlayer=body;Fail(body);RecoilLegacyCompat.Tick();
        Need(ReferenceEquals(f.Instance(f.Recoil),oldRecoil)&&ReferenceEquals(f.Instance(f.Camera),oldCamera),
            "dead authoritative body restores singletons for retry");
        Need(!((Behaviour)oldRecoil).enabled&&!((Behaviour)oldCamera).enabled,"dead recovered components stay disabled");
        Update(oldRecoil);Need(body.Rotations==0,"dead recovered body cannot receive recoil updates");
        Prepare(g);var retried=Body(g);Init(retried);Complete(g,body,retried);
        Need(!retried.gameObject.Destroyed&&ReferenceEquals(f.Instance(f.Recoil),retried.GetComponent(f.Recoil)),
            "retry after dead old-body recovery succeeds");

        f=Reset();g=Game();body=Initial(g);Prepare(g);var unarmed=Body(g);unarmed.HandsController=null;
        Init(unarmed);Complete(g,body,unarmed);
        Need(f.Cache("CurrentFirearmController")==null&&!unarmed.gameObject.Destroyed,"unarmed standalone replacement leaves cache empty");
        var pickedUp=unarmed.gameObject.Attach(new Player.FirearmController{Template=new(){Value=81}});
        unarmed.HandsController=pickedUp;unarmed.ProceduralWeaponAnimation.Shootingg.Calculate=()=>Calculate(unarmed,pickedUp);
        RecoilLegacyCompat.Tick();
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),pickedUp)&&((Vector2)f.Cache("CurrentRecoilVals")).x==81,
            "first weapon after unarmed respawn uses its own controller");

        f=Reset();g=Game();body=Initial(g);
        Prepare(g);failed=Body(g);Init(failed);g.LocalPlayer=failed;Fail(body);RecoilLegacyCompat.Tick();
        Need(ReferenceEquals(f.Instance(f.Recoil),failed.GetComponent(f.Recoil)),"rewire recovery can retain authoritative new body");
        Need(!failed.gameObject.Destroyed,"rewire recovery never destroys new GameObject");

        f=Reset();g=Game();body=Initial(g);oldRecoil=body.GetComponent(f.Recoil);
        Prepare(g);ForeignBehavior.FailCamera=true;failed=Body(g);
        try{Init(failed);Need(false,"partial initializer must fault");}catch(TargetInvocationException){}
        ForeignBehavior.FailCamera=false;g.GameWorld.MainPlayer=body;Fail(body);RecoilLegacyCompat.Tick();
        Need(!failed.gameObject.Destroyed&&!body.gameObject.Destroyed,"partial Awake failure never destroys players");
        Need(ReferenceEquals(f.Instance(f.Recoil),oldRecoil),"partial failure restores previous singleton");

        f=Reset();g=Game();body=Initial(g);
        var ordinary=body;var duplicate=Body(g);Init(duplicate);
        Need(duplicate.gameObject.Destroyed,"negative control reproduces unowned upstream whole-player destruction");
        f=Reset();g=Game();body=Initial(g);Prepare(g);duplicate=Body(g);Init(duplicate,false);
        Need(duplicate.gameObject.Destroyed,"negative control disabling adapter breaks actual replacement");

        f=Reset();g=Game(loading:true);body=Initial(g);
        Need(!body.gameObject.Destroyed&&body.GetComponent(f.Recoil)!=null,"ordinary loading unchanged without prior Core operation");
        var orphan=new GameObject().Attach(new LocalPlayer());ForeignBehavior.Init(orphan);
        Need(orphan.gameObject.Destroyed,"ordinary behavior not globally repaired");

        f=Reset();g=Game();body=Initial(g);Prepare(g);var nextBody=Body(g);Init(nextBody);Complete(g,body,nextBody);body=nextBody;
        var unrelated=body.gameObject.Attach(new Player.FirearmController());
        f.Cache("CurrentFirearmController",unrelated);End();
        Need(f.Instance(f.Recoil)==null&&f.Instance(f.Camera)==null,"end clears only owned singleton pair");
        Need(!body.gameObject.Destroyed,"end never destroys native body");
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),unrelated),"end preserves unowned controller cache");
        int count=ForeignBehavior.Calculations;Calculate(body,(Player.FirearmController)body.HandsController);
        Need(ForeignBehavior.Calculations==count,"ended-raid controller remains guarded by weak tombstone");
        var ordinaryGame=Game();var ordinaryAfter=Body(ordinaryGame,90);Init(ordinaryAfter);ordinaryGame.LocalPlayer=ordinaryAfter;
        var ordinaryAfterRecoil=ordinaryAfter.GetComponent(f.Recoil);
        RecoilLegacyCompat.Tick();Update(ordinaryAfterRecoil);
        Need(ordinaryAfter.Rotations==1&&!ordinaryAfter.gameObject.Destroyed,"same-process subsequent ordinary player remains native");
        Calculate(ordinaryAfter,(Player.FirearmController)ordinaryAfter.HandsController);
        Need(ReferenceEquals(f.Cache("CurrentFirearmController"),ordinaryAfter.HandsController),"subsequent ordinary cache remains foreign-owned");
        count=ForeignBehavior.Calculations;Calculate(body,(Player.FirearmController)body.HandsController);
        Need(ForeignBehavior.Calculations==count&&ReferenceEquals(f.Cache("CurrentFirearmController"),ordinaryAfter.HandsController),"late Core callback cannot overwrite new ordinary controller");
        Init(body);Need(!body.gameObject.Destroyed&&ReferenceEquals(f.Instance(f.Recoil),ordinaryAfterRecoil),
            "late retired initialization cannot disturb ordinary body or destroy corpse");
        End();Need(ReferenceEquals(f.Instance(f.Recoil),ordinaryAfterRecoil)&&!ordinaryAfterRecoil.Destroyed,"ordinary raid end leaves foreign ordinary components intact");

        // GameEnd can be missed. The old world pump retires only its own state;
        // a delayed duplicate callback must not touch an already active new world.
        f=Reset();g=Game();body=Initial(g);Prepare(g);nextBody=Body(g);Init(nextBody);Complete(g,body,nextBody);
        var expiredWorld=g.GameWorld;var expiredBody=nextBody;var expiredRecoil=nextBody.GetComponent(f.Recoil);
        RecoilLegacyCompat.RetireWorld(new GameWorld());
        Need(!expiredRecoil.Destroyed&&ReferenceEquals(f.Instance(f.Recoil),expiredRecoil)
            &&ReferenceEquals(f.Cache("CurrentFirearmController"),expiredBody.HandsController),"unrelated world cleanup is inert");
        RecoilLegacyCompat.RetireWorld(expiredWorld);BodyReplacementContext.Reset();
        Need(expiredRecoil.Destroyed&&!expiredBody.gameObject.Destroyed&&f.Instance(f.Recoil)==null
            &&f.Cache("CurrentFirearmController")==null,"missing-end world cleanup releases only owned components and cache");
        var laterGame=Game();var laterFirst=Initial(laterGame);Prepare(laterGame);var laterBody=Body(laterGame,93);
        Init(laterBody);Complete(laterGame,laterFirst,laterBody);var laterRecoil=laterBody.GetComponent(f.Recoil);
        RecoilLegacyCompat.RetireWorld(expiredWorld);
        Need(!laterRecoil.Destroyed&&ReferenceEquals(f.Instance(f.Recoil),laterRecoil)
            &&ReferenceEquals(f.Cache("CurrentFirearmController"),laterBody.HandsController),
            "delayed old-world pump preserves new-world singleton and cache");
        count=ForeignBehavior.Calculations;Calculate(expiredBody,(Player.FirearmController)expiredBody.HandsController);Init(expiredBody);
        Need(ForeignBehavior.Calculations==count&&!expiredBody.gameObject.Destroyed
            &&ReferenceEquals(f.Instance(f.Recoil),laterRecoil),"missing-end cleanup retains expired callback tombstones");

        f=Reset();g=Game();body=Initial(g);oldRecoil=body.GetComponent(f.Recoil);
        var otherBody=new GameObject().Attach(new LocalPlayer());var otherHands=otherBody.gameObject.Attach(new Player.FirearmController());
        f.Cache("CurrentFirearmController",otherHands);BodyReplacementContext.Begin(g,body);
        Rejects(()=>RecoilLegacyCompat.Prepare(g,body),"unrelated cached controller rejects before construction");
        Need(ReferenceEquals(f.Instance(f.Recoil),oldRecoil)&&((Behaviour)oldRecoil).enabled&&!body.gameObject.Destroyed,
            "rejected cache adoption leaves existing player unchanged");
        f.Cache("CurrentFirearmController",body.HandsController);f.SetInstance(f.Recoil,otherBody.gameObject.Attach((Component)Activator.CreateInstance(f.Recoil)));
        Rejects(()=>RecoilLegacyCompat.Prepare(g,body),"unrelated singleton rejects before construction");
        Need(!body.gameObject.Destroyed&&!otherBody.gameObject.Destroyed,"rejected singleton adoption never destroys a body");

        f=Reset();g=Game();body=Initial(g);
        Rejects(()=>RecoilLegacyCompat.Prepare(g,body),"adoption without exact engine operation rejected");
        BodyReplacementContext.Begin(g,body);g.GameWorld.MainPlayer=new LocalPlayer();
        Rejects(()=>RecoilLegacyCompat.Prepare(g,body),"adoption with wrong authoritative main rejected");
        f=Reset();g=Game();body=Initial(g);Prepare(g);nextBody=Body(g);nextBody.Profile=new Profile();
        Need(!BodyReplacementContext.TryOwnCandidate(nextBody),"different profile never receives Core construction lease");
        Need(!RecoilLegacyScope.MayCreate(true,false,true,false)&&!RecoilLegacyScope.MayCreate(true,true,false,false)
            &&!RecoilLegacyScope.MayCreate(false,true,true,false)&&!RecoilLegacyScope.MayCreate(true,true,true,true),
            "new components require exact active candidate and adopted prior body");

        // Own-Scav continuation: the engine creates the body with the replacement profile and only
        // afterwards commits game.Profile, so body.Profile differs from game.Profile during Init.
        f=Reset();g=Game();body=Initial(g);var pmcProfile=g.Profile;var scavProfile=new Profile();
        oldRecoil=body.GetComponent(f.Recoil);oldCamera=body.GetComponent(f.Camera);
        Prepare(g,scavProfile);var scavBody=Body(g,21,scavProfile);
        Need(!ReferenceEquals(scavBody.Profile,g.Profile),"replacement body is created under the old game profile");
        try{Init(scavBody);}catch(Exception){Need(false,"own-Scav replacement initialized without singleton destruction");}
        Need(!scavBody.gameObject.Destroyed&&!body.gameObject.Destroyed,"own-Scav replacement and old body survive foreign initialization");
        var scavRecoil=scavBody.GetComponent(f.Recoil);var scavCamera=scavBody.GetComponent(f.Camera);
        Need(scavRecoil!=null&&scavCamera!=null&&ReferenceEquals(f.Instance(f.Recoil),scavRecoil)&&ReferenceEquals(f.Instance(f.Camera),scavCamera),
            "own-Scav replacement owns both singleton guards");
        Need(!((Behaviour)oldRecoil).enabled&&!((Behaviour)oldCamera).enabled&&!((Behaviour)scavRecoil).enabled,"own-Scav handover stays pending until commit");
        Update(scavRecoil);Need(scavBody.Rotations==0,"uncommitted own-Scav body cannot rotate");
        Complete(g,body,scavBody,scavProfile);
        Need(oldRecoil.Destroyed&&oldCamera.Destroyed&&!body.gameObject.Destroyed,"own-Scav commit releases only the old components");
        Update(scavRecoil);Need(scavBody.Rotations==1&&((Vector2)f.Cache("CurrentRecoilVals")).x==21,"own-Scav recoil active after commit");
        // Scav back to PMC is the same operation with the PMC profile as replacement.
        Prepare(g,pmcProfile);var backBody=Body(g,22,pmcProfile);Init(backBody);Complete(g,scavBody,backBody,pmcProfile);
        Need(!backBody.gameObject.Destroyed&&scavRecoil.Destroyed&&ReferenceEquals(f.Instance(f.Recoil),backBody.GetComponent(f.Recoil)),
            "own-Scav chain back to the PMC profile keeps the new body");

        // A replacement that fails after the Scav body initialized restores the old pair.
        f=Reset();g=Game();body=Initial(g);oldRecoil=body.GetComponent(f.Recoil);oldCamera=body.GetComponent(f.Camera);
        var failedScav=new Profile();Prepare(g,failedScav);failed=Body(g,23,failedScav);Init(failed);g.GameWorld.MainPlayer=body;Fail(body);RecoilLegacyCompat.Tick();
        Need(ReferenceEquals(f.Instance(f.Recoil),oldRecoil)&&ReferenceEquals(f.Instance(f.Camera),oldCamera)&&!failed.gameObject.Destroyed&&!body.gameObject.Destroyed,
            "failed own-Scav replacement restores the old pair");

        // A local player body that is not the engine's replacement keeps the foreign initializer untouched,
        // even while a replacement is being created.
        f=Reset();g=Game();body=Initial(g);oldRecoil=body.GetComponent(f.Recoil);oldCamera=body.GetComponent(f.Camera);
        Prepare(g,new Profile());var stranger=Body(g,31,new Profile());
        try{Init(stranger);}catch(Exception){Need(false,"unrelated profile body stays outside the recoil handoff");}
        Need(stranger.gameObject.Destroyed&&!body.gameObject.Destroyed&&ReferenceEquals(f.Instance(f.Recoil),oldRecoil)
            &&ReferenceEquals(f.Instance(f.Camera),oldCamera)&&((Behaviour)oldRecoil).enabled&&((Behaviour)oldCamera).enabled,
            "unrelated profile body leaves the old recoil pair untouched");

        f=Reset("1.10.0");Need(RecoilLegacyCompat.GetRejectionReason()!=null,"old release rejected");
        f=Reset("2.1.0");Need(RecoilLegacyCompat.GetRejectionReason()==null,"forward API validated version admitted");
        f=Reset(wrong:true);Need(RecoilLegacyCompat.GetRejectionReason()!=null,"foreign type drift rejected");
        f=Reset(native:false);Need(RecoilLegacyCompat.GetRejectionReason()!=null,"missing native hook rejected");
        f=Reset();Harmony.GetPatchInfo(f.Init).Prefixes.Clear();
        Need(RecoilLegacyCompat.GetRejectionReason()!=null,"missing adapter receipt rejects admission");
        f=Reset();g=Game();body=Initial(g);Prepare(g);
        Singleton<GameWorld>.Instance=new GameWorld();Update(body.GetComponent(f.Recoil));
        Need(body.Rotations==0,"old world update does not cross new world");
        RecoilLegacyCompat.Shutdown();BodyReplacementContext.Reset();Chainloader.PluginInfos.Clear();Harmony.Bound.Clear();
        RecoilLegacyCompat.Initialize();Need(RecoilLegacyCompat.GetRejectionReason()==null&&Harmony.Bound.Count==0,"absent mod installs no hooks and rejects nothing");
        RecoilLegacyCompat.Prepare(null,null);RecoilLegacyCompat.Tick();RecoilLegacyCompat.Shutdown();
        if(args.Length>0)PublishedMetadata.Verify(args[0],Need);
        Console.WriteLine("Recoil Legacy compatibility: "+checks+" checks passed (native Unity/Harmony integration not simulated).");
    }
}
