# Changelog

## 0.5.0 — 2026-10-15
First public release.
- Play as the Hollow Knight (Sealed Vessel) or Pure Vessel, switchable in the mod menu.
- Boss move sets on the Knight's controls: three-hit sword combo, dash stab, glob barrage / dagger fan,
  plunge with plumes / ground swords, puppet slams / tendrils, chest shot / focus blast, counter-parry.
- Silksong-style taunt (V / right stick click): the vessel roars; the next hit within 6 s deals 1.5x.
- Look up = roar at the sky, look down = kneel.
- Bench poses: a vessel sitting sprite built in game from the bosses' own art (no AI art), the Knight's
  own sit, a slumped rest, or the guard stance. Dozes off like the Knight.
- Talk poses (separate choice for each vessel): Listen, Kneel, Bow, Guard, Idle.
- Wall cling and wall-side moves face away from the wall.
- Pure Vessel walk cycle built in game from its own art (it has no walk animation).
- Parry button is configurable (Quick Cast, Dream Nail, Quick Map, or a keyboard key).
- Music player: any song from the game (one-time library build), volume, on/off.
- HUD skins (switchable, Default keeps the normal HUD): vessel masks for health; orange soul meter for the Sealed Vessel.
- Slashes show only the swing arc (the boss follow-through pointed the sword behind the vessel), and nail,
  dash-stab and counter hitboxes grow with the Size option.
- Fixed: nail hits never landing. The slashes' damage stayed 0 (read before the save was ready), so the
  game cancelled every hit; the mod now asks the game to re-read nail damage, with a fallback hit if a
  touched enemy still isn't damaged.
- Fixed: Pure Vessel's tendrils now appear exactly where the boss's would, scaled to the vessel.
- Fixed: vessel HUD masks hid only the break animation; the resting masks are covered too.
- Fluid three-hit sword combo (wind-up, swing, follow-through) with a small lunge.
- Mod log: Mods/PlayableVessel/logs/PlayableVessel.log.
