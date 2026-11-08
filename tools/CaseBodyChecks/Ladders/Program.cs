using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using Mono.Cecil;
using Redeploy.Core.Compat;
using UnityEngine;
using tarkin.ladders.bep;

namespace UnityEngine
{
    public partial class Object
    {
        public static void DestroyImmediate(Object item)
        {
            item.GetType().GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(item, null);
            item.Destroyed = true;
        }
    }
}

namespace tarkin.ladders.bep
{
    [BepInPlugin("com.tarkin.ladders", "Climbable Ladders", "1.0.4")]
    public sealed class Plugin : BaseUnityPlugin { }
    public sealed class PlayerLadderController : MonoBehaviour
    {
        private Player player;
        public int Dismounts;
        public void Init(Player body) => player = body;
        private void OnDestroy() => Dismounts++;
    }
}

internal static class Program
{
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private static void Refused(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Unsafe dismount was accepted");
    }

    public static void Main(string[] args)
    {
        using (var release = AssemblyDefinition.ReadAssembly(args[0]))
        {
            var controller = release.MainModule.GetType("tarkin.ladders.bep.PlayerLadderController");
            Check(controller.Fields.Single(f => f.Name == "player").FieldType.FullName == "EFT.Player", "Released owner drift");
            var cleanup = controller.Methods.Single(m => m.Name == "OnDestroy").Body.Instructions;
            foreach (var method in new[] { "Cancel", "Unsubscribe", "set_IsAxesIgnored", "TrySetLastEquippedWeapon" })
                Check(cleanup.Any(i => i.Operand is MethodReference target && target.Name == method), "Missing native cleanup: " + method);
        }

        var world = new GameWorld();
        var old = new LocalPlayer { GameWorld = world };
        var next = new LocalPlayer { GameWorld = world, Profile = old.Profile };
        var game = new LocalGame { GameWorld = world, LocalPlayer = old, Profile = old.Profile };
        world.RegisterPlayer(old);
        Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = game;
        Check(ClimbableLaddersCompat.GetRejectionReason() == null && ClimbableLaddersCompat.Capture(game, old) == null, "Absent mod touched a body");
        var plugin = new Plugin();
        Chainloader.PluginInfos[ClimbableLaddersCompat.PluginGuid] = new PluginInfo { Instance = plugin, Metadata = (BepInPlugin)Attribute.GetCustomAttribute(typeof(Plugin), typeof(BepInPlugin)) };
        Check(ClimbableLaddersCompat.GetRejectionReason() == null, "Ladder binding failed");
        Refused(() => ClimbableLaddersCompat.Capture(game, old));
        BodyReplacementContext.Begin(game, old);
        Check(ClimbableLaddersCompat.Capture(game, old).Length == 0, "Non-climber touched");
        var climb = new PlayerLadderController();
        old.gameObject.Add(climb);
        climb.Init(old);
        var other = new PlayerLadderController();
        other.Init(next);
        next.gameObject.Add(other);
        var captured = ClimbableLaddersCompat.Capture(game, old);
        BodyReplacementContext.CancelPreparation();
        Refused(() => ClimbableLaddersCompat.Dismount(game, old, captured));
        Check(climb.Dismounts == 0 && other.Dismounts == 0, "A refused handoff interrupted a climb");
        BodyReplacementContext.Begin(game, old);
        climb.Init(next);
        Refused(() => ClimbableLaddersCompat.Capture(game, old));
        climb.Init(old);
        captured = ClimbableLaddersCompat.Capture(game, old);
        ClimbableLaddersCompat.Dismount(game, old, captured);
        ClimbableLaddersCompat.Dismount(game, old, captured);
        Check(climb.Dismounts == 1 && other.Dismounts == 0, "Only the outgoing controller may dismount");
        Chainloader.PluginInfos["com.fika.core"] = new PluginInfo();
        Check(ClimbableLaddersCompat.GetRejectionReason() == null && ClimbableLaddersCompat.Capture(game, old) == null, "Fika should pass through");
        Console.WriteLine("CLIMBABLE_LADDERS_CHECKS_PASSED");
    }
}
