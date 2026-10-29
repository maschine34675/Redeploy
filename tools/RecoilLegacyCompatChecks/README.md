# Recoil Legacy compatibility checks

Run `pwsh -NoProfile -File tools/RecoilLegacyCompatChecks/Verify.ps1` from the
repository. Pass `-PublishedDll` if the unmodified official 2.0.0 client is not
already in the ignored investigation directory.

The executable links all three production adapter files, the shared
`BodyReplacementContext` and `ReplacementProfileRule`. It supplies minimal Unity, EFT, Core API and Harmony
boundaries plus a dynamically generated foreign
assembly with the reviewed public identity. Calls execute production prefix,
finalizer, lifecycle and cache logic; the boundary does not install Mono
detours or emulate the full game.

The fixture deliberately retains the upstream singleton's whole-GameObject
destruction behavior and previous-controller recoil bug. Positive/negative
cases cover untouched initial loading, adoption of the actual old body (including
the first dead standalone player), canceled preparation, repeated death/live swaps,
unarmed replacement followed by the first weapon draw, pending/retired updates, cache
rollback, partial creation failure, old/new body recovery, ordinary raids,
dead old-body recovery followed by retry, own-Scav replacement (body built with
the replacement profile while the game still holds the old one, commit, chain
back to the PMC profile, failed replacement, unrelated profile body),
unrelated ownership, expired world,
weak late-call tombstones, missed-raid-end world retirement, delayed old-world
cleanup after a new replacement, and admission
contract failure. Cecil separately verifies the published DLL hash, identities
and critical IL assumptions without loading it into the harness.

`Verify.ps1` changes isolated copies of production sources under ignored
artifacts. Eleven mutations must fail specific behavioral assertions, not merely
fail compilation. It finishes by rerunning unmodified production code. No
client build, deployment, config mutation or native game execution occurs.

The default Cecil reference is explicitly `D:/SPT41/BepInEx/core/Mono.Cecil.dll`;
the known published binary fixture is read from the primary ArenaMode investigation
artifacts directory. `-PublishedDll` may select the same official binary elsewhere.
Evidence stays in this harness's ignored `artifacts/` folder. Sources and the
shared construction context are hash-checked for changes during verification.
Each baseline/mutant uses a unique build configuration and bypasses shared build
servers so parallel repository checks cannot substitute another case's executable.
