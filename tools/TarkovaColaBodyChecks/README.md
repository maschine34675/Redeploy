# Tarkova-Cola physical body checks

`Verify.ps1 -PublishedDll <official TarkovaCola.Client.dll>` compiles the production
adapter against the installed EFT API without deploying, then links the same sources
and Core's real `BodyReplacementContext` into a .NET Framework behavioral harness.
It patches independent foreign-mod doubles with the installed Harmony runtime.

The scenarios cover ordinary/no-mod/3.x negative controls, read-only preparation and
cancellation, retained old bodies, early construction, successful and duplicate events,
failed candidate creation, partially committed rewire, changed replacement profile,
world retirement, and preservation of perks/charges/progress/pending requests/inventory.
Eight deliberate broken variants must fail the expected behavioral assertion. The
optional official-DLL pass compares every reflected field/method and BepInPlugin
identity using Cecil, rather than confusing an archive version with the runtime version.

These are offline checks. They do not establish in-game acceptance, perk animation
parity, or third-party server persistence. See the dated compatibility record for native
scenarios and known boundaries.
