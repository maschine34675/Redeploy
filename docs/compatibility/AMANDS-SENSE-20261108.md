# Amand's Sense Updated 3.1.0

Core 1.0.22 (shipped in Core 1.0.24, Redeploy 1.1.3, on 2026-11-14) handles the client plugin `xyz.drakia.Sense` in a
Core-owned body replacement. The reviewed release DLL has SHA-256
`861E750D9056B330CC13ACA50D50CE21CF6A1E4C7A70317F4E421D3A190EE872`.

Sense assigns the local player and inventory during `Player.Init`. It also
clears its world markers at every local-player initialization. Removing even
one marker changes the dictionary inside its own enumeration on EFT's Mono
runtime and the next iteration throws. The same reset drops valid corpse
records from earlier in the raid. [Sense release source](https://github.com/DrakiaXYZ/SPT-AmandsSense/blob/3.1.0/AmandsSense/Components/AmandsSenseClass.cs#L249-L261)

During a replacement in the same world, Core lets Sense bind the new player
and inventory and skips its world reset and redundant audio reload. A failed
handoff before `LocalGame.LocalPlayer` changes restores the old references.
Sense can mark the previous player's corpse. Subsequent corpse scans discard
destroyed players, victims from another world and abandoned replacement bodies.
Initial raid setup still uses Sense's own reset. There is no per-frame scan.

`tools/AmandsSenseChecks` exercises an active marker, committed and failed
replacements, inventory ownership, valid old corpses, invalid victims and
ordinary initialization. It includes a change from PMC to the profile's own
Scav.

On SPT-GPU-1, the published Sense DLL ran alongside Graphics in a Factory
Arena Deathmatch. Before the two player deaths, Sense reported 369 and 317
active markers. After each respawn the new player had usable firearm hands,
Sense referenced that player's inventory and its markers remained active.
This run covered the Arena driver. Standalone Redeploy and continuing as Scav
were not exercised in the game in this check.
