# CorpseRun — Claude Code Handoff

> **Stand `spt-4.1`-Branch:** SPT 4.1 / EFT 40743, v2.0.0. Membernamen im Code sind
> die deobfuskierten 4.1-Namen; einzelne historische Abschnitte unten nennen noch
> 4.0-Dekompilat-Namen (Class633/635/643, method_0/2/7, Bool_2, GClass3746 u. ä.).

## Projektkontext

- **Ziel:** Nach Tod im Raid optional respawnen (nackt), eigene Leiche looten, Raid fortsetzen; bei Aufgeben normaler Raid-Abschluss.
- **Stack:** BepInEx-Client + SPT-Server-Mod (.NET 10). Namensschema (siehe Memory `spt-mod-naming-convention`): GUID `com.maschine.CorpseRun` (Client + Server identisch), PluginName `maschine-CorpseRun`, Assemblies `maschine-CorpseRun.Client.dll` / `maschine-CorpseRun.Server.dll`, Quellordner `CorpseRun.Client/` + `CorpseRun.Server/`, user/mods-Ordner nur `CorpseRun`.
- **Vorlage:** Struktur/Patterns wie `KillAndDamageInfo` (Harmony `ModulePatch`/`AbstractPatch`, `Shared.props`).
- **Repo (4.1-Arbeitskopie):** `D:\SPT41\Development\CorpseRun\` (Branch `spt-4.1`; 4.0-Stand liegt unter `C:\SPT\Development\CorpseRun\` auf `main`)
- **Spiel-Install:** `D:\SPT41\` (Server-Maschine; auf dem PC `C:\SPT41\`, via Syncthing gespiegelt)
- **User-Daten:** `D:\SPT41\SPT_Runtime\` (Profile, `user\mods`) — 4.1 hat den Ordner `SPT` in `SPT_Runtime` umbenannt; abweichend per `Shared.props.user`

## Wichtige Entscheidungen

| Thema | Entscheidung |
|-------|--------------|
| Equipment bei Respawn | Altes Equipment bleibt auf der **Leiche**; Profil bekommt neues leeres `InventoryEquipment`. Nicht löschen. |
| Versicherung | `Profile.InsuredItems` **nie anfassen** (verwaiste Einträge überall harmlos; Server überschreibt Client-Liste eh). Frühere Filterung strippte Versicherung für gedroppte Items + ältere Leichen. |
| Special Slots + behaltene Slots | **Immer** auf neues Equipment übertragen (keine Option mehr): Special Slots + `RetainedSlots` = SecuredContainer, Scabbard (Nahkampf), ArmBand — alles, was vanilla beim Tod behalten wird und auf Leichen unlootbar ist (`UnlootableComponent`). Leichen-Baum wird nie ins Post-Raid-Profil serialisiert → dort verbliebene Items wären unwiederbringlich weg (in-game bestätigt). Add-Fehler → Rollback auf Leiche. |
| Kill-Stats | `EftStats.Victims` vor Respawn snapshotten; nach neuem Statistics-Session (Respawn) wiederherstellen. |
| Raid-Ende blockieren | Patches auf `BaseLocalGame` (`CG_Spawn`/`InitiateGameStopping`/`Stop`; 4.0: method_19/11) solange `PendingRespawnChoice`. |
| Profil-Persistenz | **Client + Server** nötig: Client sanitized beim Swap; Server korrigiert `Inventory.Equipment` nach `SetInventory`. |
| UI | IMGUI `RespawnPromptPanel`; Cursor muss pro Frame erzwungen werden (InputManager sperrt sonst). |
| Spawn-Ort-Wahl | Im Post-Tod-Prompt (Buttons „nahe Leiche" / „am Spawnpunkt"), nicht mehr im F12-Config. `RespawnSessionState.SelectedSpawnMode` trägt die Wahl an `SpawnPointHelper`. |
| Spawn-Schutz | `SpawnProtectionSeconds` (default 10 s): nach Respawn `SetDamageCoeff(0)`, danach alten Wert wiederherstellen (`ApplyDamage` skippt bei `DamageCoeff <= 0`). Endet **sofort**, sobald eine Waffe in der Hand ist (`HandsController.Item is Weapon/Knife/ThrowWeap`) — sonst Exploit: Leiche looten + unverwundbar kämpfen. **Falle Auto-Draw:** Das Spiel zieht beim Spawn automatisch die erste verfügbare Waffe (`TrySetLastEquippedWeapon`→`SetFirstAvailableItem`) — nackt = übertragenes Messer → würde den Schutz sofort beenden. Deshalb: Coroutine steckt Auto-Draw weg (`SetEmptyHands`, Retry alle 0.5 s) und schaltet den Abbruch-Check erst scharf, nachdem die Hände einmal leer waren. Toasts (`NotificationManager`) bei Start (grün) und Ende (gelb, unterscheidet Ablauf/Waffe). Idee aus PlayerLives (SPT 3.x). |
| Respawn-Item | `RequiredRespawnItem` (default None; Dropdown: Defibrillator, Grizzly, Salewa, IFAK, AFAK, CMS, Surv12, CarFirstAidKit, Adrenaline, Propital). Check beim Tod (`RespawnSessionState.RespawnItemAvailable` → Prompt zeigt Warnung, Buttons weg), Verbrauch in `PerformRespawn` vor Equipment-Swap via `ItemManipulator.RemoveWithoutRestrictions`. Idee aus RevivalMod (SPT 3.x). |
| Leichen-Optik | Eigene Leiche nutzt FP-Body → `UpdatePlayerRenders(ThirdPerson)` nach Corpse-Erstellung. |

## Relevante Dateien / Module

### Client (`CorpseRun.Client/`)

| Bereich | Dateien |
|---------|---------|
| Einstieg | `Plugin.cs`, `Config/Settings.cs` |
| Tod / Respawn-Flow | `Services/RespawnService.cs`, `RespawnSessionState.cs`, `SpawnPointHelper.cs` |
| Equipment / Profil | `Services/RespawnEquipmentHelper.cs` |
| Stats | `Services/RespawnStatisticsHelper.cs`, `Patches/RespawnStatisticsPatch.cs` |
| UI | `UI/RespawnPromptPanel.cs`, `Services/RaidUiHelper.cs`, `DeathEffectsHelper.cs` |
| Patches | `BlockDeathRaidEndPatch.cs`, `BlackScreenPatch.cs`, `SessionResultPatch.cs`, `RespawnOwnerCleanupPatch.cs`, `RaidStartedPatch.cs` |

### Server (`CorpseRun.Server/`)

| Bereich | Dateien |
|---------|---------|
| Einstieg | `ModMetadata.cs`, `PatchManager.cs` |
| Inventory-Fix | `Helpers/InventoryEquipmentFixHelper.cs` |
| Patches | `SetInventoryPatch.cs` (Post-`InRaidHelper.SetInventory`), `PostRaidPatch.cs` (Post-`HandlePostRaidPmc`) |

### Build / Deploy

| Datei | Rolle |
|-------|-------|
| `Shared.props` | `SPTPath`, `SPTUserPath`, Client-Deploy nach `BepInEx\plugins\` |
| `CorpseRun.Server/CorpseRun.Server.csproj` | Deploy nach `$(SPTUserPath)\user\mods\CorpseRun\` |

### Decompiled Referenz

- 4.1: `D:\SPT41\Development\_decompiled\<Namespace>\` — z. B. `EFT\BaseLocalGame.cs`, `EFT\PlayerBody.cs`, `EffectsController.cs`
- 4.1-Serverquellen: `D:\SPT41\Development\_reference\server-csharp-main\Libraries\SPTarkov.Server.Core\`
- 4.0 (eingefroren): `C:\SPT\Development\decompiled\Assembly-CSharp\`

## Build / Test

```powershell
cd D:\SPT41\Development\CorpseRun

# Client → D:\SPT41\BepInEx\plugins\maschine-CorpseRun.Client.dll
dotnet build CorpseRun.Client\CorpseRun.Client.csproj -c Release

# Server → D:\SPT41\SPT_Runtime\user\mods\CorpseRun\maschine-CorpseRun.Server.dll
dotnet build CorpseRun.Server\CorpseRun.Server.csproj -c Release
```

**Manueller Testplan (Raid):**

1. Tod → Respawn-Prompt (Cursor sichtbar, Buttons klickbar)
2. Respawn → nackt, Leiche mit vollem Körper + Loot
3. Leiche looten → Equipment zurück
4. Extract → kein `KeyNotFoundException`, Profil intakt
5. Tod → Aufgeben → normaler Raid-Abschluss
6. Versichertes Gear auf Leiche → Versicherer-Meldung nach Raid
7. Kills vor Respawn → auf Raid-End-Screen sichtbar

**Logs:** BepInEx-Log (Client-Zeilen erscheinen automatisch als `[Info :maschine-CorpseRun.Client]`, kein manuelles Präfix — siehe Memory `spt-log-prefix-convention`); Server-Konsole (Server loggt aktuell nichts; falls doch: manuelles `[CorpseRun]`-Präfix nötig).

## Codex-Review 2026-07-29 — Verifikations-Ergebnis (2026-07-30)

Externes Review `CODE-REVIEW-2026-07-29.md`; jede Behauptung adversarial gegen Decompile + SPT-Server-Quellen verifiziert.

**Umgesetzt (je eigener Commit):** P1.2/P1.6 Secure/Special immer + Rollback; P1.3 InsuredItems-Filter entfernt; P1.4 Stop-Stand-down (externes Raid-Ende — MIA/Leave — lief in `Stop` durch, aber `BlackScreenPatch` schluckte dessen Continuation mit `OnGameSessionEnd`/`LocalRaidEnded` → Softlock + Raid ungespeichert; StopPatch tritt jetzt bei durchgelassenen Stops zurück) + 30s-Timeout für Player-Erstellung (Teil von P1.5); P2.8 Server-Root nur mit Equipment-TPL `55d7217a4bdc2d86028b456d` + parentlos, fail-closed; P2.10 doppelte `OnLeave`-Registrierung entfernt (func_1-Factory subscribed schon); P2.11 OwnerCleanup ruft Basis-`vmethod_1` jetzt echt non-virtual (Function-Pointer-Delegate — `MethodInfo.Invoke` dispatcht virtuell und startete doch `CloseBattleUi` auf StaticManager); P2.12 `RespawnHealthPercent` wirkt (RestoreFullHealth entfernt, Clamp-Floor 1%); P3.15 toten PostRaid-Prefix entfernt.

**Widerlegt:** Quest-Items (`Inventory.QuestRaidItems`) bleiben beim Respawn vollständig erhalten — Baum hängt am geteilten `profile.Inventory`, neuer `PlayerInventoryController` rebindet Ownership unconditional (Player.cs ~11123), Leiche bekommt nur den Equipment-Baum; Server-Roundtrip wie vanilla. Kein Fix nötig.

**Bewusst nicht umgesetzt:** P1.1 Scav-Block (laut User-Test funktionieren Scav-Respawns problemlos; falls doch Transfer-Probleme auftauchen, hier wieder ansetzen); P1.7 Stats/XP-Merge über Respawns (bewusste Balancing-Entscheidung des Users — Fortschritt vor letztem Respawn verfällt; ggf. später als Option); P1.5 voller transaktionaler Umbau mit Rollback (nur Timeout umgesetzt); Auto-Deploy-Abschaltung (gewollter Dev-Workflow); P2.13 KillcamReplay-Lebenszyklus (offen — Respawn während laufender Killcam; bei Bedarf Replay bei BeginRespawn beenden); P2.14 FiR-Verhalten (dokumentiert: `SetSpawnedInSession(false)` beim Respawn = FiR-Verlust wie bei normalem Tod).

## Bekannte Fehler / Fallen

### `KeyNotFoundException` am Raid-Ende (`Inventory.Equipment`-ID fehlt in Items)

- **Ursache:** SPT `InRaidHelper.SetInventory` merged Items, aktualisiert aber nicht zuverlässig `Inventory.Equipment` auf die neue Equipment-Root-ID nach Client-Swap.
- **Fix:** Server `InventoryEquipmentFixHelper.EnsureEquipmentRootIsValid` + Client `SanitizeProfileInventoryReferences`.
- **Symptom:** Crash auch beim Spielstart wenn Profil bereits korrupt.
- **Profil-Retten:** In `profiles\<id>.json` unter `Inventory.equipment` die ID des Root-Items in `items[]` (ohne `parentId` oder Parent bekannter Slots) setzen.

### Stale Inventory-Referenzen

Nach Equipment-Swap bereinigen: `FastAccess`/`FastPanel`, `FavoriteItemsStorage`, `DiscardLimits`, `CheckedMagazines`/`CheckedChambers`. **`InsuredItems` für Leichen-Items nicht entfernen.**

### Fika-Inkompatibilität (by design)

- CorpseRun deaktiviert sich komplett, wenn `com.fika.core` geladen ist (`FikaCompatCheck`, Chainloader-Check, Toast beim Raid-Start): Der Player-Swap kann nie über Fika-Clients synchronisiert werden (kein Packet-Flow für den Ersatz-Player, headless ohne MainPlayer). Muster aus PlayerLives 1.2.5 (`FikaReviveDetector`). PlayerLives-Update-Analyse 2026-07-30: Countdown-Toasts + Stow-Härtung übernommen (`IsChangingWeapon`-Guard — SetEmptyHands mid-Animation korrumpiert den HandsController); bewusst nicht übernommen: Stamina-Refill im Schutzfenster (Balancing), „Any"-Respawn-Item (billigstes zuerst — bei Bedarf nachrüstbar), Inventar-Zwangsschließung beim Tod.

### Client ohne Server-Mod → Profil-Korruption

- **Schutz:** Server registriert `/corpserun/ping` (`CorpseRunRouter`, StaticRouter); Client prüft einmalig lazy (`ServerModCheck.IsServerModAvailable`, gecacht). Fehlt der Server-Mod: `OnPlayerDied` lässt den Tod normal durchlaufen (kein Respawn-Angebot), `RaidStartedPatch` zeigt rote Notification beim Raid-Start.
- **Profil-Reparatur nach Korruption:** siehe `KeyNotFoundException`-Abschnitt; Skript-Weg: `Inventory.equipment` auf das Item mit `_tpl == 55d7217a4bdc2d86028b456d` (ohne `parentId`) setzen; Backup nach `user/profiles/backups/`.

### Server-Mod „wirkt nicht“

- Prüfen ob DLL in **`user\mods\CorpseRun\`** liegt (nicht nur `build\`).
- `LastWriteTime` der deployed DLL vs. Build-Zeit.

### Compile / API

- `FastAccess`: Namespace-Konflikt mit HarmonyLib → `EFT.InventoryLogic.FastAccess` qualifizieren.
- `CompoundItem`: kein `GetSlot()` → `Slots.FirstOrDefault(s => s.ID == ...)`.
- `FavoriteItemsStorage.RemoveWhere` ggf. nicht verfügbar → `foreach` + `Remove`.

### Visuelle Health-Effekte überleben Respawn (z. B. Gehirnerschütterung-Unschärfe)

- **Ursache:** `EffectsController` (Kamera-Postprocessing) hält pro Effekt-Typ (`Class633`-Subklassen: RadialBlur, FastVignette, DoubleVision, MotionBlur, Wiggle, …) eine eigene `ActiveEffects`-Liste mit `IEffect`-Referenzen. `RebindEffectsController` hat beim Player-Swap nur die Events abgemeldet und `DeathFade`/`FastBlur` zurückgesetzt — die alten `IEffect`-Referenzen (z. B. `Stun`/Contusion, treibt `Class635` DoubleVision + `Class643` GrenadeFlash) blieben in den `ActiveEffects`-Listen der übrigen Effekt-Handler stehen und wurden nie entfernt, da kein `EffectRemovedEvent` mehr ankam.
- **Fix:** `DeathEffectsHelper.RebindEffectsController` ruft zusätzlich `EffectsController.OnDisable()` (Unity-Lifecycle-Methode, Name kann nicht obfuskiert sein) per Reflection auf — das leert alle `ActiveEffects`-Listen genau wie beim normalen Component-Disable. Danach ist die Bindung an den neuen Player sauber ohne Altlasten.
- **Symptom:** Nach Tod mit aktiver Gehirnerschütterung (`Stun`) bleibt die Doppel-/Unschärfe-Sicht auch nach Respawn bestehen, obwohl `part.Effects.Clear()` (Profil) und `RestoreFullHealth()` (neuer Player) liefen — die stellen nur die Gesundheitsdaten zurück, nicht den Kamera-Postprocessing-State des alten Players.
- **Datei:** `CorpseRun.Client/Services/DeathEffectsHelper.cs`

### Visor-Overlay bleibt nach Respawn / Fremd-Mods finden PlayerCameraController nicht (Freecam-NRE)

- **Ursache:** Die `PlayerCameraController`-Komponente sitzt auf dem Player-gameObject. Alter Ansatz (nur `controller.Player` tauschen) ließ sie auf der **Leiche**: Visor/NVG-Observer blieben an den toten Player gebunden (Overlay fror ein) und Mods mit `player.GetComponent<PlayerCameraController>()` (z. B. Terkoiz.Freecam) bekamen auf dem neuen Player `null` → NRE.
- **Fix:** `CameraRebindHelper.RebindToPlayer` zerstört den alten Controller und erzeugt ihn per `PlayerCameraController.Create(newPlayer)` neu (Raid-Start-Pfad; `method_0` bindet alle Observer frisch). Danach `method_1/4/5`-Initial-Sync (Namen in 4.1 unverändert; kein Changed-Event beim nackten Player) + `FreecamCompatHelper` (s. u.).
- **FALLE — `OnDestroy`-Teardown (durch adversariale Verifikation gefunden):** `PlayerCameraController.OnDestroy` macht: Observer-Unbinds, `VisorSwitcher.Deinit()` (nullt Callback des **geteilten** HysteresisFilters), `AudioListenerConsistencyManager.Reset()` (parkt Listener bei (0,-1000,0)), `Singleton.Release(Singleton.Instance)` (released die **aktuelle** Instanz!) und feuert das statische Destroyed-Event (EffectsController nullt `player_0`). Deshalb:
  1. **`DestroyImmediate`, nie `Destroy`** — deferred liefe das Teardown am Frame-Ende gegen den NEUEN Controller (Singleton weg → FoV-Zoom tot, Effects-Rebind no-op, 2. Respawn: zwei aktive Controller kämpfen um die Kamera).
  2. **`RepairSharedCameraState`** danach: `VisorSwitcher.Init(CameraManager.CG_method_17-Delegate)` (sonst NRE beim nächsten Visier-Toggle) + `AudioListenerConsistencyManager.Follow(camera.transform)` (sonst 3D-Audio dauerhaft tot). Beides initialisiert das Spiel nur einmal pro Raid (4.0: `CameraClass.method_2/method_7`).
  3. EffectsController-`player_0=null` (via Destroyed-Event) wird vom direkt folgenden `RebindEffectsController` im selben Frame repariert — Reihenfolge nicht ändern!
- **Freecam-Kompat:** `FreecamCompatHelper.RebindOwner` (nach Owner-Erstellung): setzt per Reflection Freecams gecachtes `_gamePlayerOwner`-Feld auf den neuen Owner (alter wurde zerstört → MissingReferenceException beim Toggle) und force-exitet den Freecam-Modus (`Freecam.IsActive=false`, `_controlsToggled=false`), falls man in Freecam gestorben ist. No-op ohne Freecam.
- **Freecam „Kamera klebt im Kopf" nach Respawn:** Log-Diagnose ergab: Operation-Switch (`LockCameraState`, 4.0: `GClass3690`) und POV (`FreeCamera`) funktionieren nach dem Controller-Recreate einwandfrei — die Kamera IST frei. Verbleibende Ursache: Freecams Movement-Script (`Freecam`, beim Raid-Start per `AddComponent` auf das Kamera-GO gelegt und im Feld `_freeCamScript` gecacht) läuft nicht mehr (zerstört oder auf stale Kamera-Objekt). Tückisch: `IsActive` ist ein plain bool-Feld → Setzen wirft nie, `IsFreecamActive` bleibt true (ForceSetPosition geblockt) — Kamera frei, aber niemand bewegt sie. → `FreecamCompatHelper.EnsureFreecamScriptAlive()` im Enter-Postfix: prüft Unity-fake-null + GameObject-Match mit `CameraClass.Instance.Camera`; bei Bedarf Script auf der Live-Kamera neu erstellen, `_freeCamScript`/`_mainCamera`-Felder umbiegen, `IsActive=true`. Zusätzlich Postfixe auf `SetPlayerToFreecamMode`/`SetPlayerToFirstPersonMode` (lazy beim Raid-Start aktiviert, Plugin-Ladereihenfolge!) mit Operation-Force (inzwischen als unnötig belegt, bleibt als Absicherung) + Diagnose-Log.
- **Dateien:** `CorpseRun.Client/Services/CameraRebindHelper.cs`, `CorpseRun.Client/Services/FreecamCompatHelper.cs`

### DynamicMaps: eigener Standort nach Respawn nicht mehr angezeigt

- **Ursache:** `PlayerMarkerProvider` (SPT-DynamicMaps) erstellt den eigenen Positions-Marker einmal (Raid-Start/Map-Öffnen) und cached ihn in `_playerMarker`; `TryAddMarker` early-returned danach für immer (`if (_playerMarker is not null) return;`). Der Marker hält intern eine `Player`-Referenz (private Setter) auf den **toten** Player und aktualisiert seine Position jede LateUpdate aus dessen `Transform` — bleibt also an der Leiche eingefroren statt dem neuen Player zu folgen. `PlayerPositionText` (Koordinaten-Anzeige) ist NICHT betroffen, holt sich `GameUtils.GetMainPlayer()` jeden Frame frisch. `OtherPlayersMarkerProvider` ebenfalls nicht betroffen (`IsYourPlayer`-Skip + eigenes Unregister-Event-Handling).
- **Fix:** `DynamicMapsCompatHelper.RebindPlayerMarker` (nach Owner-Erstellung, wie Freecam): läuft rein per Reflection zu `DynamicMaps.Plugin.Instance.Map._dynamicMarkerProviders[PlayerMarkerProvider]._playerMarker.Player` durch und setzt die `Player`-Property (private Setter) auf den neuen Player um — Marker-GameObject bleibt erhalten, folgt danach automatisch wieder. No-op ohne DynamicMaps.
- **Datei:** `CorpseRun.Client/Services/DynamicMapsCompatHelper.cs`

### Halb abgebaute Map (PerfectCulling) — bei Test-Nutzern ohne Killcam/KillcamReplay

- **Ursache:** Der PlayerCameraController-Destroy/Create-Zyklus (unser Respawn) kann einen stale `Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler` hinterlassen; startet dann ein zweiter Sampler (Stop()-Cleanup/nächster Raid), wirft `method_0` GException8 („Autocull sampler exists") → Teardown bricht ab → nächster Raid mit halb gecullter Map.
- **Fix:** `FixPerfectCullingSamplerPatch` (Prefix auf `Sampler.Start`): zerstört alle anderen Sampler-Instanzen per `DestroyImmediate`. Identisch/idempotent koexistent zum selben Patch in Killcam + KillcamReplay — deshalb sahen wir es lokal nie (diese Mods waren installiert), der Test-Nutzer ohne sie schon.
- **Datei:** `CorpseRun.Client/Patches/FixPerfectCullingSamplerPatch.cs`

### Meds/Essen nach Respawn nicht nutzbar (Animation läuft, Vorgang hängt)

- **Ursache:** `PlayerHealthController` (4.0: `GClass3010`) tickt Effekte nur wenn `Bool_2 == true` (`ManualEffectUpdate`-Guard, 4.0-Name). Das Flag setzt erst `PlayerHealthController.Start()` — beim normalen Raid-Start von `LocalGame.Spawn()` aufgerufen. Der Respawn-Flow erstellte den neuen Player ohne `Start()` → MedEffect (schließt den Use-Vorgang über `EffectRemovedEvent` → `ObservedMedsControllerClass.method_8` ab) tickt nie → Vorgang hängt bis zum manuellen Abbruch. Betrifft alle Health-Effekte (auch Existence/Energie-Drain, Blutungen).
- **Fix:** `RespawnService.ApplyRespawnHealth` ruft `player.PlayerHealthController?.Start()` vor `RestoreFullHealth()` auf. Setzt auch `UpdateTime` auf jetzt (frischer Zeitstempel für neue Effekte).
- **Datei:** `CorpseRun.Client/Services/RespawnService.cs`

### Cursor im Respawn-Fenster

- Einmaliges `Cursor.visible = true` reicht nicht; `InputManager` setzt pro Frame zurück.
- Lösung: in `RespawnPromptPanel.Update()` solange sichtbar `CursorLockMode.None` + `visible = true` erzwingen.

### Leiche ohne Oberkörper

- FP-Body: Torso = `ShadowCastingMode.ShadowsOnly`.
- Nach Corpse-Erstellung: `PlayerBody.UpdatePlayerRenders(EPointOfView.ThirdPerson, side)` (wie `Corpse.CreateStillCorpse`).
- Corpse-Feld via `AccessTools.FieldRefAccess<Player, Corpse>("Corpse")`; kurz warten bis gesetzt.

## Offene Aufgaben

- [x] User-Bestätigung: Cursor-Fix + Leichen-Optik im Live-Test
- [x] Fix bestätigt (User): Visuelle Gesundheitseffekte überleben Respawn (siehe „Bekannte Fehler / Fallen")
- [x] Fix bestätigt (User): Meds/Essen nach Respawn nicht nutzbar — fehlender `PlayerHealthController.Start()`-Aufruf
- [x] Spawn-Ort-Wahl (Leiche/Spawnpunkt) vom F12-Config in den Post-Tod-Prompt verlegt (Build OK). **Noch nicht im Live-Test bestätigt.**
- [x] Spawn-Schutz + Respawn-Item-Kosten implementiert (Build OK), Ideen aus `_alteSPT3Mods` (PlayerLives/RevivalMod). **Noch nicht im Live-Test bestätigt.**
- [ ] Nicht übernommen aus alten Mods (bewusst): Critical-State/Downed-Mechanik, `IsAlive=false`-Bot-Ignore-Hack, Renderer-Blinken, Toast-Notifications, Hardcore-Optionen (Kopfschuss/Chance)
- [ ] Ggf. weitere Edge Cases: mehrfacher Respawn, Mod-Konflikte (SAIN, UIFixes), Scav-Runs
- [ ] Keine automatisierten Tests im Repo — alles manuell im Spiel

## Respawn-Engine: Stufe-0-Härtung und API-Entwurf (12.09.2026)

Entwurf samt Begründungen und In-Game-Testszenarien: `docs/RESPAWN-API.md` (deutsch,
Entwicklerdoku). Umgesetzt ist die Redeploy-Seite von Stufe 0; die öffentliche
`RespawnApi`-Fassade (Stufe 1) noch nicht.

Neue Invarianten in Core:

- **`RunRespawn` lehnt vor jedem Teardown ab** (`RespawnPreflight`): null-Request/Game/Selector,
  `!DeathInterception.IsAvailable`, Raid endet bereits, Aufruf aus einem Todes-/Pipeline-Raise
  (`CoreSignals.Respawn.Depth > 0`), laufende Pipeline. Ablehnung = synchrones `onFailure`, kein
  Zustand verändert, kein Event. **Aufrufer müssen das von einem Teardown-Fehler unterscheiden** —
  ArenaModes `LiveKitRespawn.RecoverOldBody` tut das noch nicht (S0-16 im Entwurf, offen).
- **Alle Core-Events laufen über `SignalDispatcher`** (pro Abonnent isoliert, Snapshot der
  Invocation-List, zerstörte Unity-Targets übersprungen). `CoreSignals.Respawn` für Todes- und
  Pipeline-Raises (zählt für die Reentranz-Regel), `CoreSignals.Lifecycle` für Raid-Grenzen.
- **`DeathInterception.EnsurePatched` ist fehlerisoliert und fail-closed**: fehlt ein
  Keep-alive-Patch, ist `IsAvailable == false` und Core bleibt komplett untätig (kein Claim, kein
  Guard-Block, `RunRespawn` abgelehnt). `IsAvailable` = Patches vollständig && Treiber registriert
  && kein Fika. `FikaCompatCheck` cacht nur noch eine positive Antwort.
- **Raid-Grenzen leben in `RaidLifecycle`** (Start + Ende immer gepaart; `DeathInterception.
  EnsurePatched` installiert sie mit). Start filtert `NarrateGameWorld`, Ende filtert `HideoutGame`;
  beide setzen `RespawnEngineState` zurück (`IsPipelineRunning`, `LastRespawnedPlayer`,
  `SpawnSystemCache`). Reihenfolge am Ende: Treiber → Core-Reset → Legacy-Event. Freecam-Patches
  aktiviert jetzt Core im Raid-Start (gekapselt), nicht mehr nur Redeploy.Client.
- **Owner-Cleanup-Guard ist an die Instanz gebunden** (`OwnerCleanupHelper.IsRetired`): der vom
  Pipeline-Teardown zerstörte Owner überspringt Vanilla-`CleanupOnDestroy`, egal wie lange die
  Treiber-Flags leben.
- **Finalize-Coroutinen prüfen Identität** (`game.LocalPlayer` bzw. Owner), bevor sie Kamera und
  `MyPlayer` anfassen.
- `DeadBodyGuard` wirkt nur noch in einem `LocalGame` (nicht unter Fikas `CoopGame`).
- Client: Der Cooldown bricht ab, wenn der Raid währenddessen endet (`IsRespawnInProgress` bleibt
  bis `GameEnd` absichtlich gesetzt — hält den Owner-Guard scharf). `RedeployDriver` beansprucht nur
  Tode, die `RespawnService` bedienen kann; jede verbleibende Lücke wirft → Core fail-open →
  Vanilla-Tod statt Soft-Lock.

**Deploy-Voraussetzung:** `maschine-Redeploy.Core.dll` wird flach nach `BepInEx/plugins` deployt und
von ArenaMode mitbenutzt. Dieser Core braucht **ArenaMode ≥ `668619f`** (idempotentes
`LiveKitRespawn.RecoverOldBody`, 12.09.2026): Ein älteres ArenaMode beendet einen vom Preflight
abgelehnten Live-Swap (nur bei `IsAvailable == false`, also nach einem Spiel-Update mit
fehlgeschlagenem Patch) über doppeltes `RegisterPlayer` → `EndRaidAsLastResort` als Killed-Raid
für einen lebenden Spieler. Review 12.09.: zwei Prüfer bestätigt; seit `668619f` behoben.

Tests: `Redeploy.Tests` (net10.0, xunit) deckt `SignalDispatcher` und `RespawnPreflight` ab —
`dotnet test Redeploy.Tests`. Alles andere bleibt In-Game (Szenarien: `docs/RESPAWN-API.md`, Abschnitt 10).

### Stufe 1: `RespawnApi` v1 (13.09.2026)

- **Fassade** `Redeploy.Core/Api/RespawnApi.cs` (einziger neuer öffentlicher Typ; Reflection-Name
  `Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core`): `ApiVersion` (Property, 1), die Literale
  `ReasonDeath/ReasonSwap` und `StagePrepare/StageCreate/StageRaidEnding/StageRewire`, die Abfragen
  `IsAvailable`, `IsReplacing`, `IsRespawnPending(Player)`, die Events `DeathClaimed(body, driver)`,
  `Replaced(old, new, reason)`, `ReplacementFailed(old, stage)`, `RaidEnded()`. Alle Raises laufen
  über `CoreSignals` (pro Abonnent isoliert). Die Legacy-Events bleiben: `OnDeathIntercepted` direkt
  vor `DeathClaimed`, `OnPlayerRespawned` direkt nach `Replaced`.
- **Claims** `Engine/RespawnClaims.cs` + reine Entscheidung `Engine/RespawnClaimLogic.Decide`
  (Reihenfolge: kein Body → false; Dispatch-Fenster → true; Core kann nicht ersetzen → false;
  beansprucht → hält der Treiber?; verarbeitet → false; sonst Vorhersage „aktueller toter Body und
  ein Treiber will ihn"). Zurückgesetzt an beiden Raid-Grenzen. `MarkRaidEnded(game)` sperrt späte
  `ReplacementFailed` nach `RaidEnded`.
- **Interop** `interop/RedeployRespawnInterop.cs` — Masterkopie des `LocalBodyTracker` für Konsumenten
  (nur Namespace ändern). Bindet per Reflection, pollt sonst `GameWorld.MainPlayer`.
- **Vertrag**: `Redeploy.Core/Api/api-v1.baseline.txt` ist eingefroren; `ApiContractTests` prüft den
  gebauten Core (Release oder Debug in `Redeploy.Core/bin`), die Interop-Literale, die installierten
  `ArenaMode.Client`/`Redeploy.Client` (MemberRefs/TypeRefs) und die von ihnen implementierten
  Core-Schnittstellen (InterfaceImpl). Ein neues Member ⇒ `ApiVersion` 2 + neue Baseline. Die
  DLL-Tests überspringen sich ohne Build oder ohne Spielinstallation neben dem Repo.
  Die Suite bricht nachweislich bei einer fehlenden Baseline-Zeile und bei einer abweichenden
  Handler-Signatur in der Interop-Datei.
- **Harness** `tools/RespawnInteropChecks` (`dotnet run --project tools/RespawnInteropChecks -c Release`,
  Token `RESPAWN_INTEROP_CHECKS_PASSED`): die unveränderte Interop-Datei gegen Stubs für Unity/Comfort/EFT
  (mit Unity-null) und eine Mock-Fassade mit der wörtlichen v1-Oberfläche — Tier-Äquivalenz,
  Fehlerpfad, Late Attach, Entdopplung, `IsReplacing`-Halt samt 35-s-Kappe, kein falscher raid-start
  nach `RaidEnded`, `null` bei nicht verfügbarem Core, Teilbindung, Abmeldung, werfender Konsument.
- **Nicht enthalten** (Stand Stufe 1): Golden-Order-Guard (8.4), In-Game-Probe (8.5), Bindungstest
  gegen den echten gebauten Core, Treiber-Versionsprüfung (3.9), ArenaMode-Migration 4–6 (Abschnitt 5).
  Probe, Versionsprüfung, Punkt 6 und KillcamReplay kamen danach, siehe nächster Abschnitt.

### Folgearbeiten zu Stufe 1 (13.09.2026 abends)

- **Core-Version** in `Redeploy.Core/CoreVersion.props` (1.0.0), nicht mehr aus `Shared.props`. Bei
  jeder Core-Änderung, auf die sich ein Treiber verlassen kann, dort erhöhen.
- **Treiber-Check** `drivers/CoreVersionGate.cs`, in Redeploy.Client und ArenaMode.Client verlinkt;
  beide csproj importieren `CoreVersion.props` und stempeln `RedeployCoreBuiltAgainst`. Älterer Core
  ⇒ Warnung + `Chainloader.DependencyErrors` (SPT-Toast „plugin failed to load", der Treiber läuft
  trotzdem). Die Datei darf keine Core-Typen benutzen. Tests: `CoreVersionTests` (Vergleich, Props,
  Version des gebauten Core, Stempel der gebauten Treiber inklusive ArenaMode).
- **Probe** `tools/RespawnApiProbe` (Anleitung im README dort). Ein normaler Build deployt nichts,
  auch nicht Core (`AdditionalProperties="SPTModType=Library"` an der Referenz); deployen mit
  `-p:DeployProbe=true`. Deployt ist sie seit 13.09. abends mit allen Schaltern aus; ihr immer
  werfender `Replaced`-Abonnent erzeugt pro Ersetzung absichtlich eine Core-Warnung.
- **Befund:** Eine erfolgreiche Ersetzung läuft synchron in einem Aufruf (`Player.Init` mit
  `async: false`). `IsReplacing` ist nur auf Fehler-/Timeout-Pfaden über Frames sichtbar.
- **Falle:** Jedes Projekt mit `ProjectReference` auf Core deployt Core mit, wenn es ohne
  `-p:SPTModType=Library` gebaut wird (Core setzt `SPTModType=Client` fest). Kompilierprüfungen
  deshalb immer mit dem Schalter.
- **KillcamReplay 0.9.8** ist der erste Konsument (eigenes Repo, Commit `9ba08cc`). Seit **0.9.9**
  (`49b1c8b`, User-Entscheidung) hält KillcamReplay einen Respawn seines Replay-Opfers per Postfix auf
  `RespawnEngine.RunRespawn`, bis das Replay schwarz ausgeblendet hat (Überspringen mit Leertaste).
  Wer `RunRespawn`, `RespawnRequest.Game` oder `RespawnRequest.DeadPlayer` umbenennt, schaltet diesen
  Hold still ab; `tools/HandoffChecks` im KillcamReplay-Repo liest die Member aus dem gebauten Core.
  Redeploy.Client loggt einen vom Raid-Ende abgebrochenen Respawn seitdem als Info (`de7b8f7`).
- Weiter offen: 8.4, Bindungstest gegen den echten Core, ArenaMode-Migration 4 und 5, die
  Probe-Szenarien im Spiel.

In-Game-Testauftrag für den GPU-Rechner (gemeinsam mit ArenaMode, gebunden an die deployten
Hashes): `../ArenaMode/GPU-RESPAWN-STAGE0-TEST-INSTRUCTIONS-r1.md` (Ergebnis r1: Respawn bestanden,
Stopp durch Vanilla-Race beim Raid-Stop — siehe `docs/RESPAWN-API.md`-Kopf) und die Fortsetzungen
`…-r2.md`, `…-r3.md`, `…-r4.md`. **Stand 13.09.2026:** r2/r3 stoppten nur auf Fremdbefunden
(Vanilla-Race-Familie beim `Stop`, SAIN-Gehirnkonflikt); der User-Playtest (r4 wurde nicht mehr
gebraucht) bestätigte alle Stufe-0-Szenarien, die sich nativ auslösen lassen. Details im Kopf
von `docs/RESPAWN-API.md`. Der User-Playtest am Abend des 13.09. auf Core `82A7C33A…` (Code
`9f16dc5`, Stufe 1; Arena und gewöhnlicher Raid, acht Ersetzungen, ein Give-up) lief fehlerfrei;
was er nicht auslösen konnte, steht im Kopf von `docs/RESPAWN-API.md`. **Deployt seit den
Folgearbeiten:** Core `C3DC2F12…` (`1.0.0+33c79a7`), ArenaMode.Client `54C2D7B7…` (`b5011cd`),
RespawnApiProbe `FE381E77…`; der ArenaMode-Server blieb `70FDD6CB…` (`699df57`). Der Playtest am
13.09. spät lief darauf fehlerfrei. Danach neu deployt: KillcamReplay `705FB569…` (0.9.9, `49b1c8b`)
und Redeploy.Client `10F017AD…` (`de7b8f7`, mit `-p:SPTModType=Library` gebaut und von Hand kopiert,
damit Core nicht mit neuer ProductVersion mitdeployt). Diese beiden sind in-game noch ungetestet.

## Nicht annehmen (Claude Code)

1. **Equipment wird beim Respawn gelöscht** — es bleibt auf der Leiche im World-Inventar.
2. **Nur Client oder nur Server reicht** für Profil-Persistenz — beide Seiten sind nötig.
3. **`SPTPath` = `SPTUserPath`** — getrennte Pfade; Server-Mods landen unter `user\mods`.
4. **SPT aktualisiert `Inventory.Equipment` automatisch** nach Item-Merge — tut es nicht zuverlässig bei Respawn-Swap.
5. **`InsuredItems` blind sanitizen** — Leichen-Items müssen erhalten bleiben für Versicherung.
6. **Corpse rendert korrekt ohne Patch** — bei abgefangenem Tod bleibt FP-Render-State.
7. **Cursor einmal setzen genügt** — muss pro Frame gehalten werden.
8. **Profil-Korruption ist immer unrettbar** — oft nur falsche `equipment`-ID in JSON.
9. **Csproj heißt `CorpseRun.csproj`** — korrekt: `CorpseRun.Client.csproj`.
10. **Abgeschlossene Änderungen liegen lassen** — nach fertigen, gebauten Änderungen wird automatisch committed (aktuelle Projekt-Konvention; ersetzt die frühere Regel „keine Commits ohne Anfrage").
