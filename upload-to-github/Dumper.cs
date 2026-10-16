using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace PlayableVessel
{
    /// <summary>
    /// Development helper: writes the full structure of a GameObject tree (components,
    /// tk2d animation clips, PlayMaker FSMs with every action's fields) to text files,
    /// so the boss kit can be mapped from the game's real data instead of guesses.
    /// </summary>
    internal static class Dumper
    {
        private static readonly HashSet<object> DumpedLibraries = new HashSet<object>();

        public static void DumpTree(GameObject root, string dir, string prefix)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                var fsms = new List<PlayMakerFSM>();
                var libs = new List<tk2dSpriteAnimation>();
                DumpGo(root, sb, 0, fsms, libs);
                File.WriteAllText(Path.Combine(dir, Sanitize(prefix + "_hierarchy") + ".txt"), sb.ToString());

                foreach (var fsm in fsms)
                {
                    string path = GetPath(fsm.transform, root.transform);
                    string file = Sanitize(prefix + "_fsm_" + path + "__" + fsm.FsmName) + ".txt";
                    File.WriteAllText(Path.Combine(dir, file), DumpFsm(fsm));
                }

                foreach (var lib in libs.Distinct())
                {
                    if (lib == null || !DumpedLibraries.Add(lib)) continue;
                    File.WriteAllText(Path.Combine(dir, Sanitize(prefix + "_anim_" + lib.name) + ".txt"), DumpLibrary(lib));
                }
            }
            catch (Exception e)
            {
                PlayableVessel.Instance.LogError("Dump failed for " + prefix + ": " + e);
            }
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        public static string GetPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return parts.Count == 0 ? "ROOT" : string.Join("/", parts.ToArray());
        }

        private static void DumpGo(GameObject go, StringBuilder sb, int depth, List<PlayMakerFSM> fsms, List<tk2dSpriteAnimation> libs)
        {
            string ind = new string(' ', depth * 2);
            var t = go.transform;
            sb.AppendLine($"{ind}[{go.name}] active={go.activeSelf} layer={go.layer}({LayerMask.LayerToName(go.layer)}) tag={go.tag} lpos={t.localPosition} lscale={t.localScale} lrot={t.localEulerAngles}");
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) { sb.AppendLine(ind + "  - <missing script>"); continue; }
                sb.Append(ind + "  - " + c.GetType().Name);
                switch (c)
                {
                    case PlayMakerFSM fsm:
                        sb.Append($" fsm='{fsm.FsmName}'");
                        fsms.Add(fsm);
                        break;
                    case tk2dSpriteAnimator an:
                        sb.Append($" lib='{(an.Library ? an.Library.name : "null")}' default='{an.DefaultClip?.name}' playAuto={an.playAutomatically}");
                        if (an.Library) libs.Add(an.Library);
                        break;
                    case tk2dBaseSprite sp:
                        var def = sp.Collection != null && sp.spriteId >= 0 && sp.spriteId < sp.Collection.spriteDefinitions.Length ? sp.Collection.spriteDefinitions[sp.spriteId] : null;
                        sb.Append($" coll='{(sp.Collection ? sp.Collection.name : "null")}' sprite={sp.spriteId}:{def?.name} scale={sp.scale} color={sp.color}");
                        if (def != null && def.boundsData != null && def.boundsData.Length > 1)
                            sb.Append($" bounds(center={def.boundsData[0]} size={def.boundsData[1]})");
                        break;
                    case BoxCollider2D b:
                        sb.Append($" enabled={b.enabled} trigger={b.isTrigger} size={b.size} offset={b.offset}");
                        break;
                    case CircleCollider2D cc:
                        sb.Append($" enabled={cc.enabled} trigger={cc.isTrigger} r={cc.radius} offset={cc.offset}");
                        break;
                    case PolygonCollider2D p:
                        sb.Append($" enabled={p.enabled} trigger={p.isTrigger} pts=[{string.Join(";", p.points.Select(v => v.ToString()).ToArray())}]");
                        break;
                    case DamageHero dh:
                        sb.Append($" damage={dh.damageDealt} hazard={dh.hazardType}");
                        break;
                    case HealthManager hm:
                        sb.Append($" hp={hm.hp}");
                        break;
                    case AudioSource a:
                        sb.Append($" clip={(a.clip ? a.clip.name : "null")}");
                        break;
                    case Renderer r:
                        sb.Append($" enabled={r.enabled} sortLayer={r.sortingLayerName} order={r.sortingOrder} mat={(r.sharedMaterial ? r.sharedMaterial.name : "null")}");
                        break;
                    case Rigidbody2D rb:
                        sb.Append($" type={rb.bodyType} grav={rb.gravityScale}");
                        break;
                }
                sb.AppendLine();
            }
            foreach (Transform child in t)
                DumpGo(child.gameObject, sb, depth + 1, fsms, libs);
        }

        public static string DumpLibrary(tk2dSpriteAnimation lib)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Library {lib.name} clips={lib.clips.Length}");
            foreach (var clip in lib.clips)
            {
                if (clip == null) continue;
                string coll = clip.frames.Length > 0 && clip.frames[0].spriteCollection ? clip.frames[0].spriteCollection.name : "?";
                sb.AppendLine($"CLIP '{clip.name}' frames={clip.frames.Length} fps={clip.fps} dur={(clip.fps > 0 ? clip.frames.Length / clip.fps : 0):0.###} wrap={clip.wrapMode} loopStart={clip.loopStart} coll={coll}");
                for (int i = 0; i < clip.frames.Length; i++)
                {
                    var f = clip.frames[i];
                    string spriteName = "?";
                    if (f.spriteCollection && f.spriteId >= 0 && f.spriteId < f.spriteCollection.spriteDefinitions.Length)
                    {
                        var d = f.spriteCollection.spriteDefinitions[f.spriteId];
                        spriteName = d.name + (d.boundsData != null && d.boundsData.Length > 1 ? $" c={d.boundsData[0]} s={d.boundsData[1]}" : "");
                    }
                    sb.AppendLine($"    {i}: {spriteName}" + (f.triggerEvent ? $" TRIGGER info='{f.eventInfo}' int={f.eventInt} float={f.eventFloat}" : ""));
                }
            }
            return sb.ToString();
        }

        public static string DumpFsm(PlayMakerFSM fsm)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"FSM '{fsm.FsmName}' on '{fsm.gameObject.name}' start='{fsm.Fsm.StartState}'");
            sb.AppendLine("VARIABLES:");
            try
            {
                foreach (var v in fsm.FsmVariables.GetAllNamedVariables())
                    sb.AppendLine($"  {v.VariableType} {v.Name} = {Fmt(v.RawValue)}");
            }
            catch (Exception e) { sb.AppendLine("  <vars error " + e.Message + ">"); }

            sb.AppendLine("GLOBAL TRANSITIONS:");
            foreach (var tr in fsm.FsmGlobalTransitions)
                sb.AppendLine($"  {tr.EventName} -> {tr.ToState}");

            foreach (var st in fsm.FsmStates)
            {
                sb.AppendLine();
                sb.AppendLine($"STATE '{st.Name}'");
                foreach (var tr in st.Transitions)
                    sb.AppendLine($"  ~ {tr.EventName} -> {tr.ToState}");
                FsmStateAction[] actions;
                try { actions = st.Actions; }
                catch (Exception e) { sb.AppendLine("  <actions error " + e.Message + ">"); continue; }
                for (int i = 0; i < actions.Length; i++)
                {
                    var a = actions[i];
                    if (a == null) { sb.AppendLine($"  {i}. null"); continue; }
                    sb.AppendLine($"  {i}. {a.GetType().Name}{(a.Enabled ? "" : " (disabled)")}");
                    foreach (var f in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (f.DeclaringType == typeof(FsmStateAction)) continue;
                        object val;
                        try { val = f.GetValue(a); } catch { continue; }
                        sb.AppendLine($"       {f.Name} = {Fmt(val)}");
                    }
                }
            }
            return sb.ToString();
        }

        private static string Fmt(object v, int depth = 0)
        {
            if (v == null) return "null";
            if (depth > 2) return v.ToString();
            switch (v)
            {
                case string s: return "\"" + s + "\"";
                case FsmEvent e: return "event:" + e.Name;
                case FsmOwnerDefault o:
                    return o.OwnerOption == OwnerDefaultOption.UseOwner ? "<owner>" : "go:" + Fmt(o.GameObject, depth + 1);
                case FsmEventTarget et:
                    return $"target:{et.target}" + (et.gameObject != null ? " " + Fmt(et.gameObject, depth + 1) : "") + (et.fsmName != null ? " fsm=" + Fmt(et.fsmName, depth + 1) : "");
                case NamedVariable nv:
                    string raw;
                    try { raw = Fmt(nv.RawValue, depth + 1); } catch { raw = "?"; }
                    if (nv.UseVariable || (!string.IsNullOrEmpty(nv.Name) && nv.IsNone == false && nv.UsesVariable))
                        return $"var({nv.Name})={raw}";
                    if (nv.IsNone) return "none";
                    return raw;
                case UnityEngine.Object uo:
                    if (uo == null) return "null(destroyed)";
                    if (uo is GameObject g) return "GO[" + g.name + "]";
                    if (uo is Component comp) return comp.GetType().Name + "[" + comp.gameObject.name + "]";
                    return uo.GetType().Name + "[" + uo.name + "]";
                case Array arr:
                    var items = new List<string>();
                    foreach (var x in arr) items.Add(Fmt(x, depth + 1));
                    return "[" + string.Join(", ", items.ToArray()) + "]";
                case FsmVar fv:
                    return $"fsmvar({fv.variableName})";
                default:
                    return v.ToString();
            }
        }
    }
}
