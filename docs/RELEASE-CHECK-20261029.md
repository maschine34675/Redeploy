# Native release check and logging — 29 October 2026

Client/Server 1.0.2, Core 1.0.17. Nine routine messages now use Debug: prompt and
corpse rendering, chosen spawn coordinates, expected black-screen hold,
auto-stow detail, cooldown arithmetic and known raid-end cancellations. Real
errors, inventory/protection decisions and unclassified external stops remain
visible. Gameplay and the default-off timing collector are unchanged.

The native-assembly client/Core build and all **469 client/Core tests** passed.
The choice-test logger boundary now exposes Debug, matching the real logger.
No server source was changed. Independent review verified the DLL/PDB GUID and
stamp pairs, not merely their filenames.

| Candidate | SHA-256 | Loaded MVID |
|---|---|---|
| Client | `913B17649034D08E994490BFC48D0E92DAA13EF4EBD15B62B1405CF21B7F2B82` | `45a89f55-f5ad-4ee4-ac9f-7ef2ce4e9217` |
| Core | `8E650CF1E621F9678076786A2D980BA8F7E87B18FF38B35B132C4FD60A5ED875` | `a573c750-f1eb-4a4f-9b45-aba0cf609b3b` |

SPT-GPU-2 used a disposable copy of an existing test profile with the current
installed mod stack. The original fixture and ordinary player inventory were
not used for destructive standalone scenarios. With Arena inactive:

- Native death, naked PMC respawn, movement back to the corpse, recovery of the
  exact original M4 item and three real shots (magazine 30 to 27) passed.
- A second death followed by give-up reached the native menu.
- PMC death into the account's own Scav, actual extraction and native Scav
  inventory transfer passed. SELL ALL was refused without changing inventory,
  currency or mail; one normal hideout timestamp advanced.
- One item was transferred, the remaining loot deliberately forfeited through
  the native confirmation. Journal state was committed, extracted and transfer
  complete, with no error and no duplicate item IDs.

SPT-GPU-1 separately exercised Core through all six Arena modes and a final
candidate TDM death/respawn/end. One earlier native BotFirstAid callback exception
at Arena teardown remains an attribution-limited finding; no owned premature
bot disposal was demonstrated. It was not hidden by these logging changes and
did not recur in the final TDM. This does not certify every optional integration.

Raw evidence and restoration manifests are in ArenaMode's ignored
`artifacts/release-sweep-20261029/host2/` and the shared candidate/deployment
records. The full mixed-mod report is in the neighboring ArenaMode checkout at
`docs/RELEASE-SWEEP-20261029.md`.

Cleanup restored GPU-2's 250 config files, preserved the original isolated
fixture, archived/removed only the disposable profile and its own completed
journal/reward artifacts, and retained original profile backups. All three
ordinary PMC/Scav inventories were unchanged; full-profile differences were
limited to native timestamps and a mail attachment already expired before the
test. No profile rollback was performed. Synchronization was restored only after
both hosts had stopped their games/servers/helpers.

After synchronization, eight newly created conflict copies were hash-archived
and removed; the two profile copies differed only in native time fields.
Independent live comparisons confirmed all ordinary inventories, restored host
configs and candidate binaries. No active profile was replaced.
