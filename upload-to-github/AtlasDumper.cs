using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PlayableVessel
{
    /// <summary>
    /// Development helper: saves the sprite atlases behind an animation library as PNGs plus a TSV
    /// of every sprite's atlas rectangle, so animation frames can be viewed outside the game.
    /// Output is for local inspection only and never shipped.
    /// </summary>
    internal static class AtlasDumper
    {
        public static void Dump(tk2dSpriteAnimation lib, string dir, string prefix)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var collections = new HashSet<tk2dSpriteCollectionData>();
                foreach (var clip in lib.clips)
                    if (clip != null)
                        foreach (var f in clip.frames)
                            if (f.spriteCollection != null) collections.Add(f.spriteCollection);

                var tsv = new StringBuilder("collection\tid\tname\tatlas\tw\th\tumin\tvmin\tumax\tvmax\tflipped\tposMinX\tposMinY\tposMaxX\tposMaxY\n");
                var saved = new Dictionary<Texture, string>();
                foreach (var coll in collections)
                {
                    for (int id = 0; id < coll.spriteDefinitions.Length; id++)
                    {
                        var d = coll.spriteDefinitions[id];
                        if (d == null || d.uvs == null || d.uvs.Length == 0) continue;
                        var mat = d.material != null ? d.material : (coll.materials != null && d.materialId < coll.materials.Length ? coll.materials[d.materialId] : null);
                        var tex = mat != null ? mat.mainTexture : null;
                        if (tex == null) continue;
                        if (!saved.TryGetValue(tex, out var file))
                        {
                            file = Sanitize($"{prefix}_atlas_{coll.name}_{saved.Count}") + ".png";
                            SaveTexture(tex, Path.Combine(dir, file));
                            saved[tex] = file;
                        }
                        float umin = 1, vmin = 1, umax = 0, vmax = 0;
                        foreach (var uv in d.uvs)
                        {
                            umin = Mathf.Min(umin, uv.x); vmin = Mathf.Min(vmin, uv.y);
                            umax = Mathf.Max(umax, uv.x); vmax = Mathf.Max(vmax, uv.y);
                        }
                        Vector3 pmin = d.positions[0], pmax = d.positions[0];
                        foreach (var p in d.positions) { pmin = Vector3.Min(pmin, p); pmax = Vector3.Max(pmax, p); }
                        tsv.AppendLine(string.Join("\t", new[]
                        {
                            coll.name, id.ToString(), d.name, file, tex.width.ToString(), tex.height.ToString(),
                            F(umin), F(vmin), F(umax), F(vmax), d.flipped.ToString(),
                            F(pmin.x), F(pmin.y), F(pmax.x), F(pmax.y)
                        }));
                    }
                }
                File.WriteAllText(Path.Combine(dir, Sanitize(prefix + "_sprites") + ".tsv"), tsv.ToString());
                File.WriteAllText(Path.Combine(dir, Sanitize(prefix + "_clips") + ".tsv"), ClipTable(lib));
                PlayableVessel.Instance.Log($"Atlas dump {prefix}: {collections.Count} collections, {saved.Count} textures");
            }
            catch (System.Exception e)
            {
                PlayableVessel.Instance.LogError("Atlas dump failed: " + e);
            }
        }

        private static string ClipTable(tk2dSpriteAnimation lib)
        {
            var sb = new StringBuilder("clip\tindex\tcollection\tid\n");
            foreach (var clip in lib.clips)
            {
                if (clip == null) continue;
                for (int i = 0; i < clip.frames.Length; i++)
                    sb.AppendLine($"{clip.name}\t{i}\t{clip.frames[i].spriteCollection?.name}\t{clip.frames[i].spriteId}");
            }
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        private static void SaveTexture(Texture tex, string path)
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
            File.WriteAllBytes(path, copy.EncodeToPNG());
            Object.Destroy(copy);
        }
    }
}
