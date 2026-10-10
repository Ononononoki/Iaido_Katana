# Iaido Katana (beta)

**A katana that wins on the parry. Its own slashes are light, but every perfect parry strikes back on its own.**

> **Beta, work in progress.** Numbers, looks and effects may still change, and the attack animations may get an overhaul later (not certain). Feedback is welcome.

## What it does

- **Steel Katana.** Two-handed, Swords skill, Plains tier. Its own damage is 40% of a Black Metal sword.
- **Counter.** A perfect parry staggers the attacker and the katana strikes back on its own, like a Black Metal sword (1.5 times that at Block 100). Parried arrows, spears and spells send half of it back.
- **Guard.** The katana barely blocks. Only a perfect parry stops a hit.
- **Parry timing.** Katanas parry within 200 ms (vanilla 250). An optional precise mode gives 150 ms and counters 15% stronger, each player's own choice.
- **Slashes.** A slow windup and a lightning-fast strike, on the vanilla beat. The heavy attack is an overhead strike for 6 times the damage.
- **Iai stance and scabbard.** The katana rests sheathed at the left hip and is drawn and cut on a perfect parry. After fighting it is flicked clean and slowly sheathed. Put away, it stays at the hip.
- **Impact frame.** A landed counter flashes as a black and white ink picture, with a short freeze, a camera jolt and a metal crash. Only you see it.
- **Own models** for both katanas. Other players see the stance and the cuts too.

## Flametal Katana (Ashlands)

60 slash plus 15 lightning, crafted at the Black Forge with an Iolite. Its own curved shape in heat-tinted Flametal, and the lightning of the game's Iolite weapons on the blade.

**Thunderclap.** The heavy attack crouches for 2.25 seconds while lightning gathers, then draws and dashes 13 meters through your enemies. Each one takes 10 times the katana's damage, and nothing hits you during the dash. It costs 100 stamina.

## Crafting (Steel Katana)

| Level | Cost | Forge level |
|---|---|---|
| 1 | 50 Iron, 100 Coal, 20 Fine Wood, 5 Black Metal, 10 Linen Thread | 4 |
| 2 | 10 Iron, 20 Coal, 3 Black Metal, 3 Linen Thread | 5 |
| 3 | 20 Iron, 40 Coal, 6 Black Metal, 6 Linen Thread | 6 |
| 4 | 40 Iron, 80 Coal, 12 Black Metal, 12 Linen Thread | 7 |

The Flametal Katana costs 50 Flametal, 20 Ashwood, 2 Celestial Feather, 5 Asksvin Hide and 1 Iolite (Black Forge 3).

## Stats

| | Steel Katana | Flametal Katana | Black Metal sword |
|---|---|---|---|
| Damage | 38 slash | 60 slash, 15 lightning | 95 slash |
| Block | 10 | 13 | 39 |
| Parry bonus | 13x | 13x | 2x |

## Config

`BepInEx/config/onononoki.Iaido_Katana.cfg`. Gameplay settings are synced from the server. Settings marked "yours" are each player's own.

| Setting | Default | What it does |
|---|---|---|
| `Counter.DamageMultiplier` | 2.5 | Counter damage, times the katana's damage |
| `Counter.HeavyDamageMultiplier` | 6 | Heavy attack damage, times the katana's damage |
| `Parry.PreciseMode` | false | Yours. 150 ms window, stronger counters |
| `Parry.NormalWindowMs` | 200 | Katana parry window outside precise mode |
| `Thunderclap.Distance` | 13 | Dash length in meters |
| `Thunderclap.DamageMultiplier` | 10 | Damage to each creature on the way |
| `Stance.*`, `Impact.*`, `Visual.*`, `Swing.*` | | Yours. The stance, impact frame, effects and swing timing |

## Installation

- Use a mod manager, or put `Iaido_Katana.dll` into `BepInEx/plugins`.
- For multiplayer, install it on the server and on every player.
- Requires BepInExPack Valheim and Jotunn.

## Compatibility

Only katanas change. Every other weapon, block and parry stays vanilla.
