using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace Redeploy.Core.Compat;

/// <summary>Cola 4's narrowly reviewed physical-body fields; no raid-start or inventory operations.</summary>
internal sealed class TarkovaColaBodyContract
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private readonly Assembly _assembly;
    private readonly List<(FieldInfo Field, object Value)> _clear = new();
    private readonly FieldInfo _hands, _weapon, _muleWeapon, _lastHit, _nextPhysical;
    private readonly FieldInfo _locked, _destroyed, _bleeds;
    private readonly Action _destroyTomb;
    private readonly Action<Player> _refreshPhysical;
    internal readonly MethodInfo TrackerTick, IsMe, DownedProne;

    internal TarkovaColaBodyContract(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        TrackerTick = Method("Tracker", "Tick", typeof(void));
        IsMe = Method("Util", "IsMe", typeof(bool), typeof(Player));
        DownedProne = Method("DownedPronePatch", "Prefix", typeof(bool), typeof(MovementContext), typeof(bool));
        _destroyTomb = (Action)Method("Tombstone", "DestroyTomb", typeof(void)).CreateDelegate(typeof(Action));
        _refreshPhysical = (Action<Player>)Method("PerkFx", "RefreshPhysical", typeof(void), typeof(Player)).CreateDelegate(typeof(Action<Player>));
        _hands = Field("Tracker", "_hands", typeof(object));
        _weapon = Field("Tracker", "_prevWeapon", typeof(Weapon));
        _muleWeapon = Field("MuleKick", "_cur", typeof(Item));
        _lastHit = Field("QuickRevive", "_lastHit", typeof(float));
        _nextPhysical = Field("PerkFx", "_next1Hz", typeof(float));
        _locked = Field("QuickRevive", "_locked", typeof(HashSet<EBodyPart>));
        _destroyed = Field("QuickRevive", "_wasDestroyed", typeof(HashSet<EBodyPart>));
        _bleeds = Field("QuickRevive", "_bleeds", typeof(HashSet<object>));

        Add("Perk", "MyBreath", typeof(object), null);
        // IAnimator lives in the optional AnimationSystem.Types assembly; bind its reviewed
        // shape without adding a new hard assembly reference to Core.
        var animator = _assembly.GetType("TarkovaCola.Client.PerkFx", true).GetField("_sped", Static);
        if (animator?.FieldType.FullName != "IAnimator" || !animator.FieldType.IsInterface)
            throw new MissingFieldException("Tarkova-Cola PerkFx._sped contract changed");
        _clear.Add((animator, null)); // Never dereference a disposed outgoing animator.
        Values("PerkFx", false, "_hasPos", "_wasBreath", "_wasBreathD");
        Values("PerkFx", -1f, "_prevStamina");
        Values("PerkFx", 0f, "Speed", "_still", "ConcussUntil", "OwnWaveUntil");
        Values("PerkFx", 1f, "SwayMult");
        Values("StaminUp", false, "_hasPos");
        Values("Juggernog", false, "_polled");
        Values("Tracker", -999f, "_swapAt");
        Values("Tracker", false, "_leftBroken", "_rightBroken");

        // Keep _reviveUsed, _pmcKills, progress accumulators and bot histories. A body replacement
        // cannot grant another once-per-raid revive or clear another physical actor's state.
        Add("QuickRevive", "_kit", typeof(MedKitComponent), null);
        foreach (var name in new[] { "_pistol", "_tempPistol", "_primary" }) Add("QuickRevive", name, typeof(Item), null);
        Add("QuickRevive", "_lethalInfo", typeof(DamageInfo), default(DamageInfo));
        Values("QuickRevive", false, "Downed", "Bypass", "_regenOn", "_noStimShown", "_noKitShown");
        Values("QuickRevive", 0f, "_acc", "_reserveUntil", "_reserveReady", "_next1Hz", "_downedUntil", "_nextForce", "_removeTempAt");

        // Do not reset SlotIndex, _meters or the per-weapon APEX cooldown dictionary.
        Values("MuleKick", false, "_kickArmed", "_safetyHeld", "_hasPos");
        Values("MuleKick", true, "_kickUsed", "_safetyDone");
        Values("MuleKick", -1f, "_safetyAt");
        Values("MuleKick", -99f, "_swapAt", "_muleDrawAt", "_iconAt");
        Values("MuleKick", 0f, "_kickUntil");

        // A stale reload can discharge at the successor; a pending Hungry round can consume its ammo.
        Values("ElectricCherry", false, "_reloading", "_wasBoosted", "_sprintAtReload");
        Values("ElectricCherry", 0f, "_start", "_startValue", "_until", "_lastReloadOp", "_lastStamina");
        Values("ElectricCherry", -1f, "_emptiness");
        Values("DoubleTap", 0, "_hungryPending", "_shotsSinceHit", "_burst");
        Values("DoubleTap", false, "_doubleNow", "_semi");
        Values("DoubleTap", 0f, "_rhythm", "_stepUntil");
        Values("DoubleTap", -99f, "_lastHit", "_lastShot");
        Values("DoubleTap", -1f, "_maxSince");
        Values("DoubleTap", 1f, "_irreg");

        // End only the outgoing body's specter effect, preserving cooldown, offering charge,
        // pending mail/refunds, active perks, progress, bot effects and all physical inventory.
        Values("Tombstone", false, "Specter", "_hauntShot", "_escapeHit");
        Values("Tombstone", 0f, "_until", "_hiddenUntil", "_decoyUntil", "_nextLure", "_escapeCheck", "_hauntUntil");
        Values("Tombstone", -99f, "_endedAt");
        Values("Tombstone", -1, "_hauntFrame");
    }

    internal void RetireBodyState(LocalPlayer successor)
    {
        var hands = successor?.HandsController;
        var weapon = (hands as Player.FirearmController)?.Item as Weapon;
        // Validate collection instances before the first write; foreign readonly fields can still be null.
        var locked = (HashSet<EBodyPart>)_locked.GetValue(null) ?? throw new InvalidOperationException("QuickRevive limb state is unavailable");
        var destroyed = (HashSet<EBodyPart>)_destroyed.GetValue(null) ?? throw new InvalidOperationException("QuickRevive destroyed-limb state is unavailable");
        var bleeds = (HashSet<object>)_bleeds.GetValue(null) ?? throw new InvalidOperationException("QuickRevive bleeding state is unavailable");
        foreach (var entry in _clear) entry.Field.SetValue(null, entry.Value);
        locked.Clear(); destroyed.Clear(); bleeds.Clear();
        _hands.SetValue(null, hands); _weapon.SetValue(null, weapon); _muleWeapon.SetValue(null, weapon);
        _lastHit.SetValue(null, Time.time); _nextPhysical.SetValue(null, Time.time + 1f);
        _destroyTomb();
        // _tempPistol is deliberately only detached from Cola's cache. Its physical slot belongs
        // to the corpse/retained body and is never removed, transferred or re-created here.
        if (successor != null) _refreshPhysical(successor);
    }

    private void Values<T>(string type, T value, params string[] names)
    { foreach (var name in names) Add(type, name, typeof(T), value); }
    private void Add(string type, string name, Type valueType, object value) => _clear.Add((Field(type, name, valueType), value));
    private FieldInfo Field(string type, string name, Type valueType)
    {
        var field = _assembly.GetType("TarkovaCola.Client." + type, true).GetField(name, Static);
        if (field?.FieldType != valueType) throw new MissingFieldException("Tarkova-Cola " + type + "." + name + " contract changed");
        return field;
    }
    private MethodInfo Method(string type, string name, Type result, params Type[] arguments)
    {
        var method = _assembly.GetType("TarkovaCola.Client." + type, true).GetMethod(name, Static, null, arguments, null);
        if (method?.ReturnType != result || method.GetMethodBody() == null)
            throw new MissingMethodException("Tarkova-Cola " + type + "." + name + " contract changed");
        return method;
    }
}
