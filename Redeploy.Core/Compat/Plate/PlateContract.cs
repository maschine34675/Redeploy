using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Reviewed against PLATE.Client 1.6.0. All lookups happen at bind time.</summary>
internal sealed class PlateContract
{
    internal static readonly Version Reviewed = new(1, 6, 0, 0);
    internal readonly (MethodInfo target, string prefix)[] Hooks;
    internal readonly FieldInfo States, Player, Winded, SavedBuffs, BuffFields, BuffValueField;
    internal readonly FieldInfo[] MovementBans;
    internal readonly MethodInfo CreateState;

    internal PlateContract(Type plugin, string guid, Version version)
    {
        var asm = StandaloneHudContract.Plugin(plugin, guid, PlateCompat.PluginGuid, version,
            Reviewed, "PLATE.Client.Plugin", "PLATE.Client", Reviewed);
        var state = asm.GetType("PLATE.Client.Blood.BloodState", true);
        var manager = asm.GetType("PLATE.Client.Blood.PlateBloodManager", true);
        var cripple = asm.GetType("PLATE.Client.Blood.CrippleSystem", true);
        var winded = asm.GetType("PLATE.Client.Blood.WindedSystem", true);
        var ticker = asm.GetType("PLATE.Client.Blood.BloodSystemComponent", true);
        var bloodPatches = asm.GetType("PLATE.Client.Patches.BloodPatches", true);
        var windState = winded.GetNestedType("State", BindingFlags.NonPublic)
            ?? throw new MissingMemberException(winded.FullName, "State");
        States = Field(manager, "States", typeof(Dictionary<,>).MakeGenericType(typeof(string), state));
        Player = StandaloneHudContract.Field(state, "Player", typeof(Player));
        Winded = Field(winded, "States", typeof(Dictionary<,>).MakeGenericType(typeof(string), windState));
        SavedBuffs = Field(cripple, "SavedBuffs", typeof(Dictionary<SkillManager, float[]>));
        BuffFields = Field(cripple, "_buffFields", typeof(FieldInfo[]));
        BuffValueField = Field(cripple, "_buffValueField", typeof(FieldInfo));
        MovementBans = new[] { Field(cripple, "Paralysed", typeof(HashSet<MovementContext>)),
            Field(cripple, "SprintBanned", typeof(HashSet<MovementContext>)), Field(cripple, "JumpBanned", typeof(HashSet<MovementContext>)) };
        CreateState = Method(manager, "GetOrCreate", state, typeof(Player));
        // Guard PLATE's death callback before it discards physical identity and calls
        // MarkDead(profileId). Never suppress EFT's own health-controller Kill method.
        Hooks = new[] {
            (CreateState, "AllowCreate"),
            (Method(manager, "RequestRefresh", typeof(void), typeof(Player)), "AllowPlayer"),
            (Method(cripple, "OnParalysed", typeof(void), typeof(Player)), "AllowPlayer"),
            (Method(winded, "OnTorsoImpact", typeof(void), typeof(Player), typeof(Player), typeof(float), typeof(Vector3)), "AllowPlayer"),
            (Method(bloodPatches, "KillPostfix", typeof(void), typeof(ActiveHealthController), typeof(EDamageType)), "AllowKill"),
            (Method(manager, "TickOne", typeof(void), state, typeof(float)), "AllowTick"),
            (StandaloneHudContract.Method(ticker, "Update", typeof(void), false), "AllowUpdate")
        };
    }

    private static FieldInfo Field(Type type, string name, Type expected)
    {
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field?.FieldType != expected) throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static MethodInfo Method(Type type, string name, Type result, params Type[] args)
        => StandaloneHudContract.Method(type, name, result, true, args);

    internal IDictionary Registry => (IDictionary)States.GetValue(null);

    internal object CheckOwner(IDictionary registry, string id, Player owner)
    {
        var state = registry[id];
        if (state != null && (ReferenceEquals(owner, null) || !ReferenceEquals(Player.GetValue(state), owner)))
            throw new InvalidOperationException("P.L.A.T.E. blood record belongs to another physical player");
        return state;
    }

    internal Action PlanMovementCleanup(string oldId, MovementContext movement, SkillManager skills)
    {
        var winded = (IDictionary)Winded.GetValue(null) ?? throw new InvalidOperationException("P.L.A.T.E. winded registry is missing");
        var bans = Array.ConvertAll(MovementBans, f => (HashSet<MovementContext>)f.GetValue(null)
            ?? throw new InvalidOperationException("P.L.A.T.E. movement ban set is missing"));

        // An ordinary PMC replacement reuses SkillManager. Restore only values that
        // PLATE actually zeroed. Resolve every target before changing any record: an
        // incompatible runtime snapshot must not leave half a player's bonuses restored.
        var saved = (Dictionary<SkillManager, float[]>)SavedBuffs.GetValue(null);
        var buffs = Array.Empty<(SkillManager.FloatBuff buff, float original)>();
        if (skills != null && saved.TryGetValue(skills, out var values))
        {
            var fields = (FieldInfo[])BuffFields.GetValue(null);
            var value = (FieldInfo)BuffValueField.GetValue(null);
            if (fields == null || values == null || fields.Length != values.Length
                || value?.DeclaringType != typeof(SkillManager.FloatBuff) || value.Name != "Value" || value.FieldType != typeof(float))
                throw new InvalidOperationException("P.L.A.T.E. runtime skill-bonus snapshot changed");
            buffs = new (SkillManager.FloatBuff, float)[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field?.DeclaringType != typeof(SkillManager) || field.IsStatic || !typeof(SkillManager.FloatBuff).IsAssignableFrom(field.FieldType))
                    throw new InvalidOperationException("P.L.A.T.E. runtime skill-bonus target changed");
                buffs[i] = ((SkillManager.FloatBuff)field.GetValue(skills), values[i]);
            }
        }
        return () =>
        {
            winded.Remove(oldId);
            if (movement != null) foreach (var set in bans) set.Remove(movement);
            foreach (var (buff, original) in buffs)
                if (buff != null && buff.Value == 0f) buff.Value = original;
            if (skills != null) saved.Remove(skills);
        };
    }
}
