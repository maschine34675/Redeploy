# HUD body native probe (development only)

Temporary, read-only UiBridge action for ImpactCue 2.0.3 / Enemy Markers 1.4.0
acceptance. It reads physical body/camera identities, existing marker objects and
their cached cameras, ImpactCue's existing telemetry snapshot and Arena's magazine
summary. It does not rebind a foreign field, force telemetry refresh, grant items,
change health or invoke a respawn. `remember=true` stores comparison references in
the probe only; it deliberately retains the previous body for the next observation.

Build with `dotnet build tools/HudBodyNativeProbe -c Release`. Nothing deploys
automatically. Install its single DLL under a temporary plugin folder on the test
host, then call UiBridge's `hook action=mod name=HudBodyNativeProbe/observe`.
Capture before death with `args={"remember":true}`, use the actual driver's
respawn, observe again and compare. Use a normal health/weapon change and a camera
turn to check that the foreign display reacts; identity equality alone is not a
visual acceptance test. Inspect screenshots separately. Remove the probe DLL and
any generated config after testing. Never include this tool in releases.
