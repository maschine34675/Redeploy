# Magazine Check Interrupt body compatibility — 2026-10-25

Core 1.0.12 adds an optional integration for
[Magazine Check Interrupt 1.1.1](https://github.com/ozen-m/SPT-MagCheckInterrupt/releases/tag/v1.1.1),
plugin `com.ozen.magcheckinterrupt`. The reviewed DLL SHA-256 is
`CDA9126EFEBF12B3811B0D0167AFCC3179762CD37A22B23869BC127224EDA0B5`;
source revision `e92f868eec7cc75809afb3a9c4ce5ce084ce379f`.

## Reproduced failure

On SPT-GPU-1 with Core 1.0.11, the first magazine check creates a floating panel
under the current player and caches its FPS camera. A real death and Arena
respawn replaces the camera but leaves that process-wide panel on the corpse.
The next check activates it with a destroyed camera, causing repeated
`FloatingPanelController.LateUpdate` null-reference errors. Disabling only
**Follows Magazine** lets the same check-to-reload operation finish.

## Ownership and lifecycle

- Resolve the optional plugin by its actual GUID, assembly identity, release
  version, exact members and installed native check hook. Newer versions need
  matching structure and produce an unverified-version warning.
- Prepare against Core's exact old player and world without changing ordinary
  UI. Adopt the panel only after preflight, then freeze it during replacement.
- After Core publishes the replacement with its new camera, retire only the
  panel whose physical parent was owned by that operation. Clear its singleton
  only if it still points to that same panel; let the mod create fresh UI.
- Keep owned hide completions behind weak, generation-specific tickets. The
  foreign continuation only enqueues its actual completed task; Core drains a
  bounded queue on the game thread. A later show, replacement or world teardown
  invalidates stale completion tickets.
- Never reset process-wide magazine animation flags, reparent borrowed UI, or
  change foreign settings. With the mod absent, no foreign hooks are installed;
  panels belonging to ordinary, unowned bodies keep their upstream behavior.

The fix lives entirely in Redeploy.Core. ArenaMode supplies no duplicate adapter.
The optional mod and its network library are not bundled. Core's API remains 1;
Client/Server release versions remain 1.0.1.

## Verification

Core `1.0.12+1a3eb10` was loaded and hash-verified on GPU1. Protected Factory
Deathmatch covered three physical bodies and two deaths during actual active
magazine checks. Both old panels disappeared; both replacements created visible
panels with their own parent transform and fresh FPS camera. Check→R→real shots
and repeated normal-speed checks passed without raid warnings/errors. All
three upstream reload-skip flags were false. Menu scope was inactive, its
active-panel list empty, and no floating panel remained.

Vanilla GameEnd returns players to the pool before Core's end callback. Its
pool destroys the registered Player component immediately, while child UI can
still briefly exist. Follow-up `5952741` keeps this specific disposed-owner
world-teardown diagnostic at Debug and still refuses destruction. Live-owner
parent mismatches and unrelated cleanup failures retain their warning behavior.

Final native exit retest loaded `1.0.12+5952741`, SHA-256
`1BFB4B129D2E6AD653C056E16D236AB9F808321C7DA0148D201E27EC1E6E889D`.
A real MCX magazine check and normal-speed completion passed, followed by native
raid exit with zero warnings/errors from raid start through MainMenu. The
expected disposed-owner Debug message was recorded; scope and panel counts
returned to zero. The full two-death grid above used the preceding code; this
follow-up changes only the world-teardown diagnostic.

Core/driver Release builds passed without warnings/errors. The complete unit
suite passed **251/251**, including 14 scoped ownership/callback cases. Shared
lifecycle verification passed **19 cases / 412 assertions** and **9/9 fault
mutants**. Native evidence is retained in ArenaMode's ignored
`artifacts/compat-three-input/` directory; profiles and foreign binaries are not
committed. This run validates the shared engine through Arena; it does not
claim a new standalone Redeploy or Fika acceptance run.

The integration does not change UIFixes' custom **Reload in Place** animation
speed. That foreign operation lies outside Arena's native reload-speed hook.
It also does not claim that unrelated mod combinations or all task races have
been established by a bounded native test.
