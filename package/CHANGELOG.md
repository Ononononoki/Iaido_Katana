# Changelog

## 0.2.2

- Lighter stamina: the Steel Katana's swings cost 16 (was 20) and its heavy attack 40 (was 50), the Flametal Katana's swings 20 (was 24). The Thunderclap stays at 100. Config files still at the old values move to the new ones. New setting `Counter.SwingStamina` for the Steel Katana's swings.
- The counter's area cut can hit the same enemy again after 0.15 s (was 0.25 s), so parries in quick succession land more cuts.
- Smoother animations: no more small pops in the draw cut's follow-through, no elbow wobble as the third cut strikes, and the draw cut and the heavy attack no longer flick the blade out of the scabbard for a moment as they start. The overhead cut keeps the left arm bent through its strike and lands with the hands further out in front and the blade level.
- Steadier stance: the chest stays square to where you face while standing, walking and blocking, so the elbows stay closer to the body and the block no longer looks folded. `Stance.SpineHold` and `ShoulderHold` now default to 1 (old configs at 0.8 move up). The left shoulder sits a little higher, which keeps the left elbow nearer the body. Starting or ending a block and stopping the sheathing early no longer snap.
- The scabbard hangs a little further out to the left, so it no longer cuts into the left thigh while walking. A `Stance.BladeYaw` still at the old default moves to the new one.
- New setting `Debug.RecordPoses` (off): writes your pose to `BepInEx/IaidoPoses` while a katana is in your hand, for tuning the animations.

## 0.2.1

- The counter hits harder (3 times the katana's damage, was 2.5) and its cut also hits every other enemy in reach for double a normal hit, once each.
- Steadier stance: the body no longer swings from side to side when blocking while moving left and right, less shoulder wobble while running.
- Smoother animations: in the third cut the left elbow stays out to the side, no more jiggle or bending in toward the body, smooth hand-overs between the cuts and the stance, and putting the katana away and taking it out again no longer snap.

## 0.2.0

- Custom full-body animations: a draw cut out of the scabbard, an overhead cut and a downward diagonal cut, each with a slow windup and a lightning-fast strike.
- New heavy attack: drawn up into a high stance, charged, then a huge cut straight down with a long lunge. It has its own version when the blade is already out.
- Smoother sheathing after two-handed cuts.
- Greatswords and sledges keep their own animations.
- `Swing.CustomAnimations` false brings back the older animations borrowed from Krom and Stagbreaker.

## 0.1.2 (beta)

- The Thunderclap ends in a wide horizontal finish.
- Smoother sheathing, no more jiggle.
- Recipes use four materials. Both katanas can be refined at the Forge of Potential.
- Fixed katanas losing durability too fast when blocking.

## 0.1.1 (beta)

- Natural arm poses in the stance, the parry, the sheathing and the Thunderclap.
- The katana stays sheathed through a roll.
- New setting `Stance.SpineHold`. Old default stance settings update on their own.
- GitHub link for feedback and bug reports.

## 0.1.0 (beta)

- First public test version, work in progress.
- Steel Katana (Plains) and Flametal Katana (Ashlands), with their own models.
- A perfect parry strikes back on its own. Katanas parry within 200 ms, with an optional precise mode (150 ms, stronger counters).
- Iai stance, scabbard at the hip, draw and cut on a parry, slow sheathing after fighting.
- Impact frame when a counter lands.
- The Flametal Katana's heavy attack is the Thunderclap dash cut.
