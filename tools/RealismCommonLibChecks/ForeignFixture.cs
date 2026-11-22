using System.Runtime.CompilerServices;
using EFT;
using EFT.Animations;
using RealismCommonLib.Events;
using RealismCommonLib.StateControllers.InstanceState;
using UnityEngine;

namespace RealismCommonLib
{
    [BepInEx.BepInPlugin("com.fontaine.spt-realism-common-lib", "CommonLib fixture", "0.1.0")]
    public class Plugin : BepInEx.BaseUnityPlugin
    {
        public static PlayerState PlayerStateInstance;
        public static WeaponState WeaponStateInstance;
        public static AimingState AimStateInstance;
        public static HealthState HealthStateInstance;
        public static HazardsState HazardsStateInstance;
        public static GearState GearStateInstance;
        public static FiringState FiringStateInstance;
        public static SkillState SkillStateInstance;
        public static ReloadState ReloadStateInstance;
        public static int Writes;
        public static bool ThrowAfterWeapon;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InitPlayerComponents(Player player)
        {
            PlayerStateInstance=player.gameObject.AddComponent<PlayerState>(); PlayerStateInstance.Player=player;
            WeaponStateInstance=player.gameObject.AddComponent<WeaponState>();
            if(ThrowAfterWeapon) throw new InvalidOperationException("injected foreign init fault");
            AimStateInstance=player.gameObject.AddComponent<AimingState>();
            HealthStateInstance=player.gameObject.AddComponent<HealthState>();
            HazardsStateInstance=player.gameObject.AddComponent<HazardsState>();
            GearStateInstance=player.gameObject.AddComponent<GearState>();
            FiringStateInstance=player.gameObject.AddComponent<FiringState>();
            SkillStateInstance=player.gameObject.AddComponent<SkillState>();
            ReloadStateInstance=player.gameObject.AddComponent<ReloadState>();
        }
        public void Attach() => PlayerEvents.OnPlayerInitRef += InitPlayerComponents;
    }
}
namespace RealismCommonLib.Events
{
    public static class PlayerEvents
    {
        public static Action<Player> OnPlayerInitRef;
        public static Action OnPlayerInit, OnItemAddedOrRemoved, ToggleHeadDevice, OnHandsControllerAssigned;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Init(Player p) { OnPlayerInitRef?.Invoke(p); OnPlayerInit?.Invoke(); }
    }
    public static class InputEvents { public static Action ChangeScopeInput, ChangeScopeMagnificationInput; }
    public static class PWAEvents { public static Action<Player.FirearmController> OnUpdateWeaponVariablesWithFc; }
}
namespace RealismCommonLib.StateControllers.InstanceState
{
    public class PlayerState:MonoBehaviour
    {
        public Player Player {get;set;}
        public void Awake() { PlayerEvents.OnPlayerInit+=Refresh; PlayerEvents.OnItemAddedOrRemoved+=Refresh; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Refresh() { Plugin.Writes++; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { Plugin.Writes++; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() { PlayerEvents.OnPlayerInit-=Refresh; PlayerEvents.OnItemAddedOrRemoved-=Refresh; }
    }
    public class WeaponState:MonoBehaviour
    {
        public Action OnWeaponStateChanged;
        public void Awake() { PWAEvents.OnUpdateWeaponVariablesWithFc+=UpdateWeaponStateOnStateChange; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateWeaponStateOnStateChange(Player.FirearmController fc) { Plugin.Writes++; OnWeaponStateChanged?.Invoke(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() { PWAEvents.OnUpdateWeaponVariablesWithFc-=UpdateWeaponStateOnStateChange; }
    }
    public class AimingState:MonoBehaviour { [MethodImpl(MethodImplOptions.NoInlining)] public void ReportAimState(bool value) { Plugin.Writes++; } }
    public class HealthState:MonoBehaviour { }
    public class HazardsState:MonoBehaviour { }
    public class GearState:MonoBehaviour
    {
        public Action OnGearStateChanged;
        public void Awake()
        {
            PlayerEvents.OnPlayerInit+=CheckGear; PlayerEvents.OnItemAddedOrRemoved+=CheckGear; PlayerEvents.ToggleHeadDevice+=CheckGear;
            InputEvents.ChangeScopeInput+=OnWeaponChanged; InputEvents.ChangeScopeMagnificationInput+=OnWeaponChanged;
            PlayerEvents.OnHandsControllerAssigned+=OnWeaponChanged; Plugin.WeaponStateInstance.OnWeaponStateChanged+=OnWeaponChanged;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public void CheckGear() { Plugin.Writes++; OnGearStateChanged?.Invoke(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnWeaponChanged() { CheckGear(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy()
        {
            PlayerEvents.OnPlayerInit-=CheckGear; PlayerEvents.OnItemAddedOrRemoved-=CheckGear; PlayerEvents.ToggleHeadDevice-=CheckGear;
            InputEvents.ChangeScopeInput-=OnWeaponChanged; InputEvents.ChangeScopeMagnificationInput-=OnWeaponChanged;
            PlayerEvents.OnHandsControllerAssigned-=OnWeaponChanged; Plugin.WeaponStateInstance.OnWeaponStateChanged-=OnWeaponChanged;
        }
    }
    public class FiringState:MonoBehaviour { [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { Plugin.Writes++; } }
    public class SkillState:MonoBehaviour { }
    public class ReloadState:MonoBehaviour
    {
        public int ReloadPhase;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { if(ReloadPhase!=0) ResetMagazineReloadState(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void ResetMagazineReloadState() { Plugin.Writes++; ReloadPhase=0; }
    }
}
namespace RealismCommonLib.Patches
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPrefix:Attribute {}
    [AttributeUsage(AttributeTargets.Method)] public sealed class PatchPostfix:Attribute {}
    public static class PlayerInitPatch
    { [PatchPostfix,MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { PlayerEvents.Init(p); } }
    public static class ShootPatch
    { [PatchPostfix,MethodImpl(MethodImplOptions.NoInlining)] public static void Run(ProceduralWeaponAnimation p) { Plugin.Writes++; } }
    public static class ToggleAimPatch
    { [PatchPrefix,MethodImpl(MethodImplOptions.NoInlining)] public static bool Run(Player.FirearmController fc) { Plugin.Writes++; return false; } }
    public static class OnMagInsertedPatch
    { [PatchPostfix,MethodImpl(MethodImplOptions.NoInlining)] public static void Run(ref Player.FirearmController fc) { Plugin.ReloadStateInstance.ResetMagazineReloadState(); } }
}

namespace RealismCommonLib.Patches {
public static class InventoryOpenPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class OnWeightUpdatedPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class OnItemAddedOrRemovedPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class ApplyMotionPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class ClampedSpeedPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class NVGTogglePatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class ThermalTogglePatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class FaceShieldTogglePatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class HandsControllerChangedPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class AimingChangedPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class UpdateWeaponVariablesPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class PwaWeaponParamsPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class SetAnimatorAndProceduralValuesPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class CheckAmmoPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class CheckChamberPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class BoltActionReloadPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class CanStartReloadPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class ReloadMagPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class QuickReloadMagPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class ReloadCylinderMagazinePatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class ReloadWithAmmoPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
public static class ReloadBarrelsPatch { [PatchPostfix, MethodImpl(MethodImplOptions.NoInlining)] public static void Run(Player p) { Plugin.Writes++; } }
}
