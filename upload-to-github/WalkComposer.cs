using System;

namespace PlayableVessel
{
    /// <summary>
    /// Where to cut the side-view frame and how the walk moves. Pixels of the source frame (64 px = 1 unit),
    /// measured from Pure Vessel's "Idle" frame 0.
    /// </summary>
    public sealed class WalkRecipe
    {
        public string Clip = "Idle";
        public int Frame;
        /// <summary>Everything above this row is the upper body (mask, cloak, shoulders).</summary>
        public int CutY;
        /// <summary>How far below the cut the cloak hem may hang.</summary>
        public float HemDepth = 18f;
        /// <summary>Kept below the cut as (x0, y0, x1, y1, radius) capsules: the nail, the hand on it, the free arm.</summary>
        public float[][] Keep = new float[0][];
        public float HipX, HipY, HipSpread = 22f;
        /// <summary>Row of the floor in the source frame (where the standing feet are).</summary>
        public float GroundY;
        public float Thigh = 100f, Shin = 102f;
        public float HipR = 8f, KneeR = 6f, AnkleR = 2.6f;
        /// <summary>Half the step length, foot lift during the swing, and body bob, in pixels.</summary>
        public float HalfStride = 66f, Lift = 30f, Bob = 5f;
        public int Frames = 10;
        /// <summary>Share of the cycle a foot is planted.</summary>
        public float Stance = 0.58f;
    }

    /// <summary>
    /// Builds a walk cycle for a vessel that has none: the upper body from its own side-view frame, and
    /// legs drawn each frame (solid black, tapering to pointed feet, knees bending forward) following a
    /// planted-foot / swinging-foot gait with a gentle body bob. Same pixel tools as the bench sprite.
    /// </summary>
    public static class WalkComposer
    {
        private static readonly Px Limb = new Px(0.04f, 0.04f, 0.05f, 1f);

        /// <summary>Recipe for Pure Vessel (Idle frame 0 — side view facing right).</summary>
        public static WalkRecipe ForPureVessel() => new WalkRecipe
        {
            Clip = "Idle", Frame = 0, CutY = 302, HemDepth = 16f,
            Keep = new[]
            {
                new[] { 2f, 136f, 232f, 462f, 8f },     // nail
                new[] { 166f, 370f, 186f, 392f, 11f },  // fist on the nail
                new[] { 288f, 262f, 312f, 330f, 14f },  // free arm
                new[] { 300f, 320f, 330f, 372f, 15f },  // its claw hand
            },
            HipX = 218f, HipY = 300f, GroundY = 505f,
            Thigh = 112f, Shin = 112f, HipR = 11f, KneeR = 7.5f, AnkleR = 3.2f,
            HalfStride = 70f, Lift = 34f, Bob = 4f
        };

        /// <param name="ox">Where source pixel (0,0) landed in the result.</param>
        public static PixelImage[] Compose(PixelImage src, WalkRecipe r, out float ox, out float oy)
        {
            int margin = 40;
            int w = src.W + margin * 2, h = src.H + margin * 2;
            ox = margin; oy = margin;
            var frames = new PixelImage[r.Frames];
            for (int i = 0; i < r.Frames; i++)
                frames[i] = Frame(src, r, i / (float)r.Frames, w, h, ox, oy);
            return frames;
        }

        private static PixelImage Frame(PixelImage src, WalkRecipe r, float t, int w, int h, float ox, float oy)
        {
            var img = new PixelImage(w, h);
            // Two steps per cycle: the body is highest when a leg passes under it.
            float bob = -r.Bob * (float)Math.Cos(t * 4 * Math.PI);
            float tilt = 1.5f * (float)Math.Sin(t * 4 * Math.PI) * (float)Math.PI / 180f;

            // Far leg first (behind), then the near leg, then the body over the tops of both.
            Leg(img, r, t + 0.5f, -r.HipSpread * 0.5f, bob, ox, oy, far: true);
            Leg(img, r, t, r.HipSpread * 0.5f, bob, ox, oy, far: false);

            float cos = (float)Math.Cos(tilt), sin = (float)Math.Sin(tilt);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - ox - r.HipX, dy = y - oy - r.HipY - bob;
                    float sx = r.HipX + dx * cos + dy * sin;
                    float sy = r.HipY - dx * sin + dy * cos;
                    if (sx < -1 || sy < -1 || sx > src.W || sy > src.H) continue;
                    if (sy > r.CutY && !InKeep(sx, sy, r) && (sy > r.CutY + r.HemDepth || !IsCloth(src, sx, sy))) continue;
                    var p = src.Sample(sx, sy);
                    if (p.A <= 0f) continue;
                    img.Over(x, y, p);
                }
            }
            return img;
        }

        /// <summary>One leg at gait phase <paramref name="phase"/>: planted and sliding back, then lifted and swung forward.</summary>
        private static void Leg(PixelImage img, WalkRecipe r, float phase, float hipOffset, float bob, float ox, float oy, bool far)
        {
            float p = phase - (float)Math.Floor(phase);
            float hx = ox + r.HipX + hipOffset, hy = oy + r.HipY + bob;
            float ground = oy + r.GroundY;
            float fx, fy;
            if (p < r.Stance)
            {
                float u = p / r.Stance;
                fx = hx + r.HalfStride * (1 - 2 * u);
                fy = ground;
            }
            else
            {
                float u = (p - r.Stance) / (1 - r.Stance);
                float e = u * u * (3 - 2 * u); // ease in/out
                fx = hx + r.HalfStride * (2 * e - 1);
                fy = ground - r.Lift * (float)Math.Sin(Math.PI * u);
            }

            // Two-bone IK, knee bending forward (+x: the art faces right).
            float dx = fx - hx, dy = fy - hy;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            float reach = r.Thigh + r.Shin - 0.5f;
            if (d > reach) { fx = hx + dx / d * reach; fy = hy + dy / d * reach; dx = fx - hx; dy = fy - hy; d = reach; }
            float a = (r.Thigh * r.Thigh - r.Shin * r.Shin + d * d) / (2 * d);
            float hgt = (float)Math.Sqrt(Math.Max(0f, r.Thigh * r.Thigh - a * a));
            float mx = hx + dx * a / d, my = hy + dy * a / d;
            // Perpendicular pointing forward-ish: (dy, -dx)/d for a downward leg points +x.
            float kx = mx + dy / d * hgt, ky = my - dx / d * hgt;
            if (kx < mx) { kx = mx - dy / d * hgt; ky = my + dx / d * hgt; }

            float k = far ? 0.85f : 1f; // the far leg reads a touch thinner
            Stroke(img, hx, hy, r.HipR * k, kx, ky, r.KneeR * k);
            Disc(img, kx, ky, r.KneeR * k + 1f);
            // Shin bows slightly backward, like the vessels' insect-like legs.
            float sx2 = (kx + fx) / 2f - 6f, sy2 = (ky + fy) / 2f;
            Stroke(img, kx, ky, r.KneeR * k, sx2, sy2, (r.KneeR + r.AnkleR) / 2f * k);
            Stroke(img, sx2, sy2, (r.KneeR + r.AnkleR) / 2f * k, fx, fy, r.AnkleR * k);
            // Pointed foot along the ground, toes forward; tilts down while swinging.
            float toeDrop = p < r.Stance ? 1f : 6f;
            Stroke(img, fx, fy, r.AnkleR * k, fx + 14f, fy + toeDrop, 0.6f);
        }

        private static bool InKeep(float x, float y, WalkRecipe r)
        {
            foreach (var c in r.Keep)
            {
                float vx = c[2] - c[0], vy = c[3] - c[1];
                float t = Math.Max(0f, Math.Min(1f, ((x - c[0]) * vx + (y - c[1]) * vy) / (vx * vx + vy * vy)));
                float ddx = x - (c[0] + vx * t), ddy = y - (c[1] + vy * t);
                if (ddx * ddx + ddy * ddy <= c[4] * c[4]) return true;
            }
            return false;
        }

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
                    float a = Math.Min(1f, Math.Max(0f, r - d + 0.5f));
                    if (a > 0f) img.Over(x, y, new Px(Limb.R, Limb.G, Limb.B, a));
                }
        }
    }
}
