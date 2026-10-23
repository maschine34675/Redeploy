# Continuous Load Ammo adapter checks

Run `pwsh -NoProfile -File tools/ContinuousLoadAmmoCompatChecks/Verify.ps1` from
the repository. The script restores once when assets are missing, then deliberately disables
restore and shared compiler servers for the matrix, and never invokes the production deployment targets. The published 1.2.0 DLL is read with Mono.Cecil only. Supply
`-PublishedAssembly` when it is not present in the ignored acquisition directory.
No test downloads, installs, starts EFT or changes foreign settings.

The executable compiles all three real adapter files and the shared BodyReplacementContext. Instrumented
boundary types and an independent foreign fixture expose physical controllers,
task completion, event fan-out, native side effects and Harmony receipts. The
fixture assembly deliberately has the reviewed identity so that the production
reflection contract runs rather than a parallel policy implementation. A small
dispatcher applies the production prefixes/postfixes/finalizer; this is desktop
contract evidence, not proof of Mono/Harmony detours or Unity UI behavior.

Compiled faults remove individual ownership, lifecycle or cancellation guards.
Each must build and fail its named behavioral assertion; compile errors and
unrelated failures do not count. Production sources are checked for changes
during each run. Logs stay in ignored artifacts.

Coverage includes ordinary and absent-mod controls; untouched preflight adoption; first
standalone death, repeated death and live body swaps; rollback onto the old body;
old/new inventory and task epochs; initial native completion after adoption; disposal
exceptions; native task failure/cancellation; restored movement and static-slot ownership.
Initial ordinary loading retains the original Task. Its completed true branch is observed
weakly, and actual native removal revokes that proof before any later unrelated restriction.
Pre-existing shared restrictions reject preparation without disposal. Controller/body/world/
inventory/movement identities must all match before observed ownership is transferred.
Reflection invoked foreign entrypoints are routed through the fixture dispatcher as well,
so disposal and rollback exercise production hooks rather than bypassing them.

The native test still needs ordinary loading, standalone and Arena live-swap/respawn
loading, failed live-swap recovery, and a following ordinary raid, with real UI and
exact loaded build identity. An upstream 800-ms animation task already started before
Core's first adoption cannot be cancelled retroactively; it still references only its old
physical body. Replacement controllers unsubscribe from the identity-free completion
event, so that old task cannot drive a new body's loading lifecycle.

Native BarbedWire AddStateSpeedLimit is a no-op while that cause already exists.
Overlapping restrictions already share one native slot; this adapter does not rewrite that
upstream behavior or claim it can distinguish multiple simultaneous native owners.
