# ISB SOF Notifier body replacement checks

Run from the Redeploy repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/IsbNotifierChecks/Verify.ps1
```

The script compiles linked production adapter/context sources, checks the reviewed
released `ISBNotify.dll` with Cecil, compiles the adapter against real EFT/BepInEx
assemblies, then runs a synthetic native plugin through real Harmony hooks. It
does not build or deploy Core. Success ends with `ISB_NOTIFIER_CHECKS_PASSED`.
`-ContractOnly` performs only the released-binary contract check. Supply
`-PublishedDll` to override the local review artifact path.

The reviewed plugin is **com.samc137.ISBinfo**, `ISBNotify` 2.0.4.0, independently
registered from the main ISB package (`com.samc137.isbspecialforces`). The
`main-only` test verifies that its presence alone installs no notifier hooks and
leaves notifier state untouched. The downloaded notifier SHA-256 is
`1DBFD16EB0F498992A06D075766FA79EE5F5BE66526FCAA00A4E9F3814B9F3DA`.
Contract-compatible newer versions have no upper version ceiling and produce one
warning because native behavior has not been tested for them.

The only foreign write is `LocalRaidPresentationEnded = false`, once after
`RespawnApi.Replaced` identifies the exact prepared old body and a living,
owned replacement in the same running world. Physical membership must match the
notifier's `AllAlivePlayersList` check. Disabled/headless presentation waits only
while that committed body remains current and alive. There is no profile-ID
matching, history reset, counter transfer, initial-announcement replay, or scene
search. Native `MarkLocalRaidPresentationEnded` closes that world permanently for
this adapter; native `ResetState` invalidates any prepared/pending authorization.
Both callbacks remain native: the adapter only observes them.

Coverage includes repeated same-profile respawn, own-Scav profile replacement,
read-only preparation and refusal, failure before/after candidate creation,
physical identity aliases, registration, settings, headless mode, native end,
stopping status, native reset, scoped world retirement, missing Harmony hooks,
wrong GUID/assembly/version/field shape, destroyed/superseded singleton, and a
native predicate throwing. The normal fixture deliberately aliases the profile
between bodies; the negative controls still reject those aliases.

Four review passes: implementation; native source/IL and engine-publication
ordering reread; adversarial identity/failure/lifecycle checks (including a real
EFT compile); final cleanup and rerun. The harness is offline evidence only.

Native acceptance requires the legitimate full ISB package and its Aishi 2.0.7
dependency. After raid startup, `InitialMessageShown` becomes true after 20 s.
Enable the notifier, wait over 0.5 s in the death prompt, respawn, then press O
(manual-query cooldown 3 s). On ordinary maps O emits a message only when ISB
units/commanders are present or ISB has spawned earlier in that raid. Exercise a
real subsequent ISB kill/spawn announcement as well. Compare
`InitialMessageShown`, `ISBSpawnedThisRaid`, `ISBAliveCounts`,
`CommanderKIAShown`, `DeathNotificationsScheduled`, and `CachedISBOperators`
across a quiet respawn interval, and observe the presentation latch changing
true to false without `ResetState`. Repeat for own-Scav; give-up, extraction and
a fresh raid must retain their ordinary lifecycle behavior. No native gameplay
acceptance is claimed by this script.
