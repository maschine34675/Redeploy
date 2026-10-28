using System;
using System.Collections.Generic;

namespace Redeploy.Services;

/// <summary>Identity and timer rules shared by the prompt, preparation and result fence.</summary>
internal static class RegularScavPolicy
{
    // Native side selection uses SavageLockTime < UtcNowUnixInt, not <=.
    public static double CooldownSeconds(double lockTime, double now)
    {
        if (double.IsNaN(lockTime) || double.IsInfinity(lockTime)
            || double.IsNaN(now) || double.IsInfinity(now))
            return double.PositiveInfinity;
        return lockTime < now ? 0d : Math.Floor(lockTime - now) + 1d;
    }

    public static bool IsAccountScav(string pmcId, string accountScavId, string candidateId)
        => !string.IsNullOrWhiteSpace(pmcId) && !string.IsNullOrWhiteSpace(accountScavId)
            && candidateId == accountScavId && candidateId != pmcId;

    public static bool OwnsResult(bool ending, bool sameSession, string pmcId, string scavId, string resultId)
        => ending && sameSession && !string.IsNullOrWhiteSpace(pmcId)
            && !string.IsNullOrWhiteSpace(scavId) && pmcId != scavId
            && (resultId == pmcId || resultId == scavId);

    public static void ValidateOwnedTimer<T>(IDictionary<string, T> timers, string key, T owned) where T : class
    {
        if (timers.TryGetValue(key, out var current) && !ReferenceEquals(current, owned))
            throw new InvalidOperationException($"The native {key} slot is owned by another timer; the Scav exit panel cannot be rebuilt safely.");
    }

    public static void RemoveOwnedTimer<T>(IDictionary<string, T> timers, string key, T owned) where T : class
    {
        ValidateOwnedTimer(timers, key, owned);
        timers.Remove(key);
    }
}

/// <summary>One physical Scav life, measured on the raid clock from its actual statistics start.</summary>
internal sealed class RegularScavLifeClock
{
    private object _body;
    private double _startedAt;
    private float? _elapsed;

    public bool TryStart(object body, double raidSeconds)
    {
        if (body == null || !IsValidTime(raidSeconds)) return false;
        if (_body != null) return ReferenceEquals(body, _body);
        _body = body;
        _startedAt = raidSeconds;
        return true;
    }

    public bool TryFreeze(object body, double raidSeconds, out float elapsed)
    {
        elapsed = 0f;
        if (_body == null || !ReferenceEquals(body, _body)) return false;
        if (_elapsed.HasValue) { elapsed = _elapsed.Value; return true; }
        if (!IsValidTime(raidSeconds) || raidSeconds < _startedAt || raidSeconds - _startedAt > 86400d)
            return false;
        elapsed = (float)(raidSeconds - _startedAt);
        _elapsed = elapsed;
        return true;
    }

    public bool TryRead(object body, out float elapsed)
    {
        elapsed = _elapsed.GetValueOrDefault();
        return _body != null && ReferenceEquals(body, _body) && _elapsed.HasValue;
    }

    public bool Owns(object body) => _body != null && ReferenceEquals(body, _body);

    private static bool IsValidTime(double value) => value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
}
