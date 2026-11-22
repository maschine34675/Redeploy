# Realism CommonLib controller replacement checks

`pwsh -NoProfile -File tools/RealismCommonLibChecks/Verify.ps1 -PublishedDll <official-0.1.0-dll>`

The runner builds an isolated actual-EFT compile target and a .NET Framework executable
with the production adapter, production ownership context, real Harmony and independent
foreign controller fixtures. These projects have no Core project reference or deploy targets.

The fixture exercises ordinary initialization, read-only/cancelled admission, live retained
old bodies, exact candidate and profile ownership, suspended/retired instance callbacks,
old firearm/PWA sources (including ref arguments and bool-prefix fallthrough), precise
event target removal, late destruction, rollback and partial initialization failures,
failed rewire after physical commit, explicit profile replacement and raid retirement.
Nine compiled mutants must fail the corresponding behavior assertions.

The optional official-DLL check uses Cecil to verify plugin metadata, the nine concrete
controller types and cleanup method side effects. It does not instantiate Unity or prove
native playability; the GPU tests with the official package remain a separate gate.
