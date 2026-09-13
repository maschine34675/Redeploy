# RespawnApi v1 für Redeploy.Core: finaler Vorschlag (revidiert)

> **Status (12.09.2026, Entwicklerdokument, deutsch):** Entwurf vom 11.09.2026 aus drei
> konkurrierenden Designs, drei Bewertungen und drei Widerlegungsprüfungen. Alle Zeilenangaben
> beziehen sich auf den Stand **vor** Stufe 0: Redeploy `bc95a28`, ArenaMode `0add445`.
>
> **Umsetzungsstand (12.09.2026):** Stufe 0 ist umgesetzt — Redeploy-Seite auf `main`
> (`c938f5c`: S0-1 Core-Anteil, S0-2 bis S0-8, S0-10 bis S0-14; Namen weichen teils vom Entwurf
> ab: `DeathInterception.IsAvailable` statt `RespawnApi.IsAvailable`, weil die Fassade noch fehlt,
> und der Claim-Zustand aus 3.2 existiert noch nicht), ArenaMode-Seite auf
> `feature/arena-air-control` (`668619f` S0-16, `bb82ce8` S0-15, `3a5123c` S0-1). **Dieser Core
> setzt ArenaMode ≥ `668619f` voraus** (Begründung in S0-16). Offen: S0-9 (In-Game-Repro),
> Stufe 1 (`RespawnApi`-Fassade und Interop-Datei), Stufe 2. In-Game noch unverifiziert:
> Szenarien 8 bis 11 in Abschnitt 10.
> **GPU r1 (12.09.2026, ArenaMode `GPU-RESPAWN-STAGE0-TEST-RESULTS-r1.md`):** gewöhnlicher
> Redeploy-Respawn auf dem deployten Stand bestanden (Markerfolge, Killcam-/AutoIFF-/Freecam-Rebind
> ohne Exception); Lauf danach durch ein Vanilla-Race gestoppt (`BotSurgicalKit`-Callback eines
> beim `Stop` bereits disposten Bots, kein Core-Frame). Korrektur zur Erwartung: der Marker
> `Blocked raid-end black screen while respawn is pending.` erscheint im Normalpfad nie, weil
> `CG_Spawn` vor jedem `Stop` abgefangen wird. Fortsetzung: `…-INSTRUCTIONS-r2.md`.
> **r2/r3 und User-Playtest (13.09.2026):** r2 (`…-RESULTS-r2.md`) und r3 (`…-RESULTS-r3.md`)
> stoppten erneut nur auf Fremdbefunden (dieselbe Vanilla-Race-Familie beim `Stop`, nun auch als
> `BotFirstAid`-Callback; SAIN lehnt das von SPT gewürfelte `bossKilla`-Gehirn simulierter
> Player-Scavs ab). Der User-Playtest schloss die Liste: mehrfacher gewöhnlicher Respawn
> (0/3 → 1/3, Give-up bei 2/3 endet als Killed), Reset an der Raid-Grenze, Arena-Startkitwechsel,
> natürlicher Tod, Give-up, Rundenrespawn und TDM ohne verbotenen Marker; Bots kämpfen nach dem
> Live-Swap normal (Frage 5 praktisch beantwortet, `PlayerComponent`-Detail offen). Frage 8
> (Cooldown-Race) ist nativ nicht auslösbar (kein Exit im toten Zustand); 9 bis 11 brauchen
> erzwungene Bedingungen und bleiben offen. Weiter offen: S0-9-Detail, Stufe 1, Stufe 2.
> **Stufe 1 umgesetzt (13.09.2026):** `Redeploy.Core/Api/RespawnApi.cs` wörtlich nach 3.1
> (`IsAvailable` delegiert an `DeathInterception.IsAvailable`, gleiche Operandenreihenfolge);
> Claim-Zustand in `Engine/RespawnClaims.cs`, die Entscheidung als reine Funktion
> `Engine/RespawnClaimLogic.Decide` (Wahrheitstabelle aus 8.1 in `Redeploy.Tests`); Verdrahtung
> nach 3.2 in `RunRespawn` (Grund aus `IsAlive` beim Eingang, `ReplacementFailed` je Ausgang mit
> Stufe, `Replaced` vor dem Legacy-`OnPlayerRespawned`, genau ein öffentliches Ergebnis je
> Pipeline, Unterdrückung nach `RaidEnded` desselben Spiels), im CG_Spawn-Prefix
> (`MarkProcessed` für unbeanspruchte Tode, `BeginClaim` → `OnDeathIntercepted` → `DeathClaimed`
> → Treiber → `EndDispatch`) und in `RaidLifecycle` (öffentliches `RaidEnded` nur für `LocalGame`,
> nach Treiber-Reset, Core-Reset und Legacy-Event). Interop-Masterkopie `interop/RedeployRespawnInterop.cs`
> (4.1 wörtlich). Vertragstests nach 8.2 in `Redeploy.Tests/ApiContractTests.cs`: Baseline
> `Redeploy.Core/Api/api-v1.baseline.txt` gegen den gebauten Core (MetadataLoadContext),
> Drift-Check der Interop-Literale samt Handler-Signaturen, MemberRef-/TypeRef-Auflösung der
> installierten `ArenaMode.Client`/`Redeploy.Client` gegen den neuen Core und der
> InterfaceImpl-Check (jede von einem Konsumenten implementierte Core-Schnittstelle hat exakt ihre
> Baseline-Member). Die Operandenreihenfolge von `IsAvailable` (S0-12) ist als reine Regel
> `Engine/AvailabilityRule` getestet. 8.3 als `tools/RespawnInteropChecks` (47 Prüfungen, die
> unveränderte Interop-Datei gegen Stand-ins und die wörtliche Mock-Fassade, einschließlich der
> Laufzeit-Bindung, Teilbindung und Abmeldung aus 8.2). Noch nicht umgesetzt: 8.4
> (Golden-Order-Guard), 8.5 (In-Game-Probe), der Bindungstest gegen den echten gebauten Core, die
> Treiber-Versionsprüfung aus 3.9 und die Punkte 4 bis 6 der ArenaMode-Migration (Abschnitt 5).
> Kein Konsument nutzt bisher ein neues Member.

Grundlage sind drei Entwürfe (minimal, lifecycle, consumer) und drei Bewertungen. Jede strittige Stelle habe ich im Code gegengeprüft: Redeploy 0.9.0, ArenaMode 0.1.0, das EFT-Dekompilat unter `_decompiled` und die Fremd-Referenzen unter `_reference`. Diese Fassung ist danach gegen drei Verifikationen revidiert (Fakten, Garantien, Bindung). Das Protokoll der Korrekturen steht am Ende. Alle Pfade sind relativ zu `D:\SPT41\Development`, alle Zeilenangaben beziehen sich auf den heutigen Stand.

---

## 1. Kurzfazit

Eine API lohnt sich, solange sie klein bleibt. Redeploy und ArenaMode sind beide unveröffentlicht. Die drei Respawn- und Todes-Events von Core (`RespawnEngine.OnPlayerRespawned`, `RespawnEngine.OnSpawnProtectionEnded`, `DeathInterception.OnDeathIntercepted`) haben keinen einzigen Abonnenten. Bei `RaidLifecycle.OnRaidStarted`/`OnRaidEnded` ist das anders: ArenaMode abonniert sie an sieben Stellen (`ArenaSession.cs:93-94`, `ArenaMatchService.cs:38-39`, `ArenaExtractionList.cs:31-32`, `ArenaMedicalPatches.cs:47-48`, `ArenaSpectatorService.cs:187-188`, `ArenaHandlingPatches.cs:48`, `ArenaDoubleJump.cs:57`), und die Reihenfolge dieser Abonnenten muss erhalten bleiben (S0-7). Die Respawn-Oberfläche lässt sich also jetzt noch ohne Folgekosten festlegen. Die Lifecycle-Events behalten Signatur und Verhalten.

Polling leistet schon mehr, als die Umfragen angenommen haben. Der Ersatz-Body wird synchron erzeugt: `GameWorld.MainPlayer` wechselt innerhalb von `vmethod_3`, und der komplette Rewire samt `RaiseRespawned` folgt im selben Frame. Auf dem Erfolgspfad sieht ein Poll mit `ReferenceEquals` auf `MainPlayer` deshalb jeden Body-Wechsel genau und nie halb verdrahtet. Scheitert die Ersetzung in den Stufen `create`, `raid-ending` oder `rewire`, zeigt `MainPlayer` allerdings schon auf einen Waisen-Body oder einen halb verdrahteten Body, und genau den sieht dann auch der Poller (3.5). Ob Tod oder Swap, lässt sich aus `IsAlive` des alten Bodys ablesen.

Was Polling nicht kann:
- zur `Player.OnDead`-Zeit wissen, ob Core den Tod beansprucht und den Raid offen hält;
- im Wechsel-Frame reagieren, solange die alte Kamera noch existiert;
- einen Fehlschlag erkennen;
- ohne eigenen Patch ein exaktes Raid-Ende liefern;
- robust bleiben, falls die Erzeugung je asynchron wird.

**Kernform:** eine eingefrorene Reflection-Fassade `Redeploy.Core.Api.RespawnApi` mit
- vier Events: `DeathClaimed`, `Replaced`, `ReplacementFailed`, `RaidEnded`;
- drei Abfragen: `IsAvailable`, `IsReplacing`, `IsRespawnPending(Player)`;
- einer Versionsnummer `ApiVersion`.

Alle Events werden ausschließlich an Stellen ausgelöst, die es heute schon gibt. Die Rewire-Reihenfolge in `RespawnEngine.cs:102-238` bleibt unangetastet.

Die Bequemlichkeit liegt auf der Konsumentenseite, in einer Datei, die Redeploy pflegt und die andere Mods kopieren (`LocalBodyTracker`). Sie führt die Core-Events und den Poll zu einem einzigen, duplikatfreien Strom `BodyChanged(previous, current, reason)` zusammen. Derselbe Konsumentencode läuft mit neuem Core, ohne Core, unter Fika und neben Respawn-Mods, die Core nicht nutzen. Nur der Zeitpunkt unterscheidet sich: Mit Core kommt `BodyChanged` innerhalb der Pipeline, ohne Core aus `Update` (4.1).

Der größere Nutzen steckt allerdings in Stufe 0. Diese Fixes sind unabhängig von jeder API fällig:
- Isolation pro Abonnent;
- das Cooldown-Race;
- ein Preflight vor dem Teardown;
- Fail-closed, wenn Cores Patches unvollständig sind: Core bleibt dann komplett untätig;
- ein Owner-Guard, der an die konkrete Owner-Instanz gebunden ist;
- ein gepaartes, geordnetes Raid-Ende ohne Narrate-Starts;
- eine ArenaMode-Recovery, die eine abgelehnte Anfrage verträgt.

Aufwand bis Redeploy 1.0: etwa 6 bis 7 Personentage, In-Game-Verifikation eingeschlossen.

---

## 2. Stufe 0: Bugs, die unabhängig von jeder API behoben werden

Keiner dieser Fixes verschiebt eine Anweisung im Rewire (`RespawnEngine.cs:105-236`). P0 und P1 blockieren das Release.

**S0-1 (P0): Isolation pro Abonnent bei jedem Raise**
- *Fix:* Ein reiner `SignalDispatcher` für alle Raises. Er
  - macht einen Snapshot von `GetInvocationList`,
  - fängt Fehler pro Handler einzeln ab,
  - loggt den deklarierenden Typ und die Assembly des Handlers,
  - überspringt Handler, deren `Target` ein zerstörtes `UnityEngine.Object` ist,
  - führt einen Tiefenzähler `Depth`.

  Core betreibt zwei Instanzen:
  - `CoreSignals.Respawn` für Todes- und Pipeline-Raises: `OnDeathIntercepted`, `DeathClaimed`, `Replaced`, `OnPlayerRespawned`, `ReplacementFailed`, `OnSpawnProtectionEnded`. Nur das `Depth` dieser Instanz prüft der Preflight (S0-3).
  - `CoreSignals.Lifecycle` für Raid-Start und Raid-Ende. Diese Instanz zählt nicht für die Reentranz-Regel.

  Dasselbe Muster bekommen in ArenaMode `ArenaEvents.Raise` und `ArenaMatch.ReportLocalRespawn`.
- *Evidenz:*
  - `RespawnEngine.cs:513-535` und `DeathInterception.cs:160-167` legen ein einziges `try` um den ganzen Multicast. `ArenaEvents.cs:31-41` macht es genauso.
  - `ArenaMatch.cs:111` ruft `Scoreboard.ReportLocalRespawn` außerhalb von `RaiseSafe` auf.
  - Das richtige Muster steht schon in `RaidLifecycle.cs:32-50/62-77`.
- *Relevanz:* Der erste werfende Abonnent bringt alle späteren zum Schweigen. `ArenaMatchService` abonniert zuerst (`ArenaMatchService.cs:41` über `Plugin.cs:43`), danach `ArenaSpectatorService` (`Plugin.cs:44`, Handler in `ArenaSpectatorService.cs:969`). Wirft `ArenaMatchService`, läuft der Spectator-Handler nie. Das muss behoben sein, bevor irgendetwas beworben wird.

**S0-2 (P0): Cooldown-Race in Redeploy**
- *Fix:* In `RespawnService.RespawnCoroutine` direkt nach `WaitForSeconds` abbrechen (`yield break`), wenn eine der Bedingungen zutrifft:
  - `!ReferenceEquals(RespawnSessionState.CurrentLocalGame, game)`
  - `game.Status != GameStatus.Started`
  - `!RespawnSessionState.IsInRaid`
  - `RespawnSessionState.HasGivenUp`

  `IsRespawnInProgress` beim Abbruch **nicht** zurücksetzen, das erledigt das Reset bei `GameEnd`. So bleibt `ShouldGuardOwnerCleanup` während des Teardowns scharf, wie `RedeployDriver.cs:80-84` es beabsichtigt. Im Fehlerzweig `GiveUpRaid()` nur aufrufen, wenn die Exception keine `OperationCanceledException` ist und `Status == Started` gilt.
- *Evidenz:*
  - `RespawnService.cs:145-171` prüft nach dem Cooldown (Standard 5 s, `Settings.cs:50-54`) nichts mehr.
  - `RedeployDriver.cs:51-60` setzt `HasGivenUp`, aber `RespawnSessionState.cs:53-67` löscht es bei `GameEnd` wieder. `HasGivenUp` allein genügt deshalb nicht.
  - Im Fehlerfall ruft Redeploy `GiveUpRaid` → `InitiateGameStopping` → `BattleUiPanelDeath(Killed)` + `Stop(Killed)` (`_decompiled/EFT/BaseLocalGame.cs:844-848`).
  - Die Engine verändert in `:105-116` den Zustand, bevor bei `:182` zum einzigen Mal der Raid-Status geprüft wird.
- *Relevanz:* Wer während des Cooldowns per MIA, Extract oder Menü den Raid verlässt, bekommt trotzdem Teardown und Profiländerung, danach bricht die Engine bei `:182` ab. Wie es weitergeht, hängt davon ab, wann der Cooldown endet:
  - **Zwischen `Stop` und `GameEnd`:** Es bleibt ein während `Stopping` registrierter Waisen-Body, auf den `MainPlayer` zeigt. `GiveUpRaid` legt ein Killed-Panel über den eigentlichen Exit-Status.
  - **Nach `GameEnd`:** Das Reset hat `CurrentLocalGame` gelöscht. `GiveUpRaid` kehrt deshalb vor `InvokeDeathScreen` zurück (`RespawnService.cs:120-122`), und es erscheint kein Panel. Die Pipeline läuft dann aber gegen ein `Stopped`-Spiel, dessen `_players` `CleanUp` bereits entsorgt oder gepoolt hat (`BaseLocalGame.cs:1064`).

  In beiden Fällen sieht jeder Poller in diesem Moment Müll.

**S0-3 (P0): Preflight am Eingang von `RunRespawn`**
- *Fix:* Vor `:92` eine Prüfung, die nichts anfasst und bei Ablehnung synchron `onFailure` aufruft. Abgelehnt werden:

  | Grund | Exception |
  |---|---|
  | `request` oder `Game` ist null, `SpawnSelector` ist null | `ArgumentException` |
  | `!RespawnApi.IsAvailable` (unvollständige Patches, kein Treiber, Fika; S0-4) | `InvalidOperationException` |
  | `RaidIsTearingDown(game)` | `OperationCanceledException` |
  | Aufruf während eines Todes- oder Pipeline-Raises (`CoreSignals.Respawn.Depth > 0`) | `InvalidOperationException` |
  | `IsPipelineRunning` ist gesetzt | `InvalidOperationException` |

  Die `IsPipelineRunning`-Regel ist nur zusammen mit dem Reset aus S0-6 zulässig, damit ein hängendes Flag höchstens bis zur nächsten Raid-Grenze blockiert. Bei Ablehnung gibt es kein öffentliches Event. Danach `LastRespawnedPlayer = null`.
- *Evidenz:*
  - `:92` setzt das Flag bedingungslos.
  - Ein null-`request` wirft bei `:94` außerhalb jedes `try`. Die Coroutine bricht mit Fehler ab, und `IsPipelineRunning` bleibt true.
  - Ein null-`SpawnSelector` wirft erst bei `:118`, also nach dem Teardown in `:105-116`.
  - Ein null-`Game` fällt **nicht** schon bei `:102/:103` auf. Alle vier Aufrufer setzen `DeadPlayer` (`RespawnService.cs:156`, `ArenaRespawnDriver.cs:189/:275`, `LiveKitRespawn.cs:235`), und `LocalGameAccessor.GetPlayerOwner(null)` geht über Harmony-`Traverse` und liefert null, ohne zu werfen (`LocalGameAccessor.cs:22-25`). Die Folge:
    - `:105` erzeugt einen Delegate mit null-Target, sofern `CreateDelegate` dort nicht schon wirft.
    - `:110` meldet den Body ab.
    - `:112` ruft `OnBeforeProfileMutation` auf; Redeploy verbraucht dort das Respawn-Item.
    - Sicher wirft erst `:114` (`game.Profile`).
  - Ein Raid, der schon endet, durchläuft den Teardown und bricht erst bei `:182-187` ab, mit Waisen-Body.
  - Ein verschachteltes `RunRespawn` aus einem Handler führt dazu, dass `FinalizeRespawnView` bei N+2 (`:288-301`) die Kamera auf den Zwischen-Body zurückbindet (Kritiker, Lücke 3).
- *Relevanz:* Die Reentranz-Regel der API ist damit definiert. Der Waisen-Pfad `:182-187` wird unerreichbar, solange zwei Bedingungen gelten:
  - Die Erzeugung ist synchron.
  - Kein Code im synchronen Abschnitt `:105-146` ruft selbst `Stop` auf. Dort laufen die Anforderer-Callbacks `OnBeforeProfileMutation`, `EquipmentStrategy` und `SpawnSelector`, außerdem `OnPersonAdd`-Abonnenten und `Player.Init`-Patches (`GameWorld.cs:2057`).

  Die neuen Ablehnungen setzen voraus, dass jeder Anforderer ein `onFailure` ohne vorherigen Teardown verträgt. ArenaModes Live-Swap tut das heute nicht (S0-16).

**S0-4 (P1): `DeathInterception.EnsurePatched` isoliert Fehler und schließt im Zweifel ab**
- *Fix:*
  - Jedes `Enable()` bekommt ein eigenes `try/catch`.
  - `IsOperational` ist nur true, wenn alle Keep-alive-Patches installiert sind: CgSpawn, InitiateGameStopping, Stop, BlackScreenGuard, SessionResultGuard, OwnerCleanupGuard, GameEnd.
  - StatisticsRestore und DeadBodyGuard bleiben optional.
  - Ist `RespawnApi.IsAvailable` false, bleibt Core **komplett untätig**:
    - der CG_Spawn-Prefix beansprucht nichts und gibt `true` zurück;
    - InitiateGameStopping-, Black-Screen-, Session-Result- und Owner-Cleanup-Prefix blockieren nichts;
    - der Stop-Prefix blockiert keinen Killed-Stop mehr (`OnExternalStop` darf weiter gemeldet werden);
    - der Preflight lehnt `RunRespawn` ab (S0-3).

    Die Postfixe an den Raid-Grenzen laufen weiter, weil die Legacy-Events gepaart bleiben müssen (S0-7).
  - `RaidLifecycle.EnsurePatched()` behält die Signatur `void`, weil alte Binaries sie referenzieren.
- *Evidenz:*
  - `DeathInterception.cs:90-107` setzt zuerst `_patched = true` und aktiviert dann 8 Patches ohne Schutz. `ModulePatch.Enable` wirft `PatchException`.
  - Nur `DeadBodyGuard.cs:40-49` ist gekapselt.
  - Aufgerufen wird das mitten im Awake: Redeploy `Plugin.cs:30`, ArenaMode `Plugin.cs:52`. In ArenaMode folgen danach noch rund 35 Patch-Sets (`:54-93`).
  - `RunRespawn` hat heute kein Verfügbarkeits-Gate (`RespawnEngine.cs:90-92`), und die installierten Guard-Prefixe fragen weiter die Treiber (`DeathInterception.cs:185-260`). ArenaModes Raidstart- und Rundenpausen-Swaps brauchen keinen Claim. Sie liefen also auch in einem halb installierten Hub, eventuell ohne OwnerCleanupGuard. Arenas `ShouldBlockDeathScreen`/`ShouldBlockKilledStop` (`ArenaRespawnDriver.cs:385-389`) könnten einen angeblich vanilla laufenden Tod trotzdem blockieren.
- *Relevanz:* Speist `RespawnApi.IsAvailable`, und `IsAvailable == false` bedeutet damit wirklich "Core ist untätig". Nach einem Spiel-Update beansprucht ein halb installierter Hub keinen Tod mehr, und das Awake von ArenaMode läuft durch.

**S0-5 (P1): Owner-Cleanup-Guard an die Instanz binden**
- *Fix:* `OwnerCleanupHelper` markiert den alten Owner vor `Object.Destroy` (`OwnerCleanupHelper.cs:39`) in einer `ConditionalWeakTable<GamePlayerOwner, object>`. `OwnerCleanupGuardPatch` (`DeathInterception.cs:334`) überspringt markierte Instanzen zusätzlich zu den bestehenden Bedingungen.
- *Evidenz:*
  - `RespawnEngine.cs:259` löscht `IsPipelineRunning` noch in Frame N. Das `Destroy` wird aber erst am Ende von N wirksam.
  - Danach hängt der Guard nur noch an den Treiber-Flags (`RedeployDriver.cs:83-84`, `ArenaRespawnDriver.cs:409`). Die überleben zufällig bis zur Fortsetzung des Aufrufers, mindestens bis N+1 (Kritiker, Lücke 2).
  - Vanilla setzt `MyPlayer` auf null (`_decompiled/EFT/GamePlayerOwner.cs:411-416`).
- *Relevanz:* Jeder Anforderer ohne ein solches Flag bekommt zwischen N und N+2 `MyPlayer == null`. Das ist Voraussetzung für jede spätere Lease-Freigabe in Frame N (Stufe 2).

**S0-6 (P1): Pro-Raid-Zustand an beiden Raid-Grenzen zurücksetzen**
- *Fix:*
  - Im `GameWorld.OnGameStarted`-Postfix und im `GameEnd`-Postfix werden zurückgesetzt: `IsPipelineRunning`, `LastRespawnedPlayer` und der Claim-Zustand aus 3.2.
  - Zusätzlich `LastRespawnedPlayer = null` am Eingang von `RunRespawn`, direkt nach dem Preflight.
  - Der `GameEnd`-Postfix merkt sich außerdem das beendete Spiel (`RespawnClaims.RaidEndedFor(game)`). Für dieses Spiel veröffentlicht Core keine Pipeline-Events mehr (3.5).
- *Evidenz:*
  - `LastRespawnedPlayer` wird nur bei `:238` gesetzt und nie gelöscht.
  - `IsPipelineRunning` hat in `:92-259` kein `finally`, schaltet aber `DeathInterception.cs:334` (Owner-Cleanup überspringen) und `:359` (Victims wiederherstellen) scharf.
  - Keine EFT-Spielklasse ruft `StopAllCoroutines` auf, und das `LocalGame` lebt nach `GameEnd` noch Backend-Await plus Verzögerung weiter (5 s bei Killed, `BaseLocalGame.cs:844-848, 1089-1098`). Anforderer-Coroutinen laufen also über das Raid-Ende hinaus.
- *Relevanz:* Ohne Reset leben Bodies über Raids hinweg weiter, und ein hängendes Flag bleibt in allen folgenden Raids aktiv.

**S0-7 (P1): Ein einziger, geordneter GameEnd-Postfix in `RaidLifecycle`, gepaart mit einem gefilterten Start**
- *Fix:*
  - Den GameEnd-Postfix aus `DeathInterception.cs:291-315` nach `RaidLifecycle` verschieben.
  - `RaidLifecycle.EnsurePatched()` installiert Start und Ende. `DeathInterception.EnsurePatched()` ruft es auf.
  - Reihenfolge im einen GameEnd-Postfix:
    1. `__instance is HideoutGame` → sofort zurück (unbedingt)
    2. `OnRaidEnded` der Treiber (wie heute zuerst)
    3. Core-Reset
    4. Legacy `RaidLifecycle.OnRaidEnded`, **sonst ungefiltert wie heute**, also auch für Fikas `CoopGame`
    5. `RespawnApi.RaidEnded`, **nur für `LocalGame`**, einmal pro Spielinstanz
  - Im GameStarted-Postfix: `__instance is NarrateGameWorld` → sofort zurück, weder Core-Reset noch Legacy-Start. Danach kommt zuerst der Core-Reset, dann die Legacy-Handler.
- *Evidenz:*
  - `RaidLifecycle.cs:8-30` löst das Ende nur aus dem DeathInterception-Postfix aus, und der hat keinen `LocalGame`-Filter.
  - Redeploy ruft `RaidLifecycle.EnsurePatched` nie auf (`Redeploy.Client/Plugin.cs:23-37`).
  - ArenaMode schaltet `RaidLifecycle` scharf (`Plugin.cs:39` → `ArenaSession.cs:92`), bevor es DeathInterception aufruft (`Plugin.cs:52`). Zwei getrennte Postfixe würden `ArenaSession.OnRaidEnded` vor `ArenaRespawnDriver.ResetSession` ausführen.
  - Fika ruft `gameWorld.OnGameStarted` auf (`TarkovApplication_LocalGameCreator_Patch.cs:226`). Würde nur das Legacy-Ende gefiltert, liefe unter Fika der Start ohne passendes Ende: `_startedWorld` bliebe gesetzt, und jeder Folge-Raid liefe durch den Missed-GameEnd-Zweig (`ArenaSession.cs:128-160`). Die Fika-Ablehnung (`ArenaSession.cs:206-213`) braucht außerdem den Start.
  - `GameWorld.OnGameStarted` rufen nur `TarkovApplication.cs:586` (Narrate), `:814`, `:2897` (Netzwerkspiel) und `:3018` (`LocalGame`) auf. Die Hideout-Erzeugung ruft es nicht auf. Deshalb ist der `HideoutGame`-Filter am Ende in jedem Fall sicher: Es gibt dort keinen Start, der ungepaart bliebe.
  - Trader-Narrate-Szenen erzeugen eine `NarrateGameWorld` (`: ClientLocalGameWorld`) und ein `NarrateGame : AbstractGame` (`NarrateGame.cs:18`). Sie rufen `OnGameStarted` auf (`TarkovApplication.cs:569-586`), durchlaufen aber nie den GameEnd-Postfix, weil `NarrateGame` kein `BaseLocalGame` ist. Heute setzt `ArenaSession` dabei `_startedWorld` auf die Narrate-Welt, und der nächste Raid läuft durch den Missed-GameEnd-Zweig. Ist ArenaMode armiert, verbraucht die Narrate-Szene `ArmedForNextRaid` und läuft in den Zweig "armed map did not match" (`:215-224`); sonst ruft sie `ConfirmOrdinaryRaidStarted` auf. Das ist aus dem Code abgeleitet und nicht im Spiel beobachtet (offene Frage 4).
- *Relevanz:* Das Raid-Ende hängt nicht mehr davon ab, welcher Treiber welchen Patch scharf geschaltet hat. Start und Ende sind gepaart, und das öffentliche Raid-Ende ist exakt und gefiltert.

**S0-8 (P1): Staleness-Guard in den Finalize-Coroutinen, per Identität**
- *Fix:*
  - `FinalizeRespawnView`: `if (!ReferenceEquals(game.LocalPlayer, player)) yield break;`
  - `EnsureBattleUiReady`: `if (!ReferenceEquals(LocalGameAccessor.GetPlayerOwner(game), owner)) yield break;`

  Ausdrücklich **keine** Generationsnummer, die bei `:233-234` erfasst wird. `StartCoroutine` führt den Iterator synchron bis zum ersten `yield` aus, also vor einem Inkrement bei `:239`. Jede Finalisierung würde sonst abbrechen, und damit gingen `RestoreExtractionTimer`/`ShowBattleUIScreen` (`:278/:285`) sowie `SetMyPlayer` (`:300`) verloren.
- *Evidenz:* `:268-301` prüft nur `RaidIsTearingDown`. `CameraRebindHelper.cs:38-56` zerstört jeden Controller, der nicht dem übergebenen Spieler gehört.
- *Relevanz:* Überlappen sich zwei Ersetzungen, ziehen die Coroutinen sonst Kamera und `MyPlayer` auf den vorherigen Body zurück.

**S0-9 (P1, zuerst verifizieren): Unspawn beim Live-Swap und ProfileId-Registries (SAIN)**
- *Fix:* Keine Änderung in Core. Zuerst ein In-Game-Repro, danach eine eigene ArenaMode-Änderung. Kandidat: den lebenden Body nach `TryDestroy` über SAINs `AddPlayerManual` erneut registrieren. Bis dahin gehört das Verhalten in den `Replaced("swap")`-Vertrag (3.4).
- *Evidenz:*
  - `ArenaRetiredBodyEffects.cs:66` zerstört den lebenden Alt-Body, nachdem sich der neue Body unter derselben ProfileId registriert hat.
  - `Player.Dispose` löst `OnIPlayerDeadOrUnspawn` aus (`_decompiled/EFT/Player.cs:30972-30973`).
  - In `_reference/SAIN-master` reagiert SAIN darauf mit `PlayerSpawnTracker.RemovePerson` → `TryRemove(profileId)` und entsorgt dabei die `PlayerComponent` des neuen Bodys (`.../PlayerSpawnTracker.cs:93-145`).
  - Einschränkung: Dass das deployte SAIN 4.5.1 genauso arbeitet, ist nur über Methodennamen im Binary belegt. Die Auswirkung im Spiel ist nicht beobachtet.
- *Relevanz:* Betroffen ist jede Registry, die beim Unspawn nach ProfileId statt nach Instanz entfernt.

**S0-10 (P2): Redeploy beansprucht nur Tode, die es auch bedient**
- *Fix:* `RedeployDriver.ShouldIntercept` prüft zusätzlich `game.LocalPlayer != null && game.LocalPlayer.IsYourPlayer` und `game.Status == GameStatus.Started`. Jede verbleibende Früh-Rückkehr in einem bereits beanspruchten `OnPlayerDied` wirft stattdessen, damit Core den Tod wieder freigibt.
- *Evidenz:*
  - `RespawnService.cs:40-42` kehrt nach dem Claim still zurück.
  - `RedeployDriver.cs:18-39` spiegelt Hideout (`:35`) sowie Fika und Server-Mod (über `IsInRaid`, `RaidStartedPatch.cs:32-52`) bereits, genau diese eine Prüfung aber nicht.
  - `DeathInterception.cs:169-181` wertet jede normale Rückkehr als erfolgreichen Claim.
  - Folge: Es erscheint kein Prompt, und der Tod beendet den Raid nicht. Nur MIA-Timer oder Menü-Exit beenden ihn noch; bis dahin ist der Raid blockiert. `Stop` ist dann nicht Killed, `PendingRespawnChoice` ist false, und der Black-Screen wird nicht gehalten.
- *Relevanz:* `DeathClaimed` und `IsRespawnPending` dürfen keinen Claim ankündigen, der danach still verfällt. Das Werfen funktioniert auch gegen den heutigen Core, weil der CG_Spawn-Prefix bei einem Wurf fail-open ist (`DeathInterception.cs:169-179`).

**S0-11 (P2): `DeadBodyGuard` nur in `LocalGame`**
- *Evidenz:* `DeadBodyGuard.cs:95-109` ist ungefiltert, beide Awakes installieren ihn. Fikas `CoopGame` ist kein `LocalGame` (`_reference/Fika-Plugin-main/.../CoopGame.cs:53`).
- *Relevanz:* Unter Fika bekommt die eigene Leiche keinen `DeadBodiesController`-Eintrag (abgeleitet, nicht beobachtet). `IsAvailable == false` soll heißen, dass Core untätig ist.

**S0-12 (P2): `FikaCompatCheck` cacht nur eine positive Antwort**
- *Evidenz:* `FikaCompatCheck.cs:13-30` cacht die erste Antwort für immer, auch `false`.
- *Relevanz:* Heute trifft das nur direkte Aufrufe des bereits öffentlichen `FikaCompatCheck.IsFikaLoaded`. Die Fassade wertet `IsOperational && DriverCount > 0` zuerst aus. Vor dem Awake eines Treibers bricht die Auswertung deshalb vorher ab (beide Treiber sortieren nach `com.fika.core`), und `IsRespawnPending` geht durch dasselbe Gate. Der Fix bleibt trotzdem sinnvoll. Die Operandenreihenfolge (Fika-Prüfung zuletzt) wird dokumentiert und getestet (Abschnitt 8).

**S0-13 (P2): Stabile Treiber-Sortierung**
- *Fix:* Nach Priorität absteigend, bei Gleichstand in Registrierungsreihenfolge.
- *Evidenz:* `DeathInterception.cs:81` nutzt `List.Sort`, und das ist nicht stabil.
- *Relevanz:* Der Treibername in `DeathClaimed` muss deterministisch sein.

**S0-14 (P2): Freecam-Patches aus Core aktivieren, gekapselt**
- *Fix:* `FreecamCompatHelper.EnablePatchesIfPresent()` im GameStarted-Reset von Core aufrufen, **in einem eigenen `try/catch`** und unabhängig vom Legacy-Dispatch. Ein Fehler dort darf `OnRaidStarted` nicht verhindern. Die Methode ist bereits idempotent (`FreecamCompatHelper.cs:41-46`).
- *Evidenz:*
  - Heute ruft nur `Redeploy.Client/Patches/RaidStartedPatch.cs:30` sie auf. Die Engine bindet den Freecam-Owner aber für jeden Anforderer neu (`RespawnEngine.cs:223`).
  - `EnablePatchesIfPresent` hat kein `try/catch` (`:41-55`), und `ModulePatch.Enable` wirft eine `PatchException`, wenn sich Freecams Zielmethoden ändern. Ungekapselt würde das den Postfix abbrechen, `ArenaSession.OnRaidStarted` und die übrigen Legacy-Handler liefen nie, und die Exception liefe in den Raid-Start-Delegate von `TarkovApplication` (`TarkovApplication.cs:3018`).
- *Relevanz:* Installationen nur mit ArenaMode bekommen sonst die Owner-Neubindung ohne die zugehörigen Kamera-Patches.

**S0-15 (P2): ArenaMode veröffentlicht nur geprüfte Bodies**
- *Fix:*
  - `ArenaRespawnDriver.cs:255` prüft den Body so wie `:325-334` (nicht null, `IsYourPlayer`, `ReferenceEquals(game.LocalPlayer, body)`).
  - `OnKitApplied` wird nach einem erfolgreichen `RunRespawn` ausgelöst oder entfällt ganz.
- *Evidenz:* `KitBuilder.cs:42` löst `OnKitApplied` innerhalb von `RespawnEngine.cs:116` aus, also bevor der neue Body existiert. Das Event hat keine Abonnenten.
- *Relevanz:* `ArenaEvents` veröffentlicht nie einen veralteten oder toten Body.

**S0-16 (P1, neu): ArenaModes `RecoverOldBody` verträgt einen nicht abgebauten Body**
- *Fix:*
  - Die Recovery-Schritte werden idempotent. `RegisterPlayer(oldBody)` läuft nur, wenn `!RegisteredPlayers.Contains(oldBody)` gilt. Ein neuer Owner entsteht nur, wenn der aktuelle fehlt oder nicht zu `oldBody` gehört. Unsubscribe plus Subscribe des Death-Handlers bleibt wie heute.
  - Ist der alte Body noch vollständig verdrahtet (registriert, `game.LocalPlayer`, Owner lebt und gehört zu ihm), meldet der Swap nur `Report(false)`, ohne Neuverdrahtung und ohne Area-Abbruch.
  - Die Aufrufstelle `RecoverOldBody(game, oldBody)` und ihre Position bleiben wörtlich erhalten, weil `tools/BleedingCleanupChecks/Verify.ps1:129-130` sie prüft.
  - Optional prüft `LiveKitRespawn` vor `BeginPipelineUse` zusätzlich `RespawnEngineState.IsPipelineRunning`. Das Member gibt es schon.
- *Evidenz:*
  - `LiveKitRespawn.cs:245-264` ruft bei jedem `failure` im selben aktiven Raid `RecoverOldBody` auf, und die Methode (`:329-366`) setzt voraus, dass der Teardown gelaufen ist.
  - `GameWorld.RegisterPlayer` (`GameWorld.cs:2012-2057`) prüft nicht auf Duplikate: `RegisteredPlayers.Add` legt einen zweiten Eintrag an, `ItemOwners.Add` (`Dictionary`, `:507`) wirft beim doppelten Schlüssel. Der `catch` ruft `EndRaidAsLastResort` (StandDown + InvokeDeathScreen).
  - `LiveKitRespawn.cs:202-213` prüft nur Arenas eigene Lease, nicht Cores `IsPipelineRunning`.
- *Relevanz:* Mit S0-3/S0-4 kann `RunRespawn` ablehnen, ohne dass ein Teardown gelaufen ist (`IsPipelineRunning`, `Depth`, `IsAvailable`). Ohne diesen Fix endet dann der Raid eines lebenden Spielers als Killed.
  - Unkritisch ist die Ablehnung wegen `RaidIsTearingDown`: Dann ist `sameActiveRaid` false, und der stille Stale-Ausgang greift.
  - Unkritisch ist auch Arenas Todes-Pfad (`ArenaRespawnDriver.cs:223-247`): Er setzt StandDown und zeigt den Death-Screen für einen ohnehin toten Spieler, und er versucht es nicht erneut.
  - Alte ArenaMode-Binaries kann Core nicht reparieren. Weil 0.1.0 unveröffentlicht ist, betrifft das nur Dev-Builds.

---

## 3. Stufe 1: API v1

### 3.1 Öffentliche Oberfläche (der einzige neue öffentliche Typ)

```csharp
// NEW: D:\SPT41\Development\Redeploy\Redeploy.Core\Api\RespawnApi.cs
// Assembly simple name (frozen): maschine-Redeploy.Core
// Reflection name (frozen, no Version/Culture/PublicKeyToken):
//   "Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core"
using System;
using EFT;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;

namespace Redeploy.Core.Api;

/// <summary>
/// Frozen respawn contract for other mods. BCL/EFT types only; bindable by reflection or by reference.
/// Unity main thread only. Each subscriber runs in its own try/catch; handlers whose target is a
/// destroyed UnityEngine.Object are skipped. Evolution is additive only (new members => ApiVersion 2).
/// </summary>
public static class RespawnApi
{
    /// <summary>Contract level of the INSTALLED Core. A property, never a const (a const would be baked into consumers).
    /// Fix-level Core releases do not change it; RedeployCore.Version reports those.</summary>
    public static int ApiVersion => 1;

    // ---- frozen literals (values never change; later versions may add new ones: treat unknown values generically) ----
    /// <summary>Replaced reason: the old body was dead at takeover and stays in the world as the corpse.</summary>
    public const string ReasonDeath = "death";
    /// <summary>Replaced reason: the old body was alive at takeover (kit swap, round break); its requester disposes it later.</summary>
    public const string ReasonSwap = "swap";

    /// <summary>ReplacementFailed stages. Diagnostic only: the consumer rule is the same for every stage.</summary>
    public const string StagePrepare = "prepare";        // failed before a replacement body existed
    public const string StageCreate = "create";          // creation faulted or timed out
    public const string StageRaidEnding = "raid-ending"; // the raid began stopping while creation was pending
    public const string StageRewire = "rewire";          // failed while wiring the created body

    // ---- queries ----
    /// <summary>Core can replace the local body in this session: keep-alive patches installed, at least one
    /// driver registered, Fika not loaded (evaluated in this order; the Fika probe runs last). When false, Core
    /// is idle: it claims no death, blocks nothing and rejects RunRespawn. Says nothing about whether a given
    /// death will be respawned. A false read before the chainloader has finished is not final.</summary>
    public static bool IsAvailable =>
        DeathInterception.IsOperational && DeathInterception.DriverCount > 0 && !FikaCompatCheck.IsFikaLoaded;

    /// <summary>A replacement pipeline is running: from RunRespawn entry (after the preflight) to its synchronous
    /// tail, plus one frame on failure.</summary>
    public static bool IsReplacing => RespawnEngineState.IsPipelineRunning;

    /// <summary>Is a Core driver holding the raid open for the death of <paramref name="body"/> (null = game.LocalPlayer)?
    /// true: a driver claimed this death and has not released it; inside Player.OnDead postfixes and
    /// OnPlayerDeadStatic handlers (before CG_Spawn): a driver is predicted to claim it. A replacement is possible,
    /// not promised: the player may give up, a required item may be missing, the mode may end the match, and a
    /// Killed Stop can still follow. The pre-claim value is a heuristic: it knows a driver will claim, not whether
    /// that driver will replace (known case: the match-deciding death in ArenaMode Team DM predicts true and turns
    /// false right after the claim). Always true while DeathClaimed is being raised.
    /// false: Core will not replace this body (not the current local body, alive, bot, unclaimed, released,
    /// resolved, raid stopping, Core unavailable). false is NOT "final death": a respawn mod outside Core may act.</summary>
    public static bool IsRespawnPending(Player body) => RespawnClaims.IsPending(body);

    // ---- events ----
    /// <summary>(deadBody, driverName) A driver claimed the local player's death. Raised in the CG_Spawn prefix,
    /// after the vanilla death events and before the driver acts. A claim, not a promise: a Killed Stop may follow.</summary>
    public static event Action<Player, string> DeathClaimed;

    /// <summary>(oldBody, newBody, reason) The local body was replaced and is fully wired. Raised synchronously in
    /// the replacing frame, before spawn protection and before the requester's own follow-up work.</summary>
    public static event Action<Player, Player, string> Replaced;

    /// <summary>(oldBody, stage) A started replacement ended without Replaced. Core does not roll back: drop
    /// cached bodies and read GameWorld.MainPlayer again in the next frame. Never raised after RaidEnded of
    /// the same game.</summary>
    public static event Action<Player, string> ReplacementFailed;

    /// <summary>A LocalGame raid ended (GameEnd postfix, after the drivers and Core have reset). Raised once per
    /// raid. Never raised for Fika's CoopGame, the hideout or trader narrate scenes.</summary>
    public static event Action RaidEnded;

    // ---- engine-internal (not part of the contract) ----
    internal static void PublishDeathClaimed(Player body, string driver)
        => CoreSignals.Respawn.Raise(DeathClaimed, h => h(body, driver), nameof(DeathClaimed));
    internal static void PublishReplaced(Player oldBody, Player newBody, string reason)
        => CoreSignals.Respawn.Raise(Replaced, h => h(oldBody, newBody, reason), nameof(Replaced));
    internal static void PublishReplacementFailed(Player oldBody, string stage)
        => CoreSignals.Respawn.Raise(ReplacementFailed, h => h(oldBody, stage), nameof(ReplacementFailed));
    internal static void PublishRaidEnded()
        => CoreSignals.Lifecycle.Raise(RaidEnded, h => h(), nameof(RaidEnded));
}
```

**Bestehende Mitglieder:** Die Signaturen bleiben unverändert. Die Events sind ab jetzt pro Abonnent isoliert, werden aber nicht nach außen beworben.
- `RespawnEngine.OnPlayerRespawned` (`Action<LocalPlayer>`) feuert direkt nach `Replaced`.
- `RespawnEngine.OnSpawnProtectionEnded` feuert zum selben Zeitpunkt wie heute.
- `DeathInterception.OnDeathIntercepted` feuert direkt vor `DeathClaimed`.
- `RaidLifecycle.OnRaidStarted`/`OnRaidEnded` bleiben ungefiltert wie heute, bis auf zwei Filter: Narrate-Welten beim Start und `HideoutGame` beim Ende (S0-7).
- `RespawnEngineState` bleibt, `RespawnRequest` bleibt ohne neues Feld.
- `IDeathInterceptionDriver` und `ISpawnSelector` bleiben ohne neues Mitglied (3.9).

Einzige Vertragsänderung ist Doku: Core liest `IDeathInterceptionDriver.ShouldHoldBlackScreen()` (`DeathInterception.cs:44-45`) zusätzlich als "hält den Raid für den beanspruchten Tod noch offen". Das heißt: Ein Respawn ist möglich, aber nicht versprochen. Beide Treiber erfüllen das heute schon:
- Redeploy: `Settings.Enabled.Value && !HasGivenUp && (PendingRespawnChoice || IsRespawnInProgress)`, `RedeployDriver.cs:62-68`. Der Prompt wird auch dann gehalten, wenn das nötige Respawn-Item fehlt und nur "Raid beenden" angeboten wird (`RespawnService.cs:49-51`, `RespawnPromptPanel.cs:101-137`).
- Arena: `IsActive && IsRespawnInProgress && !StandDown && (!IsTerminalTdmHold || IsPipelineRunning)`, `ArenaRespawnDriver.cs:400-402`. Tacticals `HoldRespawn` hält die Lease über die Rundenpause, auch in einer Schlussrunde, die das Match mit einem `Stop` beendet (`TacticalMode.cs:294`).

**Neue interne Mitglieder:**
- `DeathInterception.IsOperational`, `DriverCount`, `FindClaimant(LocalGame)` (die heutige Schleife `:140-155` als reine Methode) und `NotifyDriversRaidEnded()`;
- `RespawnClaims` mit `RaidEndedFor(game)`;
- `SignalDispatcher` (rein, instanzbasiert, Logger und Prädikat "zerstörtes Target" werden injiziert) mit dem Unity-Host `CoreSignals` und dessen Instanzen `Respawn` und `Lifecycle`;
- `RespawnPreflight` (rein);
- die Retired-Markierung in `OwnerCleanupHelper`.

### 3.2 Verdrahtung in Core (nur neue Zeilen; `:105-236` bleibt unverändert)

```csharp
// RespawnEngine.RunRespawn
var rejection = RespawnPreflight.Check(/* request, game, selector null; !RespawnApi.IsAvailable; RaidIsTearingDown;
                                          CoreSignals.Respawn.Depth > 0; IsPipelineRunning */);
if (rejection != null) { CoreLog.LogWarning("RunRespawn rejected before teardown: " + rejection.Message); onFailure(rejection); yield break; }
RespawnEngineState.LastRespawnedPlayer = null;
RespawnEngineState.IsPipelineRunning = true;                                   // :92
string reason = RespawnApi.ReasonDeath; bool replacedPublished = false;
...
deadPlayer = request.DeadPlayer ?? game.LocalPlayer;                           // :102
reason = deadPlayer?.HealthController?.IsAlive == true ? RespawnApi.ReasonSwap : RespawnApi.ReasonDeath; // read-only
...
// in each failure branch, between "IsPipelineRunning = false" and "onFailure(...)":
//   :159-160 -> StagePrepare   :170-171 -> StageCreate   :185-186 -> StageRaidEnding
//   :254-255 -> (createTask != null && createTask.IsFaulted) ? StageCreate : StageRewire
if (!replacedPublished)
{
    RespawnClaims.Resolve(deadPlayer);                                         // marks deadPlayer processed
    if (!RespawnClaims.RaidEndedFor(game)) RespawnApi.PublishReplacementFailed(deadPlayer, stage);
}
...
RespawnEngineState.LastRespawnedPlayer = newPlayer;                            // :238
replacedPublished = true;
RespawnClaims.Resolve(deadPlayer);
RespawnApi.PublishReplaced(deadPlayer, newPlayer, reason);                     // :239 (isolated)
RaiseRespawned(newPlayer);                                                     // :239 legacy, now isolated

// DeathInterception.CgSpawnPatch.Prefix (:135-182)
if (__instance is not LocalGame localGame) return true;                        // :137-138
if (!RespawnApi.IsAvailable) return true;                                      // fail-closed: incomplete patches, no driver, Fika
var body = localGame.LocalPlayer;
var claimant = FindClaimant(localGame);                                        // :140-155, pure
if (claimant == null) { RespawnClaims.MarkProcessed(body); return true; }      // :157-158
RespawnClaims.BeginClaim(body, claimant);                                      // dispatch window: pending = true
CoreSignals.Respawn.Raise(OnDeathIntercepted, h => h(localGame, claimant), nameof(OnDeathIntercepted)); // :160-167
RespawnApi.PublishDeathClaimed(body, claimant.Name);
try { claimant.OnPlayerDied(localGame); }                                      // :169-171
catch (Exception ex) { RespawnClaims.EndDispatch(failedOpen: true); CoreLog.LogError(...); return true; } // :173-179
RespawnClaims.EndDispatch(failedOpen: false);
return false;                                                                  // :181

// Guard prefixes (InitiateGameStopping, BlackScreenGuard, SessionResultGuard, OwnerCleanupGuard):
if (!RespawnApi.IsAvailable) return true;                                      // Core idle: block nothing
// StopPatch: skip the Killed block when !IsAvailable; OnExternalStop may still be delivered.

// RespawnClaims.IsPending(Player body): live evaluation, no latch that depends on Stop-prefix order
var game = Singleton<AbstractGame>.Instance as LocalGame;
body ??= game?.LocalPlayer;
if (body == null) return false;
if (_dispatching && ReferenceEquals(body, _claimedBody)) return true;         // DeathClaimed window, independent of Status
if (game == null || !RespawnApi.IsAvailable || RespawnEngine.RaidIsTearingDown(game)) return false; // RaidIsTearingDown becomes internal
if (ReferenceEquals(body, _claimedBody)) return SafeHolds(_claimant);          // ShouldHoldBlackScreen(): raid held open
if (ReferenceEquals(body, _processedBody)) return false;                       // unclaimed at CG_Spawn, driver threw, or resolved
return ReferenceEquals(body, game.LocalPlayer) && body.HealthController?.IsAlive == false
       && DeathInterception.FindClaimant(game) != null;                        // heuristic prediction at OnDead time

// RespawnClaims transitions (all reset at both raid boundaries, S0-6)
// BeginClaim(body, c):     _claimedBody = body; _claimant = c; _dispatching = true
// EndDispatch(failedOpen): _dispatching = false; if (failedOpen) Resolve(_claimedBody)
// MarkProcessed(body), Resolve(body): _processedBody = body;
//                          if (ReferenceEquals(body, _claimedBody)) { _claimedBody = null; _claimant = null; }
```

Die Entscheidung in `IsPending` liegt als reine Funktion `RespawnClaimLogic.Decide(...)` vor, damit sie testbar ist (Abschnitt 8). Dass `Resolve` den alten Body als verarbeitet markiert, ist wesentlich. Ohne diese Markierung würde die Vorhersage für einen toten `game.LocalPlayer` nach `ReplacementFailed` in den Stufen `prepare`/`create` wieder anspringen. In einem Arena-Raid mit installiertem Redeploy wäre sie sofort wieder true, weil Arenas Lease nur Arena blockiert und `Redeploy.WantsToHandleDeath` true bleibt.

Zum GameEnd-Postfix in `RaidLifecycle`: Er deklariert `object __instance` und `Task __result`, damit `HideoutGame` und `CoopGame` sicher gecastet werden können und ein fehlgeschlagener synchroner Teil geloggt wird (3.7). Er hält die Reihenfolge aus S0-7 ein. Der GameStarted-Postfix filtert `NarrateGameWorld` und führt den Core-Reset (samt gekapseltem Freecam-Aufruf, S0-14) aus, bevor er die Legacy-Handler auslöst.

### 3.3 Die Events im Detail

#### `DeathClaimed(Player deadBody, string driverName)`
- **Raise-Punkt:** CG_Spawn-Prefix, an der Stelle von `OnDeathIntercepted` (`DeathInterception.cs:160-167`). Das ist nach der Treiberwahl (`:140-155`) und vor `claimant.OnPlayerDied` (`:169-179`).
- **Garantierter Zustand:**
  - Das Event läuft innerhalb von `HealthController.DiedEvent`, also im Damage-Call-Stack.
  - `Player.OnDead` ist durch: `OnBeenKilledByAggressor`, `OnPlayerDead`, `OnPlayerDeadStatic` und `OnPlayerDeadOrUnspawn` haben gefeuert, die Leiche existiert (`_decompiled/EFT/Player.cs:30096-30112, 30198`). Grund: `Player.Init` abonniert `OnDead` (`:28271`) vor CG_Spawn (`BaseLocalGame.cs:834`, `RespawnEngine.cs:200`).
  - `deadBody == game.LocalPlayer == GameWorld.MainPlayer`. Der Body ist noch registriert; vanilla entfernt ihn einen Frame später (`Player.cs:30238-30249`). Alter Owner, `PlayerCameraController` und FPS-Kamera leben noch.
  - `IsRespawnPending(deadBody) == true` (Dispatch-Fenster, unabhängig vom Raid-Status).
  - Der Treiber hat noch nicht gehandelt: kein Prompt, kein `ArenaEvents.OnPlayerDied`.
  - CG_Spawn-Prefixe anderer Mods laufen danach trotzdem, weil HarmonyX alle Prefixe ausführt.
  - `IsReplacing` ist in der Regel false.
  - `Status` ist in der Regel `Started`, das ist aber nicht garantiert. Nach S0-10 prüft Redeploy den Status. ArenaMode prüft ihn in `WantsToHandleDeath` nicht (`ArenaRespawnDriver.cs:39-45`), ein Arena-Claim kann also während `Stopping` kommen, etwa bei einem Tod im Stop-Fade nach einem Team-DM-Ergebnis. Direkt nach dem Dispatch liefert `IsRespawnPending` dann false.
- **Auflösung:** Ein Claim ist kein Versprechen. Er endet auf eine von drei Arten:
  - `Replaced(deadBody, ...)`;
  - `ReplacementFailed(deadBody, ...)`;
  - `IsRespawnPending(deadBody)` wird false (Give-up, Stand-down, Treiber wirft, terminaler Team-DM-Hold), danach folgt `RaidEnded`.

  Ein späteres Aufgeben löst über `InitiateGameStopping` sehr wohl noch `Stop(Killed)` für diesen Tod aus (`BaseLocalGame.cs:844-848`). Das gilt auch, wenn Redeploy mangels Respawn-Item nur "Raid beenden" anbietet, und wenn ein Modus das Match beendet.
- **Konsumenten:** CombatLog (Life-Segment), BtrTurretControl (Exit, solange der sterbende Body noch `MainPlayer` ist), Killcams (Diagnose; entscheiden sollten sie über die Abfrage, aber das Stop-Handling behalten).

#### `Replaced(Player oldBody, Player newBody, string reason)`
- **Raise-Punkt:** `RespawnEngine.cs:239` anstelle von `RaiseRespawned`, nach `:238` und vor `:241-244`. Einmal pro erfolgreichem `RunRespawn`, für alle vier Aufrufer:
  - Redeploy (`RespawnService.cs:171`)
  - Arena-Tod (`ArenaRespawnDriver.cs:212`)
  - Arena Protected Team (`:304`)
  - Arena Raidstart/Rundenpause (`LiveKitRespawn.cs:245`)
- **Beim Handler-Aufruf bereits erledigt (Frame N, synchron):**
  - `newBody == game.LocalPlayer` (`:197`) `== MainPlayer` (`:214`) `== GamePlayerOwner.MyPlayer` (`:222`), und `_players[ProfileId]` zeigt auf `newBody` (`:199`).
  - CG_Spawn ist abonniert (`:200`).
  - Bots: `DestroyInfo(old)`, Purge, `AddActivePLayer(new)` (`:203-211`).
  - Neuer Owner erzeugt und gestartet (`:218-229`), Freecam und DynamicMaps neu gebunden, Secret-Exfils initialisiert.
  - **Neuer** `PlayerCameraController` und **neue** FPS-/Optik-Kamera (`:227`). Der alte Controller ist schon per `DestroyImmediate` weg (`CameraRebindHelper.cs:51-52`). Das alte Kamera-GameObject lebt bis zum Ende von Frame N und stirbt vor dem Rendering.
  - Effects, Black-Screen und Input sind wiederhergestellt, der Health-Controller ist gestartet, Hinken/Recoil vom Pool-Body zurückgesetzt (`:236`).
  - `IsRespawnPending(oldBody) == false`.
  - Eine in diesem Handler gecachte Kamera überlebt, weil N+2 nur reaktiviert (`CameraRebindHelper.cs:41-45`). Das gilt aber nur, solange `Singleton<PlayerCameraController>` bei N+2 noch zum neuen Body gehört. Übernimmt oder gibt ein Spectator oder eine Killcam den Controller zwischen N und N+2 frei, ruft `RebindToPlayer` erneut `PlayerCameraController.Create` auf und zerstört die gecachte Kamera (`CameraRebindHelper.cs:38-73`).
- **Noch nicht erledigt:**
  - Spawn-Schutz; er beginnt direkt nach den Handlern im selben Frame.
  - `IsReplacing` ist noch true.
  - Der alte Owner lebt bis Frame-Ende; sein Cleanup ist durch S0-5 geschützt.
  - Die Fortsetzung des Anforderers kommt frühestens bei N+1: Arena `EndPipelineUse`, `ArenaEvents`, Zerstörung des Alt-Bodys.
  - N+2 `FinalizeRespawnView`, N+3 `EnsureBattleUiReady`.
  - Solange der Anforderer seine Lease hält, lehnt **nur der anfordernde Treiber** einen Tod des neuen Bodys ab (`ArenaRespawnDriver.cs:43`, `RedeployDriver.cs:29-30`). Jeder Treiber prüft nur sein eigenes Flag. In einem Arena-Raid mit installiertem Redeploy kann Redeploy (Priorität 0) diesen Tod beanspruchen, weil `RaidStartedPatch` kein Arena-Gate hat und `IsInRaid` setzt (`RaidStartedPatch.cs:24-53`). Es folgen `DeathClaimed(newBody, "Redeploy")`, ein Redeploy-Prompt im Arena-Raid und `IsRespawnPending == true`. Ohne Redeploy geht der Tod an vanilla, und Arenas `ShouldBlockDeathScreen` blockiert ihn; das ergibt einen abgeleiteten Soft-Lock. Das Fenster reicht von Frame N bis zur Fortsetzung, in den Logs etwa 0,5 s. Der Raidstart-Swap läuft ohne Schutz (`Protection = null`). Behebung in Stufe 2 (Lease in Core, Raid-Besitz), das tatsächliche Verhalten klärt offene Frage 3.
- **Alter Body:** siehe Taxonomie in 3.4.
- **Konsumenten:** alle mit Body-Bindung. In der Regel erreichen sie das Event über `BodyChanged`.

#### `ReplacementFailed(Player oldBody, string stage)`
- **Raise-Punkt:** die vier Fehlerausgänge, jeweils nach dem bestehenden Ein-Frame-`yield` und `IsPipelineRunning = false`, direkt vor `onFailure`. Das Event wird nie nach `Replaced` desselben Aufrufs ausgelöst und nie, nachdem `RaidEnded` für dasselbe Spiel veröffentlicht wurde. Bei einer Preflight-Ablehnung gibt es kein Event.
- **Garantierter Zustand:** `IsReplacing == false`, `IsRespawnPending(oldBody) == false`, weil Core `oldBody` als verarbeitet markiert. Die Welt ist so, wie die jeweilige Stufe sie hinterlassen hat (3.5). Der Anforderer reagiert frühestens einen Frame später: Redeploy mit `GiveUpRaid`, Arena mit Recovery oder Abbruch.

#### `RaidEnded()`
- **Raise-Punkt:** der einzelne GameEnd-Postfix in `RaidLifecycle`, als Schritt 5 aus S0-7.
- **Garantierter Zustand im Normalfall:**
  - Der synchrone Teil von `GameEnd` ist durchgelaufen: `OnGameSessionEnd`, `CleanUp()`, `Status == Stopped`, Health/Profil-Deskriptor.
  - `CleanUp()` entsorgt oder pooled nur die Einträge in `_players`, also nur den neuesten Body. Leichen bleiben bis `GameWorld.Dispose`.
  - Offen sind noch Backend (`LocalRaidEnded`), Session-Result und `GameWorld.Dispose` (`BaseLocalGame.cs:1054-1108`).
  - Treiber und Core sind zurückgesetzt: `IsReplacing == false`, keine Claims, `LastRespawnedPlayer == null`.
  - Referenzen auf Bodies nur noch fallen lassen, nichts mehr anfassen.
- **Einschränkung:** `GameEnd` ist `async`. Wirft sein synchroner Teil, steckt die Exception im Task, und der Postfix läuft trotzdem. `RaidEnded` feuert dann ohne `CleanUp` und ohne `Stopped`. Core loggt diesen Fall (`__result.IsFaulted`). Anforderer-Coroutinen laufen nach `RaidEnded` weiter (S0-6). Ihre `RunRespawn`-Aufrufe lehnt der Preflight wegen `RaidIsTearingDown` ab.

### 3.4 Reason-Taxonomie

**`"death"`**
- Der alte Body war beim Eingang in `RunRespawn` tot.
- Er bleibt als Leiche: dasselbe GameObject, alle Mod-Komponenten laufen weiter.
- `IsYourPlayer` bleibt true; Profile und ProfileId teilt er mit dem neuen Body.
- Er ist unregistriert, hat keinen Owner und keinen Death-Handler mehr (`:105`).
- Er kann später in den Player-Pool zurückkehren, samt Komponenten.
- Seine Vanilla-Todes-Events sind lange vorher gefeuert.

**`"swap"`**
- Der alte Body war lebendig (Kit-Swap zum Raidstart, Rundenpause in Rounds/Tactical).
- Core lässt ihn unregistriert (Cores `UnregisterPlayer` wirkt nur hier), ohne Owner und ohne Death-Handler. Er behält aber seinen lebenden `HealthController`, seine Collider und eventuelle Blutungen (`ArenaRetiredBodyEffects.cs:11-16`).
- Über sein Schicksal entscheidet der Anforderer. ArenaMode zerstört ihn frühestens bei N+1 (`LiveKitRespawn.cs:271` → `ArenaRetiredBodyEffects.cs:66`).
- Seine Komponenten bekommen mitten im Raid `OnDestroy`.
- `Player.Dispose` löst `OnPlayerDeadOrUnspawn`/`OnIPlayerDeadOrUnspawn` unter **derselben ProfileId** wie der lebende Body aus.
- `OnPlayerDead` kommt **in der Regel** nie. `TryDestroy` lehnt aber unter mehreren Bedingungen ab: kein exakt aktiver Raid, `MainPlayer != LocalPlayer`, `Status != Started`, Body tot oder Leiche vorhanden (`ArenaRetiredBodyEffects.cs:27-40`). Dann lebt der Alt-Body weiter und kann später sterben (`LiveKitRespawn.cs:266-271`). `OnPlayerDead`/`OnPlayerDeadStatic` feuern in diesem Fall für einen `IsYourPlayer`-Body, der nicht der aktuelle ist, und `IsRespawnPending` liefert false.
- Scheitert der Swap außerhalb begrenzter Arena-Areas und im noch aktiven selben Raid, stellt ArenaMode den Alt-Body wieder her (`LiveKitRespawn.cs:329-366`), und `MainPlayer` zeigt wieder auf ihn. Mit aktiver Area bricht ArenaMode die Area ohne Neuverdrahtung ab (`:331-335`); bei einem Stale-Ausgang läuft gar keine Recovery (`:251-263`). Rettet ArenaMode stattdessen den Ersatz (`:369-398`), gibt es kein `Replaced`; der Poll des Trackers meldet den Wechsel.

**Allgemein:**
- Core leitet den Grund aus `IsAlive` beim Eingang ab. Es gibt kein neues Request-Feld, kein Treiber kann falsch etikettieren, und alte Treiber-Binaries liefern trotzdem den richtigen Grund.
- Unbekannte künftige Werte behandeln Konsumenten als generische Ersetzung.
- Feinere Arten wie Raidstart gegen Rundenpause werden bewusst nicht exponiert.
- **Treibernamen** sind `IDeathInterceptionDriver.Name`, heute `"Redeploy"` (`RedeployDriver.cs:14`) und `"ArenaMode"` (`ArenaRespawnDriver.cs:35`).

### 3.5 Fehler- und Abbruchsemantik

Genau einmal: Jeder Aufruf, der den Preflight passiert, endet in genau einem `Replaced` oder genau einem `ReplacementFailed`. Es gibt zwei Ausnahmen:
1. Core hat für dieses Spiel bereits `RaidEnded` veröffentlicht. Dann unterdrückt es das öffentliche `ReplacementFailed`; `onFailure` läuft trotzdem. Mit synchroner Erzeugung ist das unerreichbar, mit asynchroner Erzeugung (die 30-s-Warteschleife steht noch im Code) nicht.
2. Eine Coroutine wird mit dem `LocalGame`-Objekt zerstört. `RaidEnded` ist dann bereits gefeuert.

Wirft etwas nach `:239`, ruft der Anforderer wie heute `onFailure` auf, Core erzeugt aber kein zweites öffentliches Event.

| Stufe | Ausgang heute | Welt bei Raise |
|---|---|---|
| Preflight-Ablehnung | neu, vor `:92` | Nichts wurde verändert. `onFailure` läuft synchron, kein Event. Der Anforderer muss das von einem Teardown-Fehler unterscheiden (S0-16). |
| `prepare` | `:153-161` | Teilweise abgebaut: Owner zerstört, Body unregistriert, Profil (Item, Health, Equipment) eventuell schon verändert. Kein neuer Body. Ein null-`request` (Wurf bei `:94` außerhalb jedes `try`) und ein null-`Game` (sicher erst bei `:114`, nach `:110`/`:112`) erreichen diese Stufe nicht mehr, der Preflight fängt sie ab. |
| `create` (Timeout) | `:164-173` | Wie `prepare`, zusätzlich ein Task, der noch fertig werden kann. Nicht erreichbar, solange die Erzeugung synchron läuft. |
| `create` (Fehler in der Erzeugung) | Wurf bei `:192` → `:251-257` | `MainPlayer` kann schon auf einen registrierten, unverdrahteten Waisen-Body zeigen (`GameWorld.cs:2027-2029`), etwa bei einem werfenden `OnPersonAdd`-Abonnenten. Der alte Body ist unregistriert und ohne Owner. |
| `raid-ending` | `:182-187` | Der Raid endet, `MainPlayer` zeigt auf den unverdrahteten Waisen-Body. Nach S0-3 nur noch erreichbar mit asynchroner Erzeugung oder wenn Code im synchronen Abschnitt `:105-146` selbst `Stop` aufruft. |
| `rewire` | `:251-257` | `game.LocalPlayer`/`MainPlayer` können schon der teilweise verdrahtete neue Body sein. |

Core rollt nichts zurück. Für jeden Konsumenten gilt dieselbe Regel: gecachte Bodies verwerfen und `MainPlayer` im nächsten Frame neu lesen. Der Tracker erledigt das automatisch. Er meldet dabei unter Umständen einen Wechsel alt→Waise und, wenn ArenaMode den Alt-Body wiederherstellt, Waise→alt (4.1).

### 3.6 Isolation, Reihenfolge, Reentranz

**Isolation**
- Ein gemeinsamer Dispatcher macht einen Snapshot der Invocation-List und ruft jeden Handler in einem eigenen `try/catch` auf.
- Die Warnung nennt Typ und Assembly des Handlers. Nichts propagiert in die Engine oder den Damage-Stack.
- Handler, deren `Target` ein zerstörtes Unity-Objekt ist, werden übersprungen.
- An- und Abmelden während eines Raises wirkt ab dem nächsten Raise. Alles läuft auf dem Main-Thread.

**Reihenfolge**
- Innerhalb eines Events gilt die Abonnement-Reihenfolge, grob die GUID-Reihenfolge der Plugins. Das ist **kein Vertrag**, und es gibt keine Prioritäten. Die Legacy-Lifecycle-Handler von ArenaMode verlassen sich auf ihre Abonnement-Reihenfolge (`Plugin.cs:39-44`); der Snapshot erhält sie.
- Legacy-Events feuern daneben: `OnDeathIntercepted` direkt vor `DeathClaimed`, `OnPlayerRespawned` direkt nach `Replaced`.
- Ablauf eines Todes-Respawns über alle Events:
  1. Vanilla-Death-Events und `OnDead`-Postfixe; hier liefert `IsRespawnPending(body)` die heuristische Vorhersage.
  2. `OnDeathIntercepted` → `DeathClaimed` (Frame D).
  3. `claimant.OnPlayerDied` → `ArenaEvents.OnPlayerDied` oder der Redeploy-Prompt.
  4. Prompt, Cooldown, Hold oder Preload (0 bis n Sekunden).
  5. Frame N: Pipeline. Mittendrin feuert `OnPersonAdd` (`MainPlayer` ist neu, `game.LocalPlayer` noch alt), und bei `:227` feuern die Kamera-Events.
  6. `Replaced` → `OnPlayerRespawned` → Start des Spawn-Schutzes.
  7. Ende von Frame N: alter Owner und altes Kamera-GameObject werden zerstört.
  8. Frühestens N+1: Fortsetzung des Anforderers (`ArenaEvents.OnPlayerRespawned`, Zerstörung des Swap-Bodys mit Unspawn).
  9. N+2 `FinalizeRespawnView`, N+3 `EnsureBattleUiReady`.

**Reentranz**
- `RunRespawn` wird abgelehnt (`InvalidOperationException`), wenn es während eines Todes- oder Pipeline-Raises (`CoreSignals.Respawn.Depth > 0`) oder bei laufender Pipeline aufgerufen wird.
- Lifecycle-Raises (`CoreSignals.Lifecycle`) zählen nicht. ArenaMode startet den Raidstart-Swap synchron aus dem Legacy-`OnRaidStarted`-Dispatch (`ArenaSession.cs:299-303` → `LiveKitRespawn.AtRaidStart`) und erreicht `RunRespawn` heute erst nach mehreren `yield`s (`LiveKitRespawn.cs:45-84, 142-190`). Selbst ein künftig synchroner Pfad würde deshalb nicht abgelehnt. Ein `RunRespawn` aus einem `RaidEnded`-Handler lehnt der Preflight ohnehin wegen `RaidIsTearingDown` ab.
- Verschachtelte Raises sind erlaubt und isoliert, zum Beispiel ein Tod des neuen Bodys innerhalb eines `Replaced`-Handlers.
- Handler dürfen nicht:
  - `LocalGame.Stop`/`InitiateGameStopping` aufrufen,
  - einen der beiden Bodies zerstören,
  - alten Owner oder alte Kamera anfassen,
  - den neuen Body töten (der Schutz ist noch nicht aktiv).

  Wer so etwas braucht, verschiebt es per Coroutine um einen Frame. Das gilt auch für `BodyChanged`-Handler des Trackers in **jeder** Stufe, weil ein Handler nicht erkennen kann, ob er aus `Replaced` oder aus dem Poll aufgerufen wird.
- Beobachter außerhalb von Core (`GameWorld.OnPersonAdd`, Patches auf `Player.Init`, statische `PlayerCameraController`-Events) sehen einen halb verdrahteten Zustand und sind **nicht** isoliert. Wirft dort ein Abonnent, scheitert der Respawn nach dem Teardown (Stufe `create` oder `rewire`). Das wird dokumentiert.

**Zeitverhalten**
- Zugesagt:
  - `Replaced` im Rewire-Frame, vor dem Schutz;
  - `DeathClaimed` im Todes-Frame, nach den Vanilla-Death-Events;
  - `RaidEnded` nach dem synchronen Teil von `GameEnd` (3.3).
- Nur beobachtet, nicht zugesagt: Die Erzeugung ist synchron, und Update-Poller sehen den neuen Body deshalb auf dem Erfolgspfad nie vor `Replaced`. Die 30-s-Wartezeit steht noch im Code. Der Tracker ist über `IsReplacing` davon unabhängig.

### 3.7 Raid-Ende

- `RespawnApi.RaidEnded` bedeutet "der Raid ist vorbei", nicht "der Raid endet gerade". Stop-Fade und Death-Screen liegen davor.
- Es gilt nur für `LocalGame`, nie für `HideoutGame`, Fikas `CoopGame` oder Narrate-Szenen, und kommt einmal pro Spielinstanz. `Stop` schützt vor erneutem Eintritt (`BaseLocalGame.cs:960`), und `GameEnd` wird nur aus dem Fade-Callback aufgerufen (`:978-987`).
- Raids ohne `GameEnd` (verpasstes Ende) setzt Core beim nächsten `OnGameStarted` zurück, ohne ein synthetisches `RaidEnded` auszulösen.
- Warf der synchrone Teil von `GameEnd`, feuert `RaidEnded` trotzdem, aber ohne die Garantien zu `CleanUp`/`Stopped` (3.3).
- Nach `RaidEnded` veröffentlicht Core für dieses Spiel keine Pipeline-Events mehr.
- Wer spät bindet, bekommt keine vergangenen Events nachgeliefert.
- Ein Signal "Raid endet" auf Basis eines committeten `Stop` gehört in Stufe 2.

### 3.8 Fika und Nichtverfügbarkeit

- `IsAvailable == false`, wenn Fika geladen ist, kein Treiber registriert ist oder die Patches nicht vollständig installiert sind. Dann ist Core untätig: Es beansprucht keinen Tod, blockiert nichts und lehnt `RunRespawn` ab (S0-4).
- Unter Fika raist Core keine `RespawnApi`-Events:
  - CG_Spawn und die Stop-Patches filtern schon auf `LocalGame`;
  - das öffentliche `RaidEnded` filtert jetzt ebenfalls;
  - `DeadBodyGuard` ist gefiltert (S0-11).
- Die Legacy-Lifecycle-Events behalten ihr heutiges, ungefiltertes Verhalten und feuern auch für `CoopGame`, weil `ArenaSession` davon abhängt. Nach S0-7 feuert der Legacy-Start in jeder Installation, also auch in reinen Redeploy-Installationen unter Fika.
- Ist Core nicht installiert oder älter als v1, findet der Tracker die Fassade nicht und pollt einfach weiter. Ist Core installiert, aber nicht verfügbar (etwa unter Fika), liefert der Tracker bei `IsRespawnPending` `null`, nicht `false` (4.1).

### 3.9 Versionierung, Bindung, Paketierung

**Eingefrorene Identität**
- Assembly-Simple-Name `maschine-Redeploy.Core`, Namespace, Typname und alle v1-Membernamen samt exakter Delegatetypen.
- Der Reflection-Name hat keine Version, Culture oder Token. BepInEx' `LocalResolve` vergleicht nur Simple-Names: Versionssprünge brechen nichts, Umbenennungen brechen alles.
- Eingefroren sind außerdem die Interfaces, die kompilierte Treiber implementieren: `IDeathInterceptionDriver` und `ISpawnSelector`. Beide Treiber implementieren sie implizit, ohne MemberRefs auf die Interface-Member. Eine neue Interface-Methode fiele erst beim Laden der Treiberklasse im Awake als `TypeLoadException` auf (Test in 8.2).

**Evolution**
- Nur additiv. Neue Member bedeuten `ApiVersion 2` und eine neue Baseline.
- Eine Formänderung bekommt einen neuen Namen (zum Beispiel `ReplacedV2`), das alte Member feuert weiter.
- Literale bleiben eingefroren.

**Fähigkeiten**
- Eine Fähigkeit gilt als vorhanden, wenn das Member existiert: `GetEvent`/`GetProperty` plus `Delegate.CreateDelegate(..., throwOnBindFailure: false)`.
- `ApiVersion` ist nur eine Mindestprüfung. Kein `Supports(string)`: UIFixes zeigt, dass eine bestandene Versionsprüfung trotz fehlenden Members zu NREs führt (`MultiSelectInterop.cs:120-140`).

**Kompilierte Konsumenten (ArenaMode, Redeploy.Client)**
- Sie referenzieren Core weiter mit `Private=false`.
- In 1.0 benutzen beide **kein** neues Core-Member. Deshalb laufen sie auch gegen einen älteren Core, dann eben ohne die Fixes.
- Spätere Nutzung neuer Member gehört in `[MethodImpl(MethodImplOptions.NoInlining)]`-Bridges in eigenen Klassen. Core-Typen oder neue Interfaces dürfen nie in Klassen auftauchen, die immer geladen werden, auch nicht in deren vom Compiler erzeugten Closure-Klassen. Beispiel: Beide Treiber instanziieren ihre Treiberklasse direkt im Awake (Redeploy `Plugin.cs:31`, ArenaMode `Plugin.cs:53`). Ein neues Interface darauf gäbe gegen einen alten Core eine `TypeLoadException`.
- Bestehende Signaturen bleiben. `RaidLifecycle.EnsurePatched()` behält `void`. Ein `bool`-Rückgabewert würde alte ArenaMode-Binaries (`ArenaSession.cs:92`, `ArenaMedicalPatches.cs:46`, `ArenaSpectatorService.cs:186`) mit `MissingMethodException` brechen. `RespawnEngine.RaidIsTearingDown` ist heute `private` (`:262`); `internal` zu machen ist binärsicher.

**Treiber-Check (Skew-Erkennung)**
- `ApiVersion` ändert sich bei Fix-Releases nicht. Ein Downgrade von Core 1.0.1 auf 1.0.0 ist darüber also unsichtbar, und Last-install-wins-Downgrades bleiben über jedes ältere, noch herunterladbare Zip erreichbar.
- Beide Treiber vergleichen deshalb im Awake `RedeployCore.Version` mit der Core-Version, gegen die sie gebaut wurden (eine beim Build erzeugte Konstante). `RedeployCore.Version` ist eine Laufzeit-Property, die die installierte Assembly-Version liest (`RedeployCore.cs:15`), und existiert seit 0.9. Der Aufruf ist also auch gegen jeden älteren Core sicher.
- Ist die Version niedriger, loggen die Treiber eine Warnung ("installiertes maschine-Redeploy.Core ist älter als erwartet: neuestes Redeploy/ArenaMode neu installieren") und tragen den Befund nach dem LateToTheParty-Muster in `Chainloader.DependencyErrors` ein (`ExternalModHandler.cs:46`).
- `ApiVersion` lesen sie zusätzlich per Reflection, sobald sie neue Member **brauchen**. Erst dann deaktiviert sich ein Treiber.

**Paketierung (Soll-Zustand)**
- Genau eine flache Datei, `BepInEx/plugins/maschine-Redeploy.Core.dll`, byte-identisch im Redeploy- und im ArenaMode-Zip. Es gewinnt die zuletzt entpackte.
- Heute gibt es dafür noch keine Werkzeuge. `Redeploy/build` und `ArenaMode/build` enthalten nur Server-Ausgaben. Core deployt sich über `Redeploy/Shared.props` selbst. Deployt ist derzeit der Debug-Build (`plugins/maschine-Redeploy.Core.dll` hat 60928 Bytes und ist identisch mit `bin/Debug`, `bin/Release` hat 57344 Bytes). Keines der beiden Mods ist in `spt-release` registriert.
- Regel: nie einen Core ausliefern, der älter ist als der neueste veröffentlichte.
- Empfehlung: eine eigene Versionslinie für Core, also die Kopplung an Redeploys `Shared.props:3` lösen. Der eigentliche Vertrag ist `ApiVersion`; die Fix-Ebene prüft `RedeployCore.Version`.
- Core bleibt in 1.0 ein Nicht-Plugin. Getrennte Statics sind **nicht** der Grund: Bei genau einer Datei ist das ausgeschlossen, auch als Plugin, weil der Chainloader pro GUID nur die neueste Kopie behält und pro Pfad lädt. Der Grund ist, dass die Versionsprüfung über `RedeployCore.Version` den Skew ohne Umbau abdeckt. Ein `BepInPlugin` mit eigener GUID bleibt eine Option (Abschnitt 6): `[BepInDependency(coreGuid, "x.y.z")]` liefert eingebauten Mindestversions-Schutz über `DependencyErrors`.

---

## 4. Konsumentenseite

### 4.1 Die Interop-Datei (von Redeploy gepflegt, wird kopiert)

Die Masterkopie liegt in `Redeploy/interop/RedeployRespawnInterop.cs`.

```csharp
// RedeployRespawnInterop.cs - interop v1 for maschine-Redeploy.Core "RespawnApi" (needs ApiVersion >= 1, degrades below).
// Master copy (provider-maintained): Redeploy repo, interop/RedeployRespawnInterop.cs.
// Copy verbatim and change ONLY the namespace. No reference to Redeploy or ArenaMode is needed.
//
// Emits ONE chained, de-duplicated BodyChanged(previous, current, reason) stream from two sources:
//   1) RespawnApi v1 events, bound by reflection when maschine-Redeploy.Core is installed;
//   2) GameWorld.MainPlayer reference polling, always on (no Core, older Core, Fika, non-Core respawn mods).
// C# 7.3; net471 or netstandard2.1. Unity main thread only.
using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace YourMod.Interop
{
    /// <summary>Add once to your plugin's own GameObject: gameObject.AddComponent&lt;LocalBodyTracker&gt;().</summary>
    internal sealed class LocalBodyTracker : MonoBehaviour
    {
        public const string ReasonRaidStart = "raid-start";
        public const string ReasonDeath = "death";
        public const string ReasonSwap = "swap";
        public const string ReasonRaidEnd = "raid-end";
        public const string ReasonUnknown = "unknown";

        private const string ApiTypeName = "Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core";
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const float MaxReplacingHoldSeconds = 35f;

        /// <summary>Test seam. Production resolves through BepInEx's assembly resolver.</summary>
        internal static Func<string, Type> ResolveType = name => Type.GetType(name, false);

        /// <summary>First tracker of this plugin (for Harmony patches that need the queries).</summary>
        public static LocalBodyTracker Instance { get; private set; }

        /// <summary>(previous or null, current or null, reason). Chained: each current becomes the next previous;
        /// at most one event per real change; previous == null always comes with "raid-start".
        /// Treat any reason other than the constants above like "unknown".
        /// Delivery differs by source: with Core it runs inside Core's Replaced dispatch (mid-pipeline, before spawn
        /// protection); without Core it runs from Update. So in EVERY case: never start a respawn, stop the raid or
        /// destroy a body directly from a handler; defer such work by one frame.
        /// After a failed replacement the poll may report old->orphan (and orphan->old if the requester recovers).
        /// "raid-end" is exact only with Core (GameEnd); without Core it arrives late, when the GameWorld goes away.
        /// The hideout and trader narrate scenes also show up as "raid-start": filter them yourself if needed.</summary>
        public event Action<Player, Player, string> BodyChanged;

        /// <summary>Core only: (dead body, driver name). The raid continues for now; a Killed Stop may still follow.</summary>
        public event Action<Player, string> DeathClaimed;

        /// <summary>Core only: (old body, stage). No Replaced follows for that request. BodyChanged follows only
        /// if MainPlayer really changed.</summary>
        public event Action<Player, string> ReplacementFailed;

        /// <summary>Core only: the LocalGame raid ended (GameEnd postfix). Never under Fika, in the hideout or in
        /// narrate scenes. Without Core, use your own LocalGame-filtered BaseLocalGame.GameEnd postfix for per-raid latches.</summary>
        public event Action RaidEnded;

        public Action<string> Log = message => UnityEngine.Debug.Log("[RespawnInterop] " + message);
        public Player Current { get; private set; }
        public bool IsBound { get; private set; }

        private readonly List<KeyValuePair<EventInfo, Delegate>> _subscriptions = new List<KeyValuePair<EventInfo, Delegate>>();
        private Func<Player, bool> _isRespawnPending;
        private Func<bool> _isReplacing;
        private Func<bool> _isAvailable;
        private GameWorld _world;
        private GameWorld _endedWorld;
        private float _holdSince = -1f;

        /// <summary>true: a Core driver claimed (or, before CG_Spawn, is predicted to claim) this death and still holds
        /// the raid open; a replacement is possible, NOT promised, and a Killed Stop may still follow.
        /// false: Core will not replace this body (another respawn mod still might); it does not mean "final death".
        /// null: unknown (Core absent, older than RespawnApi v1, or unavailable, e.g. under Fika): keep today's logic.</summary>
        public bool? IsRespawnPending(Player body)
        {
            if (_isRespawnPending == null || !IsRespawnAvailable) return null;
            try { return _isRespawnPending(body); }
            catch (Exception) { return null; }
        }

        /// <summary>Core can respawn in this session (false when unbound, under Fika, or if Core's patches failed).</summary>
        public bool IsRespawnAvailable { get { return ReadFlag(_isAvailable); } }

        /// <summary>The vanilla current-body test for LIVING bodies. Never IsYourPlayer (true on corpses) or ProfileId
        /// (shared by all bodies). In death handlers use ReferenceEquals(MainPlayer, body) instead: a dead body fails IsAlive.</summary>
        public static bool IsCurrentLocalBody(Player body)
        {
            if (body == null || !Singleton<GameWorld>.Instantiated) return false;  // Unity-null included
            return ReferenceEquals(Singleton<GameWorld>.Instance.MainPlayer, body)
                   && body.HealthController != null && body.HealthController.IsAlive;
        }

        /// <summary>Adds a fresh T. Pooled player GameObjects (corpses returned to the pool) may carry a stale one.</summary>
        public static T AttachFresh<T>(Player body) where T : Component
        {
            T stale = body.GetComponent<T>();
            if (stale != null) DestroyImmediate(stale);
            return body.gameObject.AddComponent<T>();
        }

        /// <summary>Removes T from a body that stopped being the local body; a corpse keeps every component.</summary>
        public static void Detach<T>(Player body) where T : Component
        {
            if (body == null) return;                                   // Unity-null: destroyed, nothing to clean
            T component = body.GetComponent<T>();
            if (component != null) Destroy(component);
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            TryBind();
        }

        private void OnDestroy()
        {
            Unbind();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        private void Update()
        {
            GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (!ReferenceEquals(world, _world))
            {
                _world = world;
                _endedWorld = null;
                _holdSince = -1f;
                if (!ReferenceEquals(Current, null)) Change(null, ReasonRaidEnd);   // late fallback; Core reports GameEnd earlier
                if (world != null && !IsBound) TryBind();                          // never cache a negative probe
            }

            if (world == null || ReferenceEquals(world, _endedWorld)) return;        // after RaidEnded MainPlayer may be pooled
            Player main = world.MainPlayer;
            if (main == null || ReferenceEquals(main, Current)) return;            // ReferenceEquals: destroyed != same

            if (ReadFlag(_isReplacing))                                            // Core reports the body once it is wired
            {
                if (_holdSince < 0f) _holdSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _holdSince < MaxReplacingHoldSeconds) return;
            }

            _holdSince = -1f;
            Change(main, ReferenceEquals(Current, null) ? ReasonRaidStart : Infer(Current));
        }

        private static string Infer(Player previous)
        {
            if (previous == null) return ReasonUnknown;                             // destroyed (Unity-null)
            var health = previous.HealthController;
            return health != null && health.IsAlive ? ReasonSwap : ReasonDeath;
        }

        private void Change(Player next, string reason)
        {
            Player previous = Current;
            Current = next;
            Action<Player, Player, string> handlers = BodyChanged;
            if (handlers == null) return;
            foreach (Action<Player, Player, string> handler in handlers.GetInvocationList())
            {
                try { handler(previous, next, reason); }
                catch (Exception ex) { Log("BodyChanged subscriber failed: " + ex); }
            }
        }

        private void Forward(Action<Player, string> handlers, Player body, string text, string name)
        {
            if (handlers == null) return;
            foreach (Action<Player, string> handler in handlers.GetInvocationList())
            {
                try { handler(body, text); }
                catch (Exception ex) { Log(name + " subscriber failed: " + ex); }
            }
        }

        // ---- bound to Core by reflection: exact v1 parameter types, so no relaxed delegate binding is needed ----
        private void OnApiReplaced(Player oldBody, Player newBody, string reason)
        {
            _holdSince = -1f;
            if (ReferenceEquals(newBody, Current)) return;
            Change(newBody, ReferenceEquals(Current, null) ? ReasonRaidStart : (reason ?? ReasonUnknown)); // late attach keeps the chain rule
        }

        private void OnApiReplacementFailed(Player oldBody, string stage)
        {
            _holdSince = -1f;              // the poll reconciles with MainPlayer as soon as IsReplacing is false again
            Forward(ReplacementFailed, oldBody, stage, "ReplacementFailed");
        }

        private void OnApiDeathClaimed(Player deadBody, string driver)
        {
            Forward(DeathClaimed, deadBody, driver, "DeathClaimed");
        }

        private void OnApiRaidEnded()
        {
            _endedWorld = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : _world;
            if (!ReferenceEquals(Current, null)) Change(null, ReasonRaidEnd);
            Action handlers = RaidEnded;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception ex) { Log("RaidEnded subscriber failed: " + ex); }
            }
        }

        private void TryBind()
        {
            try
            {
                Type api = ResolveType(ApiTypeName);
                if (api == null) return;                                           // Core absent: poll only
                PropertyInfo version = api.GetProperty("ApiVersion", PublicStatic);
                if (version == null || version.PropertyType != typeof(int) || (int)version.GetValue(null, null) < 1) return;
                if (!Subscribe(api, "Replaced", nameof(OnApiReplaced))) return;    // the only required member
                Subscribe(api, "DeathClaimed", nameof(OnApiDeathClaimed));
                Subscribe(api, "ReplacementFailed", nameof(OnApiReplacementFailed));
                Subscribe(api, "RaidEnded", nameof(OnApiRaidEnded));
                _isReplacing = BoolGetter(api, "IsReplacing");
                _isAvailable = BoolGetter(api, "IsAvailable");
                MethodInfo pending = api.GetMethod("IsRespawnPending", PublicStatic, null, new[] { typeof(Player) }, null);
                if (pending != null && pending.ReturnType == typeof(bool))
                    _isRespawnPending = (Func<Player, bool>)Delegate.CreateDelegate(typeof(Func<Player, bool>), pending, false);
                IsBound = true;
                Log("bound to RespawnApi v" + version.GetValue(null, null));
            }
            catch (Exception ex)
            {
                Unbind();
                Log("binding failed, polling only: " + ex.Message);
            }
        }

        private void Unbind()
        {
            foreach (KeyValuePair<EventInfo, Delegate> subscription in _subscriptions)
            {
                try { subscription.Key.RemoveEventHandler(null, subscription.Value); }   // the SAME delegate instance
                catch (Exception) { }
            }
            _subscriptions.Clear();
            _isRespawnPending = null;
            _isReplacing = null;
            _isAvailable = null;
            IsBound = false;
        }

        private bool Subscribe(Type api, string eventName, string handlerName)
        {
            EventInfo evt = api.GetEvent(eventName, PublicStatic);
            MethodInfo method = typeof(LocalBodyTracker).GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (evt == null || method == null) return false;
            Delegate handler = Delegate.CreateDelegate(evt.EventHandlerType, this, method, false);
            if (handler == null) return false;                                     // signature drift: skip this member only
            evt.AddEventHandler(null, handler);
            _subscriptions.Add(new KeyValuePair<EventInfo, Delegate>(evt, handler));
            return true;
        }

        private static Func<bool> BoolGetter(Type api, string name)
        {
            PropertyInfo property = api.GetProperty(name, PublicStatic);
            MethodInfo getter = property != null && property.PropertyType == typeof(bool) ? property.GetGetMethod() : null;
            return getter == null ? null : (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter, false);
        }

        private static bool ReadFlag(Func<bool> flag)
        {
            if (flag == null) return false;
            try { return flag(); }
            catch (Exception) { return false; }
        }
    }
}

// ---- Usage: per-body component (DoorDash style) ----
// Plugin.Awake:
//   var bodies = gameObject.AddComponent<LocalBodyTracker>();
//   bodies.Log = m => Logger.LogInfo(m);
//   bodies.BodyChanged += (previous, current, reason) =>
//   {
//       LocalBodyTracker.Detach<RaycastBreacher>(previous);          // the corpse would keep ramming doors
//       if (current != null) LocalBodyTracker.AttachFresh<RaycastBreacher>(current);
//   };
// RaycastBreacher.Update:
//   if (!LocalBodyTracker.IsCurrentLocalBody(player)) return;
//
// ---- Usage: death-time decision (e.g. a Player.OnDead postfix) ----
//   if (!Singleton<GameWorld>.Instantiated
//       || !ReferenceEquals(Singleton<GameWorld>.Instance.MainPlayer, __instance)) return; // bot or retired swap body
//   bool? pending = LocalBodyTracker.Instance?.IsRespawnPending(__instance);
//   if (pending == true)       { /* Core holds the raid open: do not treat the raid as over yet; a Killed Stop may still follow, keep your Stop handling */ }
//   else if (pending == false) { /* Core will not replace this body; another respawn mod still might */ }
//   else                       { /* unknown (no Core, older Core, Fika): keep today's logic */ }
```

### 4.2 Fallback-Kette

1. **RespawnApi v1 gebunden und verfügbar.**
   - `Replaced` liefert den Wechsel synchron in Frame N, innerhalb von Cores Dispatch. Der Poll ist entdoppelt und hält sich zurück, solange `IsReplacing` true ist.
   - `IsRespawnPending` liefert true oder false, mit der Bedeutung aus 4.1: true heißt "Raid wird offen gehalten", false heißt "Core ersetzt nicht".
   - `RaidEnded` ist exakt.
2. **Core installiert, aber nicht verfügbar** (Fika, unvollständige Patches, kein Treiber). Der Tracker bleibt gebunden, bekommt aber keine Events. `IsRespawnPending` liefert über das Gate `IsRespawnAvailable` `null`; Wechsel meldet der Poll wie in Stufe 3.
3. **Core fehlt oder ist älter.** Nur Poll:
   - Gründe werden abgeleitet: alter Body lebendig → `swap`, tot → `death`, zerstört → `unknown`.
   - `raid-end` kommt verspätet, erst wenn die `GameWorld` verschwindet.
   - `IsRespawnPending` liefert `null`, der Konsument behält seine heutige Logik.
4. **Andere Respawn-Mods** (etwa das alte CorpseRun oder PlayerLives-artige Mods) werden über den Poll ebenfalls erfasst. Fängt ein solcher Mod CG_Spawn mit eigenem Prefix ab, liefert ein gebundener Tracker für den Tod `false` ("Core ersetzt nicht"), nicht "endgültiger Tod".

Einen Tier für das `OnPlayerRespawned` von Core 0.9 gibt es nicht. 0.9.0 wurde nie veröffentlicht.

### 4.3 Ladereihenfolge

- Es wird kein `BepInDependency` gebraucht. Soft-Dependencies auf `com.maschine.Redeploy` und `com.maschine.ArenaMode` schaden nicht.
- `Type.GetType(aqn, false)` löst über BepInEx' `AssemblyResolve` auch aus einem frühen Awake heraus auf. In einem CoreCLR-Harness mit einem Resolver nach Vorbild von `LocalResolve` hat das funktioniert. Den nativen Mono-Pfad im Spiel prüft offene Frage 1. Der Tracker bindet deshalb im Awake und versucht es pro neuer `GameWorld` erneut.
- Nie `AppDomain.GetAssemblies()` scannen. Das liefert falsche Negative, solange Core noch nicht geladen ist.
- Nie ein negatives Ergebnis cachen.
- `Chainloader.PluginInfos` im Awake ist ohne Soft-Dependency unzuverlässig, weil es inkrementell befüllt wird.
- Redeploy und ArenaMode dürfen **nie** von Konsumenten abhängen. Ein Abhängigkeitszyklus bricht das Laden aller Plugins ab.

### 4.4 Regeln für respawn-sichere Mods

1. Der aktuelle lebende Body ist nur `ReferenceEquals(GameWorld.MainPlayer, p) && p.HealthController.IsAlive`. Nicht `IsYourPlayer` (die Leiche behält es), nicht `ProfileId` (alle Bodies teilen sie), nicht `==` gegen einen Body, der zerstört sein könnte.
2. Komponenten pro Body beim Wechsel vom vorherigen Body entfernen. Die Leiche lässt sie weiterlaufen und kann sie in den Pool mitnehmen; beim Swap bekommen sie mitten im Raid `OnDestroy`. Beim Anhängen `AttachFresh` statt GetOrAdd verwenden.
3. `OnDestroy`, `UnregisterPlayer` (vanilla ruft es einen Frame nach **jedem** Tod), Unspawn, ein lokaler Tod und ein Prefix auf `LocalGame.Stop` sind keine Raid-End-Signale. Stattdessen `RaidEnded` oder einen eigenen, auf `LocalGame` gefilterten `GameEnd`-Postfix nehmen.
4. Kameras nach einem Wechsel neu auflösen. Eine vorher gecachte Kamera stirbt am Ende von Frame N. Eine im `Replaced`/`BodyChanged`-Handler gecachte überlebt, solange bis N+2 niemand den `PlayerCameraController` übernimmt.
5. Registries mit ProfileId als Schlüssel: bei `OnPersonAdd` hinzufügen oder überschreiben, beim Unspawn nur entfernen, wenn die gespeicherte Instanz der verschwundene Body ist.
6. In Todes-Handlern (`OnDead`-Postfix, `OnPlayerDeadStatic`) zuerst `ReferenceEquals(MainPlayer, body)` prüfen, denn auch ein ausgemusterter Swap-Body kann sterben. Danach `IsRespawnPending(body)` fragen, statt zu raten. `true` heißt "Raid noch nicht vorbei", ein Killed-`Stop` kann trotzdem folgen. `false` heißt "Core ersetzt nicht", nicht "endgültiger Tod".
7. In Core-Handlern und in `BodyChanged`-Handlern (in jeder Tracker-Stufe) keinen Respawn starten, den Raid nicht stoppen und keine Bodies zerstören. So etwas um einen Frame verschieben.
8. Vanilla-Events innerhalb der Pipeline (`OnPersonAdd`, `PlayerCameraController`-Created) dürfen nie werfen. Ein Wurf dort bricht den Respawn nach dem Teardown ab.

---

## 5. ArenaMode-Migration

**Schichtung:** Core-Events geben das Timing der Engine an (Frame N, Lease noch gehalten). `ArenaEvents` bleibt die eigene, "anforderer-vollständige" Schicht von ArenaMode für das Mode-Framework (N+1 oder später, nach Epoch- und Stand-down-Prüfung und `EndPipelineUse`). Beide werden nicht verschmolzen, und `OnPlayerRespawned` wird nie für Swaps ausgelöst. Grund: `TacticalMode.cs:783-789` wertet im Protected-5v5 jede nicht angeforderte Ersetzung als Fehler.

**Mit Core 1.0 (etwa 1 Tag, Build ohne neue Core-Member):**
1. `ArenaEvents.Raise` isoliert jeden Abonnenten; `ArenaMatch.ReportLocalRespawn` (`ArenaMatch.cs:111`) läuft ebenfalls isoliert (S0-1).
2. `RecoverOldBody` wird idempotent und verträgt eine Preflight-Ablehnung ohne Teardown (S0-16). Die Aufrufstelle bleibt wörtlich, wegen `tools/BleedingCleanupChecks/Verify.ps1:129-130`.
3. `ArenaRespawnDriver.cs:255` prüft den Body wie `:325-334` (S0-15).
   - Der Protected-Team-Pfad bleibt wörtlich unverändert, damit die String-Guards in `tools/PitFireTeamChecks/Program.cs:537/:897` grün bleiben.
   - `tools/TdmMatchChecks/Extract-RespawnGuards.ps1` schneidet `ShouldHoldBlackScreen` & Co. heraus, dazu den terminalen TDM-Preload-Hold in `RespawnRoutine` und den `if (failure != null)`-Zweig nach der Engine (`Extract-RespawnGuards.ps1:11-17`; `ArenaRespawnDriver.cs:160-176, 223-235`). Die Änderung bei `:255` liegt außerhalb beider Ausschnitte.
4. XML-Doku in `ArenaEvents.cs`:
   - "internes Mode-Framework von ArenaMode; Drittmods nutzen `Redeploy.Core.Api.RespawnApi`";
   - die Zusammenfassung von `OnPlayerRespawned` korrigieren: "nur nach Todes-Respawns, frühestens einen Frame nach der Engine; nie für Swaps".
5. `OnKitApplied` nach einem erfolgreichen Respawn verschieben oder löschen. Es hat keine Abonnenten.
6. Im Awake `RedeployCore.Version` gegen die Build-Konstante prüfen und bei zu altem Core warnen (3.9).
7. An `RaidLifecycle` ändert sich für ArenaMode nichts. Die Legacy-Events bleiben ungefiltert, abgesehen von Narrate-Start und Hideout-Ende (S0-7). Die Reihenfolge "`ResetSession` der Treiber vor den `ArenaSession`/`ArenaMatchService`-Handlern" bleibt erhalten, weil es nur noch einen Postfix gibt, obwohl ArenaMode `RaidLifecycle` zuerst scharf schaltet (`Plugin.cs:39` vor `:52`).

**Abbildung für die Doku:**

| ArenaEvents | RespawnApi |
|---|---|
| `OnPlayerDied` | `DeathClaimed` mit Treiber `"ArenaMode"` kommt im selben Frame direkt davor; `ArenaEvents.OnPlayerDied` folgt innerhalb von `OnPlayerDied` (`ArenaRespawnDriver.cs:72`) |
| `OnPlayerRespawned` | `Replaced(reason "death")` für Arena-Anfragen, aber mindestens einen Frame später und hinter den Arena-Prüfungen |
| (fehlt) | Live-Swaps (`LiveKitRespawn.cs:245`) sind nur als `Replaced("swap")` sichtbar |

**Nach 1.0 (über eine `NoInlining`-Bridge mit `ApiVersion`-Gate):**
- **TDM-Poll** (`TeamDeathmatchMode.cs:117-119`): Bei `Replaced` ein Flag "Rebind ausstehend" setzen und es im `Tick` unter derselben Bedingung abarbeiten, die der Poll heute schon benutzt (`!ArenaRespawnDriver.IsRespawnInProgress`). Nicht "einen Frame später": Die Fortsetzung kommt frühestens bei N+1, und die Coroutine-Reihenfolge innerhalb eines Frames ist nicht festgelegt.
- **Spectator:** `Replaced` → `Stop` + `RestoreFirstPerson` statt `ArenaEvents.OnPlayerRespawned`. Deckt Swaps mit ab, braucht aber einen In-Game-Check.
- **MovementRuntime:** `ArenaSlideController` bei `Replaced` vom alten Body entfernen.
- Die stillen Stale- und Stand-down-Ausgänge von `LiveKitRespawn` (`:157-161, 182-186, 196-200, 251-256`) bleiben still. `tools/PitFireTeamChecks/Program.cs:537-541` erzwingt, dass der `if (!sameActiveRaid)`-Block kein `Report(` enthält.
- SAIN-Fix nach dem Repro (S0-9).

---

## 6. Stufe 2+: spätere Erweiterungen

| Erweiterung | Auslöser/Voraussetzung | Risiko | Aufwand |
|---|---|---|---|
| `Replacing(old, reason)` vor dem Teardown (API v2) | Nur wenn offene Frage 6 zeigt, dass `Replaced` für einen echten Konsumenten zu spät ist. Rein beobachtend, nach dem Preflight zwischen `:103` und `:105`, danach erneute Prüfung von `RaidIsTearingDown` (Abbruch ohne Mutation, neue Stufe "cancelled"). Reentranz-Regel bleibt. | niedrig bis mittel (fremder Code direkt vor irreversiblen Schritten) | 0,5 Tage |
| Committetes `RaidEnding(exitStatus)` | Stop-Prefix speichert den Status in `__state`, ein Postfix vergleicht den Übergang nach `Stopping` (`BaseLocalGame.cs:968`). Nie aus einem Prefix, denn HarmonyX führt alle Prefixe aus. Für Killcams und CombatLog. | niedrig | 0,5 Tage |
| Treiberspezifische Ersetzungs-Vorhersage | Macht die Vorhersage von `IsRespawnPending` vor dem Claim exakt, etwa für den matchentscheidenden Team-DM-Tod. Optionaler Delegate `Func<LocalGame, bool>` pro Treiber, registriert über eine neue statische Core-Methode und aufgerufen per `NoInlining`-Bridge. Keine neue Interface-Methode (`TypeLoadException`). | niedrig | 0,5 Tage |
| Lease in Core oder synchroner Anforderer-Callback bei Commit | Erst nach dem Repro zu Frage 3 und nach S0-5. Die Fortsetzung darf `EndPipelineUse` dann nicht noch einmal aufrufen und nie einen Body veröffentlichen, der nicht mehr aktuell ist. | mittel | 1 Tag |
| Raid-Besitz des Treibers | Hat ein höher priorisierter Treiber in einem Raid einen Tod beansprucht oder eine Ersetzung angefordert, bekommen niedrigere keine Tode mehr angeboten. Verhindert den im Code belegten Cross-Driver-Claim, also den Redeploy-Prompt in einem Arena-Raid (3.3). Da Arenas Raidstart-Swap claimlos ist, muss auch der Swap den Besitz registrieren. Erst nach dem Repro. | niedrig bis mittel | 0,5 Tage |
| Waisen-Cleanup bei `create`/`rewire` | Waisen **vor** `RegisterPlayer(survivor)` unregistrieren, weil die Einträge nach ProfileId entfernt werden (`GameWorld.cs:2069-2070`). `MainPlayer` wiederherstellen. | mittel, seltener Pfad | 1 Tag |
| Generische Fixes in Core | Corpse-Render-Fix (`RespawnService.cs:66-84` = `ArenaRespawnDriver.cs:366-383`), GPUInstancer-Grass-Kamera (`ArenaGpuAcceptanceCompat.cs:84-104`) für alle Anforderer. Braucht einen GPU-Retest. | niedrig bis mittel | 1 Tag |
| Schutz-Arbitrierung | `OnSpawnProtectionEnded` nicht für einen überholten Body (`:418-421`); `DamageCoeff` nur zurücksetzen, wenn der Wert noch 0 ist (`:465`); `CursorRequestNode` mit Owner-Token (`CursorRequestNode.cs:34-36`). | niedrig | 0,5 bis 1 Tag |
| Core als `BepInPlugin` mit eigener GUID | Nur falls die Versionsprüfung über `RedeployCore.Version` nicht reicht. `[BepInDependency(coreGuid, "x.y.z")]` liefert eingebauten Mindestversions-Schutz über `DependencyErrors`, und der Chainloader wählt pro GUID die neueste Kopie. Kosten: eine Plugin-Klasse in Core, und ein harter Dependency-Fehler deaktiviert den Treiber ganz, statt ihn gegen einen älteren Core degradiert laufen zu lassen. | niedrig | 0,5 Tage |
| Facade-Zusätze (`Generation`, `RaidStarted`, `UnavailableReason`) | Nur bei konkretem Bedarf, additiv. | niedrig | je unter 0,5 Tage |
| Registry für Komponenten pro Body, Kamera-Claims | Frühestens nach Stufe 2. Muss mit Fika funktionieren, wo Core bewusst stillsteht (`IsAvailable == false`) und keine Ersetzung stattfindet. Konsumenten bräuchten dort ohnehin einen eigenen Pfad. Der Tracker deckt heute denselben Bedarf. | mittel | 2 bis 3 Tage |
| Treiber-Registrierung per Delegate für Respawn-Mods von Dritten | Erst wenn es einen solchen Konsumenten gibt. | mittel | 1 Tag |

---

## 7. Adoption pro Konsument

Für Redeploy 1.0 muss sich kein Drittkonsument ändern. Das heutige Polling funktioniert weiter. ArenaMode ändert sich ohnehin (Abschnitt 5).

| Mod | Status | Heute | Mit API v1 (über den Tracker) | Aufwand |
|---|---|---|---|---|
| ArenaMode | kompiliert, unveröffentlicht | eigene `ArenaEvents`, Polls | siehe Abschnitt 5 | 1 Tag (1.0) |
| KillcamReplay | eingesetzt | Das Replay startet bei jedem abgefangenen Tod über dem Prompt; der Respawn schneidet es ab, weil die Kamera ab R+1 Unity-null ist (`ReplayController.cs:207-211`) | `OnDead`-Postfix (`ReplayPatches.cs:70-131`): erst `ReferenceEquals(MainPlayer, __instance)`, dann `IsRespawnPending(__instance)`. Ob das Replay bei `true` gekürzt wird, entscheidet das Produkt. Der Stop-Handoff (`DeferStopPatch`) **bleibt nötig**, weil trotz `true` ein Killed-`Stop` folgen kann (Give-up, fehlendes Item, Match-Ende). Bekannte Abweichung: Beim matchentscheidenden Team-DM-Tod sagt die Vorhersage true, kurz darauf gilt false. `BodyChanged`/`Replaced` mit dem Opfer als altem Body → `StopPlayback("replacement")` in Frame N, solange die alte Kamera noch lebt. Den alten `PlayerCameraController` nicht mehr anfassen, er ist bereits zerstört. `ArenaSession.ReplaySeconds` bleibt als Arena-Vertrag. | 0,5 bis 1 Tag |
| Killcam | nicht eingesetzt | Reflection in die privaten Interna von Redeploy.Client; übersieht ArenaMode-Tode | `IsHandlingDeath = IsRespawnPending(deadPlayer) ?? legacy`, das Stop-Handling bleibt; `Replaced` mit dem Toten als altem Body → Abbruch ohne Kamera-Create/Destroy | 0,5 Tage |
| AutoIFF | eingesetzt | `LocalPlayerWatcher` pollt; Raid-Ende über eine Heuristik aus `UnregisterPlayer` und Status | `BodyChanged` ersetzt den Watcher (gleicher Code); Raid-Ende über `RaidEnded` oder einen Vanilla-`GameEnd`-Postfix | 2 Stunden |
| CombatLog | eingesetzt | Das Pending-Fatal-Flag schleppt sich ins nächste Leben; das Raid-Ende rastet schon beim ersten Stop-Prefix ein | `DeathClaimed` → `ResolvePendingFatal` + neues Segment; Raid-Ende auf einen `LocalGame`-gefilterten `GameEnd`-Postfix (funktioniert auch ohne Core) | 0,5 Tage |
| DoorDash (Dritt-Mod, lokaler Fork) | eingesetzt | Pollt, aber der Breacher läuft auf jeder Leiche weiter | `BodyChanged` → `Detach`/`AttachFresh`; `Update` gegen `IsCurrentLocalBody` absichern. Upstream nur, wenn der Autor es will. | 1 Stunde |
| CombatSlide | nicht eingesetzt | `SlideController` bleibt auf der Leiche oder dem Pool-Body | Vanilla reicht: Selbstzerstörung, wenn nicht mehr aktueller Body; optional Tracker | 1 Stunde |
| BangAndClear | nicht eingesetzt | Beim Swap steuert die Sequenz den alten Body weiter | `Replaced` mit dem Sequenz-Spieler als altem Body → `Fail("replaced")`; vanilla: Identität prüfen | 1 Stunde |
| BtrTurretControl | nicht eingesetzt | Der Exit läuft erst mit dem neuen Body | `DeathClaimed` → `ExitUnifiedMode(force)` mit dem sterbenden Body; vanilla: `enteredBody` merken | 2 Stunden |
| Airburst | nicht eingesetzt | Die Range-Sperre überlebt | `BodyChanged` → `AirburstRangeLock.Clear()` | 30 Minuten |
| ScopeRangefinder | eingesetzt | Auto-Zero bleibt bei der Leiche | `BodyChanged` → `RestoreAutoZero()` + `DestroyReticleCommandBuffer()` | 1 Stunde |
| AutoCorpseSearch | eingesetzt | `JobValid` ist korrekt | nichts, optional Queue leeren | 0 |
| KillAndDamageInfo | nicht eingesetzt | veralteter Killer-Snapshot | ohne API: `KillerSnapshot.Clear()` vor der Rückkehr bei Killer = null (`DeathScreenPatches.cs:41-43`) | 10 Minuten |
| SAIN (Dritt-Mod) | eingesetzt | Beim Swap-Unspawn wird der Eintrag nach ProfileId entfernt (nur im Referenz-Quelltext belegt) | Gefahr ist dokumentiert; Fix in ArenaMode nach dem Repro | nicht zutreffend |
| PitFireTeam (Dritt-Mod) | eingesetzt | Der Boss-Wrapper bleibt nach Redeploy-Respawns an die Leiche gebunden | als bekannte Inkompatibilität dokumentiert | nicht zutreffend |
| rund 26 weitere | | lesen `MainPlayer` bei jeder Nutzung neu | nichts | 0 |

---

## 8. Tests

1. **`Redeploy.Tests`** (neu; net10.0, xunit, `Xunit.SkippableFact`, gleiches Link-Muster wie `ArenaMode.Tests`). Verlinkt werden nur reine Dateien.
   - `SignalDispatcher`:
     - ein werfender Handler hält die folgenden nicht auf, und das Log nennt seine Assembly;
     - Snapshot-Semantik beim An- und Abmelden während eines Raises, Reihenfolge bleibt die Abonnement-Reihenfolge;
     - `Depth` ist in Handlern größer 0 und danach 0, auch nach Exceptions;
     - Raises der `Lifecycle`-Instanz erhöhen das `Depth` der `Respawn`-Instanz nicht;
     - verschachtelte Raises funktionieren;
     - zerstörte Targets werden übersprungen;
     - Symmetrie: dieselbe Delegate-Instanz meldet ab, ein neu erzeugtes Lambda nicht (die Lehre aus Fika und MCS).
   - `RespawnPreflight`: die vollständige Ablehnungsmatrix mit Exception-Typen, einschließlich `!IsAvailable`; eine gesunde Anfrage ergibt `null`.
   - `RespawnClaimLogic.Decide`, Wahrheitstabelle:
     - Vorhersage zur `OnDead`-Zeit;
     - Dispatch-Fenster: immer true, auch wenn `RaidIsTearingDown` gilt (Arena-Claim während `Stopping`);
     - Treiber hält ja/nein (Give-up, Stand-down, terminaler TDM-Hold);
     - Redeploy-Prompt ohne Respawn-Item: true bis zum Give-up, danach false vor dem `Stop(Killed)`;
     - matchentscheidender Team-DM-Tod: Vorhersage true, nach dem Claim false (dokumentierte Heuristik-Abweichung);
     - Treiber wirft (fail-open) → false;
     - bereits verarbeitet und unbeansprucht;
     - nach `ReplacementFailed` in `prepare`/`create` bleibt der tote `game.LocalPlayer` false, auch wenn ein anderer Treiber ihn beanspruchen würde (Arena-Raid mit Redeploy);
     - ausgemusterter Swap-Body stirbt → false;
     - fremder CG_Spawn-Prefix, kein Core-Claim → false;
     - Bot oder alte Leiche;
     - Raid endet;
     - nicht verfügbar;
     - Operandenreihenfolge von `IsAvailable`: Die Fika-Prüfung läuft zuletzt (S0-12).
2. **Vertragstest** (SkippableFact, übersprungen, wenn DLL oder Managed-Ordner fehlt). `System.Reflection.MetadataLoadContext` öffnet **genau den Build, der ins Zip geht**, für das Release also den Release-Build, dazu `EscapeFromTarkov_Data/Managed`, `BepInEx/core` und `BepInEx/plugins/spt`. Ein Deploy-Check vergleicht den Hash von `BepInEx/plugins/maschine-Redeploy.Core.dll` mit dem paketierten Build. Heute ist dort der Debug-Build deployt.
   - Jede Zeile der eingecheckten `Redeploy.Core/Api/api-v1.baseline.txt` muss unverändert vorhanden sein. Beispiele:
     - `event Replaced System.Action`3[EFT.Player,EFT.Player,System.String]`
     - `method IsRespawnPending System.Boolean(EFT.Player)`
     - `property ApiVersion System.Int32 get`
     - die Const-Werte
     - die vollständigen Memberlisten von `IDeathInterceptionDriver` und `ISpawnSelector`
   - `ApiVersion` muss eine Property sein, kein Literal.
   - Drift-Check der Interop-Datei: Jedes String-Literal, das als Membername dient, muss in der Baseline stehen, mit exakt der Handler-Signatur (die Lehre aus SAIN und LootingBots). Das gilt auch für Namen, die indirekt über `Subscribe(api, "Replaced", ...)` und `BoolGetter(api, "IsReplacing")` übergeben werden. Ein Scan nur der `GetEvent`/`GetProperty`-Aufrufstellen würde sie übersehen.
   - Old-Binary-Check: Mit `System.Reflection.Metadata` alle TypeRefs und MemberRefs von `ArenaMode.Client.dll` und `Redeploy.Client.dll` aufzählen, die auf `maschine-Redeploy.Core` zeigen, und jede mit Name und Signatur gegen den neuen Core auflösen. Genau dieser Check fängt zum Beispiel einen `void`→`bool`-Wechsel an `RaidLifecycle.EnsurePatched`.
   - InterfaceImpl-Check: Für jeden Typ in diesen DLLs, der ein Core-Interface implementiert (`ArenaRespawnDriver`/`RedeployDriver`: `IDeathInterceptionDriver`; `ArenaSpawnSelector`, `FixedSpawnSelector`, `Prepared`: `ISpawnSelector`), muss die Memberliste des Interfaces im neuen Core exakt der Baseline entsprechen. Die Implementierungen sind implizit (0 MethodImpls, keine MemberRefs), der MemberRef-Check sieht eine neue Interface-Methode also nicht.
   - Laufzeit-Bindungstest (SkippableFact): Er lädt das echte gebaute Core über einen `AssemblyResolve`-Handler nach Vorbild von `LocalResolve` und führt `TryBind` der unveränderten Interop-Datei aus. Erwartet werden `IsBound`, genau ein `Replaced`-Abonnent und nach `OnDestroy` null Abonnenten auf allen vier Events. Ein solcher Harness lief in der Verifikation gegen eine Mock-Core mit der wörtlichen Fassade in etwa 1 s.
3. **`tools/RespawnInteropChecks`** (net10-Konsole mit Stubs für EFT und Unity, im Stil der ArenaMode-`tools`; Unity-null wird emuliert). Die Interop-Datei wird unverändert verlinkt.
   - Tier-Äquivalenz auf dem Erfolgspfad: Ein geskripteter Raid (Start, Todes-Respawn, Swap, Ende) erzeugt mit Core-Stub und mit `ResolveType → null` dieselbe Folge von (`previous`, `current`)-Paaren. Abweichen dürfen nur der Zeitpunkt von `raid-end`, der Auslöser (Dispatch gegen `Update`) und bei Swaps nach Zerstörung des Alt-Bodys der Grund (`swap` gegen `unknown`).
   - Fehlerpfad: Nach einem `ReplacementFailed` in `create`/`rewire` meldet der Poll alt→Waise, bei Recovery Waise→alt. Das ist dokumentiertes Verhalten und wird genau so geprüft.
   - Late Attach: Kommt als erste Beobachtung ein `Replaced`, ist der Grund `raid-start` mit `previous == null`.
   - `Replaced` und Poll im selben Frame ergeben genau ein Event.
   - Ketten-Invariante: `previous` ist immer das vorherige `current`.
   - `MainPlayer` kippt drei Frames vor `Replaced`, während `IsReplacing` true ist: kein Poll-Event.
   - Nach `RaidEnded` mit einem Pool-Body als `MainPlayer` kommt kein falsches `raid-start`.
   - `IsRespawnPending` liefert `null`, wenn der Stub `IsAvailable == false` meldet (Fika), auch wenn die Methode gebunden ist.
   - Teilbindung: Fehlt `ReplacementFailed` oder ist seine Signatur verändert, bleibt der Tracker trotzdem gebunden.
   - Nach `OnDestroy` hat jedes Stub-Event null Abonnenten.
   - Ein werfender Konsumenten-Handler bricht den Tracker nicht.
4. **Golden-Order-Source-Guard** (PowerShell wie `Invoke-ArenaSourceChecks.ps1`):
   - Die Anweisungen von `RunRespawn` in `:105-116` und `:196-236` stimmen mit einer extrahierten, eingecheckten Liste überein.
   - Kein `?.Invoke` auf öffentlichen Core-Events; alles läuft über den Dispatcher.
   - `FreecamCompatHelper.EnablePatchesIfPresent` wird im GameStarted-Postfix nur innerhalb eines `try` aufgerufen.
5. **In-Game-Probe** `tools/RespawnApiProbe` (GUID `com.maschine.AAA.RespawnApiProbe`, sortiert vor beiden Treibern).
   - Sie enthält die Interop-Datei unverändert und einen zweiten Abonnenten, der immer wirft.
   - Zu jedem Event loggt sie `Time.frameCount`, `IsReplacing`, `IsRespawnPending` und `IsAvailable`.
   - Config-Schalter:
     - "`RunRespawn` aus `Replaced`";
     - "`RunRespawn` während `Stopping`";
     - "`RunRespawn` während laufender Pipeline" (prüft S0-16 im Live-Swap);
     - "Tod kurz nach dem Raidstart-Swap" (offene Frage 3).

   Die Szenarien stehen in Abschnitt 10.
6. **Bestehende Checks** müssen grün bleiben: `ArenaMode.Tests`, `tools/TdmMatchChecks`, `tools/PitFireTeamChecks`, `tools/BleedingCleanupChecks`.

---

## 9. Bewusst verworfen

- **Transaktionale Pipeline, `SpawnSelector.Resolve`/`GetNextPlayerId` vor den Teardown ziehen, Rewire-Schritte als "best effort" einstufen:** Das ordnet erprobte Schritte für alle vier Aufrufer um. Das eigentliche Ziel, eine Ablehnung vor dem Teardown, erreicht der Preflight ohne jede Umordnung. Der einzige echte reihenfolgeabhängige Seiteneffekt ist `ArenaSpawnSelector.Remember` auf dem Pfad ohne Area (`ArenaSpawnSelector.cs:56`), der bei abgelehnten Anfragen verbraucht würde. `ArenaAreaRuntime.PickRespawn` (`:380-395`) hält keinen Zustand, und mit aktiver Area löst ArenaMode den Spawn schon heute vor `RunRespawn` auf (`ArenaAreaRespawn.TryPrepare`, `ArenaAreaRespawn.cs:9-26`). Für diesen Teil trägt das Umordnungs-Argument also nicht.
- **Rollback und Recovery in Core** (Portierung von `RecoverOldBody`/`RecoverReplacementBody`, `RecoveryPolicy`/`OldBodyFate`/`ReplacementOutcome`), `TryBeginReplacement`/`ReplacementHandle`, `RunRespawn` als Wrapper: großer Umbau für seltene Pfade. ArenaMode müsste seine Respawn-Flows neu schreiben, und ein älterer Core würde ArenaMode abschalten. Der kleine Idempotenz-Fix in ArenaMode (S0-16) reicht.
- **Lease-Freigabe in Frame N über `OnReplaced`**, bevor S0-5 steht: öffnet von Ende N bis N+2 das Fenster mit `MyPlayer == null`.
- **`ArenaRespawnDriver` beansprucht auch bei `IsRespawnInProgress`:** Die erste Fortsetzung ruft `EndPipelineUse` auf und überschreibt damit die neue Lease; `:255` veröffentlicht dann einen toten Body.
- **Swap-Bodies per `SetActive(false)` parken, Early-Unspawn in Core, SAIN-Handler per Reflection aus Delegate-Listen entfernen:** unverifiziert. Parken sammelt bei jeder Rundenpause Bodies an; Early-Unspawn betrifft viele Vanilla-Abonnenten.
- **Registry für Komponenten pro Body und Kamera-Claims in v1:** Unter Fika findet keine Ersetzung statt (kein CG_Spawn-Claim, kein `RunRespawn`), und Core steht dort bewusst still (`IsAvailable == false`). Eine Registry, die über Core anhängt, würde also nichts anhängen, und Konsumenten bräuchten trotzdem einen eigenen Pfad. Das DoorDash-Beispiel aus dem Lifecycle-Entwurf bräche unter Fika. Nicht der Grund ist ein fehlender Raidstart: Ist `RaidLifecycle` gepatcht, feuert Cores Start auch für `CoopGame`. Der Tracker deckt denselben Bedarf.
- **Austausch von Vanilla-Event-Backing-Fields** (`_onPersonAdd`, Camera-Created) während der Erzeugung: fragile Reflection auf einem heißen Pfad.
- **Große String-Zustandsmaschine** (`Phase`, `Availability`, `kind/detail`-Reasons, acht Capability-Tokens), `Settled` (die Kamera ist schon bei `Replaced` final, `CameraRebindHelper.cs:41-45`), `BodyRetired`, `CurrentBody`/`CurrentRequester`: zu viel, um es jahrelang eingefroren zu halten.
- **`Supports(string)` als primäres Gate:** Fähigkeit wird über die Existenz des Members geprüft.
- **Claim-Modus `"held"` in `DeathClaimed`:** Beim Raise nicht bekannt. `TacticalMode.HoldRespawn` (`:294`) wird erst in `OnLocalDeath` gesetzt (`:762`), und das läuft über `ArenaEvents.OnPlayerDied` innerhalb von `OnPlayerDied` (`ArenaRespawnDriver.cs:72`), also nach dem Raise.
- **Pending-Latch, der nur im Stop-Prefix von Core oder erst bei `RaidEnding` gelöscht wird:** Der Wert hinge von der GUID-Reihenfolge der Prefixe ab. Aus der GUID-Sortierung abgeleitet, aber nicht beobachtet: Ohne ArenaMode läuft `DeferStopPatch` von KillcamReplay vor dem Prefix von Core. Der einzige Log-Beleg (`LogOutput.log:7370-7375`) stammt aus einer Installation mit ArenaMode, in der Cores Prefix zuerst registriert ist. Außerdem bliebe der Latch nach einem Give-up mit zurückgestelltem `Stop(Killed)` true. Die Live-Abfrage hat das Problem nicht, weil `HasGivenUp` vor `InvokeDeathScreen` gesetzt wird (`RespawnService.cs:124-128`).
- **`PeekDeathClaim` als Prefix auf `LocalPlayer.OnDead`:** heißer Pfad, der für jeden Bot läuft. Die reine Live-Auswertung liefert dieselbe Antwort.
- **Neue Interfaces oder Interface-Methoden auf Treiberklassen** (`IDeathClaimStatus`, `IDeathClaimModeProvider`, neue Member in `IDeathInterceptionDriver`/`ISpawnSelector`): Gegen einen älteren Core gibt es beim Laden der Treiberklasse im Awake eine `TypeLoadException`, bevor irgendein Versions-Gate greifen kann. Die vorhandene Methode `ShouldHoldBlackScreen()` reicht für v1. Eine exakte Vorhersage kommt, falls nötig, per Delegate-Registrierung in Stufe 2.
- **GameEnd auf zwei Postfixe aufteilen:** vertauscht in ArenaMode die Reihenfolge von `ResetSession` und `ArenaSession`.
- **Nur das Legacy-`OnRaidEnded` auf `LocalGame` filtern:** entkoppelt es unter Fika vom Start.
- **`IsRespawnInProgress` beim Abbruch im Cooldown löschen:** schaltet `ShouldGuardOwnerCleanup` ab.
- **Generationsnummer bei `:233-234` erfassen:** Off-by-one, jede Finalisierung würde abbrechen.
- **Öffentliches `ReplacementFailed("rejected")` bei Preflight-Ablehnung:** reines Rauschen, denn es hat sich nichts geändert, und aus einem Handler heraus wäre es ein verschachtelter Raise.
- **Interop-Tier für `OnPlayerRespawned` aus Core 0.9:** nie veröffentlicht. Die Lifecycle-Variante etikettiert Swaps außerdem als `death` und liefert `old = null`. Relaxed Binding eines `Player`-Handlers an `Action<LocalPlayer>` wäre dagegen kein Hindernis: Unitys Mono-`mscorlib` erlaubt es (`Delegate.arg_type_match`).
- **Wechsel der GameWorld-Instanz als maßgebliches Raid-Ende:** kommt erst nach `LocalRaidEnded` und dem Session-Result. Es bleibt nur als dokumentierter, verspäteter Fallback.
- **Separate Contracts-Assembly, Strong Naming:** eine Datei mehr zum Ausliefern und dasselbe Problem bei fehlendem Core. Core als `BepInPlugin` ist **nicht** verworfen, sondern auf Stufe 2 verschoben (Abschnitt 6). Die frühere Begründung "zweite Kopie, getrennte Statics" hält nicht: Bei genau einer Datei und dem GUID-weisen Chainloader ist das ausgeschlossen.
- **Vetos oder Holds durch Konsumenten, Abonnenten-Prioritäten:** Ein Mod könnte den Respawn eines anderen blockieren.
- **Selbstaktivierende `add`-Accessoren:** unnötig, weil Core nur zusammen mit einem Treiber ausgeliefert wird. Sonst würde DeathInterception samt `DeadBodyGuard` ohne Treiber installiert.
- **Gleicher Frame als Vertrag; `Stop`/`RunRespawn` aus Handlern erlauben:** Das erste ist nur beobachtet, das zweite bricht die Reentranz-Regel.
- **Zustellung von `BodyChanged` in der Core-Stufe auf den nächsten Frame verschieben**, damit beide Stufen gleich getaktet sind: Dann wäre die alte Kamera schon tot, und genau für den Frame-N-Zeitpunkt existiert `Replaced`. Stattdessen gilt Regel 7 für alle Stufen.
- **Neues öffentliches `ArenaEvents.OnLocalBodyReplaced` in 1.0:** Arena-Subsysteme, die jede Ersetzung sehen müssen, abonnieren später `RespawnApi.Replaced`.
- **`CoreVersion` in der Fassade:** `RedeployCore.Version` gibt es bereits (`RedeployCore.cs:15`), als Laufzeit-Property. Die Treiber-Prüfung nutzt es (3.9).

---

## 10. Offene Fragen, die ein In-Game-Check klären muss

Alle Punkte laufen über die Probe aus Abschnitt 8.5.

1. **Bindung aus einem frühen Awake:** Lädt `Type.GetType(aqn)` Core im Spiel (nativer Mono-Pfad) per `LocalResolve`, bevor irgendein Treiber sein Awake durchlaufen hat? Im CoreCLR-Harness funktionierte es. Wenn nicht, bleibt es beim erneuten Versuch pro `GameWorld`.
2. **Frame-Abstand:** Wie viele Frames liegen zwischen `Replaced`, der Fortsetzung des Anforderers (N+1 oder N+2), `FinalizeRespawnView` und `EnsureBattleUiReady`? Fällt `Replaced` mit dem Wechsel von `MainPlayer` in denselben Frame, auch mit SAIN, BigBrain und UIFixes, also bleibt die Erzeugung synchron?
3. **Todesfenster nach der Pipeline:** Der Code belegt, dass nur der anfordernde Treiber ablehnt. Offen ist das tatsächliche Verhalten bei einem Tod innerhalb von etwa 0,5 s nach dem Raidstart-Swap (Protection = null): Zeigt Redeploy (installiert) seinen Prompt im Arena-Raid, und blockiert sich der Raid ohne Redeploy? Das entscheidet über die Stufe-2-Punkte "Lease" und "Raid-Besitz". Eine günstige Übergangslösung wäre ein kurzer Schutz für den Raidstart-Swap.
4. **Hideout und Narrate:**
   - Der Hideout ruft `OnGameStarted` nicht auf. Der `HideoutGame`-Filter am Ende ist deshalb unbedingt sicher und steht in S0-7. Offen ist nur, ob der `GameEnd`-Postfix über geteilte Generics dort überhaupt feuert (Log-Probe beim Verlassen des Hideouts). Ohne Filter würde `ArenaSession.OnRaidEnded` `ArmedForNextRaid` löschen (`ArenaSession.cs:366-370`).
   - Sind Trader-Narrate-Szenen in SPT 4.1 erreichbar? Wenn ja: Unterdrückt der `NarrateGameWorld`-Filter den Start, bleibt ArenaMode armiert, und unterbleibt `ConfirmOrdinaryRaidStarted`?
5. **SAIN nach Live-Swap:** Entspricht das deployte SAIN 4.5.1 dem Referenz-Quelltext? Ist die `PlayerComponent` des lokalen Spielers weg, und hören und sehen Bots den Spieler noch? Entscheidet S0-9.
6. **KillcamReplay-`StopPlayback` im `Replaced`-Handler:** Keine Exception am bereits zerstörten alten `PlayerCameraController`, kein Bildfehler? Entscheidet, ob `Replacing` (Stufe 2) überhaupt gebraucht wird.
7. **Fika-Testbett (`D:\SPT41Fika`):**
   - `IsAvailable == false`, keine Core-Events, der Tracker liefert Poll-Wechsel und `IsRespawnPending == null`;
   - die eigene Leiche bekommt einen `DeadBodiesController`-Eintrag (S0-11);
   - Fika-Ablehnung und Missed-GameEnd-Verhalten von `ArenaSession` sind unverändert, auch in einer reinen Redeploy-Installation, in der der Legacy-Start nach S0-7 jetzt ebenfalls feuert.
8. **Cooldown-Race nach S0-2:** Mit MIA, Extract und Menü-Verlassen während des Cooldowns, einmal mit Cooldown-Ende zwischen `Stop` und `GameEnd` und einmal danach: kein Pipeline-Log, kein Killed-Panel, der Exit-Status bleibt erhalten.
9. **Fail-closed nach S0-4:** Debug-Build mit einem umbenannten Patch-Ziel. Beide Awakes laufen durch, `IsAvailable == false`, Tode laufen vanilla, kein Guard-Prefix blockiert. ArenaModes Raidstart-Swap wird abgelehnt und behält dank S0-16 den alten Body, ohne Killed-Ende.
10. **Reentranz:** `RunRespawn` aus einem `Replaced`-Handler wird abgelehnt, es läuft genau eine Pipeline, und die Kamera bleibt auf dem neuen Body.
11. **Preflight-Ablehnung im Live-Swap (S0-16):** Mit dem Schalter "`RunRespawn` während laufender Pipeline" gibt es keinen doppelten `RegisteredPlayers`-Eintrag, keine `ItemOwners`-Exception und kein `EndRaidAsLastResort`; der Spieler behält seinen Body.
12. **Pool-Bodies:** Überleben Mod-Komponenten auf zurückgegebenen Leichen `UnloadTemporaryPools` bis in den nächsten Raid? Das bestimmt, wie dringend `AttachFresh` und eine spätere Registry sind.
13. **PitFireTeam:** Wie verhalten sich die Follower nach einem gewöhnlichen Redeploy-Respawn, wenn der Boss-Wrapper an die Leiche gebunden bleibt? Ergebnis ist ein Doku-Eintrag oder eine Anfrage an Upstream.

---

## Verifikationsprotokoll

**Übernommene Korrekturen (widerlegte Aussagen)**
- Kurzfazit: "keinen einzigen Abonnenten" auf die drei Respawn- und Todes-Events eingeschränkt; die sieben ArenaMode-Abonnenten der `RaidLifecycle`-Events genannt.
- 3.3 `Replaced`: "beide Treiber lehnen ab" ersetzt durch "nur der anfordernde Treiber lehnt ab"; der Cross-Driver-Claim durch Redeploy in Arena-Raids (bzw. der Soft-Lock ohne Redeploy) steht jetzt im Vertrag; Stufe-2-Zeile "Raid-Besitz" entsprechend geschärft.
- ArenaMode-`Plugin.cs`-Zeilen korrigiert: `EnsurePatched` `:52`, Treiber-Instanziierung/-Registrierung `:53`, Patch-Sets `:54-93` (S0-4, S0-7, 3.9, Abschnitt 5).
- Abschnitte 6 und 9 (Registry): "unter Fika kein Raidstart in Core" ersetzt durch "unter Fika keine Ersetzung und Core steht bewusst still"; Cores Start feuert für `CoopGame`, sobald `RaidLifecycle` gepatcht ist.
- Abschnitt 9 (Umordnung): `PickRespawn` ist zustandslos, und mit Area löst ArenaMode schon vor `RunRespawn` auf; als einziger echter Seiteneffekt bleibt `Remember`; die Ablehnung stützt sich jetzt darauf, dass der Preflight das Ziel ohne Umordnung erreicht.
- `IsRespawnPending`: neu definiert als "Core-Treiber hält den Raid offen, Ersetzung möglich, nicht versprochen, Killed-Stop kann folgen"; "no Stop is coming" aus dem Usage-Snippet entfernt; KillcamReplay/Killcam behalten ihr Stop-Handling (3.1, 4.1, 4.4, 7).
- Vorhersage vor dem Claim als Heuristik dokumentiert, mit dem matchentscheidenden Team-DM-Tod als bekannter Abweichung; Wahrheitstabellen-Test ergänzt; exakte treiberspezifische Vorhersage als Stufe-2-Zeile (Delegate statt Interface).
- 3.4 Swap: "`OnPlayerDead` kommt nie" → "in der Regel nie"; ein ausgemusterter Swap-Body kann weiterleben und sterben (Ablehnungsbedingungen von `TryDestroy`); Usage und Regel 6 prüfen jetzt zuerst `ReferenceEquals(MainPlayer, body)`; `false` heißt "Core ersetzt nicht", nicht "endgültiger Tod".
- Tracker: `IsRespawnPending` liefert `null`, wenn `IsRespawnAvailable` false ist (vorher unter Fika fälschlich `false`); 4.2 als vierstufige Kette neu geschrieben.

**Übernommene Einschränkungen (abgeschwächte Aussagen)**
- Kurzfazit: Poll "nie halb verdrahtet" gilt nur auf dem Erfolgspfad; Fehlerstufen zeigen dem Poller Waisen-Bodies.
- S0-2: Killed-Panel nur bei Cooldown-Ende zwischen `Stop` und `GameEnd`; danach kein Panel, aber Pipeline gegen ein `Stopped`-Spiel nach `CleanUp`.
- S0-3/3.5: Evidenz zum null-`Game` korrigiert (erster sicherer Wurf bei `:114`, nach `:110`/`:112`); null-`request` wirft bei `:94` außerhalb jedes `try` und lässt das Flag stehen.
- S0-3: Unerreichbarkeit des Waisen-Pfads zusätzlich an "kein synchroner `Stop` im Abschnitt `:105-146`" gebunden.
- S0-10: "Raid endet nie" → Soft-Lock bis MIA-Timer oder Menü-Exit.
- 3.1: Redeploys `ShouldHoldBlackScreen` enthält auch `Settings.Enabled.Value`.
- 3.4: Wiederherstellung des Alt-Bodys nur außerhalb begrenzter Areas und im selben aktiven Raid.
- Abschnitt 5: Bullet "LiveKitRespawn meldet auf jedem Ausgang ein Ergebnis" gestrichen; die stillen Stale-Ausgänge bleiben still, weil `PitFireTeamChecks/Program.cs:537-541` das erzwingt.
- Abschnitt 5: Ausschnitte von `Extract-RespawnGuards.ps1` vollständig benannt; Schlussfolgerung unverändert.
- S0-9 und Abschnitt 7: SAIN-Mechanismus nur im Referenz-Quelltext belegt, Binary nur über Methodennamen.
- Abschnitt 9: Prefix-Reihenfolge von `DeferStopPatch` ohne ArenaMode als abgeleitet, nicht beobachtet markiert.
- 3.9/8.2: Paketierung als Soll-Zustand gekennzeichnet (keine Client-Paketierung, Debug-Build deployt); der Vertragstest prüft den paketierten Build, dazu ein Hash-Check des Deploys.
- `ReplacementFailed`: `Resolve` markiert `oldBody` als verarbeitet (Pseudocode 3.2), sonst liefe die Vorhersage in Arena-Raids mit Redeploy wieder auf true.
- Preflight-Ablehnung: ArenaModes `RecoverOldBody` registriert einen intakten Body doppelt, `ItemOwners.Add` wirft, Folge `EndRaidAsLastResort`; neuer Release-Blocker S0-16 und Punkt 2 in der ArenaMode-Migration.
- `IsAvailable == false` bedeutete bisher nicht "Core untätig": S0-4 ergänzt ein Preflight-Gate und lässt alle Guard-Prefixe durch; CG_Spawn-Gate auf `IsAvailable` umgestellt.
- Reentranz: `Depth` zählt nur Todes- und Pipeline-Raises (`CoreSignals.Respawn`), Lifecycle-Raises nicht; der synchrone Start des Arena-Swaps aus `OnRaidStarted` wird deshalb nie abgelehnt.
- `DeathClaimed`: `IsPending` prüft das Dispatch-Fenster vor dem Teardown-Check, damit die Garantie "pending == true" auch bei einem Arena-Claim während `Stopping` hält; die Status-Aussage ist entsprechend eingeschränkt.
- `RaidEnded`: Die Garantien gelten nur, wenn der synchrone Teil von `GameEnd` nicht wirft; Anforderer-Coroutinen laufen nach `RaidEnded` weiter; Core unterdrückt Pipeline-Events nach `RaidEnded` (`RaidEndedFor`), und die "genau einmal"-Ausnahme ist neu formuliert.
- S0-14: Freecam-Aufruf in eigenem `try/catch` und unabhängig vom Legacy-Dispatch; Source-Guard ergänzt.
- S0-7: `NarrateGameWorld` beim Start gefiltert (Narrate-Szenen feuern `OnGameStarted` ohne Ende); `HideoutGame`-Filter am Ende unbedingt, weil der Hideout keinen Start auslöst; offene Frage 4 umformuliert.
- Tracker-Stufen: Zustellung innerhalb der Pipeline gegen Zustellung aus `Update` dokumentiert; Regel 7 gilt für `BodyChanged` in jeder Stufe; Fehlerpfad-Wechsel (alt→Waise, Waise→alt) dokumentiert; Late-Attach liefert jetzt `raid-start`; Test 8.3 angepasst.
- Gecachte Kamera überlebt nur, solange bis N+2 niemand den `PlayerCameraController` übernimmt (3.3, Regel 4).
- 8.2: InterfaceImpl-Check für `IDeathInterceptionDriver`/`ISpawnSelector` ergänzt; `ISpawnSelector` in 3.1/3.9 als eingefroren aufgenommen.
- 3.9: Skew-Erkennung auf Fix-Ebene über `RedeployCore.Version` gegen eine Build-Konstante, plus `DependencyErrors`-Eintrag; Downgrade über ältere Zips und fehlendes Tooling benannt.
- Abschnitt 9/3.9: BepInPlugin-Begründung "getrennte Statics" gestrichen; `BepInPlugin` mit Mindestversions-Dependency als Stufe-2-Option aufgenommen.
- Abschnitt 9: Relaxed Binding ist unter Mono überprüfbar und funktioniert; die Ablehnung des 0.9-Tiers stützt sich nur noch auf die übrigen Gründe.
- S0-12: Begründung korrigiert (die Fassade prüft Fika zuletzt, vergiftet wird nur ein direkter Aufruf); Operandenreihenfolge dokumentiert und getestet.
- 8.2: Drift-Check deckt auch indirekt übergebene Membernamen ab; Laufzeit-Bindungstest gegen das echte Core ergänzt.
- Aufwand auf 6 bis 7 Personentage angehoben (S0-16, Gates, Tests); ArenaMode-1.0 auf 1 Tag.

**Selbst im Code gegengeprüft**
- `GameWorld.RegisterPlayer` hat keinen Duplikat-Schutz (`GameWorld.cs:2012-2057`, `ItemOwners` `:507`).
- `RecoverOldBody` (`LiveKitRespawn.cs:329-366`) und sein Aufruf ohne Intaktheitsprüfung (`:245-264`).
- `IsSameActiveRaid` verlangt `Status == Started`, die `RaidIsTearingDown`-Ablehnung ist also unkritisch (`ArenaRespawnDriver.cs:445-449`).
- Arenas Todes-Pfad versucht es nach einem Fehler nicht erneut; die Markierung als verarbeitet ist daher sicher (`ArenaRespawnDriver.cs:223-247`).
- Kette zum Team-DM-Ergebnis in `OnDead`: `Player.cs:30096-30101`, `ArenaMatch.cs:72-89`, `TeamDeathmatchMode.cs:414`; der Hold greift nur bei gesetztem `LastAggressor`.
- Narrate-Start: `TarkovApplication.cs:569-586`, `NarrateGameWorld : ClientLocalGameWorld`, `NarrateGame : AbstractGame`; `OnGameStarted`-Aufrufer nur `:586/:814/:2897/:3018`.
- Folgen eines Narrate-Starts in `ArenaSession.OnRaidStarted` (`:123-224`).
- Prompt ohne Item (`RespawnPromptPanel.cs:102`, `RespawnService.cs:49-51`); `ShouldHoldBlackScreen` samt `Settings.Enabled` (`RedeployDriver.cs:62-68`).
- `ArenaMode/Plugin.cs:52-53`.
- `ArenaAreaRuntime.PickRespawn` (`:380-395`) und `ArenaAreaRespawn.TryPrepare`.
- Neu gefunden: `tools/BleedingCleanupChecks/Verify.ps1:129-130` prüft die Aufrufstelle von `RecoverOldBody`, deshalb liegt S0-16 innerhalb der Methode.
- `FreecamCompatHelper.EnablePatchesIfPresent` ohne `try` (`:41-55`); Guard-Prefixe in `DeathInterception.cs:185-360`.

**Verworfene oder nur teilweise übernommene Feststellungen**
- Keine Feststellung ganz verworfen.
- Teilweise (Garantien, TDM-Vorhersage): In v1 keine neue treiberseitige Vorhersage. Sie bräuchte in beiden Treibern ein neues Core-Member hinter `NoInlining`-Bridges und widerspräche "1.0 nutzt kein neues Core-Member". Umgesetzt ist die ebenfalls vorgeschlagene Alternative (dokumentierte Heuristik plus Test), die exakte Variante steht in Stufe 2.
- Teilweise (Bindung, `BepInPlugin`): Die falsche Begründung ist korrigiert, Core bleibt in 1.0 aber ein Nicht-Plugin. Die Prüfung über `RedeployCore.Version` schließt die Skew-Lücke ohne Umbau, und ein harter Dependency-Fehler würde Treiber gegen einen älteren Core ganz abschalten statt degradiert laufen zu lassen.
- Teilweise (Fakten, Release-DLL im Vertragstest): Für das Release ist der Release-Build maßgeblich. Übernommen ist, dass der Test den tatsächlich paketierten Build prüft und ein Hash-Check den Deploy abgleicht; den Test auf den Debug-Build umzustellen wäre falsch.
- Teilweise (Garantien, Stufen-Äquivalenz): Nur per Doku und Test übernommen. Die Core-Stufe wird nicht auf `Update` verschoben, weil die Zustellung in Frame N der Zweck von `Replaced` ist (Abschnitt 9).