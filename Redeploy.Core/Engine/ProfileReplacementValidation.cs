using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using Redeploy.Core.GameAccess;

namespace Redeploy.Core.Engine;

/// <summary>Only explicit identity changes pay for the ownership and graph checks.</summary>
internal static class ProfileReplacementValidation
{
    internal static void BeforeTeardown(LocalGame game, LocalPlayer oldBody, Profile replacement)
    {
        if (replacement == null) return;
        if (oldBody == null || !oldBody.IsYourPlayer || !ReferenceEquals(game.LocalPlayer, oldBody)
            || !ReferenceEquals(oldBody.Profile, game.Profile) || !ReferenceEquals(oldBody.GameWorld, game.GameWorld))
            throw new InvalidOperationException("The original local body no longer owns the profile being replaced.");
        ValidateProfiles(game.Profile, replacement);
        LocalGameAccessor.ValidateReplacementIdentity(game, oldBody, replacement.Id);
        if (game.GameWorld.RegisteredPlayers.Any(player => player != null && player.ProfileId == replacement.Id))
            throw new InvalidOperationException("The replacement identity is already registered in this world.");
    }

    internal static void ValidateProfiles(Profile original, Profile replacement)
    {
        ProfileReplacementPolicy.Validate(Describe(original), Describe(replacement));
        foreach (var part in new[] { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach,
                     EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg })
        {
            if (!replacement.Health.BodyParts.TryGetValue(part, out var health) || health?.Health == null
                || float.IsNaN(health.Health.Maximum) || float.IsInfinity(health.Health.Maximum) || health.Health.Maximum <= 0f)
                throw new InvalidOperationException($"The replacement profile has no usable health for {part}.");
        }
    }

    internal static void ValidateCreated(LocalGame game, LocalPlayer oldBody, LocalPlayer candidate,
        Profile original, Profile replacement)
    {
        if (replacement == null) return;
        if (candidate == null || ReferenceEquals(candidate, oldBody) || !candidate.IsYourPlayer
            || !ReferenceEquals(candidate.Profile, replacement) || !ReferenceEquals(candidate.GameWorld, game.GameWorld)
            || !ReferenceEquals(game.Profile, original) || !ReferenceEquals(oldBody.Profile, original)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(game.GameWorld.MainPlayer, candidate))
            throw new InvalidOperationException("The new local body does not own the requested replacement identity.");
        LocalGameAccessor.ValidateReplacementIdentity(game, oldBody, replacement.Id, candidate);
    }

    private static ProfileReplacementPolicy.Shape Describe(Profile profile)
    {
        if (profile == null) return null;
        // Read Stats.Eft directly: EftStats is a lazy getter which would mutate the old profile.
        var stats = profile.Stats?.Eft;
        var roots = new Dictionary<string, object>
        {
            ["Info"] = profile.Info,
            ["Inventory"] = profile.Inventory,
            ["Inventory.Equipment"] = profile.Inventory?.Equipment,
            ["Health"] = profile.Health,
            ["Health.BodyParts"] = profile.Health?.BodyParts,
            ["Health.Energy"] = profile.Health?.Energy,
            ["Health.Hydration"] = profile.Health?.Hydration,
            ["Stats"] = profile.Stats,
            ["Skills"] = profile.Skills
        };
        // Eft is lazy on a freshly generated profile. Optional collections may be null
        // until native initialization, but any existing instance must still be independent.
        var optionalRoots = new Dictionary<string, object>
        {
            ["Stats.Eft"] = stats,
            ["Stats.SessionCounters"] = stats?.SessionCounters,
            ["Stats.OverallCounters"] = stats?.OverallCounters,
            ["Stats.Victims"] = stats?.Victims,
            ["QuestsData"] = profile.QuestsData,
            ["TaskConditionCounters"] = profile.TaskConditionCounters,
            ["TradersInfo"] = profile.TradersInfo
        };
        if (profile.Health?.BodyParts != null)
            foreach (var part in profile.Health.BodyParts)
            {
                roots[$"Health.{part.Key}"] = part.Value;
                roots[$"Health.{part.Key}.Health"] = part.Value?.Health;
                // A missing effects dictionary is supported by ResetProfileHealth.
                if (part.Value?.Effects != null) roots[$"Health.{part.Key}.Effects"] = part.Value.Effects;
            }
        return new ProfileReplacementPolicy.Shape
        {
            Profile = profile,
            Id = profile.Id,
            MutableRoots = roots,
            OptionalMutableRoots = optionalRoots,
            ItemIds = profile.Inventory?.GetPlayerItems().Select(item => item?.Id).ToArray()
        };
    }
}
