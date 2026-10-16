[README.md](https://github.com/user-attachments/files/33523301/README.md)
# Playable Vessel

Play Hollow Knight as **the Hollow Knight** (the Sealed Vessel from the Black Egg Temple) or as
**Pure Vessel** (Godhome). You get each boss's own sprites, animations, sounds and attack effects, and its
move set mapped onto the Knight's controls.

The mod ships **no game art or audio**. Everything is read from your own install when the game starts.

## Features

- **Two vessels**: switch between them in the mod menu at any time.
- **Boss move sets** on the Knight's controls:

| Input | Hollow Knight (Sealed Vessel) | Pure Vessel |
|---|---|---|
| Attack (x3) | Three-hit sword combo with longer reach | same |
| Up / down attack | Rising slash / down-stab pogo | same |
| Dash | Dash stab (damages what you pass through) | same |
| Cast (Vengeful Spirit) | Infection glob barrage | Shadow dagger fan |
| Down + Cast in the air (Desolate Dive) | Plunge, shockwave, plumes erupt outward | Plunge, ground swords erupt outward |
| Down + Cast on the ground | Puppet slams | Void tendrils |
| Up + Cast (Howling Wraiths) | Chest shot | Focus blast orbs |
| Parry button (Quick Cast by default) | Counter stance: get hit during it to block and counter-slash | same |
| Taunt: **V** / **right stick click** | Stares up and roars. As in Silksong, the next hit within 6 s deals 1.5x damage | same |
| Look up / look down | Roars at the sky / kneels | same |

- Spells still need to be unlocked, still cost soul, and still scale with upgrades and Shaman Stone.
  Nail damage follows your nail upgrades. You take hits on the Knight's own hitbox.
- **Pure Vessel walks**: the boss has no walk animation, so the mod builds one in game from Pure Vessel's
  own side-view art (its mask, cloak and nail), with legs drawn in its style stepping through a full gait.
- **Benches**: the vessel sits like the Knight does. Its sitting sprite is assembled in game from its
  own front-facing art, and it dozes off when the Knight would.
- **NPC conversations**: hold a pose while talking (Listen, Kneel, Bow, Guard or Idle, picked separately for each vessel).
- **Music player**: play any song from the game, with its own volume and an on/off switch.

## Requirements

- Hollow Knight **1.5.78** (current Steam/GOG version)
- [Hollow Knight Modding API](https://github.com/hk-modding/api) (installed automatically by Lumafly)
- No other mods are required

## Install

**Lumafly (recommended).** Search for **PlayableVessel** in Lumafly and press Install. Lumafly also
handles updates and uninstalling.
Before the mod is listed in Lumafly, you can drag `PlayableVessel.zip` onto the Lumafly window to install
it manually.

**By hand.** Download `PlayableVessel-<version>-manual-install.zip` and follow its `INSTALL.txt`: install the
Modding API if you don't have it, then copy the `PlayableVessel` folder into
`Hollow Knight/hollow_knight_Data/Managed/Mods/`.

The first start takes a little longer, because the mod loads the two boss scenes
(`Room_Final_Boss_Core` and `GG_Hollow_Knight`).

**Uninstall:** use Lumafly, or delete the `Mods/PlayableVessel` folder.

## Options (Options → Mods → Playable Vessel)

| Option | Values |
|---|---|
| Play as Vessel | Off / On (applies instantly) |
| Vessel | Hollow Knight / Pure Vessel |
| Size | 1.0x to 2.5x the Knight's height (the hitbox stays Knight-sized) |
| Parry button | Quick Cast / Dream Nail / Quick Map / Keyboard key. The chosen button loses its normal use while you play as the vessel (Quick Cast mode only disables *quick* casting) |
| Bench pose | Vessel sit / Knight's sit / Rest / Guard |
| Talk pose: Sealed / Talk pose: Pure | Listen / Kneel / Bow / Guard / Idle |
| HUD | Default / Sealed Vessel / Pure Vessel / Match vessel. The vessel skins swap the health masks for the vessel's own mask; the Sealed Vessel's soul meter is infection-orange. Default is the untouched game HUD |
| Music player | Off / On |
| Music library | "Build now" collects every song once (background, cached) |
| Music volume | 0–100% |
| Side slash arcs | Hidden / Shown (the Knight's white arc, on top of the vessel's own swing) |

The settings file is `%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight\PlayableVessel.GlobalSettings.json`.
`ParryKey`, `TauntKey` and `MusicKey` accept any Unity KeyCode name (for example `"G"` or `"Mouse3"`), and
`TauntOnRightStick` turns the controller taunt on or off.

## Music player

Open the list with **M** or a **left stick click**. Up/Down chooses a song, Jump plays it, Attack
stops or plays, Left/Right changes the volume, and M, left stick click or the cancel button closes the list.
The Knight stands still while the list is open; it closes on pause, room change or death.

Songs live inside each level's files. Build the full library once from the mod menu (**Music library → Build now**).
It runs in the background (about a minute) and is cached in `Mods/PlayableVessel/MusicLibrary`; delete that folder
to rebuild it. Songs you hear while playing (boss themes, for example) join the list automatically.

## Troubleshooting

- The mod keeps its own log at `Mods/PlayableVessel/logs/PlayableVessel.log` (previous session: `PlayableVessel.prev.log`).
  It records settings, room changes, the vessel's moves, the music list and the HUD, and (with `"DebugHits": true`,
  the default) every swing, what the nail touched, every enemy hit and swings that hit nothing. Attach it to bug reports.
- Log: `%APPDATA%\..\LocalLow\Team Cherry\Hollow Knight\ModLog.txt`. Search it for `PlayableVessel`.
- If the vessel doesn't appear, check that the mod menu shows **Playable Vessel** and that
  *Play as Vessel* is **On**.
- Skin mods such as Custom Knight change the Knight's sprites, which the vessel hides. They still apply to
  the **Knight's sit** bench pose.

## Building from source

Requires the .NET SDK (6 or newer) and a Hollow Knight install with the Modding API.

```
dotnet build -c Release -p:HKRoot="C:\path\to\Hollow Knight"
dotnet build -c Release -p:HKRoot="C:\path\to\Hollow Knight" -t:Package   # dist/PlayableVessel.zip + SHA256
```

You can also set the game path once with the `HOLLOW_KNIGHT_ROOT` environment variable, or in a
git-ignored `Directory.Build.props`. A normal build copies the DLL into your game's Mods folder.

### How it works (for modders)

| File | Role |
|---|---|
| `PlayableVessel.cs` | Mod entry, settings, menu, boss preloads, `BossSource` (boss sprite data, custom clips) |
| `VesselController.cs` | Hides the Knight's sprite and drives a "puppet" sprite with the boss's tk2d library. It mirrors the Knight's animation through `ClipMap` and handles combos, benches, talk poses and wall facing |
| `ClipMap.cs` | Which boss clip shows for each Knight clip |
| `VesselAbilities.cs` | Boss moves: hooks into the Knight's `Spell Control` FSM, parry/counter, taunt, dash stab |
| `VesselAssets.cs`, `EnemyDamager.cs` | Reuses boss projectiles and effects as player-owned copies that damage enemies |
| `SitComposer.cs`, `WalkComposer.cs`, `SitSprites.cs` | Build the bench-sitting sprite and Pure Vessel's walk cycle from boss art (pure C#, previewable offline with `tools/sitpreview sit|walk <dir>`) |
| `MusicPlayer.cs` | Music library (repacked level music cues) and its overlay |
| `TauntBuff.cs` | Silksong's taunt damage bonus |
| `Dumper.cs`, `AtlasDumper.cs` | Development: with `"DumpData": true`, writes boss hierarchies, FSMs, clips and sprite atlases to `Mods/PlayableVessel/dump/` for local study. Never commit that folder |

`docs/DEVLOG.md` records what was learned about the game along the way: object paths, clip names, FSM
states, gotchas. `tools/` contains PowerShell helpers that render the dumped clips, and the offline
sit-sprite preview.

Contributions are welcome. Open an issue or a pull request.

## Credits and disclosure

- Hollow Knight and all its art, animation and audio: **Team Cherry**. None of it is included here.
- Built on the [Hollow Knight Modding API](https://github.com/hk-modding/api) and MonoMod hooks.
- The taunt mechanic follows **Hollow Knight: Silksong** (Team Cherry).
- **AI-assisted**: this mod was designed and written with Claude Code (Anthropic's Claude, Opus) working with
  the mod's creator, who tested every build in game. No AI-generated art is used. The bench-sitting sprite is
  assembled in code from the game's own sprites.

## License

The source code is MIT-licensed (see `LICENSE`). Game content belongs to Team Cherry.
