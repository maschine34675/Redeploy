using EFT.InventoryLogic;
using JsonType;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Redeploy.Services;

/// <summary>
/// Preparation stopped before this client changed anything: the dead PMC's statistics session is still
/// open, no cost item was touched and no Scav was reserved, so the PMC respawn choices are still valid.
/// </summary>
internal sealed class ScavPreparationRefused : InvalidOperationException
{
    public string Code { get; }

    public ScavPreparationRefused(string reason, string code = null, Exception inner = null)
        : base(reason, inner) { Code = code; }
}

/// <summary>A well-formed server answer that declines the request. <see cref="Code"/> is the raw server code for the log.</summary>
internal sealed class ScavServerRefusal : InvalidOperationException
{
    public string Code { get; }

    public ScavServerRefusal(string code)
        : base(ScavEligibilityPolicy.DescribeServerCode(code)) { Code = code; }
}

internal enum ScavPrepareFailureAction
{
    /// <summary>The raid is no longer running; the native end owns what happens next.</summary>
    Nothing,
    /// <summary>Nothing was changed; offer the PMC choices again.</summary>
    ReopenPmcChoices,
    /// <summary>Something may already have changed; fail closed with the normal PMC death.</summary>
    EndRaid,
}

/// <summary>Read-only prerequisites shared by the choice prompt and preparation.</summary>
internal static class ScavEligibilityPolicy
{
    public const string PriorDeliveryReason = "Scav continuation is unavailable after sending items through BTR or transit.";
    public const string BtrDeliveryReason = "BTR item delivery is unavailable while continuing as your own Scav. Extract to transfer your loot to the stash.";
    public const string TransitArrivalReason = "Scav continuation is unavailable in a raid you reached by map transit.";
    public const string ServerUnreachableReason = "The server did not answer the Scav check (connection problem, or an older Redeploy server mod).";

    // The raid the server registered for a Scav continuation is a fresh one; StartRejection
    // refuses an arrival from another map by its nonzero count or non-empty history. Mirror that
    // test on the start response the client keeps: no transition data at all is a fresh raid, but
    // a missing history list is not (the server treats it as non-fresh too).
    public static bool IsTransitArrival(TransitSettings transition)
        => transition != null && (transition.transitionCount != 0 || transition.visitedLocations is not { Length: 0 });

    public static string UnavailableReason(bool hasDeathSnapshot, bool hasRaidIdentity,
        bool hasScavExits, bool hasExitAuthority, bool hasDeliveredItems, bool isTransitArrival)
    {
        if (!hasDeathSnapshot)
            return "The PMC death snapshot is unavailable.";
        if (!hasRaidIdentity)
            return "The original raid identity is unavailable.";
        if (isTransitArrival)
            return TransitArrivalReason;
        if (!hasScavExits)
            return "This location has no Scav extraction points.";
        if (!hasExitAuthority)
            return "Scav extraction compatibility could not be verified.";
        return hasDeliveredItems ? PriorDeliveryReason : null;
    }

    /// <summary>Wraps a pre-change blocker: a held or unsafe continuation is never a clean state to return from.</summary>
    public static InvalidOperationException Block(string reason, bool continuationHeld)
        => continuationHeld ? new InvalidOperationException(reason) : new ScavPreparationRefused(reason);

    // Only a failure that provably changed nothing may hand the PMC choices back. Every other
    // failure keeps the old fail-closed ending, so a respawn item or a half-ended session is
    // never combined with a PMC life the player did not choose.
    public static ScavPrepareFailureAction AfterPrepareFailure(bool refusedBeforeAnyChange,
        bool raidRunning, bool pmcChoiceStillPossible)
    {
        if (!raidRunning)
            return ScavPrepareFailureAction.Nothing;
        return refusedBeforeAnyChange && pmcChoiceStillPossible
            ? ScavPrepareFailureAction.ReopenPmcChoices
            : ScavPrepareFailureAction.EndRaid;
    }

    public static string DescribeServerCode(string code) => code switch
    {
        "scav-cooldown" => "Your regular Scav is still on cooldown. No Scav or equipment was consumed.",
        "no-issued-pmc-raid" => "The server did not register this raid for a Scav continuation. "
            + "That happens in a raid reached by map transit and after a server restart during the raid.",
        "previous-continuation-pending" => "An earlier Scav continuation or loot transfer is still unresolved. Finish it from the main menu first.",
        "regular-scav-unavailable" => "Your regular Scav is not available on the server.",
        "continuation-already-prepared" or "continuation-closed" or "prepare-expired" or "recovery-already-requested"
            or "unknown-continuation" or "body-already-activated" or "generation-mismatch"
            => "A Scav continuation for this raid was already used or closed.",
        "invalid-prepare-request" or "invalid-frozen-pmc-death" or "regular-scav-shares-pmc-items" or "profile-too-large"
            or "prepare-unavailable" or "control-unavailable"
            => "The server could not verify your PMC death for a Scav continuation.",
        _ => "The server refused the Scav continuation.",
    };

    /// <summary>The raw server code behind a failure, for log lines. The player-facing text never carries it.</summary>
    public static string RefusalCode(Exception failure)
    {
        for (var current = failure; current != null; current = current.InnerException)
        {
            if (current is ScavServerRefusal refusal)
                return refusal.Code;
            if (current is ScavPreparationRefused { Code: not null } refused)
                return refused.Code;
        }
        return null;
    }

    public static string DescribeForLog(Exception failure)
    {
        var code = RefusalCode(failure);
        return (failure?.Message ?? "no profile returned") + (code == null ? "" : $" [server code: {code}]");
    }

    // Native GetTransferItems serializes the stash root even when its grids are empty.
    // A direct child in any grid is enough to prove actual delivery, without flattening
    // every item tree on each prompt OnGUI call. Temporary, unpurchased grids are not
    // this stash and are deliberately not treated as delivered contents.
    public static bool HasDeliveredItems(Stash stash)
        => stash?.Grids != null && stash.Grids.Any(grid => grid?.ItemCollection?.Any() == true);

    public static FlatItem[] SelectInsuredSnapshot(IEnumerable<FlatItem> serialized, IEnumerable<string> insuredIds)
    {
        var insured = new HashSet<string>(insuredIds);
        var snapshot = new Dictionary<string, FlatItem>();
        foreach (var item in serialized)
        {
            string id = item._id;
            if (!insured.Contains(id))
                continue;
            // Match native deduplication: preserve the record with a real parent.
            if (snapshot.TryGetValue(id, out var previous)
                && previous.parentId.HasValue && !string.IsNullOrEmpty(previous.parentId.Value))
                continue;
            snapshot[id] = item;
        }
        return snapshot.Values.ToArray();
    }
}
