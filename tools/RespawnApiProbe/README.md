# RespawnApiProbe (Entwicklerwerkzeug, wird nie ausgeliefert)

In-Game-Probe für `RespawnApi` v1 nach `docs/RESPAWN-API.md`, Abschnitt 8.5. Sie beantwortet die
offenen Fragen aus Abschnitt 10 aus dem Log. Ohne eingeschaltete Szenarien beobachtet sie nur.

## Was sie tut

- **Frühe Bindung (Frage 1):** Die GUID `com.maschine.AAA.RespawnApiProbe` sortiert vor beiden
  Treibern, eine Abhängigkeit auf sie gibt es bewusst nicht. Im Awake hängt die Probe zuerst die
  unveränderte Interop-Datei an, bevor irgendein gegen Core kompilierter Code läuft.
- **Direkte Abonnenten:** Alle vier Events von `RespawnApi`, dazu ein zweiter `Replaced`-Abonnent,
  der immer wirft. Core muss diese Exception loggen und trotzdem alle anderen Abonnenten bedienen.
- **Interop-Abonnenten:** `BodyChanged`, `DeathClaimed`, `ReplacementFailed`, `RaidEnded` des
  Trackers, markiert mit `(interop)`.
- **Zustandszeilen:** Wechsel von `MainPlayer` und eine Spur über drei Frames nach jedem `Replaced`
  (Frage 2). `evt=ReplacingStarted`/`evt=ReplacingEnded` erscheinen nur, wenn eine Pipeline über
  eine Frame-Grenze läuft. Eine erfolgreiche Ersetzung tut das nie: Das Spiel erzeugt den neuen
  Spieler mit `async: false` (`LocalPlayer.Create` → `Player.Init`), also läuft `RunRespawn` vom
  Teardown bis `Replaced` in einem einzigen Aufruf. Nur Fehler- und Timeout-Pfade warten.

Jede Zeile beginnt mit `[probe] evt=` und trägt `frame`, `replacing`, `available` und `main`.
Ereigniszeilen tragen zusätzlich `pending` für ihren Body. Bodies erscheinen als `local#<id>`,
`other#<id>`, `destroyed#<id>` oder `null`.

## Installieren und entfernen

Bauen und in `BepInEx/plugins` kopieren. Ein Build ohne den Schalter kopiert nichts, auch Core
nicht (die Referenz baut Core als Library):

```bash
dotnet build tools/RespawnApiProbe -c Release -p:DeployProbe=true
```

Entfernen nach dem Test:

```bash
rm "D:/SPT41/BepInEx/plugins/maschine-RespawnApiProbe.dll" "D:/SPT41/BepInEx/config/com.maschine.AAA.RespawnApiProbe.cfg"
```

Die Schalter stehen in `BepInEx/config/com.maschine.AAA.RespawnApiProbe.cfg`, Abschnitt
`Scenarios`, oder im F12-Menü. Alle stehen ab Werk auf aus. Pro Test nur einen einschalten und
danach wieder aus.

## Testszenarien

Das Log danach mit `[probe]` filtern. Zeilen von Core stehen unter `Redeploy.Core`.

1. **Frühe Bindung, Frage 1.** Nichts einschalten, Spiel starten. Erwartet in der Zeile
   `evt=Awake`: `interopBound=True`. Steht dort `coreLoadedBeforeBind=False`, hat die Interop-Datei
   Core aus dem frühen Awake selbst aufgelöst. `interopBound=False` heißt: Die Bindung gelingt erst
   beim ersten `GameWorld`, dann folgt später eine `interop: bound to RespawnApi v1`-Zeile.
2. **Isolation und Frame-Abstand, Frage 2.** Nichts einschalten. Ein gewöhnlicher Redeploy-Respawn
   und ein Arena-Respawn. Erwartet je Ersetzung:
   - eine Warnung `Replaced subscriber ... AlwaysThrow ... failed` von `Redeploy.Core`, und trotzdem
     die Zeilen `evt=Replaced` und `evt=BodyChanged(interop)` sowie die normalen Zeilen der Treiber;
   - `evt=Replaced` und `evt=BodyChanged(interop)` im selben Frame, `mainIsNew=True` und
     `replacing=True` (die Pipeline hält den Flag bis direkt nach dem Event);
   - `evt=MainPlayerChanged` im selben oder im nächsten Frame, je nachdem, ob der Treiber die
     Pipeline vor oder nach dem `Update` der Probe startet; dazu drei `evt=Trail`-Zeilen mit
     `pipelineRunning=False`. Wechselt `camera` zwischen `evt=Replaced` und der ersten Spurzeile,
     ist die alte Kamera am Ende des Ersetzungs-Frames gestorben.
3. **Reentranz, Frage 10.** `RunRespawnFromReplaced` an. Ein Respawn. Erwartet:
   `name=from-replaced-handler result=refused` mit dem Hinweis auf den Event-Handler, genau ein
   `evt=Replaced` für diesen Tod, die Kamera bleibt auf dem neuen Body.
4. **Ablehnung während einer Pipeline, Frage 11.** `RunRespawnDuringPipeline` an. Die Probe patcht
   dann die Spielererzeugung (`BaseLocalGame.vmethod_3`) und fragt aus der laufenden Pipeline heraus
   nach einem zweiten Respawn. Ein Respawn oder Arena-Rundenrespawn. Erwartet:
   `name=during-pipeline action=patched`, danach je Ersetzung `name=during-pipeline result=refused`
   mit `A replacement pipeline is already running.` und eine Warnung `Respawn refused before any
   teardown` von Core, genau ein `Replaced`, keine `ItemOwners`- oder `RegisterPlayer`-Exception,
   der Spieler steht im neuen Body. Beim Raidstart erzeugt das Spiel den Spieler ebenfalls über
   diese Methode; ohne laufende Pipeline fragt die Probe dort nicht.
   Hinweis: Den Recovery-Pfad aus S0-16 erreicht dieser Schalter nicht. ArenaModes Live-Swap prüft
   eine laufende Pipeline selbst, bevor er Core fragt, und eine erfolgreiche Pipeline endet im
   selben Aufruf. S0-16 bleibt Szenario 9 aus Abschnitt 10 (Fail-closed-Build).
5. **Ablehnung beim Raid-Ende.** `RunRespawnDuringStopping` an. Einen Raid regulär beenden, per
   Extract, MIA oder Aufgeben ohne laufenden Respawn. Erwartet: `name=during-stopping result=refused`
   mit `The raid is already ending`, danach kein Core-Event außer `RaidEnded`. Mit KillcamReplay
   kommt die Zeile erst nach dessen verzögertem `Stop`.
6. **Tod kurz nach dem Raidstart-Swap, Frage 3.** `KillAfterRaidStartSwap` an, Verzögerung 0,5 s.
   Einen Arena-Raid starten.
   - Mit installiertem Redeploy: Wer beansprucht den Tod (`evt=DeathClaimed driver=`)? Erscheint im
     Arena-Raid der Redeploy-Prompt?
   - Ohne Redeploy, nur ArenaMode: Folgt ein Arena-Respawn, ein sauberer Killed-Stop oder hängt
     der Raid?

Szenario 3 bis 5 brauchen mindestens einen registrierten Treiber. Ohne ihn lehnt Core jeden Aufruf
mit `The respawn engine is unavailable` ab, und das ist dann kein Testergebnis.

## Stand 21.09.2026

GPU-Lauf auf Factory Tag (Arena Deathmatch Endless):
`docs/RESPAWN-API-PROBE-RESULTS-2026-09-21.md`.

| Szenario | Ergebnis |
|---|---|
| 1 Frühe Bindung | Teilweise (`interopBound=True`, `coreLoadedBeforeBind=True`) |
| 2 Isolation / Frames | Bestätigt (Swap und Tod) |
| 3 Reentranz aus `Replaced` | Bestanden |
| 4 Ablehnung während Pipeline | Bestanden |
| 5 Ablehnung beim Raid-Ende | Bestanden |
| 6 Tod nach Raidstart-Swap | Arena beansprucht; Nachzug 15:13 mit Delay **0,50 s** |

Nach beiden Läufen stehen alle Schalter auf aus. `KillAfterRaidStartSwapDelaySeconds`
ist 0,5. Szenario 6 ist damit im 0,5-s-Fenster belegt (Arena-Claim, kein
Redeploy-Prompt).
