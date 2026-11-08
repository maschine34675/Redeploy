using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace EFT
{
    public class MasteryGroup { public string Id; }
    public class Mastering { public MasteryGroup MasteringGroup = new() { Id = "test-rifle" }; }
    public class SkillManager
    {
        public event Action<Mastering> OnMasteringExperienceChanged;
        public readonly Dictionary<string, Mastering> Mastering = new();
        public void Raise(Mastering value) => OnMasteringExperienceChanged?.Invoke(value);
    }
    public class Hands { public object Held = new(); public object GetItem() => Held; }
    public partial class Player
    {
        public SkillManager Skills = new();
        public ActiveHealthController ActiveHealthController = new();
        public Hands HandsController = new();
        public int Nato, Eastern, Medical;
    }
}
namespace EFT.HealthSystem
{
    public interface IHealthEffect { }
    public class ActiveHealthController
    {
        public class Effect : IHealthEffect { public ActiveHealthController HealthController; }
        public event Action<IHealthEffect> EffectStartedEvent;
        public void Raise() => EffectStartedEvent?.Invoke(new Effect { HealthController = this });
    }
}
namespace SkillsExtended
{
    [BepInPlugin("com.cj.skillsextended", "Skills Extended", "3.1.4")]
    public sealed class SkillsExtendedPlugin : BaseUnityPlugin { }
}
namespace SkillsExtended.Skills.Shared.Patches
{
    internal static class OnGameStartedPatch
    {
        private static Player Player;
        public static Player Bound => Player;
        public static bool EasternEnabled;
        public static void Start(Player body, bool eastern)
        {
            EasternEnabled = eastern;
            Postfix(body.GameWorld);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Postfix(GameWorld world)
        {
            var body = world.MainPlayer;
            Player = body;
            body.ActiveHealthController.EffectStartedEvent += ApplyMedicalXp;
            body.Skills.OnMasteringExperienceChanged += ApplyNatoRifleXp;
            if (EasternEnabled) body.Skills.OnMasteringExperienceChanged += ApplyEasternRifleXp;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyNatoRifleXp(Mastering mastering)
        {
            _ = Player.HandsController.GetItem().GetType();
            Player.Nato++;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyEasternRifleXp(Mastering mastering)
        {
            _ = Player.HandsController.GetItem().GetType();
            Player.Eastern++;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyMedicalXp(IHealthEffect effect) => Player.Medical++;
        public static void LateMedical(ActiveHealthController old) =>
            ApplyMedicalXp(new ActiveHealthController.Effect { HealthController = old });
    }
}
namespace SkillsExtended.Skills.Hacking
{
    public class HackingView : MonoBehaviour
    {
        public static HackingView Current { get; private set; }
        private Player _player;
        public bool Aborted;
        public bool InRaid => _player != null;
        public static HackingView Open(Player player) => Current = new HackingView { _player = player };
        public void Abort() { Aborted = true; Current = null; }
    }
}
namespace SkillsExtended.Skills.LockPicking
{
    public class LockPickingGame : MonoBehaviour
    {
        public static LockPickingGame Current { get; private set; }
        private Player _player;
        public bool Aborted;
        public bool InRaid => _player != null;
        public static LockPickingGame Open(Player player) => Current = new LockPickingGame { _player = player };
        public void Abort() { Aborted = true; Current = null; }
    }
}
