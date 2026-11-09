// SPDX-License-Identifier: MIT
// Copyright (c) 2026 maschine. Part of Redeploy, https://github.com/maschine34675/Redeploy (MIT licence).
// RespawnConsumerExample.cs - the smallest complete consumer of Redeploy's respawn API: a BepInEx plugin that
// follows the local player's body through respawns, with or without Redeploy installed.
// Redeploy's tests compile this file together with ../RedeployRespawnInterop.cs against the game's assemblies
// and BepInEx, as C# 7.3, so it stays buildable. Copy whatever you need.
// Guide: docs/SOFT-DEPENDENCY.md (setup), docs/RULES.md (what to get right), docs/RECIPES.md (worked cases).
using BepInEx;
using Comfort.Common;
using EFT;
using UnityEngine;
using YourMod.Interop;

namespace YourMod
{
    [BepInPlugin("com.example.yourmod", "YourMod", "1.0.0")]
    public sealed class YourModPlugin : BaseUnityPlugin
    {
        private LocalBodyTracker _bodies;

        private void Awake()
        {
            // One tracker per plugin, on the plugin's own GameObject. It binds to Redeploy.Core when it is installed
            // and polls GameWorld.MainPlayer when it is not, so this plugin needs no dependency on Redeploy.
            _bodies = gameObject.AddComponent<LocalBodyTracker>();
            _bodies.Log = message => Logger.LogInfo(message);
            _bodies.BodyChanged += OnBodyChanged;

            // The tracker's own first bind line goes to Unity's log, which BepInEx writes to disk only with
            // WriteUnityLog on. This line is the one to quote in a bug report.
            Logger.LogInfo("Redeploy interop revision " + LocalBodyTracker.Revision + ", bound: " + _bodies.IsBound);
        }

        private void OnDestroy()
        {
            if (_bodies != null) _bodies.BodyChanged -= OnBodyChanged;
        }

        // previous: the body followed until now, null at raid start. After "death" it is the corpse; after "swap" it is
        // a living body that its requester disposes later. current: the new local body, null at raid end.
        private void OnBodyChanged(Player previous, Player current, string reason)
        {
            // Per-body behaviour: off the old body, a fresh instance on the new one. A corpse keeps every component.
            LocalBodyTracker.Detach<YourBodyBehaviour>(previous);
            if (current != null) LocalBodyTracker.AttachFresh<YourBodyBehaviour>(current);

            // Per-life state ends with the old body, whatever the reason: timers, "already dead" latches, overrides.
            if (previous != null) ResetPerLifeState();

            // A new identity, not only a new body: Redeploy's own-Scav continuation. The reason is still "death".
            if (previous != null && current != null && previous.ProfileId != current.ProfileId) StartNewIdentity(current);

            // With Core this handler runs in the middle of the replacement. Never start a respawn, stop the raid or
            // destroy a body here; defer such work by one frame.
        }

        private void ResetPerLifeState()
        {
            // Clear whatever described the previous life.
        }

        private void StartNewIdentity(Player current)
        {
            // Initialise state keyed by current.ProfileId, as you would at raid start.
        }

        /// <summary>Call this from your own Player.OnDead postfix.</summary>
        internal static void OnPlayerDead(Player body)
        {
            if (!Singleton<GameWorld>.Instantiated
                || !ReferenceEquals(Singleton<GameWorld>.Instance.MainPlayer, body)) return;  // a bot or a retired swap body

            LocalBodyTracker tracker = LocalBodyTracker.Instance;
            bool? pending = tracker != null ? tracker.IsRespawnPending(body) : null;
            if (pending == true)
            {
                // A Core driver holds the raid open for this death. A replacement is possible, not promised:
                // a Killed Stop may still follow, so keep your raid-end handling.
            }
            else if (pending == false)
            {
                // Core will not replace this body. Another respawn mod still might; this is not "final death".
            }
            else
            {
                // Unknown: Core absent, older than RespawnApi v1, or idle (Fika). Keep your vanilla logic.
            }
        }
    }

    /// <summary>Example of a per-body behaviour. It must act only while its body is the living local body.</summary>
    public sealed class YourBodyBehaviour : MonoBehaviour
    {
        private Player _player;

        private void Awake()
        {
            _player = GetComponent<Player>();
        }

        private void Update()
        {
            if (!LocalBodyTracker.IsCurrentLocalBody(_player)) return;
            // Per-frame work for the living local body.
        }
    }
}
