using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VisualAssist;

// Independent minimal boundary: deliberately retains the released lifecycle/resource defects.
[BepInPlugin("com.janky.visualassist", "Janky-VisualAssist", "1.2.0")]
public class Plugin : BaseUnityPlugin
{
    public static ConfigEntry<bool> GrenadeArcEnabled = new() { Value = true };
}
public struct GrenadeThrow { public Vector3 ThrowPosition, ThrowForce; }
public class GrenadeArc : MonoBehaviour
{
    public static bool FailAwake;
    public static GrenadeArc LastCreated;
    public Player localPlayer;
    public GrenadeThrow GrenadeThrow;
    private LineRenderer _line;
    private GameObject _sphere;
    private Renderer _sphereRenderer;
    public LineRenderer Line => _line;
    public GameObject Sphere => _sphere;
    public Renderer Knob => _sphereRenderer;
    public int CleanupCalls;
    public int NativeUpdates;
    public void Awake()
    {
        LastCreated = this;
        _line = gameObject.AddComponent<LineRenderer>(); _line.sharedMaterial = new Material();
        _sphere = new GameObject();
        _sphereRenderer = _sphere.AddComponent<Renderer>();
        if (FailAwake) throw new InvalidOperationException("synthetic partial Awake");
        _sphereRenderer.sharedMaterial = new Material();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnDestroy() { CleanupCalls++; } // Upstream only unsubscribes configuration handlers.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void LateUpdate()
    {
        if (localPlayer == null || !localPlayer.HealthController.IsAlive || !Plugin.GrenadeArcEnabled.Value) return;
        NativeUpdates++; _line.enabled = true; _sphereRenderer.enabled = true;
        GrenadeThrow = new() { ThrowPosition = new(1, 2, 3), ThrowForce = new(4, 5, 6) };
    }
}
public class GrenadeAssistGameWorldStartedPostfixPatch
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Postfix(GameWorld __instance)
    {
        var arc = Singleton<GrenadeArc>.Instance = __instance.MainPlayer.gameObject.AddComponent<GrenadeArc>();
        arc.localPlayer = __instance.MainPlayer;
    }
}
public class GrenadeAssistPlayerOnDeadPrefixPatch
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Postfix(Player __instance)
    {
        if (!__instance.IsYourPlayer) return;
        var arc = __instance.gameObject.GetComponent(typeof(GrenadeArc)) as GrenadeArc;
        if (arc == null) return;
        Singleton<GrenadeArc>.Release(arc); Object.DestroyImmediate(arc);
    }
}
public class GrenadeAssistPlayerDisposePrefixPatch
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Postfix(Player __instance)
    {
        if (!__instance.IsYourPlayer) return;
        var arc = __instance.gameObject.GetComponent(typeof(GrenadeArc)) as GrenadeArc;
        if (arc == null) return;
        Singleton<GrenadeArc>.Release(arc); Object.DestroyImmediate(arc);
    }
}
