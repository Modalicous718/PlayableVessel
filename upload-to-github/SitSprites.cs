using System.Collections.Generic;
using UnityEngine;

namespace PlayableVessel
{
    /// <summary>
    /// Builds the vessels' bench-sitting sprites in game, from the player's own installed boss art:
    /// reads the boss sprite atlas, cuts out the front-facing frame named by the recipe, and runs
    /// <see cref="SitComposer"/>. Nothing is shipped with the mod; it's all made at runtime.
    /// </summary>
    internal static class SitSprites
    {
        private static readonly Dictionary<Texture, (Color32[] px, int w, int h)> Atlases = new Dictionary<Texture, (Color32[], int, int)>();
        private static readonly Dictionary<(VesselForm, bool), Sprite> Cache = new Dictionary<(VesselForm, bool), Sprite>();

        public static Sprite Get(BossSource src, bool asleep)
        {
            if (Cache.TryGetValue((src.Form, asleep), out var s) && s != null) return s;
            try
            {
                var recipe = SitComposer.For(src.Form);
                var clip = src.Library.GetClipByName(recipe.Clip);
                if (clip == null || recipe.Frame >= clip.frames.Length) return null;
                var frame = clip.frames[recipe.Frame];
                var def = frame.spriteCollection.spriteDefinitions[frame.spriteId];
                var img = ReadSprite(def, frame.spriteCollection);
                if (img == null) return null;

                var result = SitComposer.Compose(img, recipe, asleep, out float px, out float py);
                var tex = new Texture2D(result.W, result.H, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                var colors = new Color32[result.W * result.H];
                for (int y = 0; y < result.H; y++)
                    for (int x = 0; x < result.W; x++)
                    {
                        var p = result.D[y * result.W + x];
                        // Unity textures store the bottom row first.
                        colors[(result.H - 1 - y) * result.W + x] = new Color32(B(p.R), B(p.G), B(p.B), B(p.A));
                    }
                tex.SetPixels32(colors);
                tex.Apply(false, true);
                // 64 texels per unit, like the boss atlases, so it scales exactly like the boss sprites.
                s = Sprite.Create(tex, new Rect(0, 0, result.W, result.H), new Vector2(px / result.W, 1f - py / result.H), 64f);
                s.name = $"{src.Form} Sit{(asleep ? " Asleep" : "")}";
                Cache[(src.Form, asleep)] = s;
                return s;
            }
            catch (System.Exception e)
            {
                PlayableVessel.Instance.LogError("Building sit sprite failed: " + e);
                return null;
            }
        }

        internal static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>Copy one tk2d sprite out of its atlas, upright, row 0 at the top.</summary>
        internal static PixelImage ReadSprite(tk2dSpriteDefinition def, tk2dSpriteCollectionData coll)
        {
            var mat = def.material != null ? def.material : coll.materials[def.materialId];
            var tex = mat != null ? mat.mainTexture : null;
            if (tex == null) return null;
            if (!Atlases.TryGetValue(tex, out var atlas))
                Atlases[tex] = atlas = ReadTexture(tex);

            Vector2 uv0 = def.uvs[0], uv3 = def.uvs[3];
            bool flipped = def.flipped == tk2dSpriteDefinition.FlipMode.Tk2d;
            float du = Mathf.Abs(uv3.x - uv0.x) * atlas.w, dv = Mathf.Abs(uv3.y - uv0.y) * atlas.h;
            int w = Mathf.RoundToInt(flipped ? dv : du);
            int h = Mathf.RoundToInt(flipped ? du : dv);
            var img = new PixelImage(w, h);
            for (int y = 0; y < h; y++)
            {
                float sy = 1f - (y + 0.5f) / h; // sprite space: 0 = bottom
                for (int x = 0; x < w; x++)
                {
                    float sx = (x + 0.5f) / w;
                    // Same mapping as tk2dSpriteGeomGen: flipped sprites swap the axes.
                    float u = flipped ? Mathf.Lerp(uv0.x, uv3.x, sy) : Mathf.Lerp(uv0.x, uv3.x, sx);
                    float v = flipped ? Mathf.Lerp(uv0.y, uv3.y, sx) : Mathf.Lerp(uv0.y, uv3.y, sy);
                    int tx = Mathf.Clamp((int)(u * atlas.w), 0, atlas.w - 1);
                    int ty = Mathf.Clamp((int)(v * atlas.h), 0, atlas.h - 1);
                    var c = atlas.px[ty * atlas.w + tx];
                    img.D[y * w + x] = new Px(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
                }
            }
            return img;
        }

        private static (Color32[], int, int) ReadTexture(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.ARGB32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            Object.Destroy(copy);
            return (px, tex.width, tex.height);
        }
    }
}

namespace PlayableVessel
{
    /// <summary>
    /// Pure Vessel's walk cycle, built in game by <see cref="WalkComposer"/> from its own "Idle" frame
    /// (Pure Vessel has no walk animation). Each frame keeps the source sprite's origin, so it lines up
    /// exactly with the boss sprites on the puppet.
    /// </summary>
    internal static class WalkSprites
    {
        private static Sprite[] _pureVessel;

        public static Sprite[] PureVessel(BossSource src)
        {
            if (_pureVessel != null) return _pureVessel;
            try
            {
                var recipe = WalkComposer.ForPureVessel();
                var clip = src.Library.GetClipByName(recipe.Clip);
                if (clip == null || recipe.Frame >= clip.frames.Length) return null;
                var frame = clip.frames[recipe.Frame];
                var def = frame.spriteCollection.spriteDefinitions[frame.spriteId];
                var img = SitSprites.ReadSprite(def, frame.spriteCollection);
                if (img == null) return null;

                // Sprite-space origin, in source pixels (64 px per unit, row 0 at the top).
                Vector3 min = def.positions[0], max = def.positions[0];
                foreach (var p in def.positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                float originX = -min.x * img.W / (max.x - min.x);
                float originY = max.y * img.H / (max.y - min.y);
                float ppu = img.W / (max.x - min.x);

                var frames = WalkComposer.Compose(img, recipe, out float ox, out float oy);
                var sprites = new Sprite[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    var f = frames[i];
                    var tex = new Texture2D(f.W, f.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    var colors = new Color32[f.W * f.H];
                    for (int y = 0; y < f.H; y++)
                        for (int x = 0; x < f.W; x++)
                        {
                            var p = f.D[y * f.W + x];
                            colors[(f.H - 1 - y) * f.W + x] = new Color32(SitSprites.B(p.R), SitSprites.B(p.G), SitSprites.B(p.B), SitSprites.B(p.A));
                        }
                    tex.SetPixels32(colors);
                    tex.Apply(false, true);
                    var pivot = new Vector2((ox + originX) / f.W, 1f - (oy + originY) / f.H);
                    sprites[i] = Sprite.Create(tex, new Rect(0, 0, f.W, f.H), pivot, ppu);
                    sprites[i].name = $"Pure Vessel Walk {i}";
                }
                _pureVessel = sprites;
                PlayableVessel.Instance.Log($"Built Pure Vessel walk: {sprites.Length} frames");
                return sprites;
            }
            catch (System.Exception e)
            {
                PlayableVessel.Instance.LogError("Building Pure Vessel walk failed: " + e);
                return null;
            }
        }
    }
}
