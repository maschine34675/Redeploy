# Enemy Markers body and camera checks

Run from the Redeploy repository:

```powershell
dotnet run --project tools/EnemyMarkersChecks/EnemyMarkersChecks.csproj -c Release
dotnet test Redeploy.Tests/Redeploy.Tests.csproj -c Release --filter FullyQualifiedName~EnemyMarkersTests
```

The executable compiles the production `EnemyMarkersCompat`, `EnemyMarkersContract`,
`EnemyMarkersScope`, and `BodyReplacementContext` sources. It runs actual Harmony
prefixes/postfixes against small Unity/EFT/BepInEx boundary fixtures and a separate
foreign-shaped assembly. The foreign fixture intentionally retains both reviewed
defects: local bodies are accepted by its person-added event and billboard
orientation reads its original camera. Production hooks must correct them. Marker
identity, persistent state, and quaternion orientation are observed directly.

Coverage includes missing-mod no-op, ordinary world behavior, physical identity
versus matching profiles, early cancellation, failure before unregister, failure
after candidate registration with recovery to the old body, delayed old-body and
retired-world events, repeated camera replacement while the old camera is still
alive, camera identity mismatch, newer API-compatible versions with a warning,
broken newer APIs, missing upstream/Core hooks, and retryable admission recovery.
The actual runtime world override is tested with the vanilla ClientGameWorld
early-return pattern: an already-unregistered corpse must still activate the
pending replacement even though the override never calls the patched base method.
The xUnit tests link the same pure scope and exercise its reference-identity and
retirement rules independently of Unity.

The reviewed public 1.4.0 DLL is inspected as metadata only. Default evidence path:
`../ArenaMode/artifacts/compat-seven-20261025/visual/enemy-release/BepInEx/plugins/flir.enemymarkers/flir.enemymarkers.dll`
(relative to the Redeploy repository). Pass a DLL path as the executable's first
argument to inspect a differently located copy. Source review used GitLab commit
`489f67e3fe2c4eaf3345b1665f5228d4ea74409d`; SHA-256 of that published DLL is
`CB1CD678019A32D1B8095B94C406E07AD3C5E2A5CE08FEFA2A92FC17C6FDA415`.

The fixtures do not emulate game rendering or prove native visibility, optics,
DLSS, LOS, performance, or event order in a running raid. Native validation remains
required for standalone Redeploy and ArenaMode. No foreign assembly is a production
Core compile dependency; this harness does not install or deploy anything.

## Integration

The shared coordinator must include the GUID in its optional-mod check and call
`GetRejectionReason`, `Prepare`, `AbortPreparation`, `Tick`, and `RetireWorld` at the
existing family boundaries. No Arena adapter, camera-helper call, or replacement
signal subscription is required. The adapter activates at the exact native
`GameWorld.UnregisterPlayer` virtual call after Core preparation, including the
verified runtime override when it differs from the base method. A candidate is acquired
from the existing `BodyReplacementContext.TryOwnCandidate` only after vanilla
registration sets the exact world `MainPlayer`.

Existing billboard instances update only their camera cache before upstream
`LateUpdate`, preserving upstream rotation (including roll), marker state, colors,
lights, text, ghost history, subscriptions, and coroutine lifecycle. Core-owned
local physical bodies are excluded from `OnPersonAdd`; bot events retain upstream
behavior. Delayed person-added/death callbacks from a retired owned component are
fenced without changing the mod's unrelated configuration subscriptions.

## Owned component destruction

Native standalone and Arena exits exposed the reviewed teardown defect: upstream
removes `OnPlayerDeadOrUnspawn` instead of its actual `OnIPlayerDeadOrUnspawn`
subscription, then reads `player.name` both in the try block and its catch. A
destroyed body aborts the remaining canvas/world/logger cleanup. Retired-world
event fences can leave these rows until destruction, so owned `OnDestroy` now
detaches the actual interface handlers, destroys surviving marker objects, and
removes those rows before allowing the original cleanup to run. No callback names
or Unity properties are read from destroyed bodies. Ordinary components retain
their original behavior.

The harness demonstrates the original exception on an unowned component, proves
the original tail runs for owned components, checks exact subscription removal
while preserving another subscriber, covers already-destroyed markers, verifies
that old-world destruction cannot affect a newer component, and rejects a future
changed teardown signature at admission. The published DLL check verifies both
unsafe name calls and the mismatched event removal from IL metadata.
