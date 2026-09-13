// Stand-ins for the engine types the interop file touches. Only what it uses, with Unity's
// overloaded null (a destroyed object compares equal to null) because that is the trap the
// interop file is written around.
using System;
using System.Collections.Generic;

#pragma warning disable CS0414 // the drifted mock's events exist to be skipped by the binding, never raised

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = a is null || a.Destroyed;
            bool bNull = b is null || b.Destroyed;
            if (aNull || bNull) return aNull && bNull;
            return ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => this == other as Object;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        public static void Destroy(Object target) { if (!(target is null)) target.Destroyed = true; }
        public static void DestroyImmediate(Object target) { if (!(target is null)) target.Destroyed = true; }
    }

    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();

        public T AddComponent<T>() where T : Component
        {
            var component = Activator.CreateInstance<T>();
            component.gameObject = this;
            Components.Add(component);
            return component;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;

        public T GetComponent<T>() where T : Component
        {
            if (gameObject == null) return null;
            foreach (var component in gameObject.Components)
                if (component is T match && !match.Destroyed) return match;
            return null;
        }
    }

    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public static class Time { public static float realtimeSinceStartup; }

    public static class Debug { public static void Log(object message) { } }
}

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}

namespace EFT
{
    using UnityEngine;

    public sealed class HealthStub { public bool IsAlive = true; }

    public class Player : MonoBehaviour
    {
        public readonly string Name;
        public HealthStub HealthController = new HealthStub();
        public Player(string name) { Name = name; gameObject = new GameObject(); }
        public override string ToString() => Name;
    }

    public sealed class GameWorld : MonoBehaviour
    {
        public Player MainPlayer;
    }
}

namespace RespawnInteropChecks
{
    using EFT;

    /// <summary>The literal v1 surface (api-v1.baseline.txt) with test knobs behind it.</summary>
    public static class MockRespawnApi
    {
        public static int Version = 1;
        public static bool Available = true;
        public static bool Replacing;
        public static bool PendingAnswer;

        public static int ApiVersion => Version;
        public const string ReasonDeath = "death";
        public const string ReasonSwap = "swap";
        public const string StagePrepare = "prepare";
        public const string StageCreate = "create";
        public const string StageRaidEnding = "raid-ending";
        public const string StageRewire = "rewire";
        public static bool IsAvailable => Available;
        public static bool IsReplacing => Replacing;
        public static bool IsRespawnPending(Player body) => PendingAnswer;
        public static event Action<Player, string> DeathClaimed;
        public static event Action<Player, Player, string> Replaced;
        public static event Action<Player, string> ReplacementFailed;
        public static event Action RaidEnded;

        public static void RaiseDeathClaimed(Player body, string driver) => DeathClaimed?.Invoke(body, driver);
        public static void RaiseReplaced(Player oldBody, Player newBody, string reason) => Replaced?.Invoke(oldBody, newBody, reason);
        public static void RaiseReplacementFailed(Player oldBody, string stage) => ReplacementFailed?.Invoke(oldBody, stage);
        public static void RaiseRaidEnded() => RaidEnded?.Invoke();

        public static int Subscribers(string eventName)
        {
            var field = typeof(MockRespawnApi).GetField(eventName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return field?.GetValue(null) is Delegate handlers ? handlers.GetInvocationList().Length : 0;
        }

        public static void Reset()
        {
            Version = 1; Available = true; Replacing = false; PendingAnswer = false;
            DeathClaimed = null; Replaced = null; ReplacementFailed = null; RaidEnded = null;
        }
    }

    /// <summary>A facade without ReplacementFailed and with a drifted DeathClaimed: partial binding must survive it.</summary>
    public static class MockRespawnApiDrifted
    {
        public static int ApiVersion => 1;
        public static bool IsAvailable => true;
        public static bool IsReplacing => false;
        public static bool IsRespawnPending(Player body) => true;
        public static event Action<Player, Player, string> Replaced;
        public static event Action<Player, string, int> DeathClaimed;
        public static event Action RaidEnded;

        public static void RaiseReplaced(Player oldBody, Player newBody, string reason) => Replaced?.Invoke(oldBody, newBody, reason);
        public static int Subscribers(string eventName)
        {
            var field = typeof(MockRespawnApiDrifted).GetField(eventName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return field?.GetValue(null) is Delegate handlers ? handlers.GetInvocationList().Length : 0;
        }
        public static void Reset() { Replaced = null; DeathClaimed = null; RaidEnded = null; }
    }
}
