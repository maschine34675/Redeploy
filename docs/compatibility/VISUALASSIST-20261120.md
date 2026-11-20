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
These do not substitute for Unity gameplay. Native acceptance and exact candidate
identity are recorded separately after the GPU test.
