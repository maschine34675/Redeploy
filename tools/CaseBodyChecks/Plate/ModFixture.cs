using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace PLATE.Client
{
    [BepInPlugin("com.anamelash.plate", "P.L.A.T.E.", "1.6.0")]
    internal class Plugin : BaseUnityPlugin { }
}
namespace PLATE.Client.Blood
{
    internal class BloodState { public Player Player; public float Cur; public bool Dead; public int Ticks; }
    internal static class PlateBloodManager
    {
        internal static readonly Dictionary<string, BloodState> States = new();
        internal static bool FailCreate;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static BloodState GetOrCreate(Player player)
        {
            if (!States.TryGetValue(player.ProfileId, out var state)) States.Add(player.ProfileId, state = new BloodState { Player = player, Cur = 100 });
            if (FailCreate) throw new System.InvalidOperationException("injected PLATE initialization failure");
            return state;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RequestRefresh(Player player) { if (States.TryGetValue(player.ProfileId, out var state)) state.Ticks++; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void TickOne(BloodState state, float dt) { if (!state.Dead) state.Ticks++; }
    }
    internal static class CrippleSystem
    {
        internal static readonly HashSet<MovementContext> Paralysed = new(), SprintBanned = new(), JumpBanned = new();
        internal static readonly Dictionary<SkillManager, float[]> SavedBuffs = new();
        internal static FieldInfo[] _buffFields = { typeof(SkillManager).GetField("Endurance"), typeof(SkillManager).GetField("Strength") };
        internal static FieldInfo _buffValueField = typeof(SkillManager.FloatBuff).GetField("Value");
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OnParalysed(Player player) { Paralysed.Add(player.MovementContext); SprintBanned.Add(player.MovementContext); JumpBanned.Add(player.MovementContext); }
    }
    internal static class WindedSystem
    {
        private class State { }
        private static readonly Dictionary<string, State> States = new();
        public static bool Has(Player body) => States.ContainsKey(body.ProfileId);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OnTorsoImpact(Player victim, Player attacker, float joules, Vector3 point) { States[victim.ProfileId] = new State(); }
    }
    internal class BloodSystemComponent
    {
        public int EndWrites, BotUpdates;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Update()
        {
            var world = Comfort.Common.Singleton<GameWorld>.Instance;
            if (world == null || world.MainPlayer == null) { EndWrites++; PlateBloodManager.States.Clear(); }
            else BotUpdates++;
        }
    }
}
namespace PLATE.Client.Patches
{
    internal static class BloodPatches
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void KillPostfix(ActiveHealthController __instance, EDamageType damageType)
        {
            if (Blood.PlateBloodManager.States.TryGetValue(__instance.Player.ProfileId, out var state)) state.Dead = true;
        }
    }
}
