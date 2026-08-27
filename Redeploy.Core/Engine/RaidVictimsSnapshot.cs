using EFT;
using EFT.BinarySerialization;
using System.Collections.Generic;

namespace Redeploy.Core.Engine;

/// <summary>
/// A respawn starts a new statistics session which clears EftStats.Victims;
/// snapshot the kill list before player re-creation and restore it after.
/// </summary>
public static class RaidVictimsSnapshot
{
    private static List<VictimStats> _victimsSnapshot;

    public static void Snapshot(Profile profile)
    {
        if (profile?.EftStats?.Victims == null || profile.EftStats.Victims.Count == 0)
        {
            _victimsSnapshot = null;
            return;
        }

        _victimsSnapshot = new List<VictimStats>(profile.EftStats.Victims.Count);
        foreach (var victim in profile.EftStats.Victims)
        {
            if (victim != null)
                _victimsSnapshot.Add(BinaryCloneExtensions.ClonePolymorph<VictimStats>(victim));
        }
    }

    public static void Restore(Profile profile)
    {
        if (profile?.EftStats?.Victims == null || _victimsSnapshot == null || _victimsSnapshot.Count == 0)
            return;

        var victims = profile.EftStats.Victims;
        foreach (var victim in _victimsSnapshot)
        {
            if (victim != null)
                victims.Add(victim);
        }

        _victimsSnapshot = null;
    }
}
