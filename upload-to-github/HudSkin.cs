using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PlayableVessel
{
    /// <summary>Which HUD to show. Default leaves the game's HUD untouched.</summary>
    public enum HudStyle
    {
        Default = 0,
        SealedVessel = 1,
        PureVessel = 2,
        /// <summary>Follows the vessel you are playing.</summary>
        MatchVessel = 3
    }

    /// <summary>
    /// Vessel HUD skins, switchable at any time. The health masks are drawn as the chosen vessel's own
    /// mask (cut from its sprite by <see cref="HudComposer"/>, full / empty / Lifeblood), on top of the
    /// game's masks, which are only hidden, never changed, so "Default" restores the normal HUD.
    /// The Sealed Vessel skin also turns the soul meter infection-orange, like its eyes.
    /// </summary>
    public class HudSkin : MonoBehaviour
    {
        internal static HudSkin Instance;

        private static readonly Regex HealthName = new Regex(@"^Health \d+$");
        private static readonly Color SealedSoul = new Color(1f, 0.55f, 0.18f, 1f);

        private readonly Dictionary<(VesselForm, HudComposer.MaskState), Sprite> _masks = new Dictionary<(VesselForm, HudComposer.MaskState), Sprite>();
        private readonly Dictionary<Transform, SpriteRenderer> _overlays = new Dictionary<Transform, SpriteRenderer>();
        private readonly Dictionary<Component, Color> _tinted = new Dictionary<Component, Color>();
        private GameObject _hud;
        private readonly List<Transform> _healthSlots = new List<Transform>();
        private readonly HashSet<MeshRenderer> _hidden = new HashSet<MeshRenderer>();
        private readonly List<Component> _soulParts = new List<Component>();
        private bool _dumped;

        internal static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("PlayableVessel HUD Skin");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<HudSkin>();
        }

        private VesselForm? ActiveSkin()
        {
            switch (PlayableVessel.GS.HudStyle)
            {
                case HudStyle.SealedVessel: return VesselForm.HollowKnight;
                case HudStyle.PureVessel: return VesselForm.PureVessel;
                case HudStyle.MatchVessel: return PlayableVessel.GS.Enabled ? PlayableVessel.GS.Form : (VesselForm?)null;
                default: return null;
            }
        }

        private void LateUpdate()
        {
            var cams = GameCameras.instance;
            var hud = cams != null ? cams.hudCanvas : null;
            if (hud != _hud) { Restore(); _hud = hud; Discover(); }
            if (_hud == null) return;

            var skin = ActiveSkin();
            if (skin == null || !PlayableVessel.Sources.ContainsKey(skin.Value)) { Restore(); return; }
            var form = skin.Value;
            var pd = PlayerData.instance;

            foreach (var slot in _healthSlots)
            {
                if (slot == null) continue;
                var rend = slot.GetComponent<MeshRenderer>();
                if (rend == null) continue;
                // A slot draws with its own renderer (appear/break/empty) or its "Idle" / "Idle Hive" children
                // (the full mask at rest). Hide them all; show ours whenever any of them would be visible.
                bool any = false;
                foreach (var r in slot.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (r.GetComponent<tk2dBaseSprite>() == null) continue;
                    any |= r.enabled;
                    r.forceRenderingOff = true;
                    _hidden.Add(r);
                }
                var ov = Overlay(slot, rend);
                bool show = slot.gameObject.activeInHierarchy && any;
                ov.enabled = show;
                if (!show) continue;

                int n = int.Parse(slot.name.Substring(7));
                var state = n <= pd.GetInt("health") ? HudComposer.MaskState.Full : HudComposer.MaskState.Empty;
                ov.sprite = Mask(form, state);
                FitTo(ov, rend);
            }

            // Lifeblood masks are separate objects; anything under the HUD named like "Blue Health…".
            foreach (var t in _hud.GetComponentsInChildren<Transform>(false))
            {
                if (!t.name.StartsWith("Blue Health") && !t.name.StartsWith("Health Blue")) continue;
                var rend = t.GetComponent<MeshRenderer>();
                if (rend == null) continue;
                rend.forceRenderingOff = true;
                var ov = Overlay(t, rend);
                ov.enabled = rend.enabled;
                ov.sprite = Mask(form, HudComposer.MaskState.Blue);
                FitTo(ov, rend);
            }

            // Soul meter colour: the Sealed Vessel's is infection-orange.
            if (form == VesselForm.HollowKnight) foreach (var c in _soulParts) Tint(c, SealedSoul);
            else foreach (var c in _soulParts) Untint(c);
        }

        private void Discover()
        {
            _healthSlots.Clear();
            _soulParts.Clear();
            if (_hud == null) return;
            foreach (var t in _hud.GetComponentsInChildren<Transform>(true))
            {
                if (HealthName.IsMatch(t.name) && t.GetComponent<tk2dSprite>() != null) _healthSlots.Add(t);
                // The soul liquid: renderers under the soul orb whose names say liquid/fill.
                string path = Dumper.GetPath(t, _hud.transform).ToLowerInvariant();
                if (path.Contains("soul orb") && (t.name.ToLowerInvariant().Contains("liquid") || t.name.ToLowerInvariant().Contains("fill")))
                {
                    var spr = t.GetComponent<tk2dBaseSprite>();
                    if (spr != null) _soulParts.Add(spr);
                    var sr = t.GetComponent<SpriteRenderer>();
                    if (sr != null) _soulParts.Add(sr);
                }
            }
            VesselLog.Write("hud", $"style={PlayableVessel.GS.HudStyle} slots={_healthSlots.Count} soul parts={_soulParts.Count}");
            PlayableVessel.Instance.Log($"HUD: {_healthSlots.Count} mask slots, {_soulParts.Count} soul meter parts");
            if (PlayableVessel.GS.DumpData && !_dumped)
            {
                _dumped = true;
                Dumper.DumpTree(_hud, Path.Combine(PlayableVessel.ModDir, "dump"), "HUD");
            }
        }

        private SpriteRenderer Overlay(Transform slot, Renderer original)
        {
            if (_overlays.TryGetValue(slot, out var ov) && ov != null) return ov;
            var go = new GameObject("Vessel Mask");
            go.layer = slot.gameObject.layer;
            go.transform.SetParent(slot, false);
            ov = go.AddComponent<SpriteRenderer>();
            ov.sortingLayerID = original.sortingLayerID;
            ov.sortingOrder = original.sortingOrder + 1;
            _overlays[slot] = ov;
            return ov;
        }

        /// <summary>Size the vessel mask to the game's mask slot (a little taller: the horns).</summary>
        private static void FitTo(SpriteRenderer ov, MeshRenderer original)
        {
            if (ov.sprite == null) return;
            // Size from the resting mask (the "Idle" child), not whatever appear/break frame is current.
            var idle = original.transform.Find("Idle");
            var spr = idle != null ? idle.GetComponent<tk2dSprite>() : original.GetComponent<tk2dSprite>();
            var def = spr != null ? spr.GetCurrentSpriteDef() : null;
            float targetH = def != null && def.boundsData != null && def.boundsData.Length > 1 ? def.boundsData[1].y : 1f;
            if (targetH < 0.2f) targetH = 1f; // break/empty frames can be tiny; keep a sane size
            float h = ov.sprite.bounds.size.y;
            float k = h > 0.001f ? targetH * 1.25f / h : 1f;
            ov.transform.localScale = new Vector3(k, k, 1f);
            ov.transform.localPosition = new Vector3(0f, targetH * 0.12f, -0.01f);
        }

        private Sprite Mask(VesselForm form, HudComposer.MaskState state)
        {
            if (_masks.TryGetValue((form, state), out var s) && s != null) return s;
            s = HudSprites.Build(PlayableVessel.Sources[form], state);
            _masks[(form, state)] = s;
            return s;
        }

        private void Tint(Component c, Color color)
        {
            if (c == null) return;
            if (!_tinted.ContainsKey(c)) _tinted[c] = GetColor(c);
            var orig = _tinted[c];
            SetColor(c, new Color(color.r, color.g, color.b, orig.a));
        }

        private void Untint(Component c)
        {
            if (c != null && _tinted.TryGetValue(c, out var orig)) { SetColor(c, orig); _tinted.Remove(c); }
        }

        private static Color GetColor(Component c) => c is tk2dBaseSprite t ? t.color : c is SpriteRenderer s ? s.color : Color.white;

        private static void SetColor(Component c, Color col)
        {
            if (c is tk2dBaseSprite t) t.color = col;
            else if (c is SpriteRenderer s) s.color = col;
        }

        /// <summary>Back to the game's own HUD.</summary>
        private void Restore()
        {
            foreach (var r in _hidden) if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
            foreach (var kv in _overlays)
            {
                if (kv.Key != null)
                {
                    var r = kv.Key.GetComponent<MeshRenderer>();
                    if (r != null) r.forceRenderingOff = false;
                }
                if (kv.Value != null) kv.Value.enabled = false;
            }
            foreach (var c in new List<Component>(_tinted.Keys)) Untint(c);
        }
    }

    /// <summary>Turns the extracted vessel masks into Unity sprites (built once per form and state).</summary>
    internal static class HudSprites
    {
        public static Sprite Build(BossSource src, HudComposer.MaskState state)
        {
            try
            {
                var recipe = HudComposer.MaskFor(src.Form);
                var clip = src.Library.GetClipByName(recipe.Clip);
                if (clip == null || recipe.Frame >= clip.frames.Length) return null;
                var frame = clip.frames[recipe.Frame];
                var img = SitSprites.ReadSprite(frame.spriteCollection.spriteDefinitions[frame.spriteId], frame.spriteCollection);
                if (img == null) return null;
                var v = HudComposer.Variant(HudComposer.ExtractMask(img, recipe), state);
                var tex = new Texture2D(v.W, v.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var colors = new Color32[v.W * v.H];
                for (int y = 0; y < v.H; y++)
                    for (int x = 0; x < v.W; x++)
                    {
                        var p = v.D[y * v.W + x];
                        colors[(v.H - 1 - y) * v.W + x] = new Color32(SitSprites.B(p.R), SitSprites.B(p.G), SitSprites.B(p.B), SitSprites.B(p.A));
                    }
                tex.SetPixels32(colors);
                tex.Apply(false, true);
                var s = Sprite.Create(tex, new Rect(0, 0, v.W, v.H), new Vector2(0.5f, 0.5f), 64f);
                s.name = $"{src.Form} HUD Mask {state}";
                return s;
            }
            catch (System.Exception e)
            {
                PlayableVessel.Instance.LogError("Building HUD mask failed: " + e);
                return null;
            }
        }
    }
}
