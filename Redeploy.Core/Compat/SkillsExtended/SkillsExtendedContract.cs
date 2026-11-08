using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class SkillsExtendedContract
{
    internal static readonly Version Reviewed = new(3, 1, 4, 0);
    internal readonly FieldInfo PlayerField, MasteryEvent, MedicalEvent;
    internal readonly MethodInfo Started;
    internal readonly Action<Mastering> Nato, Eastern;
    internal readonly Action<IHealthEffect> Medical;
    internal readonly Minigame Hacking, Picking;

    internal SkillsExtendedContract(Type plugin, string guid, Version version)
    {
        var assembly = StandaloneHudContract.Plugin(plugin, guid, SkillsExtendedCompat.PluginGuid, version, Reviewed,
            "SkillsExtended.SkillsExtendedPlugin", "SkillsExtended.Client.Skills", Reviewed);
        var start = assembly.GetType("SkillsExtended.Skills.Shared.Patches.OnGameStartedPatch", true);
        Started = StandaloneHudContract.Method(start, "Postfix", typeof(void), true, typeof(GameWorld));
        PlayerField = StandaloneHudContract.Field(start, "Player", typeof(Player), true);
        Nato = Handler<Mastering>(start, "ApplyNatoRifleXp");
        Eastern = Handler<Mastering>(start, "ApplyEasternRifleXp");
        Medical = Handler<IHealthEffect>(start, "ApplyMedicalXp");
        MasteryEvent = StandaloneHudContract.Field(typeof(SkillManager), nameof(SkillManager.OnMasteringExperienceChanged), typeof(Action<Mastering>));
        MedicalEvent = StandaloneHudContract.Field(typeof(ActiveHealthController), nameof(ActiveHealthController.EffectStartedEvent), typeof(Action<IHealthEffect>));
        Hacking = new Minigame(assembly.GetType("SkillsExtended.Skills.Hacking.HackingView", true));
        Picking = new Minigame(assembly.GetType("SkillsExtended.Skills.LockPicking.LockPickingGame", true));
    }

    private static Action<T> Handler<T>(Type owner, string name) =>
        (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), StandaloneHudContract.Method(owner, name, typeof(void), true, typeof(T)));

    internal sealed class Minigame
    {
        private readonly MethodInfo _current, _abort;
        private readonly FieldInfo _player;

        internal Minigame(Type type)
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                throw new MissingMemberException(type.FullName, "MonoBehaviour");
            _current = StandaloneHudContract.Method(type, "get_Current", type, true);
            _player = StandaloneHudContract.Field(type, "_player", typeof(Player));
            _abort = StandaloneHudContract.Method(type, "Abort", typeof(void), false);
        }

        internal MonoBehaviour OwnedBy(Player body)
        {
            var view = Current;
            return view != null && ReferenceEquals(_player.GetValue(view), body) ? view : null;
        }

        internal MonoBehaviour Current => (MonoBehaviour)_current.Invoke(null, null);

        internal void AbortIfStillOwned(MonoBehaviour expected, Player body)
        {
            if (expected != null && ReferenceEquals(OwnedBy(body), expected))
                _abort.Invoke(expected, null);
        }
    }

    /// <summary>Capture exact event owners before EFT tears down their player. Only move delegates installed by the mod.</summary>
    internal sealed class Events
    {
        private readonly SkillsExtendedContract _contract;
        private readonly SkillManager _skills;
        private readonly ActiveHealthController _health;
        private bool _medical, _nato, _eastern;

        internal Events(SkillsExtendedContract contract, Player old)
        {
            _contract = contract;
            _skills = old.Skills;
            _health = old.ActiveHealthController;
            Observe();
        }

        private static int Count(FieldInfo field, object target, Delegate handler)
        {
            if (target == null || field.GetValue(target) is not Delegate events) return 0;
            int count = 0;
            foreach (var callback in events.GetInvocationList())
                if (callback.Equals(handler)) count++;
            return count;
        }

        private void Observe()
        {
            _medical |= Count(_contract.MedicalEvent, _health, _contract.Medical) != 0;
            _nato |= Count(_contract.MasteryEvent, _skills, _contract.Nato) != 0;
            _eastern |= Count(_contract.MasteryEvent, _skills, _contract.Eastern) != 0;
        }

        internal void Move(Player next)
        {
            // The upstream world-start postfix can run while Arena's entry swap is yielding.
            Observe();
            var health = next.ActiveHealthController ?? throw new InvalidOperationException("replacement has no active health controller");
            var skills = next.Skills ?? throw new InvalidOperationException("replacement has no skill manager");
            var changes = new List<Change>();
            if (!ReferenceEquals(_health, health))
            {
                MedicalChange(changes, health, _medical);
                MedicalChange(changes, _health, false);
            }
            // Normal PMC respawns share this object. A single existing subscription stays
            // in place. Only duplicates of the exact upstream handlers are removed.
            if (ReferenceEquals(_skills, skills))
            {
                Deduplicate(changes);
            }
            else
            {
                MasteryChange(changes, skills, _contract.Nato, _nato);
                MasteryChange(changes, skills, _contract.Eastern, _eastern);
                MasteryChange(changes, _skills, _contract.Nato, false);
                MasteryChange(changes, _skills, _contract.Eastern, false);
            }
            Apply(changes, () => _contract.PlayerField.SetValue(null, next));
        }

        private void Deduplicate(List<Change> changes)
        {
            if (Count(_contract.MasteryEvent, _skills, _contract.Nato) > 1)
                MasteryChange(changes, _skills, _contract.Nato, true);
            if (Count(_contract.MasteryEvent, _skills, _contract.Eastern) > 1)
                MasteryChange(changes, _skills, _contract.Eastern, true);
        }

        internal void KeepOneSubscription()
        {
            var changes = new List<Change>();
            Deduplicate(changes);
            if (Count(_contract.MedicalEvent, _health, _contract.Medical) > 1)
                MedicalChange(changes, _health, true);
            Apply(changes, null);
        }

        internal void Detach()
        {
            var changes = new List<Change>();
            MedicalChange(changes, _health, false);
            MasteryChange(changes, _skills, _contract.Nato, false);
            MasteryChange(changes, _skills, _contract.Eastern, false);
            Apply(changes, null);
        }

        private sealed class Change
        {
            internal Func<int> Count;
            internal Action Add, Remove;
            internal int Before, After;
            internal void Set(int target)
            {
                int count = Count();
                for (int i = count; i < target; i++) Add();
                for (int i = count; i > target; i--) Remove();
            }
        }

        private void MedicalChange(List<Change> changes, ActiveHealthController owner, bool subscribe)
        {
            if (owner == null) return;
            changes.Add(new Change { Count = () => Count(_contract.MedicalEvent, owner, _contract.Medical),
                Add = () => owner.EffectStartedEvent += _contract.Medical, Remove = () => owner.EffectStartedEvent -= _contract.Medical,
                After = subscribe ? 1 : 0 });
        }

        private void MasteryChange(List<Change> changes, SkillManager owner, Action<Mastering> handler, bool subscribe)
        {
            if (owner == null) return;
            changes.Add(new Change { Count = () => Count(_contract.MasteryEvent, owner, handler),
                Add = () => owner.OnMasteringExperienceChanged += handler, Remove = () => owner.OnMasteringExperienceChanged -= handler,
                After = subscribe ? 1 : 0 });
        }

        private static void Apply(List<Change> changes, Action commit)
        {
            foreach (var change in changes) change.Before = change.Count();
            try
            {
                foreach (var change in changes) change.Set(change.After);
                commit?.Invoke();
            }
            catch (Exception original)
            {
                var failures = new List<Exception> { original };
                for (int i = changes.Count - 1; i >= 0; --i)
                    try { changes[i].Set(changes[i].Before); } catch (Exception restore) { failures.Add(restore); }
                if (failures.Count > 1) throw new AggregateException(failures);
                throw;
            }
        }
    }
}
