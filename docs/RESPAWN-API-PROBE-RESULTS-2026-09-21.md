# RespawnApi-Probe — GPU-Ergebnisse vom 21.09.2026

In-Game-Lauf der offenen Probe-Szenarien aus `docs/RESPAWN-API.md` Abschnitt 10 und
`tools/RespawnApiProbe/README.md`. Steuerung über UiBridge plus Windows-MCP auf dem
GPU-Host. Keine Produktionsänderung, kein Neubau, keine Profilbearbeitung.

**Keine vollständige Abnahme der API.** P3, P4, P5 und P6/F3 (auch bei echten
0,50 s) sind belegt. P1 bleibt nur teilweise beantwortet (Core war beim frühen
Awake schon geladen). F7–F9, F12, F13 und `ReplacementFailed` waren nicht Teil
dieser Läufe.

## Ergebnis

| ID | Fall | Ergebnis |
|---|---|---|
| **P1 / F1** | Frühe Bindung | **Teilweise.** `interopBound=True`, aber `coreLoadedBeforeBind=True` (12:00:28). Die Interop-Datei hat Core nicht selbst aus einem noch leeren Prozess geladen. |
| **P2 / F2** | Isolation + Frame-Abstand | **Bestätigt** (Raidstart-Swap und jeder Tod). `Replaced` und `BodyChanged(interop)` im selben Frame, `MainPlayerChanged` einen Frame später, `AlwaysThrow` isoliert, Treiber laufen trotzdem. |
| **P3 / F10** | `RunRespawn` aus `Replaced` | **Bestanden.** `result=refused` / `InvalidOperationException`: *RunRespawn was called from inside a death or respawn event handler; defer it by one frame.* Genau ein `Replaced` für diesen Tod, Kamera auf dem neuen Body. |
| **P4 / F11** | `RunRespawn` während Pipeline | **Bestanden.** `result=refused` / `InvalidOperationException`: *A replacement pipeline is already running.* Core: *Respawn refused before any teardown.* Kein `ItemOwners`-/`RegisterPlayer`-Fehler, kein `EndRaidAsLastResort`. |
| **P5** | Ablehnung beim Raid-Ende | **Bestanden.** Disconnect → `status=Stopping` → `OperationCanceledException`: *The raid is already ending; respawn refused before any teardown.* Danach `RaidEnded`, kein `ReplacementFailed`. |
| **P6 / F3** | Tod nach Raidstart-Swap | **Bestanden** (Nachzug 15:13, Delay **0,50 s**). Arena beansprucht, kein Redeploy-Prompt. `DeathClaimed driver=ArenaMode pending=True` → `Arena death #1 — instant respawn`. Der Morgenlauf (12:13, 1,53 s) bleibt als Vorläufer im Logteil unten. |
| **F4** | Hideout-`GameEnd` | Beim Verlassen **kein** `RaidEnded` und kein `GameEnd`. Beim Betreten sieht der Interop-Tracker ein `BodyChanged reason=raid-start` (Hideout-Spieler). Der `HideoutGame`-Filter hält. |
| **F5** | SAIN nach Swap | Bots nach dem Respawn in `SAIN : Combat Layer` (UiBridge-Botliste). `PlayerComponent` selbst nicht prüfbar. |

Keine `maschine-*`-Errorzeile. Die einzige `[Error : Unity Log]`-Zeile ist das
unverwandte Mikrofon (`Starting microphone failed`, 12:01:23).

## Gebundener Stand

| Komponente | ProductVersion | SHA256 |
|---|---|---|
| Redeploy.Core | 1.0.6+3f2125f | `673D9A106F747E5EE0A70EDE21E9DE9E5AB48E5DDE12E0B2922B2139ECDFE4DD` |
| Redeploy.Client | 0.9.0+3f2125f | `DE1D221367C209F1C5EACA7A4C6FB6FB90C77ECFB2E5D79B7EF6A516269192C9` |
| RespawnApiProbe | 0.9.0+33c79a7 | `FE381E77050713DD34FD1FBA357FEFCCB65139F838F746C594B4F1C9DD73A566` |
| ArenaMode.Client | 0.1.0+be7c49d | `2263FBDDEEF9DFD516B04B04792B764171B4B5B8FD9EDB6AE3970D86C3DC5C6A` |
| UiBridge | 0.5.0+b22395a8 | `9A3EDAEF1C7BB0938C9478A84E532E03764F9DC3EA713A30B33F19B287FC9197` |

Offline vor dem Lauf: `Redeploy.Tests` **171/171**. Die Probe ist älter als Core 1.0.6;
die API-v1-Oberfläche, die sie prüft, ist unverändert.

- Instanz: UiBridge `6941c3bc`.
- Raid: Factory Tag (`factory4_day`), Arena Deathmatch Endless, ~10,5 min,
  0 Kills / 3 Tode.
- Log: `D:\SPT41\BepInEx\LogOutput.log` (Prozessstart 12:00:28 MESZ). Raid-Zeilen
  12:12:30–12:25:02. Hideout-Betreten/Verlassen danach live über UiBridge
  beobachtet; die Hideout-Probezeilen stehen nicht in dem bis 12:25:02
  festgehaltenen Logausschnitt.
- Nach dem Lauf: alle Szenario-Schalter aus. `KillAfterRaidStartSwapDelaySeconds`
  in der cfg wieder **0,5** (gilt nach dem nächsten EFT-Start).

## Ablauf

Reihenfolge P6 → P3 → P4 → P5 → F4 in **einem** Raid, ein Schalter nach dem
anderen. F12 ist IMGUI und für UiBridge unsichtbar; Klicks über Windows-MCP
(Enabled x≈980, Disabled x≈1100).

1. Erster Factory-Versuch ohne Arena-Karte (gewöhnlicher Offline-PMC) — abgebrochen,
   zurück ins Menü, Arena-Karte gewählt, Bestätigung „profile and gear stay untouched“.
2. **P6** um 12:13:17: Raidstart-Swap, Kill nach 1,53 s. Arena-Tod #1.
3. **P3** um 12:16:28: Tod #2, `RunRespawnFromReplaced`.
4. **P4** um 12:21:39: Tod #3 (Give-up nach Teleport), `RunRespawnDuringPipeline`.
5. **P5** um 12:23:52: Escape / Arena-Leave, `RunRespawnDuringStopping`.
6. Hideout betreten und wieder verlassen.

Operatornotizen, die das Ergebnis nicht entwerten:

- Der F12-Slider hat den Kill-Delay von 0,5 auf 1,53 s gezogen, bevor P6
  scharf war. Deshalb die P6-Einschränkung.
- `RunRespawnDuringPipeline` war kurz um 12:03:47 an (Fehlklick auf die
  Enabled-Spalte). Der Prefix prüft den Schalterwert; ohne anstehenden Raid
  ist das inert. Für P4 war der Schalter bewusst an.
- Arena-Give-up über Windows-`Shortcut` Backspace war unzuverlässig
  (`Input.GetKeyDown` / IMGUI). Tode 2 und 3 kamen über späteren Give-up
  bzw. Teleport in den Kampf.

## Logbelege

Zeiten lokale MESZ. Zeilen aus `BepInEx/LogOutput.log`.

### P1 — Awake

```
[12:00:28.677] [probe] evt=Awake frame=0 coreLoadedBeforeBind=True interopBound=True coreLoadedAfterBind=True
```

### P6 / P2 — Raidstart-Swap, Isolation, Tod nach 1,53 s

```
[12:12:49.633] [maschine-ArenaMode] Arena armed for next raid on factory4_day (deathmatch).
[12:13:17.831] [probe] evt=BodyChanged(interop) frame=40888 … reason=swap … current=local#-1423064
[12:13:17.833] [probe] evt=Replaced frame=40888 … reason=swap … mainIsNew=True camera=-1423208
[12:13:17.834] [probe] evt=Scenario … name=kill-after-raid-start-swap action=scheduled … delay=1.53s
[12:13:17.836] [Warning:Redeploy.Core] Replaced subscriber RespawnApiProbe.ProbeRuntime.AlwaysThrow … failed: InvalidOperationException: RespawnApiProbe: this Replaced subscriber always throws (8.5).
[12:13:17.891] [probe] evt=MainPlayerChanged frame=40889 … from=local#-1400136 to=local#-1423064
[12:13:21.700] [probe] evt=Scenario … name=kill-after-raid-start-swap action=kill
[12:13:21.730] [probe] evt=DeathClaimed … driver=ArenaMode pending=True
[12:13:21.731] [maschine-ArenaMode] Arena death #1 — instant respawn.
[12:13:21.739] [probe] evt=Scenario … name=kill-after-raid-start-swap result=killed alive=False
```

`AlwaysThrow` erscheint bei jeder späteren Ersetzung erneut; die Pipeline und
die übrigen Abonnenten laufen jedes Mal weiter.

### P3 — Reentranz aus `Replaced`

```
[12:16:28.730] [probe] evt=DeathClaimed … driver=ArenaMode pending=True
[12:16:28.816] [probe] evt=Replaced frame=50885 … reason=death … new=local#-1718188
[12:16:28.817] [Warning:Redeploy.Core] Respawn refused before any teardown: RunRespawn was called from inside a death or respawn event handler; defer it by one frame.
[12:16:28.817] [probe] evt=Scenario … name=from-replaced-handler result=refused error=InvalidOperationException message="RunRespawn was called from inside a death or respawn event handler; defer it by one frame."
[12:16:28.867] [probe] evt=MainPlayerChanged frame=50886 … to=local#-1718188 camera=-1718364
```

Genau ein `Replaced` für diesen Tod. Die Kamera bleibt `-1718364`.

### P4 — Reentranz während der Pipeline

```
[12:21:39.320] [probe] evt=DeathClaimed … driver=ArenaMode
[12:21:39.321] [maschine-ArenaMode] Arena death #3 — instant respawn.
[12:21:39.380] [probe] evt=Scenario frame=61213 … name=during-pipeline action=RunRespawn … status=Started
[12:21:39.381] [Warning:Redeploy.Core] Respawn refused before any teardown: A replacement pipeline is already running.
[12:21:39.381] [probe] evt=Scenario … name=during-pipeline result=refused error=InvalidOperationException message="A replacement pipeline is already running."
[12:21:39.443] [probe] evt=Replaced frame=61213 … new=local#-1938538
[12:21:39.510] [probe] evt=MainPlayerChanged frame=61214 … to=local#-1938538 camera=-1938682
```

Die README erwartet zusätzlich `action=patched`; dieser Lauf loggte
`action=RunRespawn` aus dem Prefix, dann die Ablehnung. Kein
`ItemOwners`/`RegisterPlayer`/`EndRaidAsLastResort` irgendwo in der Session.

### P5 — Ablehnung beim Stop

```
[12:23:52.587] [maschine-ArenaMode] Arena leave committed through native Stop(Left); protected raid end follows.
[12:23:52.588] [probe] evt=Scenario … name=during-stopping action=RunRespawn … status=Stopping
[12:23:52.589] [Warning:Redeploy.Core] Respawn refused before any teardown: The raid is already ending; respawn refused before any teardown.
[12:23:52.589] [probe] evt=Scenario … name=during-stopping result=refused error=OperationCanceledException message="The raid is already ending; respawn refused before any teardown."
[12:23:53.856] [probe] evt=BodyChanged(interop) … reason=raid-end
[12:23:53.857] [probe] evt=RaidEnded(interop)
[12:23:53.857] [probe] evt=RaidEnded
[12:23:57.461] [probe] evt=World … main=null world=null
```

Kein `ReplacementFailed`. Arena: `0 kills / 3 deaths`, 10,5 min.

### F4 — Hideout

Nach dem Raid: Hideout über das Hauptmenü betreten (UiBridge `screen=Hideout`),
danach wieder verlassen (`MainMenu`). Während des Laufs: beim Betreten
`BodyChanged reason=raid-start` für den Hideout-Spieler; beim Verlassen weder
`RaidEnded` noch `GameEnd`. Das bestätigt den `HideoutGame`-Filter aus S0-7.

## Nachzug F3 bei 0,50 s (21.09.2026, 15:12–15:14)

Derselbe GPU-Host, neuer EFT-Prozess `18450c88` (Start 14:42:15). Core, Probe und
ArenaMode unverändert. UiBridge jetzt **0.6.0+e8e638e2**, SHA256
`3F722D9020C97632122544066009A006B2C56D752C1623CCBC79798CE2807A22` — der
Szenario-Schalter ging über `uibridge_config_set` (kein F12).

- `KillAfterRaidStartSwap` false → true, Delay blieb **0,5**. Die anderen
  Szenarien blieben aus. Nach dem Raid wieder aus.
- Raid: Factory Tag (`factory4_day`), Arena Deathmatch Endless, Kit Reaper,
  0 Kills / 1 Tod, 0,6 min.

```
[15:12:45.764] [maschine-ArenaMode] Arena armed for next raid on factory4_day (deathmatch).
[15:13:01.464] [probe] evt=BodyChanged(interop) frame=99519 … reason=swap … current=local#-1704410
[15:13:01.465] [probe] evt=Replaced frame=99519 … reason=swap … mainIsNew=True camera=-1704554
[15:13:01.466] [probe] evt=Scenario … name=kill-after-raid-start-swap action=scheduled … delay=0.50s
[15:13:01.522] [maschine-ArenaMode] Arena raid-start kit swap complete: Reaper.
[15:13:02.002] [probe] evt=Scenario … name=kill-after-raid-start-swap action=kill
[15:13:02.034] [probe] evt=DeathClaimed … driver=ArenaMode pending=True
[15:13:02.035] [maschine-ArenaMode] Arena death #1 — instant respawn.
[15:13:02.043] [probe] evt=Scenario … name=kill-after-raid-start-swap result=killed alive=False
[15:13:07.279] [probe] evt=Replaced frame=99824 … reason=death … new=local#-1719054
[15:13:38.576] [maschine-ArenaMode] Arena match ended: 0 kills / 1 deaths, score 0, best streak 0, 0.6 min.
```

Kill 0,536 s nach `scheduled` (15:13:01.466 → 15:13:02.002). HUD danach: 1 Death,
Spawn-Protection, kein Redeploy-Prompt. Kein Redeploy-`prompt`/`Respawn chosen`
im Log. Arena ersetzt ~5,2 s später (`reason=death`). Disconnect über das
Pause-Menü; Schalter danach wieder aus.

## Was diese Läufe nicht beantworten

- **F4 Narrate-Szenen** (nur Hideout geprüft).
- **F5 `PlayerComponent`** (nur Bot-Layer beobachtet).
- **F6** KillcamReplay-`StopPlayback` im `Replaced`-Handler (seit 13.09. / 0.9.9
  anderweitig belegt, hier nicht wiederholt).
- **F7** Fika-Testbett.
- **F8** Cooldown-Race nach S0-2.
- **F9** Fail-closed-Build (umbenanntes Patch-Ziel).
- **F12** Pool-Bodies über `UnloadTemporaryPools`.
- **F13** PitFireTeam-Follower nach gewöhnlichem Redeploy-Respawn.
- **`ReplacementFailed`**, CG_Spawn-Fail-open, `MarkProcessed`,
  `AvailabilityRule == false` — weiter nur offline bzw. ungespielt.
