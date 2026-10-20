# Changelog

## 0.3.2

- Animation speed control releases safely after interrupted attacks and weapon changes, while preserving hit stop.
- Lost or changed custom-animation overrides can recover.
- Animation-controller changes wait for attacks to finish and preserve the current animation when switching weapons.
- Stalled animation speed control can release without repeating damage.
- Optional diagnostics retain recent animation events throughout long sessions.

## 0.3.1

- A katana parry never staggers you anymore. Running out of stamina still breaks it. New setting `Parry.NoStagger`.
- A new parry sound made from Valheim's own metal sounds and inspired by Sekiro's deflects, with six variations that change with every parry. New setting `Impact.ParrySoundVolume`.
- Your own parry sounds. Put .wav files (16 bit) in `BepInEx/config/Iaido_Katana/ParrySounds` and each parry plays one at random.
- The Steel Katana's heavy attack lights up its surroundings much less, and hits leave only a small glint.
- The Thunderclap's streak is brighter and stays longer, and its cut also reaches enemies just past where the dash ends. `Thunderclap.Afterimage` can add an arc of light there.
- A much shorter config file with the settings that matter in play. Settings that an update changes now move only once, so your own values stay.

## 0.3.0

- The counter's cut sweeps wider and further and leaves a golden (Steel) or blue-white lightning (Flametal) afterimage that other players with the mod see too. A bigger parry glint.
- The counter's cut, the Steel Katana's heavy attack and the Thunderclap fell trees, logs and stumps like an axe.
- The katana's own cuts hit along the blade's path, and every swing hits all enemies in reach.
- The Steel Katana's heavy attack hits from high down to the feet, with a golden glint climbing the blade, a deeper swing sound and an upright golden afterimage.
- The Thunderclap gets a charge-up sound and a ball of lightning while gathering, a streak of light along the dash and a wider path that cuts through trees.
- Fixed the blade shaking on female characters.
- Works with Valheim 1.0.20.

## 0.2.2

- Lighter stamina costs for the swings and the Steel Katana's heavy attack.
- The counter's area cut can hit the same enemy again sooner, so quick parries land more cuts.
- Smoother animations with fewer pops and wobbles. The draw cut and the heavy attack no longer flick the blade out of the scabbard as they start.
- A steadier stance. The chest stays square while standing, walking and blocking, and starting or ending a block no longer snaps.
- The scabbard no longer cuts into the left thigh while walking.

## 0.2.1

- The counter hits harder, and its cut also hits every other enemy in reach.
- A steadier stance with less sway when blocking while moving sideways and less shoulder wobble while running.
- Smoother animations and hand-overs between the cuts and the stance.

## 0.2.0

- Custom full-body animations. A draw cut from the scabbard, an overhead cut and a downward diagonal cut, each with a slow windup and a lightning-fast strike.
- A new heavy attack, drawn up into a high stance and then one huge cut straight down.
- Smoother sheathing after two-handed cuts.
- Greatswords and sledges keep their own animations.
- `Swing.CustomAnimations` false brings back the older borrowed animations.

## 0.1.2 (beta)

- The Thunderclap ends in a wide horizontal finish.
- Smoother sheathing, no more jiggle.
- Recipes use four materials. Both katanas can be refined at the Forge of Potential.
- Fixed katanas losing durability too fast when blocking.

## 0.1.1 (beta)

- Natural arm poses in the stance, the parry, the sheathing and the Thunderclap.
- The katana stays sheathed through a roll.
- GitHub link for feedback and bug reports.

## 0.1.0 (beta)

- First public test version, work in progress.
- Steel Katana (Plains) and Flametal Katana (Ashlands), with their own models.
- A perfect parry strikes back on its own. Katanas parry within 200 ms, with an optional precise mode (150 ms, stronger counters).
- Iai stance, scabbard at the hip, draw and cut on a parry, slow sheathing after fighting.
- Impact frame when a counter lands.
- The Flametal Katana's heavy attack is the Thunderclap dash cut.
