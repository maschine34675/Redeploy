# CorpseRun – vollständiges Code-Review

**Reviewdatum:** 2026-07-29  
**Projekt:** `C:\SPT\Development\CorpseRun`  
**Mod-Version:** 1.0.0  
**Geprüfte Laufzeit:** SPT 4.0.13 / EFT 0.16.9.40087  
**Schwerpunkt:** Profilintegrität, Inventarpersistenz und Raid-Ende

## Gesamturteil

Die frühere PMC-Profilkorruption durch eine veraltete `Inventory.Equipment`-ID ist im normalen PMC-Ablauf überzeugend behoben. Die aktuellen Profile und alle geprüften jüngeren Backups sind strukturell sauber.

CorpseRun ist trotzdem noch nicht freigabereif. Es verbleiben mehrere reproduzierbare Datenverlustpfade und ein ernstes Risiko, dass ein Raid-Ende nicht vollständig serialisiert wird.

### Prioritäten

- **P0:** unmittelbar kritischer Defekt ohne vertretbaren Workaround
- **P1:** Freigabeblocker; Datenverlust, Profilrisiko oder wesentliche Kernfunktion betroffen
- **P2:** ernstes Problem, aber nur unter bestimmten Bedingungen oder mit begrenzterem Schaden
- **P3:** Härtung, Wartbarkeit oder niedriger priorisierte Abweichung

## P1 – Freigabeblocker

### 1. Scav-Respawns zerstören den normalen Loot-Transfer

**Betroffene Stellen:**

- `CorpseRun.Client/Patches/BlockDeathRaidEndPatch.cs:19-60`
- `CorpseRun.Client/Services/RespawnEquipmentHelper.cs:40-55`
- `server-csharp-main/Libraries/SPTarkov.Server.Core/Services/LocationLifecycleService.cs:673-754`
- `server-csharp-main/Libraries/SPTarkov.Server.Core/Controllers/InRaidController.cs:27-43`

CorpseRun interceptet jeden lokalen Player ohne PMC-Prüfung und vergibt auch beim Scav eine neue Equipment-ID.

Vanilla ruft bei einem normalen überlebten Scav-Raid `SetInventory` nicht auf. Der CorpseRun-Serverfix läuft dadurch nicht. Der spätere Scav-Save akzeptiert das Inventar nur, wenn die ursprüngliche und die zurückgesendete Equipment-ID identisch sind. Nach dem Respawn ist diese Bedingung absichtlich nicht mehr erfüllt.

**Folge:** Auf dem Transferbildschirm fehlen neue Quellitems beziehungsweise der Loot-Transfer schlägt fehl. Scav-Runs waren laut `CLAUDE-CODE-HANDOFF.md` ausdrücklich ungetestet.

**Erforderliche Korrektur:** CorpseRun zunächst auf PMC beschränken. Scav-Unterstützung erst aktivieren, wenn ein eigener und getesteter Persistenzpfad für den Scav-Transfer vorhanden ist.

### 2. `AllowSecureContainer=false` löscht den gesamten Secure Container

**Betroffene Stellen:**

- `CorpseRun.Client/Services/RespawnEquipmentHelper.cs:40-55`
- `CorpseRun.Client/Services/RespawnEquipmentHelper.cs:119-124`
- `decompiled/Assembly-CSharp/EFTInventoryClass.cs:168-177`
- `server-csharp-main/Libraries/SPTarkov.Server.Core/Helpers/InRaidHelper.cs:83-109`

Bei deaktivierter Option wird ein neuer leerer Equipment-Root erstellt, der Secure Container aber nicht dorthin übertragen. Er bleibt am alten Corpse-Root.

EFT serialisiert für das Raid-Ende ausschließlich den aktuellen `Inventory.Equipment`-Baum. SPT löscht anschließend den alten serverseitigen Equipment-Baum vollständig und importiert nur den neuen Post-Raid-Baum. Es existiert kein Vanilla-Schutzpfad, der den Secure Container rekonstruiert.

**Folge:** Nach mindestens einem Respawn und anschließendem `SURVIVED` verschwinden der gesamte Secure Container und sein Inhalt dauerhaft aus dem Profil.

Die aktuelle Live-Konfiguration steht auf `AllowSecureContainer=true` und ist daher momentan nicht betroffen.

**Erforderliche Korrektur:** Der permanente Secure Container muss unabhängig von dieser Option im Profil-Root verbleiben. Die Option darf höchstens festlegen, ob Inhalte beim neuen Leben direkt verfügbar sind.

### 3. Versicherungen früherer Leichen gehen verloren

**Betroffene Stellen:**

- `CorpseRun.Client/Services/RespawnEquipmentHelper.cs:147-165`
- `CorpseRun.Client/Services/RespawnEquipmentHelper.cs:179-191`
- `decompiled/Assembly-CSharp/EFT/BaseLocalGame.cs:766-807`

`PreserveInsuredItemsOnCorpse` filtert `Profile.InsuredItems` nur gegen:

1. das aktuelle Profilinventar und
2. den Equipment-Baum der jüngsten Leiche.

Dadurch werden bereits vor dem Tod gedroppte versicherte Gegenstände entfernt. Beim zweiten Respawn werden zusätzlich die Versicherungsreferenzen der ersten Leiche entfernt.

Vanilla kann verlorene versicherte Gegenstände am Raid-Ende nur melden, wenn ihre IDs noch in `Profile.InsuredItems` vorhanden sind.

**Folge:** Betroffene Gegenstände werden nicht an die Versicherung gemeldet und kommen dauerhaft nicht zurück.

Die Live-Einstellung `MaxRespawnsPerRaid=2` macht den Mehrfach-Respawn-Fehler unmittelbar erreichbar.

**Erforderliche Korrektur:** Versicherungs- und Corpse-IDs raidweit akkumulieren oder die Einträge nicht vor dem abschließenden Lost-Insurance-Pfad filtern.

### 4. Der globale Blackscreen-Patch kann das Raid-Ende verhindern

**Betroffene Stellen:**

- `CorpseRun.Client/Patches/BlackScreenPatch.cs:16-37`
- `decompiled/Assembly-CSharp/EFT/BaseLocalGame.cs:844-865`

Der Patch blockiert jeden Aufruf von `PreloaderUI.StartBlackScreenShow`, solange ein Respawn aussteht oder läuft. Er unterscheidet nicht zwischen dem Todes-Fade von CorpseRun und einem echten externen Raid-Ende.

Endet der Raid während des Respawn-Dialogs oder Player-Wechsels durch MIA, manuelles Verlassen oder einen anderen Mod, wird auch der Callback verworfen, der unter anderem folgende Schritte ausführt:

- `OnGameSessionEnd`
- Profilserialisierung
- `LocalRaidEnded`

**Folge:** Das Spiel kann im Stopping-Zustand hängen. Wird es anschließend beendet, bleibt der Raid möglicherweise ungespeichert.

**Erforderliche Korrektur:** Nur den konkreten Todes-Fade kontrollieren. Fremde Endpfade müssen durchgelassen oder der ursprüngliche Callback gespeichert und beim Give-up beziehungsweise externen Stop zuverlässig ausgeführt werden.

### 5. Der Player-Wechsel ist nicht transaktional und wartet unbegrenzt

**Betroffene Stelle:** `CorpseRun.Client/Services/RespawnService.cs:159-266`

Vor der erfolgreichen Erstellung des neuen Players werden bereits:

- der Death-Handler entfernt,
- der alte Owner zerstört,
- der alte Player aus der World abgemeldet,
- das Respawn-Item verbraucht,
- die Profilgesundheit verändert,
- Statistiken gesichert,
- Equipment-Root und Inventarreferenzen verändert.

Anschließend wartet die Coroutine ohne Timeout auf `createTask`.

Bei einem Fault, einem niemals abgeschlossenen Task oder einer späteren Wiring-Exception wird Give-up gegen einen teilweise umgebauten Zustand ausgeführt. Es existiert weder ein vollständiger Snapshot noch ein Rollback.

**Folge:** Inkonsistente Raid-End-Serialisierung, hängender Raid oder Datenverlust im Fehlerpfad.

**Erforderliche Korrektur:** Zweiphasiger Aufbau:

1. Ersatzplayer und neue Zustände vorbereiten, ohne den aktiven Zustand zu verändern.
2. Nach erfolgreicher Vorbereitung atomar umschalten.

Zusätzlich werden ein Timeout, Cancellation und ein vollständiger Rollback für Profil-, Item-, Owner- und Playerzustand benötigt.

### 6. Secure- und Special-Slot-Transfers haben keinen Rollback

**Betroffene Stellen:** `CorpseRun.Client/Services/RespawnEquipmentHelper.cs:81-141`

Die Items werden zuerst aus dem alten Slot entfernt. Erst danach wird versucht, sie in das neue Equipment einzufügen.

Schlägt `AddWithoutRestrictions` fehl, wird lediglich eine Warnung protokolliert. Das Item wird nicht in den alten Slot zurückgelegt und die Gesamtoperation wird nicht abgebrochen.

**Folge:** Das Item besitzt keinen Parent mehr und kann samt Inhalt aus beiden serialisierten Equipment-Bäumen verschwinden.

**Erforderliche Korrektur:** Ziel vorab validieren und bei einem Add-Fehler zwingend in den alten Slot zurückrollen. Alternativ muss die gesamte Equipment-Transaktion abgebrochen werden.

### 7. Fast alle bisherigen Raid-Statistiken werden verworfen

**Betroffene Stellen:**

- `CorpseRun.Client/Services/RespawnStatisticsHelper.cs:8-39`
- `CorpseRun.Client/Patches/RespawnStatisticsPatch.cs:18-32`
- `decompiled/Assembly-CSharp/LocationStatisticsCollectorAbstractClass.cs:75-98`
- `decompiled/Assembly-CSharp/GClass2266.cs:37-67`

CorpseRun sichert und restauriert ausschließlich `EftStats.Victims`.

Beim Initialisieren des neuen Players startet Vanilla eine neue Statistiksession und leert zusätzlich:

- Session-Counter,
- Damage-History,
- Aggressor,
- XP-Werte und Multiplikatoren.

**Folge:** Endscreen-, XP- und potenziell questrelevanter Fortschritt vor dem letzten Respawn fehlen. Bei zwei Respawns zählt weitgehend nur das letzte Leben.

**Erforderliche Korrektur:** Den vollständigen Sessionzustand sichern und sinnvoll zusammenführen oder verhindern, dass für den Ersatzplayer eine neue, leere Statistiksession angelegt wird.

## P2 – Wesentliche weitere Befunde

### 8. Der Server-Reparaturpfad akzeptiert falsche Equipment-Roots

**Betroffene Stelle:** `CorpseRun.Server/Helpers/InventoryEquipmentFixHelper.cs:29-89`

Eine bevorzugte Equipment-ID wird bereits akzeptiert, wenn irgendein Item mit dieser ID existiert. Rootstatus, Parent und Template werden nicht geprüft.

Der letzte Fallback wählt sogar das erste parentlose Item im Profil. In echten Profilen kann dies beispielsweise ein Stash- oder Hideout-Root sein.

**Folge:** Bei einer beschädigten Payload oder einem vorangegangenen Equipment-Aufbaufehler kann die Reparaturlogik selbst eine semantisch korrupte `Inventory.Equipment`-Referenz speichern.

**Erforderliche Korrektur:** Ausschließlich einen parentlosen Root mit Equipment-TPL `55d7217a4bdc2d86028b456d` akzeptieren. Ist kein eindeutiger gültiger Root vorhanden, muss der Save fail-closed abbrechen beziehungsweise zurückgerollt werden.

### 9. Der alte Player wird nicht sauber stillgelegt

**Betroffene Stellen:**

- `CorpseRun.Client/Services/RespawnService.cs:170-249`
- `decompiled/Assembly-CSharp/EFT/Player.cs:7785-7894`

Der alte Player wird aus World und Dictionary entfernt, erhält aber nie einen passenden, respawn-sicheren Abschluss. Das reguläre `OnGameSessionEnd` wird später nur für den neuen Player ausgeführt.

**Folge:** Statistik-, Quest-, Skill- und globale Eventhandler können am alten Player beziehungsweise Corpse hängen bleiben und sich über mehrere Respawns vervielfachen.

Auch `InitSecretExfils(newPlayer)` fügt neue Inventory-Handler hinzu, ohne den vorherigen Player sauber abzumelden.

### 10. `OnLeave` wird doppelt registriert

**Betroffene Stellen:**

- `CorpseRun.Client/Services/RespawnService.cs:241-244`
- `CorpseRun.Client/Services/LocalGameAccessor.cs:103-107`

Die verwendete Vanilla-Factory registriert `OnLeave` bereits. CorpseRun fügt denselben Handler erneut hinzu.

**Folge:** Leave- beziehungsweise Reconnect-Abläufe können zweimal angestoßen werden.

### 11. `OwnerCleanupHelper` erfüllt seine dokumentierte Aufgabe nicht

**Betroffene Stelle:** `CorpseRun.Client/Services/OwnerCleanupHelper.cs:8-27`

Der Kommentar behauptet, nur die Base-Implementierung aufzurufen und `CloseBattleUi` zu vermeiden. `MethodInfo.Invoke` dispatcht hier jedoch virtuell auf `GamePlayerOwner.vmethod_1`, das gerade den unerwünschten UI-Abbau startet.

**Folge:** Verzögerte Race zwischen `CloseBattleUi` und dem späteren UI-Restore.

### 12. `RespawnHealthPercent` ist praktisch wirkungslos

**Betroffene Stelle:** `CorpseRun.Client/Services/RespawnService.cs:307-328`

Zuerst werden die Körperteile auf den konfigurierten Prozentsatz gesetzt. Danach ruft CorpseRun `RestoreFullHealth()` auf, wodurch Vanilla eine vollständige Regeneration startet.

**Folge:** Werte ungleich 100 verhalten sich nicht wie konfiguriert. Bei 0 besteht zusätzlich das Risiko eines nicht lebenden beziehungsweise sofort erneut sterbenden Ersatzplayers.

Die Live-Konfiguration steht auf 100 und verdeckt den Defekt.

### 13. Konkreter Konflikt mit KillcamReplay

Die Live-Konfiguration verwendet:

- CorpseRun-Cooldown: 5 Sekunden
- KillcamReplay: 10 Sekunden Replay plus 5 Sekunden Live-Tail

KillcamReplay kontrolliert die Kamera weiterhin über `Application.onBeforeRender`, während CorpseRun den neuen Player bereits erstellt und die Kamera neu bindet.

**Folge:** Der neue Player kann bereits leben und angreifbar sein, während der Benutzer noch die Killcam sieht. Der Spawn-Schutz wird dabei teilweise oder vollständig verbraucht.

**Erforderliche Korrektur:** Replay bei Beginn des Respawns explizit beenden oder Respawn-Aktionen bis zum vollständigen Replay-Ende sperren.

### 14. FiR-Verhalten muss ausdrücklich entschieden werden

Vanilla ruft beim Erstellen des Ersatzplayers `profile.SetSpawnedInSession(false)` auf. Dadurch verlieren die noch im neuen Profilbaum vorhandenen Secure- und Special-Slot-Items ihren FiR-Status.

Dies kann dem normalen Tod-Verhalten entsprechen und wird daher nicht als eindeutige Profilkorruption bewertet. Das Verhalten muss jedoch dokumentiert und mit Questitems getestet werden.

## P3 – Härtung und Wartbarkeit

### 15. Der `RedeployPostRaidPatch`-Prefix ist auf SPT 4.0.13 wirkungslos

**Betroffene Stelle:** `CorpseRun.Server/Patches/RedeployPostRaidPatch.cs:37-45`

Der Prefix prüft `request.Results.Result == SURVIVED && isDead`.

SPT berechnet `isDead` aus demselben ExitStatus. `SURVIVED && isDead` kann im aktuellen Vanilla-Ablauf nicht auftreten. Der Prefix schützt daher keinen realen Pfad; lediglich der Postfix zur Equipment-Reparatur ist wirksam.

### 16. Der Stop-Patch sitzt am Basistyp

Bei einem direkten `LocalGame.Stop(Killed)` können Bot- und Wave-Systeme im Override bereits gestoppt worden sein, bevor der CorpseRun-Prefix am Basistyp den weiteren Ablauf blockiert.

### 17. Server-Sanitisierung ist unvollständig

`InventoryEquipmentFixHelper` entfernt auf Serverseite nur veraltete `FastPanel`-Referenzen. Weitere Referenzsammlungen werden dem Client überlassen. Der aktuelle Clientzustand ist sauber; als Defense-in-depth sollte der Server dennoch alle persistierten Itemreferenzen validieren.

## Build- und Releasebefunde

### P1 – Jeder normale Build deployt in die Live-Installation

**Betroffene Stellen:**

- `Shared.props:17-25`
- `CorpseRun.Server/CorpseRun.Server.csproj:31-34`

Client und Server werden nach jedem normalen Build ungefragt in die aktive SPT-Installation kopiert. Review-, Debug- und Testbuilds können damit profilverändernden Code aktivieren.

**Erforderliche Korrektur:** Deployment standardmäßig deaktivieren und nur über einen expliziten `DeployToSpt`-Schalter erlauben.

### P1 – Der Client-Buildordner darf nicht veröffentlicht werden

`CorpseRun.Client/bin/Release` enthält 126 Dateien beziehungsweise etwa 37,1 MB, darunter:

- `Assembly-CSharp.dll`,
- Unity-Runtime-DLLs,
- SPT-DLLs,
- weitere Spielbibliotheken.

Ein Release muss ausschließlich folgende Binärdateien stagen:

```text
BepInEx/plugins/maschine-CorpseRun.Client.dll
SPT/user/mods/CorpseRun/maschine-CorpseRun.Server.dll
```

Zusätzlich gehören README und Lizenztext in das Paket.

### Weitere Releaseprobleme

- Keine automatisierten Regressionstests
- Keine README
- Keine LICENSE-, COPYING- oder NOTICE-Datei, obwohl MIT deklariert wird
- Kein kontrolliertes Release-Skript
- Kein Release-ZIP
- `CorpseRun` ist kein Git-Repository
- Keine `.gitignore`, Tags oder belastbare Änderungshistorie
- `.vs`, `bin`, `obj` und `build` liegen im Projektbaum
- BepInEx-Pakete verwenden Floating Versions
- Kein `packages.lock.json`
- Kein `global.json`
- `LangVersion=latest`
- Version und GUID werden an mehreren Stellen dupliziert
- Nullable ist im Client deaktiviert; eine strengere Prüfung erzeugt 51 Warnungen
- Ein frischer Server-Build meldet `CS8625` in `InventoryEquipmentFixHelper.cs`

## Positiv verifiziert

### Historischer PMC-Equipment-Fehler

Vanillas `InRaidHelper.SetInventory`:

1. entfernt den alten Equipment-Baum,
2. importiert den neuen Post-Raid-Baum,
3. aktualisiert aber `serverProfile.Inventory.Equipment` nicht.

Der CorpseRun-Postfix setzt im normalen PMC-Happy-Path unmittelbar danach die neue Root-ID. Dieser Fix ist notwendig und grundsätzlich korrekt.

### Profile und Backups

Geprüft wurden:

- drei aktuelle Profile mit PMC- und Scav-Seite,
- 45 jüngere Backup-Dateien,
- insgesamt 90 PMC-/Scav-Seiten in diesen Backups.

Dabei wurden keine folgenden Schäden gefunden:

- fehlende Equipment-ID,
- Equipment-ID zeigt nicht auf ein Item,
- falsche Equipment-TPL,
- Equipment-Root besitzt einen Parent,
- doppelte Item-IDs,
- fehlende Parent-Items,
- Parent-Zyklen,
- ungültige FastPanel-Referenzen,
- ungültige Favorite-Referenzen.

Das historische Backup

```text
C:\SPT\SPT\user\profiles\backups\6a37b4890e2d34217c3a6f97.pre-equipfix-20260714-213434.json
```

zeigt exakt den früheren Schaden:

- `Inventory.Equipment` verweist auf eine fehlende ID,
- ein anderer gültiger Equipment-Root mit Pockets und Secure Container ist vorhanden.

Damit ist sowohl der alte Fehler als auch die Wirksamkeit des aktuellen normalen PMC-Fixes belegt.

### Build und Live-Installation

- Client und Server bauen erfolgreich.
- Build- und Live-DLLs sind identisch:
  - Client SHA-256: `86C2C631EBDAC6805D5A33450F0060069B7C1271A8111EE03494094B1CA7CB80`
  - Server SHA-256: `F0A95C737E9FFF8C3695F99BADB225FA4D7C4F992029315FF138A35B295AB0E7`
- Nur die beiden erwarteten CorpseRun-DLLs sind installiert.
- Keine veralteten CorpseRun-Doppeldateien gefunden.
- GUID, Namen, Assemblypfade und Version 1.0.0 sind konsistent.
- Die Server-Harmony-Ziele stimmen auf SPT 4.0.13.
- Das aktuelle BepInEx-Log bestätigt Client-Load und erfolgreichen `/corpserun/ping`.
- Keine aktuellen CorpseRun-, `KeyNotFoundException`- oder Profilkorruptionsfehler im Log gefunden.

Das aktuelle Log enthält allerdings keinen vollständigen Death→Respawn→Extract-Lauf. Es beweist Laden und Handshake, nicht die kritischen Runtimepfade.

### Client-Integration

Positiv bewertet wurden:

- Kamera-Neuerstellung entspricht weitgehend dem aktuellen Vanilla-Create-/OnDestroy-Ablauf.
- Visor-, Audio-, NV- und Thermal-Rebind sind berücksichtigt.
- Player-/Game-Event-Wiring entspricht abgesehen von `OnLeave` weitgehend Vanilla.
- DynamicMaps- und Freecam-Anbindungen stimmen mit den installierten Quellen überein und fallen fail-soft aus.
- Die PerfectCulling-Patches von CorpseRun und KillcamReplay koexistieren laut Harmony-Log.
- FastAccess-, Favorite-, Discard- und Checked-Referenzen werden clientseitig grundsätzlich sorgfältig bereinigt.
- `PlayerHealthController.Start()` entspricht dem Vanilla-Spawnpfad und verhindert deaktiviertes Med-/Food-Ticking.
- Fehlt die Server-Mod beim Raidstart, bleibt `RespawnSessionState.IsInRaid=false`; der Todespfad fällt dadurch sicher auf Vanilla zurück.

## Erforderliche Regressionstests

Vor einer Freigabe sollten mindestens folgende Fälle mit einem Wegwerfprofil und JSON-Diff vor/nach dem Raid getestet werden:

1. PMC, ein Respawn, anschließend `SURVIVED`
2. PMC, ein Respawn, anschließend Tod/Give-up
3. PMC, zwei Respawns, Items auf beiden Leichen
4. Versicherte Waffe vor dem ersten Tod droppen
5. Versicherte Ausrüstung auf der ersten und zweiten Leiche
6. Scav, Respawn, Loot aufnehmen, `SURVIVED`, Transferbildschirm
7. MIA während geöffnetem Respawn-Dialog
8. Manuelles Leave während geöffnetem Respawn-Dialog
9. Erzwungener Fehler beim Secure-/Special-Slot-Transfer
10. Fehler und Timeout während `vmethod_3`
11. `AllowSecureContainer=false`
12. `AllowSpecialSlots=false`
13. `RespawnHealthPercent` mit 0, 25, 50 und 100
14. FiR-Questitem im Secure Container
15. KillcamReplay aktiviert
16. Folge-Raid nach ein und zwei Respawns, um statische Handler-Leaks zu erkennen

Nach jedem Test sind mindestens zu validieren:

- Equipment-ID vorhanden
- Equipment-TPL korrekt
- Equipment-Root parentlos
- keine doppelten IDs
- keine fehlenden Parents
- keine Parent-Zyklen
- keine veralteten FastPanel-/Favorite-/Discard-/Checked-Referenzen
- Secure Container und Inhalt vorhanden
- Versicherungs-IDs aller eigenen Leichen vorhanden
- Stats und Quest-Counter korrekt zusammengeführt

## Empfohlene Reihenfolge der Korrekturen

1. Scav-Respawns blockieren.
2. Secure Container unabhängig von der Option persistent erhalten.
3. Versicherungslogik raidweit korrigieren.
4. Blackscreen-Patch auf den konkreten Todespfad begrenzen.
5. Respawn als Transaktion mit Timeout und Rollback umbauen.
6. Itemtransfers atomar machen.
7. Vollständige Statistik- und Questzustände erhalten.
8. Alten Player und sämtliche Handler sauber stilllegen.
9. Server-Reparaturlogik auf den exakten Equipment-Root beschränken.
10. KillcamReplay-Lebenszyklus integrieren.
11. Automatisierte Profil-/Raid-End-Regressionstests hinzufügen.
12. Build und Deployment trennen.
13. Kontrolliertes Release-Staging und Lizenz-/Dokumentationsdateien ergänzen.

## Vorläufig sichere Nutzung

Bis zur Behebung:

- nur PMC verwenden,
- höchstens einen Respawn erlauben,
- `AllowSecureContainer=true` lassen,
- KillcamReplay deaktivieren oder vor dem Respawn vollständig abwarten,
- vor jedem Test ein aktuelles Profilbackup erstellen,
- nicht mit dem einzigen produktiven Profil testen,
- keine öffentliche Freigabe erstellen.

## Reviewzustand

Der Quellcode und die Live-Installation wurden durch das Review nicht verändert. Vorhandene lokale Build-Artefakte wurden für die Verifikation neu erzeugt, sind aber byte-identisch mit den installierten DLLs.
