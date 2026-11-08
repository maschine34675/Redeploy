using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Mono.Cecil;
using PLATE.Client;
using PLATE.Client.Blood;
using PLATE.Client.Patches;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using UnityEngine;

internal static class Program
{
    private const string ReleaseHash = "6A2A456688C399E7230A95594D8C279D647A692B8DFC715A568F82C5B4C6CDC5";
    private static LocalGame _game;
    private static GameWorld _world;
    private static int _checks;
    private static int _unpatches;
    private static void ObserveUnpatch(string __0)
    {
        if (__0 == "com.maschine.Redeploy.Core.PlateCompat") _unpatches++;
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("CHECK FAILED: " + message);
        _checks++;
    }
    private static string Signature(Type type)
    {
        if (type.IsArray) return Signature(type.GetElementType()) + "[]";
        if (!type.IsGenericType) return type.FullName.Replace('+', '/');
        return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(Signature)) + ">";
    }
    private static void PublishedContract(string dll, PlateContract contract)
    {
        using var stream = File.OpenRead(dll);
        using var hash = SHA256.Create();
        Check(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "") == ReleaseHash, "release SHA");
        using var assembly = AssemblyDefinition.ReadAssembly(dll);
        Check(assembly.Name.Name == "PLATE.Client" && assembly.Name.Version == PlateContract.Reviewed, "release version");
        var plugin = assembly.MainModule.GetType(typeof(Plugin).FullName);
        var attribute = plugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin");
        Check((string)attribute.ConstructorArguments[0].Value == PlateCompat.PluginGuid
            && (string)attribute.ConstructorArguments[2].Value == "1.6.0", "published BepInEx metadata");
        foreach (var (hook, _) in contract.Hooks)
        {
            var args = hook.GetParameters().Select(p => Signature(p.ParameterType)).ToArray();
            var target = assembly.MainModule.GetType(hook.DeclaringType.FullName).Methods.SingleOrDefault(m =>
                m.Name == hook.Name && m.IsStatic == hook.IsStatic && m.ReturnType.FullName == Signature(hook.ReturnType)
                && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args));
            Check(target != null, "published contract " + hook.Name);
        }
        foreach (var field in contract.MovementBans.Concat(new[] { contract.Player, contract.States, contract.Winded,
                     contract.SavedBuffs, contract.BuffFields, contract.BuffValueField }))
        {
            var target = assembly.MainModule.GetType(field.DeclaringType.FullName).Fields.Single(f => f.Name == field.Name);
            Check(target.IsStatic == field.IsStatic && target.FieldType.FullName == Signature(field.FieldType), "published field " + field.Name);
        }
    }
    private static LocalPlayer Body(Profile profile)
    {
        var body = new LocalPlayer { Profile = profile, GameWorld = _world, IsYourPlayer = true };
        body.HealthController = new ActiveHealthController { Player = body };
        return body;
    }
    private static void Wire(LocalPlayer body)
    {
        if (!_world.RegisteredPlayers.Contains(body)) _world.RegisteredPlayers.Add(body);
        _world.MainPlayer = _game.LocalPlayer = body;
        GamePlayerOwner.MyPlayer = body;
        _game.Profile = body.Profile;
    }
    private static void Prepare(LocalPlayer body, Profile profile = null)
    {
        BodyReplacementContext.Begin(_game, body, profile);
        PlateCompat.Prepare(_game, body);
    }
    private static int Main(string[] args)
    {
        var info = new PluginInfo { Instance = new Plugin(), Metadata = typeof(Plugin).GetCustomAttribute<BepInPlugin>() };
        var contract = new PlateContract(typeof(Plugin), info.Metadata.GUID, info.Metadata.Version);
        PublishedContract(args[0], contract);
        new Harmony("plate-body-checks").Patch(typeof(Harmony).GetMethod("UnpatchID", BindingFlags.Public | BindingFlags.Static),
            prefix: new HarmonyMethod(typeof(Program), nameof(ObserveUnpatch)));
        new Harmony("com.maschine.Redeploy.Core.PlateCompat").UnpatchSelf();
        Check(_unpatches == 1, "instrument actual unpatch");
        for (var i = 0; i < 1000; i++) Check(PlateCompat.GetRejectionReason() == null, "absent admission");
        Check(_unpatches == 1, "absent polls never scan Harmony");
        // PLATE already patched EFT's Kill before Core installed its own fence.
        // Exercise the composed Harmony wrapper, not only a direct call to a postfix.
        new Harmony("com.anamelash.plate").Patch(typeof(ActiveHealthController).GetMethod("Kill"),
            postfix: new HarmonyMethod(typeof(BloodPatches), nameof(BloodPatches.KillPostfix)));
        Chainloader.PluginInfos.Add(PlateCompat.PluginGuid, info);
        Check(PlateCompat.GetRejectionReason() == null, "reviewed binding");
        Check(contract.Hooks.All(h => Harmony.GetPatchInfo(h.target) == null), "menu never installs hooks");

        _world = new GameWorld();
        _game = new LocalGame { GameWorld = _world, Status = GameStatus.Started };
        Singleton<GameWorld>.Instance = _world;
        Singleton<AbstractGame>.Instance = _game;
        var pmc = Body(new Profile { Id = "pmc" });
        var bot = Body(new Profile { Id = "bot" });
        bot.IsYourPlayer = false;
        Wire(pmc);
        var oldState = PlateBloodManager.GetOrCreate(pmc);
        oldState.Cur = 9;
        var botState = PlateBloodManager.GetOrCreate(bot);
        CrippleSystem.OnParalysed(pmc);
        CrippleSystem.OnParalysed(bot);
        WindedSystem.OnTorsoImpact(pmc, bot, 5f, default);
        WindedSystem.OnTorsoImpact(bot, pmc, 5f, default);
        CrippleSystem.SavedBuffs[pmc.Skills] = new[] { 12f, 16f };
        pmc.Skills.Strength.Value = 23f; // a non-PLATE correction already replaced its zero.
        pmc.HealthController.Kill(EDamageType.Bullet);
        Check(oldState.Dead, "ordinary death tracked");

        Prepare(pmc);
        PlateCompat.AbortPreparation();
        BodyReplacementContext.CancelPreparation();
        Check(ReferenceEquals(oldState, PlateBloodManager.GetOrCreate(pmc)) && oldState.Cur == 9, "refusal keeps old state");
        Prepare(pmc);
        var ticker = new BloodSystemComponent();
        _world.MainPlayer = null;
        ticker.Update();
        Check(ticker.EndWrites == 0 && PlateBloodManager.States.Count == 2, "no fake raid end / bot reset during replacement");
        _world.MainPlayer = pmc;
        var next = Body(pmc.Profile);
        _world.MainPlayer = next;
        Check(PlateBloodManager.GetOrCreate(next) == null, "candidate has no old life's state");
        var foreign = Body(pmc.Profile); // same ID but no lease, no world registration
        Check(ReferenceEquals(PlateBloodManager.GetOrCreate(foreign), oldState), "unrelated wrapper not intercepted");
        PlateBloodManager.RequestRefresh(bot);
        Check(botState.Ticks == 1, "bot still updates during creation");
        Wire(next);
        RespawnApi.Replace(pmc, next);
        BodyReplacementContext.EndAttempt();
        var fresh = PlateBloodManager.GetOrCreate(next);
        Check(!ReferenceEquals(oldState, fresh) && ReferenceEquals(fresh.Player, next) && !fresh.Dead && fresh.Cur == 100, "fresh body and fresh blood");
        Check(!CrippleSystem.SprintBanned.Contains(pmc.MovementContext) && CrippleSystem.SprintBanned.Contains(bot.MovementContext), "movement bans removed only for retired body");
        Check(next.Skills.Endurance.Value == 12f && next.Skills.Strength.Value == 23f, "restore only zeroed runtime bonuses");
        Check(!WindedSystem.Has(next) && WindedSystem.Has(bot), "winded cache reset only for owned life");
        pmc.HealthController.Kill(EDamageType.Bullet);
        PlateBloodManager.RequestRefresh(pmc);
        CrippleSystem.OnParalysed(pmc);
        WindedSystem.OnTorsoImpact(pmc, bot, 8, default);
        Check(!fresh.Dead && fresh.Ticks == 0 && !CrippleSystem.Paralysed.Contains(pmc.MovementContext)
            && !WindedSystem.Has(next) && PlateBloodManager.GetOrCreate(pmc) == null, "late retired callbacks cannot contaminate next life");

        Prepare(next);
        var orphan = Body(next.Profile);
        _world.MainPlayer = orphan;
        Check(BodyReplacementContext.TryOwnCandidate(orphan), "lease abandoned candidate");
        BodyReplacementContext.EndAttempt();
        RespawnApi.Fail(next);
        Check(PlateBloodManager.GetOrCreate(orphan) == null, "candidate fenced before recovery tick");
        Wire(next);
        PlateCompat.Tick();
        Check(ReferenceEquals(PlateBloodManager.GetOrCreate(next), fresh) && PlateBloodManager.GetOrCreate(orphan) == null,
            "original recovery keeps blood and rejects late candidate");

        var scav = Body(new Profile { Id = "scav" });
        Prepare(next, scav.Profile);
        _world.MainPlayer = scav;
        Check(BodyReplacementContext.TryOwnCandidate(scav), "own Scav lease");
        Wire(scav);
        BodyReplacementContext.EndAttempt();
        RespawnApi.Fail(next);
        PlateCompat.Tick();
        Check(!PlateBloodManager.States.ContainsKey("pmc") && ReferenceEquals(PlateBloodManager.GetOrCreate(scav).Player, scav)
            && ReferenceEquals(botState, PlateBloodManager.States["bot"]), "post-commit recovery to own Scav retains bot state");
        PlateCompat.RetireWorld(_world);
        BodyReplacementContext.Reset();
        _world.MainPlayer = null;
        ticker.Update();
        Check(ticker.EndWrites == 1, "true raid end belongs to PLATE");
        Chainloader.PluginInfos.Remove(PlateCompat.PluginGuid);
        Check(PlateCompat.GetRejectionReason() == null, "uninstall");
        Check(_unpatches == 2, "installed cleanup unpatches exactly once");
        CommitFault(info, malformed: true);
        CommitFault(info, malformed: false);
        Chainloader.PluginInfos.Add(PlateCompat.PluginGuid, info);
        Chainloader.PluginInfos.Add("com.fika.core", new PluginInfo());
        Check(PlateCompat.GetRejectionReason() == null && contract.Hooks.All(h => Harmony.GetPatchInfo(h.target)?.Prefixes
            .All(p => p.owner != "com.maschine.Redeploy.Core.PlateCompat") != false), "Fika remains inactive");
        Console.WriteLine("PLATE_BODY_CHECKS_PASSED " + _checks);
        return 0;
    }

    private static void CommitFault(PluginInfo info, bool malformed)
    {
        Chainloader.PluginInfos.Add(PlateCompat.PluginGuid, info);
        Check(PlateCompat.GetRejectionReason() == null, "rebind after independent scenario");
        var old = Body(new Profile { Id = "pmc" });
        Wire(old);
        var oldBlood = PlateBloodManager.GetOrCreate(old);
        var corpse = old.MovementContext;
        var fields = CrippleSystem._buffFields;
        CrippleSystem.OnParalysed(old);
        CrippleSystem.SavedBuffs[old.Skills] = new[] { 44f, 55f };
        if (malformed)
        {
            CrippleSystem._buffFields = new[] { fields[0], typeof(BloodState).GetField("Cur") };
            bool refused = false;
            try { Prepare(old); } catch (InvalidOperationException) { refused = true; }
            Check(refused && PlateBloodManager.States[old.ProfileId] == oldBlood && CrippleSystem.SprintBanned.Contains(corpse),
                "refuse malformed snapshot before teardown");
            PlateCompat.AbortPreparation();
            BodyReplacementContext.CancelPreparation();
            CrippleSystem._buffFields = fields;
        }
        var successor = Body(old.Profile);
        Prepare(old);
        _world.MainPlayer = successor;
        Check(BodyReplacementContext.TryOwnCandidate(successor), "owned fault-test candidate");
        Wire(successor);
        if (malformed) CrippleSystem._buffFields = new[] { fields[0], typeof(BloodState).GetField("Cur") };
        else PlateBloodManager.FailCreate = true;
        RespawnApi.Replace(old, successor);
        BodyReplacementContext.EndAttempt();
        Check(PlateCompat.GetRejectionReason() != null, "failed handoff is reported");
        Check(ReferenceEquals(oldBlood, PlateBloodManager.States[old.ProfileId]) && old.Skills.Endurance.Value == 0f
            && CrippleSystem.SavedBuffs.ContainsKey(old.Skills) && CrippleSystem.SprintBanned.Contains(corpse),
            "commit failure retains blood record and leaves every original bonus and ban intact");
        PlateBloodManager.FailCreate = false;
        CrippleSystem._buffFields = fields;
        PlateCompat.RetireWorld(_world);
        BodyReplacementContext.Reset();
        PlateBloodManager.States.Clear();
        Chainloader.PluginInfos.Remove(PlateCompat.PluginGuid);
        Check(PlateCompat.GetRejectionReason() == null, "faulted plugin removed");
    }
}
