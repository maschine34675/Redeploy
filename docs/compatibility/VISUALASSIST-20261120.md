# VisualAssist body replacement — Core 1.0.25

Development after Redeploy 1.1.3. Reviewed upstream:
[VisualAssist v1.2.0](https://github.com/SleepingPills/VisualAssist/releases/tag/v1.2.0),
plugin GUID `com.janky.visualassist`, plugin version 1.2.0, assembly version
1.2.0.23951. The generated assembly revision is normalized; identity, signatures
and lifecycle checks remain mandatory. No foreign DLL or implementation is bundled.

VisualAssist creates one GrenadeArc at GameWorld start. Death/disposal destroys
the component but originally leaves its line, sphere and materials behind. Its
`Singleton.Release(oldArc)` also clears the current singleton regardless of the
argument. A replacement therefore loses the preview, and a late old-body disposal
could erase a newer instance.

Core validates the optional contracts before replacement and adopts the exact
body only at its native unregister boundary. If death already destroyed the arc,
read-only capture of its resources allows that later adoption to clean them up.
The confirmed replacement gets its own component and singleton. Old callbacks
can only release resources and the singleton they actually own. Failed/recovered
replacements and own-Scav continuation use the same physical ownership rules.
Frame updates use cached references; no scene scans or global unpatch walks.

Ordinary unadopted bodies retain upstream behavior. The public Respawn API remains
v1, and the engine statement order does not change. Core does not modify grenade
throw calculations; Arena's separate quick-throw protection is Arena-only.

Offline evidence: actual game API compile, real-Harmony lifecycle fixtures,
released-DLL metadata, coordinator/source guards and targeted fault checks.
These do not substitute for Unity gameplay.

## Native standalone verification — 2026-11-20

GPU-2 ran Core 1.0.25 and the matching rebuilt Redeploy.Client from `430657c`,
with Arena Client, Gate and BigBrain companion absent from plugin discovery.
Core SHA256: `F90C3EBBBBC94FC4341F39F6C0B8B7527F669A57F1977F15B341DEE300516609`;
Client SHA256: `D5F9BB7DE8904919D4800A82BB83109B2E102093DCCD4C5C65DBAE0E510B1A76`.
UiBridge verified the loaded/disk identities. The official VisualAssist DLL was
`D0150D2FCC93639933F3075EF80CC303CA3E7E8B6C47539A20488C4FB6AF3EC9`.

A disposable ordinary PMC profile on Factory underwent two actual deaths and
body changes. The new arc and scope owned the exact replacement after each;
the old line and sphere were destroyed. Movement, fresh grenade equipment,
visible high/low previews and OFF/ON worked. OFF disabled both renderers and
cleared prediction; ON rebuilt it. Regular low/high throws completed, with
visible low-flight evidence; the high throw met a nearby wall, so its range
was not tested. Normal exit cleared the scope/body/arc without an error.

All 139 captured original profile/config/PFT/Cola files remained byte-identical.
Only the disposable profile's 14 attributable files and three registry keys
were archived and removed. Arena files were restored to their original candidate
hashes. Evidence is kept locally under ArenaMode's ignored
`artifacts/mod-refresh-20261120/native/visualassist/`.

Own-Scav continuation, every grenade and forced Unity creation failures were not
tested natively. Creation-failure recovery has separate real-Harmony fixtures.
Fast Grenade Throw's quick-throw combination outside Arena is not repaired by
this adapter; Core deliberately does not alter upstream throw physics.

## Arena and regular deployment

The same Core/Client binaries also passed a protected Factory Arena death and
replacement. The new arc belonged to the exact replacement and old renderers
were destroyed. Normal previews/throws and a custom Molotov throw with visible
flight and ignition worked. Arena's separate guard passed actual FGT overhand
input with no previous prediction, after a stale prediction, and after respawn;
this does not extend the standalone Core contract. These checks did not measure
exact trajectory accuracy or cover FGT underhand throws or the Translocator.

The accepted Core/Client DLLs and matching PDBs were then regularly deployed to
the VM and both GPU hosts. The combined delivery's 231 files matched on all three
installations. A fresh GPU-2 MainMenu process `17607ed7` verified Core/Client,
Arena, VisualAssist 1.2.0, PFT 1.1.2 and Tarkova Cola 3.0.1 loaded exactly the
installed files, without plugin-load/dependency errors. The three official mods
remain installed; temporary FGT and its configuration were removed. Final
deployment manifests sit beside the native evidence directory named above.
No new release was created: Redeploy 1.1.3 still publishes Core 1.0.24.
