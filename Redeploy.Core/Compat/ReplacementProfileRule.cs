namespace Redeploy.Core.Compat;

/// <summary>
/// Which profile a body under construction may carry. The engine commits <c>game.Profile</c>
/// only after the new body was created and validated, so an own-Scav replacement body has the
/// replacement profile while the game still holds the old one. Adapters that decide while a
/// body is being built must therefore not compare it with the game's profile alone.
/// </summary>
internal static class ReplacementProfileRule
{
    /// <summary>
    /// True for the game's current profile (ordinary respawn, initial load) and for the profile
    /// the active replacement expects (<see cref="BodyReplacementContext.ExpectedProfile"/>, null
    /// outside a replacement). A null body profile never qualifies.
    /// </summary>
    internal static bool CarriesRaidProfile(object bodyProfile, object gameProfile, object expectedProfile)
        => bodyProfile != null
           && (ReferenceEquals(bodyProfile, gameProfile) || ReferenceEquals(bodyProfile, expectedProfile));
}
