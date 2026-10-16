using System;

namespace PlayableVessel
{
    /// <summary>A plain RGBA pixel (0..1, straight alpha). No Unity types, so the same code runs in an offline preview tool.</summary>
    public struct Px
    {
        public float R, G, B, A;
        public Px(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
    }

    /// <summary>Pixel buffer, row 0 at the top.</summary>
    public sealed class PixelImage
    {
        public readonly int W, H;
        public readonly Px[] D;

        public PixelImage(int w, int h) { W = w; H = h; D = new Px[w * h]; }

        public Px Get(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? default : D[y * W + x];

        /// <summary>Bilinear sample with premultiplied interpolation (no dark fringes).</summary>
        public Px Sample(float x, float y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            Px a = Get(x0, y0), b = Get(x0 + 1, y0), c = Get(x0, y0 + 1), d = Get(x0 + 1, y0 + 1);
            float wa = (1 - fx) * (1 - fy), wb = fx * (1 - fy), wc = (1 - fx) * fy, wd = fx * fy;
            float A = a.A * wa + b.A * wb + c.A * wc + d.A * wd;
            if (A <= 1e-5f) return default;
            float R = (a.R * a.A * wa + b.R * b.A * wb + c.R * c.A * wc + d.R * d.A * wd) / A;
            float G = (a.G * a.A * wa + b.G * b.A * wb + c.G * c.A * wc + d.G * d.A * wd) / A;
            float B = (a.B * a.A * wa + b.B * b.A * wb + c.B * c.A * wc + d.B * d.A * wd) / A;
            return new Px(R, G, B, A);
        }

        /// <summary>Alpha-composite <paramref name="p"/> over the pixel at (x, y).</summary>
        public void Over(int x, int y, Px p)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || p.A <= 0f) return;
            int i = y * W + x;
            Px q = D[i];
            float a = p.A + q.A * (1 - p.A);
            if (a <= 1e-5f) { D[i] = default; return; }
            D[i] = new Px(
                (p.R * p.A + q.R * q.A * (1 - p.A)) / a,
                (p.G * p.A + q.G * q.A * (1 - p.A)) / a,
                (p.B * p.A + q.B * q.A * (1 - p.A)) / a,
                a);
        }
    }

    /// <summary>
    /// Where to cut a vessel's front-facing frame and how to draw its seated legs. All numbers are
    /// pixels of the source frame (64 px = 1 game unit), measured from the exported "Turn" frames.
    /// </summary>
    public sealed class SitRecipe
    {
        public string Clip;
        public int Frame;
        /// <summary>Upper body: everything above this row of the source frame is kept.</summary>
        public int CutY;
        /// <summary>Horizontal centre of the body in the source frame (between the hips).</summary>
        public float CenterX;
        /// <summary>Hip height in the source frame: the legs start here, under the cloak.</summary>
        public float HipY;
        public float HipSpread = 20f;
        public float KneeOut = 26f, KneeDown = 22f;
        public float FootOut = 16f, FootDown = 86f;
        public float HipR = 8.5f, KneeR = 7f, AnkleR = 3.2f;
        /// <summary>Extra shapes kept below the cut as (x0, y0, x1, y1, radius) capsules: the nail and the hand holding it.</summary>
        public float[][] Keep = new float[0][];
        /// <summary>How far below the cut the cloak hem may hang.</summary>
        public float HemDepth = 40f;
        /// <summary>Below the cut, light pixels right of this column are kept (the nail).</summary>
        public float SwordMinX = 9999f;
        /// <summary>Dozing: the upper body tips forward by this many degrees and sinks a little.</summary>
        public float SleepTilt = 16f, SleepSink = 10f;
    }

    /// <summary>
    /// Builds a "sitting on a bench" sprite for a vessel, the way the Knight sits: front-facing,
    /// cloak resting on the seat, legs bent over the edge. The upper body is the vessel's own
    /// front-facing art; the legs are drawn in the vessels' limb style (solid black, tapering to a point).
    /// </summary>
    public static class SitComposer
    {
        private static readonly Px Limb = new Px(0.04f, 0.04f, 0.05f, 1f);

        /// <param name="src">The source frame, upright, row 0 at the top.</param>
        /// <param name="pivotX">Seat point (between the hips) in the result, pixels from the left.</param>
        /// <param name="pivotY">Seat point in the result, pixels from the top.</param>
        public static PixelImage Compose(PixelImage src, SitRecipe r, bool asleep, out float pivotX, out float pivotY)
        {
            int margin = 24;
            int legBottom = (int)Math.Ceiling(r.HipY + r.FootDown + r.AnkleR + 4);
            int w = src.W + margin * 2;
            int h = Math.Max(r.CutY, legBottom) + margin + 16;
            var img = new PixelImage(w, h);
            float ox = margin, oy = margin; // source (0,0) lands here

            float pivX = r.CenterX, pivY = r.HipY;
            double ang = asleep ? r.SleepTilt * Math.PI / 180.0 : 0.0;
            float sink = asleep ? r.SleepSink : 0f;
            float cos = (float)Math.Cos(ang), sin = (float)Math.Sin(ang);

            // Layers, back to front: cloak hem and nail below the hips, then the new legs, then the
            // upper body. The upper body can tip forward around the hips (dozing).
            void Body(bool upper)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        // Inverse-map result pixel -> source pixel.
                        float dx = x - ox - pivX, dy = y - oy - pivY - sink;
                        float sx = pivX + dx * cos + dy * sin;
                        float sy = pivY - dx * sin + dy * cos;
                        if (sy < 0 || sx < -1 || sx > src.W || sy > src.H) continue;
                        if (upper != (sy <= r.CutY)) continue;
                        // Below the cut the standing legs (solid black) are dropped; the cloak hem and
                        // the nail are kept.
                        if (!upper && !InKeep(sx, sy, r) && !IsBlade(src, sx, sy, r) && (sy > r.CutY + r.HemDepth || !IsCloth(src, sx, sy))) continue;
                        var p = src.Sample(sx, sy);
                        // Let the hem fade out at its lowest edge instead of ending in a straight line.
                        float below = sy - r.CutY - (r.HemDepth - 10f);
                        if (!upper && below > 0f && !InKeep(sx, sy, r) && !IsBlade(src, sx, sy, r)) p.A *= Math.Max(0f, 1f - below / 10f);
                        img.Over(x, y, p);
                    }
                }
            }

            Body(upper: false);
            foreach (float side in new[] { -1f, 1f })
            {
                float hx = ox + r.CenterX + side * r.HipSpread, hy = oy + r.HipY;
                float kx = ox + r.CenterX + side * (r.HipSpread + r.KneeOut), ky = hy + r.KneeDown;
                float fx = ox + r.CenterX + side * (r.HipSpread + r.FootOut), fy = hy + r.FootDown;
                // Thigh comes forward (foreshortened), knee juts out, shin hangs with a slight inward curve.
                float mx = (kx + fx) / 2f - side * 4f, my = (ky + fy) / 2f;
                Stroke(img, hx, hy, r.HipR, kx, ky, r.KneeR);
                Disc(img, kx, ky, r.KneeR + 1.2f);
                Stroke(img, kx, ky, r.KneeR, mx, my, (r.KneeR + r.AnkleR) / 2f);
                Stroke(img, mx, my, (r.KneeR + r.AnkleR) / 2f, fx, fy, r.AnkleR);
                Stroke(img, fx, fy, r.AnkleR, fx + side * 3f, fy + 13f, 0.6f); // pointed foot, toes out
            }
            Body(upper: true);

            pivotX = ox + r.CenterX;
            pivotY = oy + r.HipY;
            return img;
        }

        /// <summary>
        /// The nail is the only light-coloured thing on the sword side of the frame; its dark outline is
        /// caught by also accepting pixels within 3 px of a light one.
        /// </summary>
        private static bool IsBlade(PixelImage src, float sx, float sy, SitRecipe r)
        {
            if (sx < r.SwordMinX) return false;
            int cx = (int)sx, cy = (int)sy;
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    var q = src.Get(cx + dx, cy + dy);
                    if (q.A > 0.5f && (q.R + q.G + q.B) / 3f > 0.42f) return true;
                }
            return false;
        }

        /// <summary>Cloak pixels are coloured (not the near-black of limbs); their outline is kept by accepting
        /// dark pixels within 2 px of cloth.</summary>
        private static bool IsCloth(PixelImage src, float sx, float sy)
        {
            int cx = (int)sx, cy = (int)sy;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var q = src.Get(cx + dx, cy + dy);
                    if (q.A > 0.6f && (q.R + q.G + q.B) / 3f > 0.13f) return true;
                }
            return false;
        }

        private static bool InKeep(float x, float y, SitRecipe r)
        {
            foreach (var k in r.Keep)
            {
                float vx = k[2] - k[0], vy = k[3] - k[1];
                float t = Math.Max(0f, Math.Min(1f, ((x - k[0]) * vx + (y - k[1]) * vy) / (vx * vx + vy * vy)));
                float dx = x - (k[0] + vx * t), dy = y - (k[1] + vy * t);
                if (dx * dx + dy * dy <= k[4] * k[4]) return true;
            }
            return false;
        }

        /// <summary>A limb segment: discs stamped along the line, radius going from r0 to r1.</summary>
        private static void Stroke(PixelImage img, float x0, float y0, float r0, float x1, float y1, float r1)
        {
            float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int steps = Math.Max(1, (int)(len * 2));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Disc(img, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r0 + (r1 - r0) * t);
            }
        }

        private static void Disc(PixelImage img, float cx, float cy, float r)
        {
            int x0 = (int)Math.Floor(cx - r - 1), x1 = (int)Math.Ceiling(cx + r + 1);
            int y0 = (int)Math.Floor(cy - r - 1), y1 = (int)Math.Ceiling(cy + r + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = (float)Math.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Math.Min(1f, Math.Max(0f, r - d + 0.5f)); // 1px soft edge
                    if (a > 0f) img.Over(x, y, new Px(Limb.R, Limb.G, Limb.B, a));
                }
        }

        /// <summary>Recipes measured from the exported frames (see MODLOG).</summary>
        public static SitRecipe For(VesselForm form) => form == VesselForm.HollowKnight
            ? new SitRecipe
            {
                Clip = "Turn", Frame = 2, CutY = 258, CenterX = 98f, HipY = 262f, HemDepth = 32f,
                Keep = new[] { new[] { 186f, 228f, 300f, 404f, 11f }, new[] { 214f, 250f, 232f, 306f, 13f } }
            }
            : new SitRecipe { Clip = "Turn", Frame = 1, CutY = 282, CenterX = 128f, HipY = 262f, SwordMinX = 215f, HemDepth = 30f };
    }
}
