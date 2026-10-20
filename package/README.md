# Iaido Katana

**Two katanas that win on the parry. Every perfect parry draws the blade and strikes back on its own.**

Inspired by real iaido, the art of drawing and cutting in one motion, and by Zenitsu's Thunderclap and Flash from Demon Slayer.

> Please report bugs on [GitHub](https://github.com/Ononononoki/Iaido_Katana/issues).

## Features

- **Counter.** A perfect parry staggers the attacker, and the katana draws and strikes back for 3 times its damage. The same cut sweeps 170 degrees around you up to 4 m and hits every other enemy for double damage. Parried projectiles send half of the counter back. The katana barely blocks, so only a perfect parry stops a hit.
- **Parry window.** 200 ms (vanilla 250), or a precise mode with 150 ms and 15% stronger counters, each player's own choice. A katana parry never staggers you.
- **Iai stance.** The katana rests sheathed at the left hip and is drawn by the parry. After fighting it is flicked clean (chiburi) and slowly sheathed (noto).
- **Iaido cuts.** Full-body animations: a draw cut from the scabbard (nukitsuke), an overhead cut (kirioroshi) and a downward diagonal cut (kesagiri), each with a slow windup and a lightning-fast strike.
- **Animation recovery.** Lost custom-animation overrides can rebuild, and interrupted attacks release the katana's animation speed safely.
- **Steel heavy attack.** Raised into a high stance (jodan) while a golden glint climbs the blade, then one huge cut straight down for 6 times the damage.
- **Flametal Thunderclap.** The heavy attack crouches for 2.25 s while lightning gathers in the hands, then dashes 13 m along a 4 m wide path. Every enemy on the way, and just in front of where the dash ends, takes 10 times the damage, and nothing hits you during the dash.
- **Chopping.** The counter's cut, the Steel heavy attack and the Thunderclap fell trees, logs and stumps like an axe.
- **Effects.** Golden (Steel) or blue-white lightning (Flametal) afterimages of the counter, the heavy attack and the Thunderclap, which other players with the mod see too. A landed counter flashes as an ink picture with a short freeze, only on your screen.

## Crafting

| Katana | Cost | Station |
|---|---|---|
| Steel Katana | 50 Iron, 100 Coal, 20 Fine Wood, 10 Linen Thread | Forge level 4 |
| Flametal Katana | 50 Flametal, 20 Ashwood, 5 Asksvin Hide, 1 Iolite | Black Forge level 3 |

Upgrades per level: Steel 10 Iron, 20 Coal, 3 Linen Thread. Flametal 10 Flametal, 2 Asksvin Hide (twice that for level 3, four times for level 4). Both can be refined past level 4 at the Forge of Potential with a Battle Idol, like the game's own swords.

## Stats

| | Steel Katana | Flametal Katana | Black Metal sword |
|---|---|---|---|
| Damage | 38 slash | 60 slash, 15 lightning | 95 slash |
| Chop | 50, tier 3 | 60, tier 4 | |
| Block | 10 | 13 | 39 |
| Parry bonus | 13x | 13x | 2x |
| Stamina, swing and heavy | 16 and 40 | 20 and 100 | 14 and 28 |

## Config

The file is `BepInEx/config/onononoki.Iaido_Katana.cfg`. Gameplay settings are synced from the server. Settings marked "yours" are each player's own.

| Setting | Default | What it does |
|---|---|---|
| `Counter.DamageMultiplier` | 3 | Counter damage on the parried attacker, times the katana's damage |
| `Counter.AreaHit` | true | The counter's cut also hits the other enemies in reach |
| `Counter.AreaDamageMultiplier` | 2 | Their damage, times the katana's damage |
| `Counter.AreaAngle`, `AreaRange` | 170, 4 | Width in degrees and reach in meters of the counter's cut |
| `Counter.HeavyDamageMultiplier` | 6 | Heavy attack damage, times the katana's damage |
| `Chop.Enabled` | true | The counter, the Steel heavy and the Thunderclap chop trees |
| `Parry.PreciseMode` | false | Yours. 150 ms window, stronger counters |
| `Parry.NoStagger` | true | A katana parry never staggers you |
| `Impact.ParrySoundVolume` | 1 | Yours. Volume of the parry sound, 0 turns it off |
| `Impact.OwnParrySounds` | true | Yours. Your own .wav files in `BepInEx/config/Iaido_Katana/ParrySounds` play on a parry, one at random |
| `Thunderclap.Distance`, `Width` | 13, 2 | Dash length and half its width in meters |
| `Thunderclap.DamageMultiplier` | 10 | Damage to each enemy on the way |
| `Thunderclap.Afterimage` | Path | Yours. What the dash leaves behind, Path (a streak along it), Arc (an arc in front of its end), Both or Off |
| `Stance.Enabled`, `Impact.*`, `Visual.*` | | Yours. The stance, the impact frame and the effects |
| `Swing.CustomAnimations` | true | The katana's own animations, false for older ones borrowed from Krom and Stagbreaker |
| `Debug.LogCounters` | false | Yours. Logs attacks and animation diagnostics for troubleshooting |

## Installation and compatibility

Use a mod manager, or put `Iaido_Katana.dll` into `BepInEx/plugins`. Requires BepInExPack Valheim and Jotunn. For multiplayer, install it on the server and on every player. Only katanas change, every other weapon, block and parry stays vanilla.

## Feedback and bug reports

Found a bug or have an idea? Open an issue on [GitHub](https://github.com/Ononononoki/Iaido_Katana/issues). The source code is in the [GitHub repository](https://github.com/Ononononoki/Iaido_Katana).
