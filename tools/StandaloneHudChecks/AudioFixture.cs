using EFT;
using UnityEngine;
namespace AccessibilityIndicators
{
#if FUTURE_MODS
    [BepInEx.BepInPlugin("com.acidphantasm.accessibilityindicators", "Indicators", "2.1.1")]
#else
    [BepInEx.BepInPlugin("com.acidphantasm.accessibilityindicators", "Indicators", "2.1.0")]
#endif
    public sealed class Plugin : BepInEx.BaseUnityPlugin { }
}
namespace AccessibilityIndicators.IndicatorUI
{
    public class Panel : MonoBehaviour
    {
        public static GameObject IndicatorHUD;
        private static Scripts.KeepNorthRotation keepNorthRotationScript;
        public static Scripts.KeepNorthRotation Rotation => keepNorthRotationScript;
        public static int Disposes;
        public static void Create()
        { IndicatorHUD = new(); keepNorthRotationScript = new(); IndicatorHUD.Add(keepNorthRotationScript); }
        public static void Dispose() { Disposes++; keepNorthRotationScript.Stop(); }
    }
}
namespace AccessibilityIndicators.Scripts
{
    public class KeepNorthRotation : MonoBehaviour
    {
        private bool isActuallyActive = true;
        public bool Active => isActuallyActive;
        public int Updates;
        public Player CameraBody;
        public void Stop() => isActuallyActive = false;
        public void HookedUpdate()
        {
            var method = GetType().GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method.Invoke(this, null);
        }
        private void Update()
        { if (isActuallyActive) { Updates++; CameraBody = Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer; } }
    }
}
namespace AccessibilityIndicators.Patches
{
    public class GameWorldUnregisterPlayerPatch
    {
        public static void Hooked(IPlayer player)
        {
            var method = typeof(GameWorldUnregisterPlayerPatch).GetMethod(nameof(PatchPostFix));
            method.Invoke(null, new object[] { player });
        }
        public static void PatchPostFix(IPlayer iPlayer)
        { if (iPlayer is Player player && player.IsYourPlayer && IndicatorUI.Panel.IndicatorHUD != null) IndicatorUI.Panel.Dispose(); }
    }
}
