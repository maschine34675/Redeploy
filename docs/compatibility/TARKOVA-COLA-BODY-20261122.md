# Tarkova-Cola 4.0 physical body lifetime

Status: focused offline checks and the bounded GPU-2 native scenarios below passed.
Core integration/version/deployment is coordinated separately from this adapter.

## Reviewed source

The Forge audit captured the `4.0.0` client (`com.tarkovacola.server`, assembly
`TarkovaCola.Client`) under ArenaMode's
`artifacts/whitelist-forge-20261121/part1/cola/Client`. Package/runtime identity and
every bound field and method can be checked against the official DLL by the harness.

* `Plugin.cs:318` reads the current world player every frame but initializes Tracker
  only when `_init` is false. Replaying its initialization replays raid-ready logic,
  rewards and all perk resets; it is not an appropriate standalone respawn fix.
* `QuickRevive.cs:187,201,243,279` can apply an outgoing downed timeout to the new
  health controller, force the outgoing pistol, or remove a temporary pistol through
  the old item's physical parent slot. `Reset` also replenishes a once-per-raid revive.
* `MuleKick.cs:130,217,239,281` retains the previous weapon and delayed held-trigger
  state. Its reset also clears per-weapon APEX cooldowns; slot registration is global.
* `ElectricCherry.cs:137` can finish the outgoing reload on the replacement and
  discharge there; `DoubleTap.cs:185` can consume replacement ammo for a pending
  outgoing shot. `Tombstone.cs:144` can finish an outgoing specter effect on the new
  body, while `Reset` would replenish Offering and reset unrelated bot histories.
* `Effects.cs:23` identifies local bodies using `IsYourPlayer`; retained Core bodies
  can share that flag. Physical identity is required for delayed local callbacks.

## Shared Core repair

The optional adapter activates on the 4.0+ family after checking the full reflected
contract. Older 3.x continues on its existing path; later versions warn once rather
than being asserted reviewed. Missing required shapes/hooks refuse Core replacement
before teardown. No global scan or new per-frame reflection is introduced.

Prepare only records Core's exact outgoing world/body and is reversible. The foreign
Tracker is fenced during creation and for an uncommitted failed candidate. The local
predicate rejects only Core-owned retired bodies; ordinary players retain Cola's
original behavior. Its downed-prone guard cannot bind the outgoing downed state to a
candidate while it is being constructed.

After both local-player pointers commit to the exact owned successor, Core clears
physical health/item/controller references and transient actions once, rebinds hands
without synthetic weapon-switch progression, and refreshes physical perk capacity.
The same path handles a rewire that commits before throwing. Cancellation, unrelated
events, duplicated events and subsequent ordinary raids are negative controls.

No physical inventory is edited. In particular, the old temporary pistol is left in
its actual old body/corpse slot and only removed from Cola's cache. Active perks,
the revive/Offering charges, Tombstone cooldown, progress, pending mail/refunds,
MuleKick slot index and per-weapon APEX cooldowns, and bot effect histories remain.
Arena's protected-session reward isolation and its intentional per-life reset remain
Arena's responsibility; this Core adapter never arms or tears down an Arena lease.

## Native scenarios that can falsify the repair

1. With official Cola 4.0 + standalone Redeploy, retain a living old body while
   Quick Revive is downed and its deadline is pending. Replace it, wait beyond the
   deadline, and verify healthy movement, hands, firing and no forced old pistol.
   Check old temporary-pistol inventory remains unchanged; repeat with a queued
   post-revive temporary-pistol cleanup.
2. Replace while holding a third-slot Mule Kick weapon with its safety trigger
   pending. After release, the successor must not fire by itself. Equip/fire/reload
   the third slot again and verify normal functionality. Repeat a real death respawn.
3. Replace during Electric Cherry reload, Double Tap pending ammo consumption and
   Tombstone specter. No delayed old-body effect may be applied to the successor;
   charges, progress and bot effects must remain. Normal new triggers still work.
4. Retain two old local bodies with the same profile and exercise a late old-body
   callback; only the live current body may mutate local perk state. Repeat
   PMC-to-Scav and a second replacement.
5. Run ordinary PMC without a Core replacement, cancel preparation, and start a
   second ordinary raid. Downed, revive, third slot, rewards and charge limits must
   match unmodified Cola. A contract mismatch must refuse before physical teardown.
6. Repeat the central respawn checks in Arena with its protection lease armed; after
   Arena, pending requests and persistent progression must match the entry snapshot.

Offline behavioral and official-metadata checks are documented by
`tools/TarkovaColaBodyChecks/Verify.ps1`; exact execution evidence belongs under
`artifacts/tarkova-cola-body-20261122/`. Native result/hashes must be recorded separately.

## Offline verification (2026-11-22)

Four passes: source/physical-state reconnaissance; production implementation and linked
behavior; adversarial cancellation/world/failed-handoff review; independent review and
final rerun. The independent review found that a missing hook after a committed swap
must keep the other guards installed. The adapter now latches that failure while
retaining remaining fences; the regression and a deliberately broken variant cover it.

Final evidence: `artifacts/tarkova-cola-body-20261122/ef4a831ea3644c5fbb9bafbb3870e3eb/`.
The installed EFT compile passed. Behavioral cases passed 68 assertions across normal,
failed-candidate, missing-contract-hook, failed-commit, post-commit hook-loss and older
family modes. The official 4.0.0 client metadata check passed 88 assertions; eight
deliberate implementation mutants failed their expected assertions. No deploy occurred.

Official client SHA256:
`0642739761E954B7E893D3A84CDE350C8305F8B246DC0D43C16679157B12BA0D`.

## Bounded GPU-2 native result (2026-11-22)

The coordinating test owner exercised candidate A on a disposable `ColaFovTest`
profile, with the official Cola 4.0.0, FOV Fix 5.0.1 and Realism Common Lib 0.1.0.
The frozen candidate archive is ArenaMode's `artifacts/cola-fov-20261122/candidate-a/`;
Core SHA256 is
`1BEBDB16F58B899F89164F20044ED22957BAD72ED575F69984007DFBC7AE792C`.

* Standalone Redeploy, Arena client absent: native death followed by the normal PMC
  respawn near the corpse succeeded; ADS, shooting and walking remained usable.
* A real Quick Revive bottle, an available stim and native lethal chest damage
  produced `Downed=true`. A Core replacement retaining the living outgoing body
  changed the current body's state to `Downed=false`, while `_reviveUsed=true`
  remained consumed. The current body remained alive and could fire the M4 after
  the original deadline. The saved observation is about 35.7 seconds after downing,
  25.7 seconds beyond its 10-second deadline (434.5332 → 460.2658 game time).
* Arena Deathmatch: normal automatic respawn and a retained-live-body replacement
  both succeeded. Common Lib's old nine components were disabled with no old
  delegates, and the new nine were enabled; the FOV owner followed the successor.
* In the protected Arena lease, the native Offering request stayed null and the
  server Offering route returned `no`. A Quick Revive bottle could be consumed.
  The full exit profile exactly matches the actual protected arm checkpoint.
  Three session counters differed only from a later intermediate disk copy:
  SPT's native raid-start handler had reset them after that checkpoint. No field
  was excluded from the accepted full-profile comparison; the Cola sidecar also
  matches entry exactly.

Read-back evidence lives beside the archive under `native/`, notably
`candidate-standalone-downed-swap.json`, `candidate-standalone-after-deadline.json`,
`candidate-arena-live-swap.json`, `candidate-arena-death.json` and the loaded-binary
records. Failure/cancellation/hook-loss paths, the other active perk combinations,
Mule Kick's pending safety trigger and queued temporary-pistol cleanup remain
offline-only coverage; they were not all exercised natively.

GPU-2's pretest own binaries/policy were restored to preserve separate unmerged map
work. Temporary foreign packages, probe and disposable profile sidecars were removed;
original profiles/PFT data and settings passed the final hash comparison. Host sync
resumed. Exact preservation and deployment evidence is in
`ArenaMode/docs/reports/2026-11/COLA-FOV-NATIVE-20261122.md`; no regular deployment,
push or release was performed.
