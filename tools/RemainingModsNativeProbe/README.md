# Remaining-mod native probe

Development-only UiBridge action `RemainingModsNativeProbe/observe`. It reads
physical player/camera identities and existing Amand, HUD Breakout, Quick
Cancel, PiP Disabler and Fast Grenade Throw state. It also reads native grenade
priority/candidates and the foreign HUD's health, ammunition and grenade values.
`remember=true` retains references solely inside the probe for
comparison; it does not fix fields, refresh foreign components or invoke their
lifecycle. Screenshots and actual native actions must accompany these reads.

Build this project separately. It never automatically deploys. Remove its DLL
and generated config after testing; never package it with a player release.
