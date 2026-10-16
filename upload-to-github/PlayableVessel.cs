using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Modding;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace PlayableVessel
{
    public enum VesselForm
    {
        HollowKnight = 0,
        PureVessel = 1
    }

    /// <summary>Which button starts the counter stance. The chosen game action loses its normal use while playing as the vessel.</summary>
    public enum ParryInput
    {
        QuickCast = 0,
        DreamNail = 1,
        QuickMap = 2,
        KeyboardKeyOnly = 3
    }

    /// <summary>What the vessel looks like while resting at a bench.</summary>
    public enum BenchPose
    {
        /// <summary>The Knight's own bench animations (sit, lean, doze, wake, get up), drawn at vessel size.</summary>
        KnightSit = 0,
        /// <summary>Slumped forward: Hollow Knight's collapse pose, Pure Vessel's focus crouch.</summary>
        Rest = 1,
        /// <summary>The vessel's parry stance.</summary>
        Guard = 2,
        /// <summary>The vessel sitting like the Knight does: a sprite built in game from its own front-facing art.</summary>
        VesselSit = 3
    }

    /// <summary>Pose held while talking to an NPC.</summary>
    public enum TalkPose
    {
        /// <summary>Turned toward the camera, attentive (the boss's front-facing turn frame).</summary>
        Listen = 0,
        Kneel = 1,
        Bow = 2,
        Guard = 3,
        Idle = 4
    }

    public class GlobalSettings
    {
        public bool Enabled = true;
        public VesselForm Form = VesselForm.HollowKnight;
        /// <summary>Visual height of the vessel relative to the Knight (1 = Knight sized).</summary>
        public float SizeMultiplier = 1.6f;
        /// <summary>Write boss/hero structure dumps to the mod folder on load (development).</summary>
        public bool DumpData = false;
        public ParryInput ParryButton = ParryInput.QuickCast;
        /// <summary>Keyboard key that always triggers the counter stance (any Unity KeyCode name). Empty = none.</summary>
        public string ParryKey = "";
        /// <summary>Old setting (v0.2), read once to migrate to <see cref="ParryButton"/>.</summary>
        public bool ParryOnDreamNail = false;
        public BenchPose BenchPose = BenchPose.VesselSit;
        public TalkPose TalkPoseHollowKnight = TalkPose.Listen;
        public TalkPose TalkPosePureVessel = TalkPose.Listen;
        /// <summary>Music player: play a chosen game song instead of the area music.</summary>
        public bool MusicOn = false;
        public string MusicSong = "";
        public float MusicVolume = 0.8f;
        /// <summary>Keyboard key that opens the music list (controller: left stick click).</summary>
        public string MusicKey = "M";
        /// <summary>HUD skin: Default keeps the game's HUD.</summary>
        public HudStyle HudStyle = HudStyle.Default;
        /// <summary>Log every swing, nail contact and enemy hit to ModLog.txt (troubleshooting).</summary>
        public bool DebugHits = true;
        /// <summary>Deal nail damage ourselves when the game's slash FSM misses a touched enemy (see NailHits).</summary>
        public bool NailHitFallback = true;
        /// <summary>Taunt (from Silksong): keyboard key, plus right stick click on a controller.</summary>
        public string TauntKey = "V";
        public bool TauntOnRightStick = true;
        /// <summary>Nail hitbox scale, to match the vessel's longer sword.</summary>
        public float NailReach = 1.3f;
        /// <summary>Show the Knight's white slash arcs for side slashes (the vessel's sword swing is in its own frames).</summary>
        public bool ShowNailArcs = false;
    }

    public class PlayableVessel : Mod, IMenuMod, IGlobalSettings<GlobalSettings>
    {
        internal static PlayableVessel Instance;
        internal static GlobalSettings GS = new GlobalSettings();

        internal const string HKScene = "Room_Final_Boss_Core";
        internal const string HKPath = "Boss Control";
        internal const string PVScene = "GG_Hollow_Knight";
        internal const string PVPath = "Battle Scene";

        /// <summary>Preloaded (inactive) source objects per form.</summary>
        internal static readonly Dictionary<VesselForm, BossSource> Sources = new Dictionary<VesselForm, BossSource>();

        internal static string ModDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private bool _heroDumped;

        public PlayableVessel() : base("Playable Vessel") { Instance = this; }

        public override string GetVersion() => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public override List<(string, string)> GetPreloadNames() => new List<(string, string)>
        {
            (HKScene, HKPath),
            (PVScene, PVPath)
        };

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Instance = this;

            TryAddSource(VesselForm.HollowKnight, preloadedObjects, HKScene, HKPath);
            TryAddSource(VesselForm.PureVessel, preloadedObjects, PVScene, PVPath);

            if (GS.DumpData)
            {
                string dir = Path.Combine(ModDir, "dump");
                if (preloadedObjects.TryGetValue(HKScene, out var hk) && hk.TryGetValue(HKPath, out var hkGo))
                    Dumper.DumpTree(hkGo, dir, "HK");
                if (preloadedObjects.TryGetValue(PVScene, out var pv) && pv.TryGetValue(PVPath, out var pvGo))
                    Dumper.DumpTree(pvGo, dir, "PV");
                foreach (var kv in Sources)
                    if (!File.Exists(Path.Combine(dir, kv.Key + "_sprites.tsv")))
                        AtlasDumper.Dump(kv.Value.Library, dir, kv.Key.ToString());
                Log("Wrote boss dumps to " + dir);
            }

            On.HeroController.Start += OnHeroStart;
            MusicPlayer.Create();
            HudSkin.Create();
            VesselLog.Start();
        }

        private void TryAddSource(VesselForm form, Dictionary<string, Dictionary<string, GameObject>> pre, string scene, string path)
        {
            if (!pre.TryGetValue(scene, out var objs) || !objs.TryGetValue(path, out var root) || root == null)
            {
                LogError($"Preload missing for {form}: {scene}/{path}");
                return;
            }
            var src = BossSource.FromRoot(form, root);
            if (src == null)
            {
                LogError($"Could not find a boss sprite/animator under {scene}/{path}");
                return;
            }
            Sources[form] = src;
            Log($"{form}: boss='{src.Boss.name}' library='{src.Library.name}' clips={src.Library.clips.Length}");
        }

        private void OnHeroStart(On.HeroController.orig_Start orig, HeroController self)
        {
            orig(self);
            if (GS.DumpData && !_heroDumped)
            {
                _heroDumped = true;
                Dumper.DumpTree(self.gameObject, Path.Combine(ModDir, "dump"), "Knight");
            }
            if (self.GetComponent<VesselController>() == null)
            {
                self.gameObject.AddComponent<VesselController>();
                self.gameObject.AddComponent<VesselAbilities>();
                self.gameObject.AddComponent<NailHits>();
            }
        }

        internal static BossSource CurrentSource
        {
            get
            {
                if (Sources.TryGetValue(GS.Form, out var s)) return s;
                return Sources.Values.FirstOrDefault();
            }
        }

        // ---------- settings / menu ----------

        public void OnLoadGlobal(GlobalSettings s)
        {
            GS = s ?? new GlobalSettings();
            if (GS.ParryOnDreamNail)
            {
                GS.ParryButton = ParryInput.DreamNail;
                GS.ParryOnDreamNail = false;
            }
            if (GS.ParryKey == "F") GS.ParryKey = ""; // v0.2 default; F is Hollow Knight's default quick cast key
        }
        public GlobalSettings OnSaveGlobal()
        {
            VesselLog.Write("setting", Newtonsoft.Json.JsonConvert.SerializeObject(GS));
            VesselLog.ApplyHitLogging(GS.DebugHits);
            return GS;
        }

        public bool ToggleButtonInsideMenu => false;

        private static readonly BenchPose[] BenchMenu = { BenchPose.VesselSit, BenchPose.KnightSit, BenchPose.Rest, BenchPose.Guard };
        private static readonly string[] TalkNames = { "Listen", "Kneel", "Bow", "Guard", "Idle" };

        /// <summary>Re-make the custom clips (after a setting that changes them) and drop cached clip choices.</summary>
        internal static void RebuildClips()
        {
            foreach (var s in Sources.Values) s.BuildCustomClips();
            ClipMap.Clear();
            VesselController.Instance?.Refresh();
        }

        private static readonly float[] Sizes = { 1.0f, 1.3f, 1.6f, 2.0f, 2.5f };

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry) => new List<IMenuMod.MenuEntry>
        {
            new IMenuMod.MenuEntry("Play as Vessel", new[] { "Off", "On" }, "Replace the Knight with the vessel",
                i => { GS.Enabled = i == 1; VesselController.Instance?.Refresh(); }, () => GS.Enabled ? 1 : 0),
            new IMenuMod.MenuEntry("Vessel", new[] { "Hollow Knight", "Pure Vessel" }, "Which boss kit to use",
                i => { GS.Form = (VesselForm)i; VesselController.Instance?.Refresh(); }, () => (int)GS.Form),
            new IMenuMod.MenuEntry("Size", Sizes.Select(x => x.ToString("0.0") + "x").ToArray(), "Visual size relative to the Knight",
                i => { GS.SizeMultiplier = Sizes[i]; VesselController.Instance?.Refresh(); },
                () => { int idx = Array.FindIndex(Sizes, x => Mathf.Approximately(x, GS.SizeMultiplier)); return idx < 0 ? 2 : idx; }),
            new IMenuMod.MenuEntry("Parry button", new[] { "Quick Cast", "Dream Nail", "Quick Map", "Keyboard key" }, "This button starts the counter stance instead of its normal use",
                i => GS.ParryButton = (ParryInput)i, () => (int)GS.ParryButton),
            new IMenuMod.MenuEntry("Bench pose", new[] { "Vessel sit", "Knight's sit", "Rest", "Guard" }, "How the vessel sits at benches",
                i => { GS.BenchPose = BenchMenu[i]; VesselController.Instance?.Refresh(); },
                () => Math.Max(0, Array.IndexOf(BenchMenu, GS.BenchPose))),
            new IMenuMod.MenuEntry("Talk pose: Sealed", TalkNames, "Hollow Knight's pose while talking to NPCs",
                i => GS.TalkPoseHollowKnight = (TalkPose)i, () => (int)GS.TalkPoseHollowKnight),
            new IMenuMod.MenuEntry("Talk pose: Pure", TalkNames, "Pure Vessel's pose while talking to NPCs",
                i => GS.TalkPosePureVessel = (TalkPose)i, () => (int)GS.TalkPosePureVessel),
            new IMenuMod.MenuEntry("HUD", new[] { "Default", "Sealed Vessel", "Pure Vessel", "Match vessel" }, "Health masks (and the Sealed Vessel's orange soul) in vessel style",
                i => GS.HudStyle = (HudStyle)i, () => (int)GS.HudStyle),
            new IMenuMod.MenuEntry("Music player", new[] { "Off", "On" }, "Pick songs from the list: M or left stick click",
                i => { GS.MusicOn = i == 1; MusicPlayer.Instance?.ApplySettings(); }, () => GS.MusicOn ? 1 : 0),
            new IMenuMod.MenuEntry("Music library", new[] { "-", "Build now" }, "One-time: collect every song from the game files (runs in the background)",
                i => { if (i == 1) MusicPlayer.Instance?.BuildLibrary(); }, () => 0),
            new IMenuMod.MenuEntry("Music volume", Enumerable.Range(0, 11).Select(v => v * 10 + "%").ToArray(), "Volume of the music player",
                i => GS.MusicVolume = i / 10f, () => Mathf.Clamp(Mathf.RoundToInt(GS.MusicVolume * 10f), 0, 10)),
            new IMenuMod.MenuEntry("Side slash arcs", new[] { "Hidden", "Shown" }, "Show the Knight's white arcs on side slashes",
                i => { GS.ShowNailArcs = i == 1; VesselController.Instance?.Refresh(); }, () => GS.ShowNailArcs ? 1 : 0),
        };
    }

    /// <summary>The preloaded boss for one form: its root and its sprite animation data.</summary>
    internal class BossSource
    {
        public VesselForm Form;
        public GameObject Root;
        public GameObject Boss;
        public tk2dSpriteAnimation Library;
        public tk2dSpriteCollectionData Collection;
        public int DefaultSpriteId;

        public static BossSource FromRoot(VesselForm form, GameObject root)
        {
            // The boss is the object with a HealthManager and a sprite animator; prefer known names.
            string[] preferred = form == VesselForm.HollowKnight
                ? new[] { "Hollow Knight Boss", "HK Boss", "Hollow Knight" }
                : new[] { "HK Prime" };
            GameObject boss = null;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var name in preferred)
            {
                boss = all.FirstOrDefault(t => t.name == name && t.GetComponent<tk2dSpriteAnimator>() != null)?.gameObject;
                if (boss != null) break;
            }
            if (boss == null)
                boss = all.FirstOrDefault(t => t.GetComponent<HealthManager>() != null && t.GetComponent<tk2dSpriteAnimator>() != null)?.gameObject;
            if (boss == null) return null;

            var anim = boss.GetComponent<tk2dSpriteAnimator>();
            var spr = boss.GetComponent<tk2dSprite>();
            if (anim == null || anim.Library == null || spr == null) return null;
            var src = new BossSource
            {
                Form = form,
                Root = root,
                Boss = boss,
                Library = anim.Library,
                Collection = spr.Collection,
                DefaultSpriteId = spr.spriteId
            };

            // Body box in boss space, from the boss's standing hurtbox (Colliders/Idle).
            var idle = boss.transform.Find("Colliders/Idle");
            var box = idle != null ? idle.GetComponent<BoxCollider2D>() : null;
            src.BodyRect = box != null
                ? new Rect(box.offset - box.size / 2f, box.size)
                : new Rect(-1.3f, -3.85f, 2.0f, 4.3f);

            // Plumes are placed relative to where the boss stands: Plume Y - (Stun Land Y + feet).
            var control = boss.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Control");
            var plumeY = control?.FsmVariables.FindFsmFloat("Plume Y");
            var landY = control?.FsmVariables.FindFsmFloat("Stun Land Y");
            if (plumeY != null && landY != null)
                src.PlumeYOffset = plumeY.Value - (landY.Value + src.BodyRect.yMin);
            src.BuildCustomClips();
            PlayableVessel.Instance.Log($"{form}: body={src.BodyRect} plumeYOffset={src.PlumeYOffset:0.##}");
            return src;
        }

        /// <summary>The boss's standing hurtbox in boss space (art facing right).</summary>
        public Rect BodyRect;
        /// <summary>Plume spawn height relative to the ground under the vessel.</summary>
        public float PlumeYOffset;

        /// <summary>Clips the bosses don't have, assembled from their own frames (see <see cref="BuildCustomClips"/>).</summary>
        public readonly Dictionary<string, tk2dSpriteAnimationClip> Custom = new Dictionary<string, tk2dSpriteAnimationClip>();

        public tk2dSpriteAnimationClip Clip(string name) =>
            name != null && Custom.TryGetValue(name, out var c) ? c : Library.GetClipByName(name);

        /// <summary>
        /// Extra clips, picked by looking at the exported frames:
        ///  - "Vessel Walk" (Pure Vessel has no walk): the alternating steps in its "Intro 5" frames 2-11.
        ///  - "Vessel Cling" (wall slide / mantis claw): arms raised overhead, as if gripping a ledge.
        ///    Hollow Knight: "Rest Struggle" (hanging from its chains). Pure Vessel: "DStab Antic" frame 0.
        /// </summary>
        public void BuildCustomClips()
        {
            Custom.Clear();
            if (Form == VesselForm.PureVessel)
            {
                // Pure Vessel has no walk. "Vessel Walk" is shown with frames built from its own art
                // (WalkSprites / WalkComposer); this tk2d clip is only the fallback if building fails.
                AddCustom("Vessel Walk", "Intro 5", new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 12f);
                AddCustom("Talk Listen", "Turn", new[] { 1 }, 1f);
                AddCustom("Talk Bow", "Focus Antic", new[] { 2 }, 1f);
                AddCustom("Vessel Cling", "DStab Antic", new[] { 0 }, 1f);
                AddCustom("Bench Rest", "Focus Antic", new[] { 3, 4, 5, 4 }, 3f);
            }
            else
            {
                AddCustom("Vessel Cling", "Rest Struggle", new[] { 0, 1, 2, 3 }, 6f);
                AddCustom("Talk Listen", "Turn", new[] { 2 }, 1f);
                AddCustom("Talk Bow", "Roar Antic", new[] { 3 }, 1f);
                AddCustom("Bench Rest", "Death Collapse", new[] { 1, 2, 3, 4, 3, 2 }, 3f);
            }

            // Fluid sword swings: end of the wind-up, the swing, and the follow-through as one clip each.
            var a1 = Library.GetClipByName("Slash1 Antic");
            int aN = a1 != null ? a1.frames.Length : 0;
            var anticTail = aN >= 3 ? new[] { aN - 3, aN - 2, aN - 1 } : null;
            AddCustom("Swing 1", 20f, tk2dSpriteAnimationClip.WrapMode.Once, 0, ("Slash1 Antic", anticTail ?? new int[0]), ("Slash1", null), ("Slash1 Recover", null));
            AddCustom("Swing 2", 20f, tk2dSpriteAnimationClip.WrapMode.Once, 0, ("Slash2", null), ("Slash2 Recover", null));
            AddCustom("Swing 3", 20f, tk2dSpriteAnimationClip.WrapMode.Once, 0, ("Slash3", null), ("Slash3 Recover", null));

            // Looking up: wind up and roar at the sky, then hold the roar while Up is held.
            var antic = Library.GetClipByName("Roar Antic");
            int anticLen = antic != null ? antic.frames.Length : 0;
            AddCustom("Vessel LookUp", 18f, tk2dSpriteAnimationClip.WrapMode.LoopSection, anticLen,
                ("Roar Antic", null), ("Roar", null));

            // Looking down: sink to one knee (the stun recovery played backwards) and stay there.
            var rise = Library.GetClipByName("StunToIdle");
            if (rise != null)
            {
                var down = Enumerable.Range(0, rise.frames.Length).Reverse().ToArray();
                AddCustom("Vessel Kneel", 15f, tk2dSpriteAnimationClip.WrapMode.LoopSection, down.Length - 1, ("StunToIdle", down));
            }
        }

        private void AddCustom(string name, string from, int[] frames, float fps) =>
            AddCustom(name, fps, tk2dSpriteAnimationClip.WrapMode.Loop, 0, (from, frames));

        /// <summary>Concatenate frames from boss clips (null frame list = all frames) into a new clip.</summary>
        private void AddCustom(string name, float fps, tk2dSpriteAnimationClip.WrapMode wrap, int loopStart, params (string clip, int[] frames)[] parts)
        {
            var frames = new List<tk2dSpriteAnimationFrame>();
            foreach (var (clipName, indices) in parts)
            {
                var lib = Library;
                string name2 = clipName;
                if (clipName.StartsWith("HK:"))
                {
                    lib = PlayableVessel.Sources[VesselForm.HollowKnight].Library;
                    name2 = clipName.Substring(3);
                }
                var src = lib.GetClipByName(name2);
                if (src == null) continue;
                foreach (int i in indices ?? Enumerable.Range(0, src.frames.Length).ToArray())
                {
                    if (i < 0 || i >= src.frames.Length) continue;
                    var f = new tk2dSpriteAnimationFrame();
                    f.CopyFrom(src.frames[i]);
                    f.ClearTrigger();
                    frames.Add(f);
                }
            }
            if (frames.Count == 0) return;
            Custom[name] = new tk2dSpriteAnimationClip
            {
                name = name,
                fps = fps,
                wrapMode = wrap,
                loopStart = Mathf.Clamp(loopStart, 0, frames.Count - 1),
                frames = frames.ToArray()
            };
        }

        /// <summary>Find an object under the preloaded root by name, or by path when the name contains '/'.</summary>
        public GameObject Find(string name)
        {
            if (name.Contains("/"))
                return Root.transform.Find(name)?.gameObject;
            foreach (var t in Root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
