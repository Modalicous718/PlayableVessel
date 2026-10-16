# Playable Vessel — mod log

## Goal
Replace the Knight with a playable vessel boss: Hollow Knight ("Sealed Vessel", Black Egg Temple)
and Pure Vessel (Godhome), toggled in the mod menu. Boss animations, slashes, dash, plunge, parry, specials.

## Environment (2026-10-13)
- Game: Hollow Knight 1.5.78.11833, Steam, `<Steam library>\steamapps\common\Hollow Knight`
- Modding API installed (MMHOOK present), PreloadMode = RepackAssets (scene hooks would force a slower mode, so none are used)
- Saves: `%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight`
  - Backup: `%USERPROFILE%\hk-backups\saves-2026-10-13` (restore = copy back)
- .NET 8 SDK installed user-local: `%LOCALAPPDATA%\dotnet-sdk` (set DOTNET_ROOT to it)
- ilspycmd 8.2: `%LOCALAPPDATA%\ilspycmd`; decompiled game code in `%USERPROFILE%\hk-decomp\` (never in the repo)

## Route
Modding API mod (C#, net472). Build: `dotnet build -c Release` → copies to `Mods\PlayableVessel`.
- Visuals: Knight renderer hidden with `forceRenderingOff`; child "Vessel Puppet" uses the boss's tk2d
  collection + animation library and mirrors the Knight's current clip (retimed to the Knight's clip duration
  so FSM "animation finished" waits never stall). Property block + sprite color copied for damage flashes.
- Abilities layer on top of the Knight's controller (physics/hitboxes stay the Knight's).

## Preloads
- HK: `Room_Final_Boss_Core` / `Boss Control` (boss expected at `Hollow Knight Boss`) — UNVERIFIED path
- PV: `GG_Hollow_Knight` / `Battle Scene` (boss `HK Prime`, blasts `Focus Blasts/HK Prime Blast`) — path used by other mods

## Knight clip names (from HeroAnimationController)
Idle, Run, Walk, Turn, Airborne, Double Jump, Walljump, Wall Slide, Land, HardLand, Dash, Shadow Dash(+Sharp/Down),
Back Dash, Slash, SlashAlt, UpSlash, DownSlash, Wall Slash, Fireball, Recoil, Stun, LookUp/LookDown, Sprint...

## Verified facts (launch 1, 2026-10-14)
- HK boss = `Boss Control/Hollow Knight Boss`, library `Hollow Knight Boss Anim` (83 clips). PV = `Battle Scene/HK Prime`, `Hollow Knight Prime Anim` (85 clips, PV has NO Walk clip).
- Boss art faces RIGHT at +x scale (FaceObject spriteFacesRight=True); Knight art faces LEFT (FaceRight sets scale.x=-1) → puppet localScale.x = -1.
- HK clip spelling `Dstab Stomp` vs PV `DStab Stomp`.
- Boss attack prefabs referenced from Control FSM actions: `Shot HK Glob`, `Shot HK Spurt`, `HK Plume`, `HK Plume Prime`, `Shot HK Shadow`, `Roar Wave Emitter`.
- Child effects: Slam Effect, PuppetSlam Effect, Counter Flash, Burst SmallShot/ChestShot, Tendrils (T1-3, T Hit, ST1-2), Focus Blast, Focus Hit, `Focus Blasts/HK Prime Blast/Blast` (Animator enables hero_damager collider).
- Knight Spell Control: intercept at `Wallside?` (fireball), `On Ground?` (quake), `Level Check 3` (scream); MP Cost FSM var.
- Screenshots: capture only works while the game is foreground (fullscreen); `tools/scripts/burst.ps1`.

## Launch 2 (v0.2, 2026-10-14)
- User started a NEW save (King's Pass → Dirtmouth) as Pure Vessel. Seen in screenshots: facing fixed,
  feet aligned, glide-run, slash combo (the white swoosh is part of the boss slash sprites, not the Knight
  arc), dash pose + dash burst, bench sit pose, focus-blast rings, dagger fan.
- No exceptions from the mod. Unrelated errors: GodSeekerPlus/Osmi scene hook, WeaverCore save settings.
- Fixed: dump filename with '/' in asset path.

## Launch 3 (v0.2.1, 2026-10-14) — Hollow Knight form
- Seen working: glob barrage (Shot HK Glob arcs out), chest shot (Shot HK Spurt spray), slash combo,
  dash; after switching to PV: ground swords (HK Plume Prime) erupt correctly at ground level, dagger fan.
- Not confirmed from screenshots: counter/parry, roar, HK puppet slams, HK plunge+plumes (no HK Plume spawned).
- Bug: Burst SmallShot / ChestShot effects drawn far too large over the body (manual scale guess).
  Fix: `BossEffect()` places effects using their boss-space offset/scale/rotation × VisualScale, mirrored to facing.
- No exceptions from the mod in ModLog.

## User feedback round 1 (2026-10-14) → v0.3
1. "PV walk needs work": PV has no walk; the dash-pose glide looked like sliding. Exported atlases
   (`AtlasDumper`, `tools/scripts/clipstrip.ps1` renders clips) → PV "Intro 5" frames 2-11 are alternating
   steps → custom loop clip "Vessel Walk" (12 fps, ×1.5 for Run).
2. "Sealed Vessel orange projectile doesn't come out": Shot HK Glob is an `EnemyBullet`; it bursts on
   OnTriggerEnter2D with tag HeroBox and on any collision → spawned inside the hero it popped instantly.
   Fix: `VesselAssets.IgnoreHero()` (Physics2D.IgnoreCollision with every hero collider); EnemyDamager
   bursts bullets on the enemy they hit via the public `EnemyBullet.OrbitShieldHit`.
3. "Mantis claw looks weird, make it look like holding a ledge": custom "Vessel Cling" — HK "Rest Struggle"
   (hanging from chains, arms overhead), PV "DStab Antic" frame 0 (arms straight up). Puppet repositioned
   while clinging: hands 0.25 above the Knight's head, body 0.15 toward the wall (Knight-local +x).

4. "Stuck in the sword attack's ending pose": the slash sequence (~0.1 + attack duration) outlasts the
   Knight's attack; the Knight is already back in Idle when it ends, and the override-end code recorded
   that clip as "seen" so the mirror never switched. Fix: at sequence end, play the Knight's current clip;
   if the Knight's attack clip lingers after `cState.attacking` is false, play Idle/Airborne (`PlayRest`).

## Launch 4 + user feedback round 2 (2026-10-15) → v0.4
- Verified: glob barrage now flies in arcs and bursts on enemies; PV walk steps; HK/PV wall cling pose shows.
- "Parry not active": user plays on a CONTROLLER; parry was keyboard F only (and F is HK's default quick
  cast key). Now `ParryButton` = QuickCast (default) / DreamNail / QuickMap / KeyboardKeyOnly. The chosen
  action is blocked: CanDreamNail / CanQuickMap hooks, and Spell Control "Can Cast? QC" sends CANCEL.
- Crystal Heart "not working" on HK: user fixed it by re-granting the skill (save flag), not the mod.
- Bench: `BenchPose` option — KnightSit (puppet plays the Knight's own bench clip, unmirrored, sprite scale
  = SizeMultiplier, scaled about a seat point 0.45 above the Knight's feet), Rest (custom "Bench Rest":
  HK Death Collapse 1-4, PV Focus Antic 3-5), Guard (old Counter Ready map).
- Taunt from Silksong (decompiled locally, `~/hk-decomp/silksong`): its own action, keyboard V,
  controller RightStickButton; reward = HeroController.SilkTaunted → next hit within 6 s ×1.5
  (TAUNT_EFFECT_DURATION / DAMAGE_MULT). Implemented as `TauntBuff` (vessel hits + Knight nail via
  HitInstanceHook). Taunt animation = Roar Antic → Roar (stares up). Replaced the old Up+parry roar attack.
- Look up = custom "Vessel LookUp" (Roar Antic + Roar, loops the roar); look down = "Vessel Kneel"
  (StunToIdle reversed, holds the kneel); LookUpEnd → RoarToIdle, LookDownEnd → StunToIdle.

## Round 3 (2026-10-15) → v0.5.0
- tk2d "Tk2d" flipped sprites are stored transposed: sprite (sx,sy) → uv (lerp(u0,u3,sy), lerp(v0,v3,sx))
  (tk2dSpriteGeomGen). Offline renders that just rotate 90° come out mirrored/upside down; earlier pose
  picks made from those renders were re-checked (`tools/scripts` now use Rotate90FlipY).
- Wall: the game turns the Knight to FACE the wall while wall-sliding (FaceLeft on a left wall); Knight
  wall clips are drawn facing away. Vessel puppet is unmirrored for Wall Slide / SD Wall Charge / Wall Slash.
- Bench "Vessel sit": `SitComposer` builds a front-facing seated sprite from HK Turn[2] / PV Turn[1]
  (upper body + cloak hem + nail kept, standing legs dropped, new tapered black legs drawn), in game from
  the atlas (`SitSprites`), so no derived art is shipped. Preview offline with `tools/sitpreview`.
- Talk poses: On.DialogueBox.StartConversation / HideText.
- Music: level music cues live in each scene's `_SceneManager` (SceneManager.musicCue / infectedMusicCue).
  The API's internal `Modding.UnitySceneRepacker.Repack(name, dataPath, json, Mode.AssetBundle)` (reflection)
  extracts them into bundles; cached in `Mods/PlayableVessel/MusicLibrary`. UNTESTED in game yet.
- Packaging: `dotnet build -c Release -t:Package` → dist/PlayableVessel.zip + SHA256; ModLinks entry in
  `modlinks/` (tag LLM-Assisted).

## Status
- [x] v0.1 build: dumper + puppet visuals (guessed clip map)
- [x] Launch 1: dumps read, clip map rebuilt, facing fixed
- [x] v0.2: slash combo, nail reach, dash stab, spells → boss specials, counter/parry, roar, menu
- [ ] Verify HK form specials (glob barrage, plumes, puppet slams, chest shot) and counter vs a real enemy
- [ ] Turn DumpData off by default for release

## Pure Vessel walk (2026-10-15)
- User wants a real PV walk built from PV art (no borrowing the Sealed walk, no AI art). `WalkComposer`:
  upper body = PV Idle[0] above row 302 (+ capsules for the nail, fist, free arm and claw), 10-frame gait
  (58% stance), two-bone IK legs (thigh = shin = 112 px, knees forward), tapered black limbs with a bowed
  shin and pointed toes, 4 px body bob. Shown via a SpriteRenderer child of the puppet (`PlayFrames`), sprite
  pivot = the source sprite origin so it lines up with the tk2d frames. Preview: `tools/sitpreview walk <dir>`.

## Nail damage bug (2026-10-15)
- Mod log showed nail contacts with enemies but no HealthManager.Hit from slashes. FSM trace: every
  `damages_enemy` "Send Event" had damageDealt=0 → IntCompare → CANCEL. Attacks/"Set Slash Damage" reads
  nailDamage via GetNailDamage once (Init) and on UPDATE NAIL DAMAGE; it had stored 0. Fix: NailHits sends
  UPDATE NAIL DAMAGE whenever Slash damageDealt <= 0 (checked each second) + fallback HealthManager.Hit.
- Superdash trace: cancels were mid-air attempts (CanSuperDash needs ground or wall); launches worked.
- HUD: resting masks are child "Idle"/"Idle Hive" tk2d sprites of each "Health N" slot.
- PV tendrils: place relative to boss origin (puppet origin) x VisualScale, mirrored by facing.
