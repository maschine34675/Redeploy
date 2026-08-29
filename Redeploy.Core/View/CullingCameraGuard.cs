using EFT.CameraControl;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Redeploy.Core.View;

/// <summary>
/// Keeps the game's culling registry from choking on the camera our respawn
/// replaces.
///
/// Rebinding the camera runs the game's own PlayerCameraController.Create,
/// which instantiates a fresh "FPS Camera" and DESTROYS the previous one. The
/// culling manager, however, only ever ADDS cameras to its lookup — it has no
/// per-camera removal, just a wholesale clear at teardown. The destroyed camera
/// therefore stays in that dictionary, and the very next object to register
/// itself for culling throws: the lookup by camera instance id happens one line
/// BEFORE the null check the game wrote for exactly this case.
///
/// The damage is not just a log line. The manager reserves a culling slot and
/// marks it active before the throw, so the object never receives its index and
/// keeps the default of ZERO. When it is later destroyed it frees slot zero —
/// which belongs to a completely different object — resetting that object's
/// culling data, while its own slot leaks for the rest of the raid.
///
/// Vanilla never hits this: it creates the camera controller once per raid.
/// Doing it again mid-raid is our doing, so the cleanup is ours too.
/// </summary>
internal static class CullingCameraGuard
{
    private static readonly FieldInfo CamerasField =
        AccessTools.Field(typeof(CullingManager), "_cameras");

    private static readonly FieldInfo ObjectsDataField =
        AccessTools.Field(typeof(CullingManager), "_objectsData");

    private static readonly FieldInfo ActiveIndicesField =
        AccessTools.Field(typeof(CullingManager), "_activeSpheresIndices");

    private static bool _warned;

    /// <summary>
    /// Drops the replaced camera from the culling registry. Call directly after
    /// the camera was swapped, with the reference captured BEFORE the swap.
    /// </summary>
    public static void EvictReplacedCamera(Camera oldCamera)
    {
        var manager = CullingManager.Instance;
        if (manager == null)
            return;

        if (CamerasField == null)
        {
            WarnOnce("Culling registry layout not recognised — camera eviction disabled.");
            return;
        }

        try
        {
            if (!(CamerasField.GetValue(manager) is IDictionary cameras) || cameras.Count == 0)
                return;

            var live = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
            var survivors = new List<DictionaryEntry>(cameras.Count);
            var doomed = new List<DictionaryEntry>(2);

            foreach (DictionaryEntry entry in cameras)
            {
                var camera = entry.Key as Camera;

                // Reference comparison still matches after destruction, where
                // Unity's == operator already reports null.
                var isLive = !ReferenceEquals(live, null) && ReferenceEquals(camera, live);
                var isDestroyed = camera == null;
                var isReplaced = ReferenceEquals(camera, oldCamera);

                if (isLive || (!isDestroyed && !isReplaced))
                    survivors.Add(entry);
                else
                    doomed.Add(entry);
            }

            if (doomed.Count == 0)
                return;

            // Rebuilt rather than removed: a destroyed key no longer hashes to
            // the bucket it was inserted into, so Remove would silently miss it.
            cameras.Clear();
            foreach (var entry in survivors)
                cameras[entry.Key] = entry.Value;

            foreach (var entry in doomed)
                (entry.Value as IDisposable)?.Dispose();

            CoreLog.LogInfo($"Culling: evicted {doomed.Count} replaced camera(s), {cameras.Count} still registered.");
            RepairStuckIndices(manager);
        }
        catch (Exception ex)
        {
            WarnOnce($"Culling camera eviction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Heals objects that registered while the stale camera was still in place:
    /// their index was never assigned, so it sits at zero and would free a
    /// stranger's slot on destruction. The culling job never reads this index,
    /// so writing it from the main thread is safe at any time.
    /// </summary>
    private static void RepairStuckIndices(CullingManager manager)
    {
        if (ObjectsDataField == null || ActiveIndicesField == null)
            return;

        try
        {
            var data = ObjectsDataField.GetValue(manager) as Array;
            if (!(ActiveIndicesField.GetValue(manager) is List<int> active) || data == null)
                return;

            var repaired = 0;
            foreach (var slot in active)
            {
                if (slot < 0 || slot >= data.Length)
                    continue;

                var entry = data.GetValue(slot);
                var cullingObject = AccessTools.Field(entry.GetType(), "CullingObject")?.GetValue(entry) as CullingObject;
                if (cullingObject == null || cullingObject.Index == slot)
                    continue;

                cullingObject.Index = slot;
                repaired++;
            }

            if (repaired > 0)
                CoreLog.LogWarning($"Culling: repaired {repaired} object(s) stranded on slot 0.");
        }
        catch (Exception ex)
        {
            WarnOnce($"Culling index repair failed: {ex.Message}");
        }
    }

    private static void WarnOnce(string message)
    {
        if (_warned)
            return;

        _warned = true;
        CoreLog.LogWarning(message);
    }
}
