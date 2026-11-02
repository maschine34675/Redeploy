using BepInEx;
namespace HollywoodFX
{
#if FUTURE_MODS
    [BepInPlugin("com.janky.hollywoodfx", "HollywoodFX", "3.1.0")]
#else
    [BepInPlugin("com.janky.hollywoodfx", "HollywoodFX", "3.0.0")]
#endif
    public sealed class Plugin : BaseUnityPlugin { }
    public static class ImpactStatic { public static EFT.Player LocalPlayer; }
}
