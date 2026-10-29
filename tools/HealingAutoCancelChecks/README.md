# Healing Autocancel body replacement checks

Run from the Redeploy repository:

```powershell
pwsh -NoProfile -File tools/HealingAutoCancelChecks/Verify.ps1 -PublishedDll <HealingAutoCancel.dll>
```

The default release path is the local ignored artifact folder. Obtain the reviewed
release from <https://sp-mod.com/mod/download/1274/healing-autocancel/5.0.1>.
Although the download is named `.zip`, its contents are a 7z archive.

- Forge package: **5.0.1**; DLL/plugin/assembly version: **1.1.0**.
- Runtime GUID: `HealingAutoCancel`; assembly: `HealingAutoCancel`.
- SHA256: `FA9478A151FDD797640F6B093C710227355ED0FAE56BFF8E3CEA7A9952081C11`.
- Upstream: <https://github.com/DeltaTwozero/HealingAutoCancel_SPT>, reviewed
  `d00067dec7a03734962277b18ccdb2e1d1e84812`; the release binary is authoritative.

The released mod binds `Globals.player`, `Globals.activeHealthController` and its
static `HealthChangedEvent` callback only in `OnGameStarted`. It never unsubscribes
from the old controller. Continuous Healing and Quick Cancel are separate mods;
their existing Core adapters do not cover this handler.

The runner compiles the actual production adapter against the installed EFT APIs,
then runs it with Harmony and physical-player test doubles. It verifies the exact
released DLL metadata/hash and the native event's sole emitter, `ChangeHealth`.
The callback implementation in the fixture mirrors the release's healing rules;
it is not an independent claim about medical animation behavior in Unity.

Coverage includes absent mods, ordinary initial raids, cancelled preparation,
exact old-body unregister (including an inherited client override that omits the base call),
same-profile unrelated bodies, bots, PMC-to-Scav, committed recovery without a
`Replaced` event, recovering the old body with a retained retired-source tombstone,
repeated replacements/ticks, pre-OnGameStarted Arena entry-kit
replacement including previous-raid static references, world retirement, nested
health events, exceptions, captured static and per-body callbacks, foreign hook
drift, older/newer versions and mid-process registry changes.

Six compile-valid mutants must fail at the intended behavioral assertion:
omitting inherited-method normalization, removing the retired-source fence, removing both redundant closure guards,
skipping old-event detach, mutating the foreign event during preparation, and
omitting replacement-event binding. The token `HEALING_AUTOCANCEL_CHECKS_PASSED`
is emitted only after all checks pass. Builds never deploy to the game.

Review passes:

1. Exact release/API: compare Forge package, installed DLL and decompiled callback.
2. Ownership: source belongs to the exact physical health controller, never a
   matching profile ID; preserve foreign configuration and unrelated bodies.
3. Failure/lifetime: preparation has no foreign mutation; failed later contracts
   retain existing fences; retired callbacks cannot act on a later ordinary raid.
4. Executable evidence: actual EFT compile, production-adapter behavior, released
   contract and independent mutation failures. Native acceptance remains a
   separate parent-owned test: use a medical item before/after standalone respawn
   and Arena kit/body replacement, observe automatic cancellation and cleanup.
