using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Engine;

/// <summary>Pure ownership checks for the optional, independent-profile replacement path.</summary>
internal static class ProfileReplacementPolicy
{
    internal sealed class Shape
    {
        internal object Profile;
        internal string Id;
        internal IReadOnlyDictionary<string, object> MutableRoots;
        internal IReadOnlyDictionary<string, object> OptionalMutableRoots;
        internal IEnumerable<string> ItemIds;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }

    internal static void Validate(Shape original, Shape replacement)
    {
        // No replacement is the existing shared-profile respawn contract.
        if (replacement == null) return;
        if (original?.Profile == null || replacement.Profile == null
            || ReferenceEquals(original.Profile, replacement.Profile)
            || string.IsNullOrWhiteSpace(original.Id) || string.IsNullOrWhiteSpace(replacement.Id)
            || string.Equals(original.Id, replacement.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("A replacement profile must have a distinct, nonempty identity.");

        var oldRoots = new HashSet<object>(ReferenceComparer.Instance);
        if (original.MutableRoots != null)
            foreach (var value in original.MutableRoots.Values)
                if (value != null) oldRoots.Add(value);
        if (original.OptionalMutableRoots != null)
            foreach (var value in original.OptionalMutableRoots.Values)
                if (value != null) oldRoots.Add(value);
        if (replacement.MutableRoots == null)
            throw new InvalidOperationException("The replacement profile has no mutable-state description.");
        foreach (var root in replacement.MutableRoots)
        {
            if (root.Value == null)
                throw new InvalidOperationException($"The replacement profile is missing {root.Key}.");
            if (oldRoots.Contains(root.Value))
                throw new InvalidOperationException($"The replacement profile shares {root.Key} with the previous identity.");
        }
        if (replacement.OptionalMutableRoots != null)
            foreach (var root in replacement.OptionalMutableRoots)
                if (root.Value != null && oldRoots.Contains(root.Value))
                    throw new InvalidOperationException($"The replacement profile shares {root.Key} with the previous identity.");

        var oldIds = new HashSet<string>(original.ItemIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var newIds = new HashSet<string>(StringComparer.Ordinal);
        if (replacement.ItemIds == null)
            throw new InvalidOperationException("The replacement profile has no inventory item identities.");
        foreach (var id in replacement.ItemIds)
            if (string.IsNullOrWhiteSpace(id) || !newIds.Add(id) || oldIds.Contains(id))
                throw new InvalidOperationException("The replacement inventory contains an empty, duplicated or previously owned item identity.");
        if (newIds.Count == 0)
            throw new InvalidOperationException("The replacement inventory has no equipment root.");
    }
}
