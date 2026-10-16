using System;
using System.Collections.Generic;

namespace PlayableVessel
{
    /// <summary>Where a vessel's mask sits in its front-facing frame (source pixels, 64 px = 1 unit).</summary>
    public sealed class MaskRecipe
    {
        public string Clip = "Turn";
        public int Frame;
        /// <summary>A pixel inside the white of the mask, where the flood fill starts.</summary>
        public int SeedX, SeedY;
        /// <summary>Only pixels inside this box can belong to the mask (keeps the fill off the nail).</summary>
        public int MinX, MinY, MaxX, MaxY;
    }

    /// <summary>
    /// Cuts a vessel's mask (horns, face and eyes, with its black outline) out of its own sprite and makes
    /// the HUD versions of it: full, empty and Lifeblood-blue. The mask is found by flood-filling the white
    /// from a seed, filling the holes it encloses (the eyes), then growing the edge into pure-black outline
    /// pixels only, so the cloak behind it is left out.
    /// </summary>
    public static class HudComposer
    {
        public static MaskRecipe MaskFor(VesselForm form) => form == VesselForm.HollowKnight
            ? new MaskRecipe { Clip = "Turn", Frame = 2, SeedX = 75, SeedY = 205, MinX = 0, MinY = 0, MaxX = 140, MaxY = 275 }
            : new MaskRecipe { Clip = "Turn", Frame = 1, SeedX = 100, SeedY = 200, MinX = 20, MinY = 0, MaxX = 185, MaxY = 270 };

        private static float Lum(Px p) => (p.R + p.G + p.B) / 3f;

        /// <summary>The mask on its own, cropped tight (transparent elsewhere).</summary>
        public static PixelImage ExtractMask(PixelImage src, MaskRecipe r)
        {
            int w = src.W, h = src.H;
            bool In(int x, int y) => x >= r.MinX && y >= r.MinY && x <= r.MaxX && y <= r.MaxY && x >= 0 && y >= 0 && x < w && y < h;
            bool White(int x, int y) { var p = src.Get(x, y); return p.A > 0.5f && Lum(p) > 0.78f; }

            // 1. White region connected to the seed.
            var mask = new bool[w * h];
            var stack = new Stack<(int, int)>();
            if (In(r.SeedX, r.SeedY) && White(r.SeedX, r.SeedY)) stack.Push((r.SeedX, r.SeedY));
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                if (!In(x, y) || mask[y * w + x] || !White(x, y)) continue;
                mask[y * w + x] = true;
                stack.Push((x + 1, y)); stack.Push((x - 1, y)); stack.Push((x, y + 1)); stack.Push((x, y - 1));
            }

            // 2. Holes enclosed by the white (the eyes): whatever the outside can't reach.
            var outside = new bool[w * h];
            for (int x = r.MinX; x <= r.MaxX; x++) { stack.Push((x, r.MinY)); stack.Push((x, r.MaxY)); }
            for (int y = r.MinY; y <= r.MaxY; y++) { stack.Push((r.MinX, y)); stack.Push((r.MaxX, y)); }
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                if (!In(x, y) || outside[y * w + x] || mask[y * w + x]) continue;
                outside[y * w + x] = true;
                stack.Push((x + 1, y)); stack.Push((x - 1, y)); stack.Push((x, y + 1)); stack.Push((x, y - 1));
            }
            for (int y = r.MinY; y <= Math.Min(r.MaxY, h - 1); y++)
                for (int x = r.MinX; x <= Math.Min(r.MaxX, w - 1); x++)
                    if (!outside[y * w + x]) mask[y * w + x] = true;

            // 3. Grow into the black outline (up to 5 px), never into coloured cloak.
            for (int pass = 0; pass < 5; pass++)
            {
                var grow = new List<int>();
                for (int y = Math.Max(1, r.MinY); y < Math.Min(h - 1, r.MaxY); y++)
                    for (int x = Math.Max(1, r.MinX); x < Math.Min(w - 1, r.MaxX); x++)
                    {
                        int i = y * w + x;
                        if (mask[i]) continue;
                        var p = src.Get(x, y);
                        if (p.A < 0.3f || Lum(p) > 0.14f) continue;
                        if (mask[i - 1] || mask[i + 1] || mask[i - w] || mask[i + w]) grow.Add(i);
                    }
                foreach (int i in grow) mask[i] = true;
            }

            // 4. Copy out, cropped to the mask's bounds with a small margin.
            int x0 = w, y0 = h, x1 = 0, y1 = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (mask[y * w + x]) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            if (x1 < x0) return new PixelImage(1, 1);
            const int m = 2;
            var outImg = new PixelImage(x1 - x0 + 1 + m * 2, y1 - y0 + 1 + m * 2);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (mask[y * w + x]) outImg.D[(y - y0 + m) * outImg.W + (x - x0 + m)] = src.Get(x, y);
            return outImg;
        }

        public enum MaskState { Full, Empty, Blue }

        /// <summary>HUD variant of an extracted mask.</summary>
        public static PixelImage Variant(PixelImage mask, MaskState state)
        {
            var o = new PixelImage(mask.W, mask.H);
            for (int i = 0; i < mask.D.Length; i++)
            {
                var p = mask.D[i];
                if (p.A <= 0f) continue;
                float l = Lum(p);
                switch (state)
                {
                    case MaskState.Full:
                        o.D[i] = p;
                        break;
                    case MaskState.Empty:
                        // Like the game's empty masks: a dark silhouette with the outline still readable.
                        o.D[i] = l > 0.5f ? new Px(0.16f, 0.17f, 0.2f, p.A * 0.85f) : new Px(0.03f, 0.03f, 0.04f, p.A * 0.9f);
                        break;
                    default:
                        // Lifeblood: the white turns blue, eyes and outline stay as they are.
                        o.D[i] = l > 0.5f ? new Px(0.45f + 0.2f * l, 0.72f + 0.15f * l, 1f, p.A) : p;
                        break;
                }
            }
            return o;
        }
    }
}
