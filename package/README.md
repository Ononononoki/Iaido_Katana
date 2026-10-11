# Iaido Katana (beta)

**A katana that wins on the parry. Its own slashes are light, but every perfect parry strikes back on its own.**

> **Beta, work in progress.** Numbers, looks and effects may still change, and the attack animations may get an overhaul later (not certain). Feedback is welcome.

## What it does

- **Steel Katana.** Two-handed, Swords skill, Plains tier. Its own damage is 40% of a Black Metal sword.
- **Counter.** A perfect parry staggers the attacker and the katana strikes back on its own, like a Black Metal sword (1.5 times that at Block 100). Parried arrows, spears and spells send half of it back. The katana barely blocks, so only a perfect parry stops a hit.
- **Parry timing.** Katanas parry within 200 ms (vanilla 250). An optional precise mode gives 150 ms and counters 15% stronger, each player's own choice.
- **Slashes.** A slow windup and a lightning-fast strike, on the vanilla beat. The heavy attack is an overhead strike for 6 times the damage.
- **Iai stance and scabbard.** In an iai stance the katana rests sheathed at the left hip. A perfect parry draws it and cuts. After fighting it is flicked clean and slowly sheathed. It stays sheathed through a roll, and put away it stays at the hip. Other players see it too.
- **Impact frame.** A landed counter flashes as a black and white ink picture, with a short freeze, a camera jolt and a metal crash. Only you see it.

## Flametal Katana (Ashlands)

Its own curved Flametal shape, with lightning along the edge.

**Thunderclap.** The heavy attack crouches for 2.25 seconds while lightning gathers, then draws and dashes 13 meters through your enemies. Each one takes 10 times the katana's damage, and nothing hits you during the dash. It costs 100 stamina.

## Crafting

| Katana | Cost | Station |
|---|---|---|
| Steel Katana | 50 Iron, 100 Coal, 20 Fine Wood, 10 Linen Thread | Forge level 4 |
| Flametal Katana | 50 Flametal, 20 Ashwood, 5 Asksvin Hide, 1 Iolite | Black Forge level 3 |

Upgrades per level: Steel 10 Iron, 20 Coal, 3 Linen Thread. Flametal 10 Flametal, 2 Asksvin Hide (twice that for level 3, four times for level 4).

Both katanas can be refined past level 4 at the Forge of Potential with a Battle Idol (Black Metal for the Steel Katana, Flametal for the Flametal Katana), like the game's own swords.

## Stats

| | Steel Katana | Flametal Katana | Black Metal sword |
|---|---|---|---|
| Damage | 38 slash | 60 slash, 15 lightning | 95 slash |
| Block | 10 | 13 | 39 |
| Parry bonus | 13x | 13x | 2x |

## Config

The file is `BepInEx/config/onononoki.Iaido_Katana.cfg`. Gameplay settings are synced from the server. Settings marked "yours" are each player's own.

| Setting | Default | What it does |
|---|---|---|
| `Counter.DamageMultiplier` | 2.5 | Counter damage, times the katana's damage |
| `Counter.HeavyDamageMultiplier` | 6 | Heavy attack damage, times the katana's damage |
| `Parry.PreciseMode` | false | Yours. 150 ms window, stronger counters |
| `Parry.NormalWindowMs` | 200 | Katana parry window outside precise mode |
| `Thunderclap.Distance` | 13 | Dash length in meters |
| `Thunderclap.DamageMultiplier` | 10 | Damage to each creature on the way |
| `Stance.*`, `Impact.*`, `Visual.*` | | Yours. Stance, impact frame and effects |
| `Swing.*` | | Swing timing is yours, the swing animations come from the server |

## Installation and compatibility

Use a mod manager, or put `Iaido_Katana.dll` into `BepInEx/plugins`. Requires BepInExPack Valheim and Jotunn. For multiplayer, install it on the server and on every player. Only katanas change, and every other weapon, block and parry stays vanilla.

## Feedback and bug reports

Found a bug or have an idea? Open an issue on [GitHub](https://github.com/Ononononoki/Iaido_Katana/issues). The source code is in the [GitHub repository](https://github.com/Ononononoki/Iaido_Katana).
