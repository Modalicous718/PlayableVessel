using System.Collections.Generic;
using GlobalEnums;
using Modding;
using UnityEngine;

namespace PlayableVessel
{
    /// <summary>
    /// Lives on the Knight. Hides the Knight's own sprite and drives a child "puppet" that uses
    /// the boss's sprite collection and animation library. The Knight's physics, hitboxes and
    /// state machines stay in charge; the puppet mirrors whatever the Knight is doing with the
    /// matching boss animation, unless an ability or the slash combo has taken the puppet over.
    /// </summary>
    public class VesselController : MonoBehaviour
    {
        internal static VesselController Instance;

        private HeroController _hc;
        private tk2dSpriteAnimator _heroAnim;
        private tk2dSprite _heroSprite;
        private MeshRenderer _heroRend;

        private GameObject _puppet;
        private tk2dSprite _pSprite;
        private tk2dSpriteAnimator _pAnim;
        private MeshRenderer _pRend;
        private MaterialPropertyBlock _mpb;

        internal BossSource Source { get; private set; }
        internal VesselAssets Assets { get; private set; }
        internal bool Active => _puppet != null;
        internal GameObject Puppet => _puppet;
        internal float VisualScale { get; private set; } = 1f;
        /// <summary>How much bigger than the default 1.6x size the vessel is drawn (hitboxes scale with it).</summary>
        internal static float SizeFactor => Mathf.Max(1f, PlayableVessel.GS.SizeMultiplier / 1.6f);

        private static readonly Dictionary<VesselForm, VesselAssets> AssetCache = new Dictionary<VesselForm, VesselAssets>();

        // mirroring
        private string _lastHeroClip;
        private float _lastHeroTime;

        // overrides: a queue of (clip, seconds); empty queue + _holdUntil in the future = hold last clip
        private readonly Queue<KeyValuePair<string, float>> _sequence = new Queue<KeyValuePair<string, float>>();
        private float _stepEnds;
        private bool _overriding;
        private bool _holdForever;

        // combo
        private int _comboIndex;
        private float _lastSlashTime;

        // nail tweaks we must undo
        private readonly Dictionary<NailSlash, Vector3> _origSlashScale = new Dictionary<NailSlash, Vector3>();

        private void Awake()
        {
            Instance = this;
            _hc = GetComponent<HeroController>();
            _heroAnim = GetComponent<tk2dSpriteAnimator>();
            _heroSprite = GetComponent<tk2dSprite>();
            _heroRend = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            ModHooks.AttackHook += OnAttack;
            On.DialogueBox.StartConversation += OnStartConversation;
            On.DialogueBox.HideText += OnHideText;
        }

        private void Start() => Refresh();

        private void OnDestroy()
        {
            ModHooks.AttackHook -= OnAttack;
            On.DialogueBox.StartConversation -= OnStartConversation;
            On.DialogueBox.HideText -= OnHideText;
            RestoreNail();
            if (Instance == this) Instance = null;
        }

        /// <summary>Rebuild or remove the puppet to match the current settings.</summary>
        public void Refresh()
        {
            DestroyPuppet();
            RestoreNail();
            if (!PlayableVessel.GS.Enabled) return;
            Source = PlayableVessel.CurrentSource;
            if (Source == null)
            {
                PlayableVessel.Instance.LogError("No vessel source loaded; staying as the Knight.");
                return;
            }
            if (!AssetCache.TryGetValue(Source.Form, out var assets))
                AssetCache[Source.Form] = assets = new VesselAssets(Source);
            Assets = assets;
            BuildPuppet();
            ApplyNail();
            GetComponent<VesselAbilities>()?.OnFormChanged();
        }

        private void BuildPuppet()
        {
            _puppet = new GameObject("Vessel Puppet");
            _puppet.layer = gameObject.layer;
            _puppet.transform.SetParent(transform, false);

            _pSprite = tk2dBaseSprite.AddComponent<tk2dSprite>(_puppet, Source.Collection, Source.DefaultSpriteId);
            _pAnim = tk2dSpriteAnimator.AddComponent(_puppet, Source.Library, 0);
            _pAnim.playAutomatically = false;
            _pRend = _puppet.GetComponent<MeshRenderer>();

            // Size and alignment from body hitboxes: the Knight's collider vs the boss's idle collider.
            var kCol = GetComponent<BoxCollider2D>();
            Rect knight = kCol != null
                ? new Rect(kCol.offset - kCol.size / 2f, kCol.size)
                : new Rect(-0.25f, -1.3f, 0.5f, 1.3f);
            Rect boss = Source.BodyRect;

            float s = PlayableVessel.GS.SizeMultiplier * knight.height / boss.height;
            VisualScale = s;
            _pSprite.scale = new Vector3(s, s, 1f);
            // Boss art faces right (+x) while the Knight's art faces left, so the puppet is mirrored.
            const float sign = -1f;
            float x = knight.center.x - sign * boss.center.x * s;
            float y = knight.yMin - boss.yMin * s;
            _puppet.transform.localPosition = new Vector3(x, y, -0.001f);
            _basePos = _puppet.transform.localPosition;
            _knightRect = knight;
            _puppet.transform.localScale = new Vector3(sign, 1f, 1f);
            _knightArt = false;

            _lastHeroClip = null;
            _sequence.Clear();
            _overriding = false;
            VesselLog.Write("vessel", $"built {Source.Form} size={PlayableVessel.GS.SizeMultiplier} scale={s:0.###}");
            PlayableVessel.Instance.Log($"Puppet built for {Source.Form}: scale={s:0.###} offset=({x:0.##},{y:0.##}) knightBody={knight} bossBody={boss}");
        }

        private void DestroyPuppet()
        {
            StopFrames();
            _frameRend = null; // child of the puppet, destroyed with it
            HideSit();
            _talkPose = false;
            if (_puppet != null) Destroy(_puppet);
            _puppet = null;
            if (_heroRend != null) _heroRend.forceRenderingOff = false;
        }

        // ---------- nail ----------

        private static readonly string[] SideSlashes = { "Slash", "AltSlash", "WallSlash" };

        private void ApplyNail()
        {
            var slashes = new[] { _hc.normalSlash, _hc.alternateSlash, _hc.upSlash, _hc.downSlash, _hc.wallSlash };
            foreach (var ns in slashes)
            {
                if (ns == null || _origSlashScale.ContainsKey(ns)) continue;
                _origSlashScale[ns] = ns.scale;
                // Reach grows with the vessel: at the default 1.6x size it is NailReach, bigger vessels reach further.
                float reach = PlayableVessel.GS.NailReach * SizeFactor;
                ns.scale = new Vector3(ns.scale.x * reach, ns.scale.y * reach, ns.scale.z);
                if (!PlayableVessel.GS.ShowNailArcs && System.Array.IndexOf(SideSlashes, ns.gameObject.name) >= 0)
                {
                    var r = ns.GetComponent<MeshRenderer>();
                    if (r != null) r.forceRenderingOff = true;
                }
            }
        }

        private void RestoreNail()
        {
            foreach (var kv in _origSlashScale)
            {
                if (kv.Key == null) continue;
                kv.Key.scale = kv.Value;
                var r = kv.Key.GetComponent<MeshRenderer>();
                if (r != null) r.forceRenderingOff = false;
            }
            _origSlashScale.Clear();
        }

        private void OnAttack(AttackDirection dir)
        {
            if (!Active || _holdForever) return;
            float dur = PlayerData.instance.GetBool("equippedCharm_32") ? _hc.ATTACK_DURATION_CH : _hc.ATTACK_DURATION;
            switch (dir)
            {
                case AttackDirection.normal:
                    _comboIndex = Time.time - _lastSlashTime < 0.75f ? (_comboIndex + 1) % 3 : 0;
                    _lastSlashTime = Time.time;
                    int n = _comboIndex + 1;
                    // Wind-up tail, swing and follow-through as one fluid clip, plus a short lunge for weight.
                    PlaySequence((Pick("Swing " + n, "Slash" + n), dur + 0.08f));
                    if (!_hc.cState.wallSliding) Lunge(dur + 0.08f);
                    if (_hc.cState.wallSliding) FaceAwayFromWall(); // wall slash: strike away from the wall
                    break;
                case AttackDirection.upward:
                    PlaySequence((Pick("Swing 3", "Slash3"), dur + 0.08f));
                    Lunge(dur + 0.08f, 0.5f);
                    break;
                case AttackDirection.downward:
                    PlaySequence((Pick("Dstab Stomp", "DStab Stomp"), dur));
                    break;
            }
        }

        // ---------- lunge ----------

        private float _lungeStart, _lungeDur, _lungeDist;

        /// <summary>Ease the body forward and back over <paramref name="seconds"/> (purely visual).</summary>
        private void Lunge(float seconds, float strength = 1f)
        {
            _lungeStart = Time.time;
            _lungeDur = seconds;
            _lungeDist = 0.28f * strength * SizeFactor;
        }

        private void ApplyLunge()
        {
            if (_lungeDur <= 0f || _knightArt || _puppet == null) return;
            float t = (Time.time - _lungeStart) / _lungeDur;
            if (t >= 1f) { _lungeDur = 0f; _puppet.transform.localPosition = _basePos; return; }
            // Quick push forward, slower settle back. Knight-local forward is -x.
            float d = t < 0.35f ? Mathf.SmoothStep(0f, 1f, t / 0.35f) : Mathf.SmoothStep(1f, 0f, (t - 0.35f) / 0.65f);
            _puppet.transform.localPosition = _basePos + new Vector3(-_lungeDist * d, 0f, 0f);
        }

        // ---------- puppet animation API for abilities ----------

        /// <summary>First clip name that exists in the current boss library.</summary>
        internal string Pick(params string[] names)
        {
            foreach (var n in names)
                if (Source != null && ClipMap.Usable(Source, n)) return n;
            return "Idle";
        }

        /// <summary>Play clips back to back, each stretched to its given duration, ignoring the Knight meanwhile.</summary>
        public void PlaySequence(params (string clip, float seconds)[] steps)
        {
            if (!Active) return;
            SetKnightArt(false);
            _sequence.Clear();
            foreach (var s in steps) _sequence.Enqueue(new KeyValuePair<string, float>(s.clip, s.seconds));
            _holdForever = false;
            _overriding = true;
            NextStep();
        }

        /// <summary>Play a clip (at its own speed times <paramref name="speed"/>) and keep the puppet on it until <see cref="Release"/>.</summary>
        public void Hold(string clip, float speed = 1f)
        {
            if (!Active) return;
            SetKnightArt(false);
            _sequence.Clear();
            var c = Source.Clip(clip);
            if (c == null || c.frames.Length == 0) return;
            _pAnim.Play(c, 0f, c.fps * speed);
            _overriding = true;
            _holdForever = true;
        }

        /// <summary>Like <see cref="PlaySequence"/> but the last clip is then held until <see cref="Release"/>.</summary>
        public void PlayThenHold(params (string clip, float seconds)[] steps)
        {
            PlaySequence(steps);
            _holdForever = true;
        }

        /// <summary>Give the puppet back to the Knight's animation.</summary>
        public void Release()
        {
            _sequence.Clear();
            _holdForever = false;
            _overriding = false;
            _lastHeroClip = null;
        }

        private void NextStep()
        {
            if (_sequence.Count == 0)
            {
                if (_holdForever) return;
                _overriding = false;
                _lastHeroTime = _heroAnim.ClipTimeSeconds;
                var cur = _heroAnim.CurrentClip;
                if (cur != null && IsAttackClip(cur.name) && _hc.cState.attacking)
                {
                    // The Knight's swing is still going: hold our last frame until it ends.
                    _lastHeroClip = cur.name;
                    return;
                }
                // Catch up with whatever the Knight is doing now (it has usually gone back to idle
                // while our longer sword animation was still playing).
                _lastHeroClip = cur?.name;
                _rested = false;
                if (cur != null) PlayMapped(cur);
                return;
            }
            var step = _sequence.Dequeue();
            var c = Source.Clip(step.Key);
            float secs = Mathf.Max(0.02f, step.Value);
            _stepEnds = Time.time + secs;
            if (c != null && c.frames.Length > 0)
                _pAnim.Play(c, 0f, c.frames.Length / secs);
        }

        // ---------- per-frame sync ----------

        private void LateUpdate()
        {
            if (!Active) return;

            // The game still toggles the Knight's renderer (transitions, cutscenes); mirror that intent.
            _heroRend.forceRenderingOff = true;
            bool frames = _frames != null && !_overriding;
            _pRend.enabled = _heroRend.enabled && !_sitShown && !frames;
            if (_frameRend != null)
            {
                _frameRend.enabled = _heroRend.enabled && frames;
                if (frames)
                {
                    int idx = (int)((Time.time - _frameStart) * _frameFps) % _frames.Length;
                    _frameRend.sprite = _frames[idx];
                    _frameRend.sortingLayerID = _heroRend.sortingLayerID;
                    _frameRend.sortingOrder = _heroRend.sortingOrder;
                    _frameRend.color = _heroSprite.color;
                }
            }
            _pRend.sortingLayerID = _heroRend.sortingLayerID;
            _pRend.sortingOrder = _heroRend.sortingOrder;
            if (_sitRend != null)
            {
                _sitRend.enabled = _heroRend.enabled && _sitShown;
                _sitRend.sortingLayerID = _heroRend.sortingLayerID;
                _sitRend.sortingOrder = _heroRend.sortingOrder;
                _sitRend.color = _heroSprite.color;
            }

            UpdateTalking();

            // Damage flashes and tints come through the property block and the sprite color.
            _heroRend.GetPropertyBlock(_mpb);
            _pRend.SetPropertyBlock(_mpb);
            Color c = _heroSprite.color;
            if (_pSprite.color != c) _pSprite.color = c;

            ApplyLunge();

            if (_overriding)
            {
                if (Time.time >= _stepEnds && (_sequence.Count > 0 || !_holdForever)) NextStep();
                return;
            }
            MirrorHeroAnimation();
        }

        private void MirrorHeroAnimation()
        {
            var hc = _heroAnim.CurrentClip;
            if (hc == null) return;
            float t = _heroAnim.ClipTimeSeconds;
            bool restarted = hc.name == _lastHeroClip && t + 0.02f < _lastHeroTime;
            if (hc.name != _lastHeroClip || restarted)
            {
                _rested = false;
                _lastHeroClip = hc.name;
                PlayMapped(hc);
            }
            else if (!_rested && IsAttackClip(hc.name) && !_hc.cState.attacking)
            {
                // The swing is over but the Knight's attack clip lingers on its last frame: settle back.
                PlayRest();
            }
            _lastHeroTime = t;
        }

        private bool _rested;

        private static bool IsAttackClip(string name) =>
            name == "Slash" || name == "SlashAlt" || name == "UpSlash" || name == "DownSlash" || name == "Wall Slash";

        /// <summary>Idle on the ground, airborne pose in the air (used after a swing ends).</summary>
        private void PlayRest()
        {
            _rested = true;
            var heroRest = _heroAnim.Library.GetClipByName(_hc.cState.onGround ? "Idle" : "Airborne");
            if (heroRest != null) PlayMapped(heroRest);
        }

        private Vector3 _basePos;
        private Rect _knightRect;

        /// <summary>
        /// Wall cling: hang from the top of the wall. The pose's raised hands go just above the Knight's
        /// head and the body is pulled toward the wall, which is behind the Knight (local +x).
        /// </summary>
        private Vector3 ClingPosition(tk2dSpriteAnimationClip clip)
        {
            var f = clip.frames[0];
            var def = f.spriteCollection.spriteDefinitions[f.spriteId];
            float top = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in def.positions) { top = Mathf.Max(top, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
            float s = VisualScale;
            // On the wall the puppet is unmirrored (see WallClips) and the wall is in front (Knight-local -x):
            // local x = puppetX + spriteX * s; put the body centre 0.15 toward the wall.
            float x = -0.15f - (minX + maxX) / 2f * s;
            float y = _knightRect.yMax + 0.25f - top * s;
            return new Vector3(x, y, _basePos.z);
        }

        // ---------- talking to NPCs ----------

        internal static bool Talking;
        private bool _talkPose;
        private float _talkFreeSince;

        private static void OnStartConversation(On.DialogueBox.orig_StartConversation orig, DialogueBox self, string convName, string sheetName)
        {
            Talking = true;
            orig(self, convName, sheetName);
        }

        private static void OnHideText(On.DialogueBox.orig_HideText orig, DialogueBox self)
        {
            Talking = false;
            orig(self);
        }

        /// <summary>Hold the chosen conversation pose while a dialogue box is open.</summary>
        private void UpdateTalking()
        {
            // Safety net: if control has been back for a while, the conversation is over.
            if (Talking && _hc.acceptingInput) { if (Time.time - _talkFreeSince > 1f) Talking = false; }
            else _talkFreeSince = Time.time;

            if (Talking && !_talkPose && !_hc.cState.dead)
            {
                _talkPose = true;
                var pose = Source.Form == VesselForm.HollowKnight ? PlayableVessel.GS.TalkPoseHollowKnight : PlayableVessel.GS.TalkPosePureVessel;
                string clip = pose switch
                {
                    TalkPose.Listen => "Talk Listen",
                    TalkPose.Kneel => "Vessel Kneel",
                    TalkPose.Bow => "Talk Bow",
                    TalkPose.Guard => "Counter Ready",
                    _ => "Idle"
                };
                Hold(Pick(clip, "Idle"));
            }
            else if (!Talking && _talkPose)
            {
                _talkPose = false;
                Release();
            }
        }

        // ---------- walls ----------

        // On a wall the game turns the Knight to face the wall, but these Knight animations are drawn facing
        // away from it. The vessel art is not, so it is mirrored for them.
        private static readonly HashSet<string> WallClips = new HashSet<string> { "Wall Slide", "SD Wall Charge", "Wall Slash" };

        private void FaceAwayFromWall() => _puppet.transform.localScale = new Vector3(1f, 1f, 1f);

        // ---------- built frame animations (Pure Vessel walk) ----------

        private SpriteRenderer _frameRend;
        private Sprite[] _frames;
        private float _frameFps, _frameStart;

        /// <summary>Play built sprites (instead of the tk2d puppet) at <paramref name="fps"/>, looping.</summary>
        private bool PlayFrames(Sprite[] frames, float fps)
        {
            if (frames == null || frames.Length == 0 || _puppet == null) return false;
            if (_frameRend == null)
            {
                var go = new GameObject("Vessel Frames");
                go.layer = gameObject.layer;
                go.transform.SetParent(_puppet.transform, false);
                _frameRend = go.AddComponent<SpriteRenderer>();
            }
            // Child of the (mirrored) puppet, scaled like the boss sprite, so it lines up with the tk2d frames.
            _frameRend.transform.localPosition = Vector3.zero;
            _frameRend.transform.localScale = new Vector3(VisualScale, VisualScale, 1f);
            if (_frames != frames) _frameStart = Time.time;
            _frames = frames;
            _frameFps = fps;
            return true;
        }

        private void StopFrames()
        {
            _frames = null;
            if (_frameRend != null) _frameRend.enabled = false;
        }

        // ---------- vessel sit sprite ----------

        private SpriteRenderer _sitRend;
        private bool _sitShown;

        /// <summary>Show the built sitting sprite (see SitSprites) at the bench seat. False if it could not be built.</summary>
        private bool ShowSit(bool asleep)
        {
            var sprite = SitSprites.Get(Source, asleep);
            if (sprite == null) return false;
            if (_sitRend == null)
            {
                var go = new GameObject("Vessel Sit");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                _sitRend = go.AddComponent<SpriteRenderer>();
            }
            _sitRend.sprite = sprite;
            _sitRend.transform.localPosition = new Vector3(0f, _knightRect.yMin + SeatHeight, _basePos.z);
            _sitRend.transform.localScale = new Vector3(VisualScale, VisualScale, 1f);
            _sitShown = true;
            return true;
        }

        private void HideSit()
        {
            _sitShown = false;
            if (_sitRend != null) _sitRend.enabled = false;
        }

        // ---------- benches ----------

        private static readonly HashSet<string> BenchClips = new HashSet<string>
        {
            "Sit", "Sit Lean", "Sit Idle", "Sitting Asleep", "Sit Fall Asleep", "Wake", "Wake To Sit",
            "Get Off", "Sit Map Open", "Sit Map Close"
        };

        /// <summary>Height above the Knight's feet that stays put when the Knight's own art is scaled up (the seat).</summary>
        private const float SeatHeight = 0.45f;
        private bool _knightArt;

        /// <summary>Show a bench clip according to the Bench pose setting. Returns false to fall back to the normal map.</summary>
        private bool PlayBench(tk2dSpriteAnimationClip heroClip)
        {
            switch (PlayableVessel.GS.BenchPose)
            {
                case BenchPose.KnightSit:
                    // The Knight's own bench animation, at the vessel's size.
                    SetKnightArt(true);
                    _pAnim.Play(heroClip, 0f, heroClip.fps);
                    return true;
                case BenchPose.Rest:
                    SetKnightArt(false);
                    var clip = heroClip.name == "Get Off" ? Source.Clip("StunToIdle") : Source.Clip("Bench Rest");
                    if (clip == null) return false;
                    float fps = heroClip.name == "Get Off" && heroClip.Duration > 0.01f ? clip.frames.Length / heroClip.Duration : clip.fps;
                    _pAnim.Play(clip, 0f, fps);
                    return true;
                case BenchPose.VesselSit:
                    SetKnightArt(false);
                    if (heroClip.name == "Get Off")
                    {
                        var up = Source.Clip("StunToIdle");
                        if (up == null) return false;
                        _pAnim.Play(up, 0f, heroClip.Duration > 0.01f ? up.frames.Length / heroClip.Duration : up.fps);
                        return true;
                    }
                    return ShowSit(heroClip.name == "Sitting Asleep" || heroClip.name == "Sit Fall Asleep");
                default:
                    return false; // Guard: the ClipMap entries (parry stance)
            }
        }

        /// <summary>
        /// Switch the puppet between drawing boss art (mirrored, boss scale) and the Knight's own art
        /// (unmirrored, scaled to vessel height around the seat point).
        /// </summary>
        private void SetKnightArt(bool on)
        {
            if (_puppet == null) return;
            HideSit();
            StopFrames();
            if (on)
            {
                float k = PlayableVessel.GS.SizeMultiplier;
                float pivotY = _knightRect.yMin + SeatHeight;
                _pSprite.scale = new Vector3(k, k, 1f);
                _puppet.transform.localScale = Vector3.one;
                _puppet.transform.localPosition = new Vector3(0f, pivotY * (1f - k), _basePos.z);
            }
            else
            {
                if (_knightArt) _pSprite.scale = new Vector3(VisualScale, VisualScale, 1f);
                _puppet.transform.localScale = new Vector3(-1f, 1f, 1f); // boss art faces right, the Knight's left
                _puppet.transform.localPosition = _basePos;
            }
            _knightArt = on;
        }

        private void PlayMapped(tk2dSpriteAnimationClip heroClip)
        {
            if (IsAttackClip(heroClip.name) && !_hc.cState.attacking && !_rested)
            {
                PlayRest();
                return;
            }
            if (BenchClips.Contains(heroClip.name) && PlayBench(heroClip)) return;
            SetKnightArt(false);
            var choice = ClipMap.Resolve(Source, heroClip.name);
            var bc = Source.Clip(choice.Clip);
            if (bc == null || bc.frames.Length == 0) return;

            float fps = bc.fps;
            if (choice.FpsMultiplier > 0f)
                fps = bc.fps * choice.FpsMultiplier;
            else if (heroClip.wrapMode == tk2dSpriteAnimationClip.WrapMode.Once && bc.wrapMode != tk2dSpriteAnimationClip.WrapMode.Loop && heroClip.Duration > 0.01f)
                fps = bc.frames.Length / heroClip.Duration; // match the Knight's timing
            if (choice.Clip == "Vessel Walk" && Source.Form == VesselForm.PureVessel
                && PlayFrames(WalkSprites.PureVessel(Source), 12f * (choice.FpsMultiplier > 0f ? choice.FpsMultiplier : 1f)))
                return;
            StopFrames();
            _puppet.transform.localPosition = choice.Clip == "Vessel Cling" ? ClingPosition(bc) : _basePos;
            if (WallClips.Contains(heroClip.name)) FaceAwayFromWall();
            _pAnim.Play(bc, 0f, fps);
        }
    }
}
