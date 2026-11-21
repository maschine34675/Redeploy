// Independent behavioral fixture, not Armory source. Production adapter is linked unchanged.
using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using EFT.Animations;
namespace WTTArmoryClient
{
    [BepInPlugin("com.wtt.armory", "WTT Armory fixture", "2.0.5")]
    public class Plugin : BaseUnityPlugin
    {
        public static string[] GunIDs = { "678fe4a4906c7bd23722c71f", "679a6a534f3d279c99b135b9" };
        public Player You
        {
            get; set;
        }
        public static bool GunHasHyperburst
        {
            get; set;
        }
        public static bool IsFiring
        {
            get; set;
        }
        public static int RecoilShotCount
        {
            get; set;
        }
        public static int ROFShotCount
        {
            get; set;
        }
        public static float ShotTimer
        {
            get; set;
        }
        public void Tick()
        {
            if (You?.HandsController is Player.FirearmController fc && !fc.autoFireOn && IsFiring && GunHasHyperburst)
            {
                ShotTimer = 0;
                IsFiring = false;
                RecoilShotCount = ROFShotCount = 0;
            }
        }
    }
}
namespace WTTArmoryClient.Patches
{
    public static class UpdateWeaponVariablesPatch
    {
        public static void Run(ProceduralWeaponAnimation pwa) => PatchPostfix(pwa);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchPostfix(ProceduralWeaponAnimation pwa)
        {
            Plugin.GunHasHyperburst = Array.IndexOf(Plugin.GunIDs, pwa._firearmController.Weapon.TemplateId) >= 0;
        }
    }
    public static class ShootPatch
    {
        public static void Run(ProceduralWeaponAnimation pwa)
        {
            float strength = 1;
            PatchPrefix(pwa, ref strength);
            PatchPostFix(pwa, ref strength);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchPrefix(ProceduralWeaponAnimation pwa, ref float strength)
        {
            Plugin.RecoilShotCount++;
            Plugin.IsFiring = true;
            Plugin.ShotTimer = 0;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PatchPostFix(ProceduralWeaponAnimation pwa, ref float strength)
        {
            Plugin.ROFShotCount++;
            Plugin.IsFiring = true;
            Plugin.ShotTimer = 0;
        }
    }
}
