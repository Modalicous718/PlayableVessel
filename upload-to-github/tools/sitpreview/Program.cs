using System.Drawing;
using System.Drawing.Imaging;
using PlayableVessel;

namespace PlayableVessel { public enum VesselForm { HollowKnight = 0, PureVessel = 1 } }

/// <summary>
/// Offline preview of the built sprites, from frames exported with tools/scripts/exportframe.ps1:
///   sitpreview sit  &lt;dir&gt;   (hk_turn2.png, pv_turn1.png)  → sit_preview.png
///   sitpreview walk &lt;dir&gt;   (pv_idle0.png)                 → walk_preview.png
/// </summary>
static class Program
{
    static PixelImage Load(string path)
    {
        using var bmp = new Bitmap(path);
        var img = new PixelImage(bmp.Width, bmp.Height);
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                img.D[y * img.W + x] = new Px(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
            }
        return img;
    }

    static Bitmap ToBitmap(PixelImage img)
    {
        var bmp = new Bitmap(img.W, img.H, PixelFormat.Format32bppArgb);
        for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
            {
                var p = img.D[y * img.W + x];
                bmp.SetPixel(x, y, Color.FromArgb((int)(p.A * 255), (int)(p.R * 255), (int)(p.G * 255), (int)(p.B * 255)));
            }
        return bmp;
    }

    static void Main(string[] args)
    {
        string mode = args[0], dir = args[1];
        if (mode == "walk") Walk(dir); else if (mode == "hud") Hud(dir); else Sit(dir);
        Console.WriteLine("ok");
    }

    static void Hud(string dir)
    {
        var jobs = new[] { ("hk_turn2.png", VesselForm.HollowKnight), ("pv_turn1.png", VesselForm.PureVessel) };
        using var sheet = new Bitmap(1100, 560);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(255, 20, 22, 30));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        int row = 0;
        foreach (var (file, form) in jobs)
        {
            var mask = HudComposer.ExtractMask(Load(Path.Combine(dir, file)), HudComposer.MaskFor(form));
            int x = 10, y = 10 + row * 270;
            using (var big = ToBitmap(mask)) g.DrawImage(big, x, y, mask.W, mask.H);
            x += mask.W + 30;
            // HUD size: about 60 px tall, 5 full, 1 blue, 3 empty
            float s = 64f / mask.H;
            var states = new[] { HudComposer.MaskState.Full, HudComposer.MaskState.Full, HudComposer.MaskState.Full, HudComposer.MaskState.Full, HudComposer.MaskState.Full,
                HudComposer.MaskState.Blue, HudComposer.MaskState.Empty, HudComposer.MaskState.Empty, HudComposer.MaskState.Empty };
            foreach (var st in states)
            {
                using var b = ToBitmap(HudComposer.Variant(mask, st));
                g.DrawImage(b, x, y + 80, mask.W * s, mask.H * s);
                x += (int)(mask.W * s) + 4;
            }
            row++;
        }
        sheet.Save(Path.Combine(dir, "hud_preview.png"));
    }

    static void Walk(string dir)
    {
        var r = WalkComposer.ForPureVessel();
        var frames = WalkComposer.Compose(Load(Path.Combine(dir, "pv_idle0.png")), r, out float ox, out float oy);
        float scale = 0.5f;
        int cw = (int)(frames[0].W * scale), ch = (int)(frames[0].H * scale);
        using var sheet = new Bitmap(cw * frames.Length, ch + 20);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(255, 90, 105, 130));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        for (int i = 0; i < frames.Length; i++)
        {
            using var bmp = ToBitmap(frames[i]);
            g.DrawImage(bmp, i * cw, 0, cw, ch);
            float gy = (oy + r.GroundY) * scale;
            g.DrawLine(Pens.DimGray, i * cw, gy, (i + 1) * cw, gy);
            g.DrawString(i.ToString(), SystemFonts.DefaultFont, Brushes.Yellow, (float)(i * cw + 2), (float)(ch + 2));
            bmp.Save(Path.Combine(dir, $"walk_{i:D2}.png"));
        }
        sheet.Save(Path.Combine(dir, "walk_preview.png"));
    }

    static void Sit(string dir)
    {
        var jobs = new[] { ("hk_turn2.png", VesselForm.HollowKnight), ("pv_turn1.png", VesselForm.PureVessel) };
        var results = new List<(PixelImage img, float px, float py)>();
        foreach (var (file, form) in jobs)
            foreach (bool asleep in new[] { false, true })
            {
                var img = SitComposer.Compose(Load(Path.Combine(dir, file)), SitComposer.For(form), asleep, out float px, out float py);
                results.Add((img, px, py));
            }
        int cellW = results.Max(x => x.img.W) + 20, cellH = results.Max(x => x.img.H) + 60;
        using var sheet = new Bitmap(cellW * results.Count, cellH);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(255, 90, 105, 130));
        for (int i = 0; i < results.Count; i++)
        {
            var (img, px, py) = results[i];
            int ox = i * cellW + 10, oy = 10, seatY = oy + (int)py;
            g.FillRectangle(Brushes.DimGray, ox + px - 110, seatY, 220, 10);
            g.FillRectangle(Brushes.DimGray, ox + px - 100, seatY, 10, 90);
            g.FillRectangle(Brushes.DimGray, ox + px + 90, seatY, 10, 90);
            g.FillRectangle(Brushes.DimGray, ox + px - 110, seatY - 70, 8, 70);
            using var bmp = ToBitmap(img);
            g.DrawImage(bmp, ox, oy);
        }
        sheet.Save(Path.Combine(dir, "sit_preview.png"));
    }
}
