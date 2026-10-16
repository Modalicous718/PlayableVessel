using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace PlayableVessel
{
    /// <summary>
    /// Boss assets for one form: projectile/effect prefabs and audio clips, found by walking every
    /// PlayMaker action on the preloaded boss (the boss FSMs reference them directly), plus the
    /// boss's own child effect objects. "Friendly" templates are private, inactive copies with the
    /// DamageHero parts replaced by <see cref="EnemyDamager"/>, so the real boss and the global
    /// object pool are never modified.
    /// </summary>
    internal class VesselAssets
    {
        public readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, AudioClip> Audio = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, GameObject> _friendly = new Dictionary<string, GameObject>();
        private readonly BossSource _src;
        private static GameObject _holder;

        public VesselAssets(BossSource src)
        {
            _src = src;
            foreach (var fsm in src.Root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                foreach (var state in fsm.FsmStates)
                {
                    FsmStateAction[] actions;
                    try { actions = state.Actions; } catch { continue; }
                    foreach (var a in actions)
                        if (a != null) Harvest(a);
                }
            }
            PlayableVessel.Instance.Log($"[{src.Form}] assets: prefabs=[{string.Join(", ", new List<string>(Prefabs.Keys).ToArray())}] audio={Audio.Count}");
        }

        private void Harvest(FsmStateAction a)
        {
            foreach (var f in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                try { v = f.GetValue(a); } catch { continue; }
                switch (v)
                {
                    case FsmGameObject fgo when fgo != null && !fgo.UseVariable && fgo.Value != null:
                        AddPrefab(fgo.Value);
                        break;
                    case GameObject go when go != null:
                        AddPrefab(go);
                        break;
                    case FsmObject fo when fo != null && fo.Value is AudioClip clip:
                        Audio[clip.name] = clip;
                        break;
                    case FsmObject[] arr when arr != null:
                        foreach (var o in arr)
                            if (o != null && o.Value is AudioClip c2) Audio[c2.name] = c2;
                        break;
                    case AudioClip clip2 when clip2 != null:
                        Audio[clip2.name] = clip2;
                        break;
                }
            }
        }

        private void AddPrefab(GameObject go)
        {
            // Scene objects (children of the preloaded root) are reached through BossSource.Find instead.
            if (go.transform.IsChildOf(_src.Root.transform)) return;
            if (!Prefabs.ContainsKey(go.name)) Prefabs[go.name] = go;
        }

        public AudioClip Clip(string name) => name != null && Audio.TryGetValue(name, out var c) ? c : null;

        /// <summary>Source object: a referenced prefab by name, or a child of the preloaded boss scene root.</summary>
        public GameObject Source(string name)
        {
            if (Prefabs.TryGetValue(name, out var p)) return p;
            return _src.Find(name);
        }

        /// <summary>An inactive, player-owned template of a boss attack object (cached).</summary>
        public GameObject Friendly(string name, int damage, AttackTypes type = AttackTypes.Spell)
        {
            if (!_friendly.TryGetValue(name, out var tpl) || tpl == null)
            {
                var src = Source(name);
                if (src == null)
                {
                    PlayableVessel.Instance.LogWarn($"[{_src.Form}] missing asset '{name}'");
                    return null;
                }
                if (_holder == null)
                {
                    _holder = new GameObject("PlayableVessel Templates");
                    _holder.SetActive(false);
                    UObject.DontDestroyOnLoad(_holder);
                }
                tpl = UObject.Instantiate(src, _holder.transform);
                tpl.name = name;
                tpl.SetActive(true); // parent is inactive, so nothing runs
                MakeFriendly(tpl, damage, type);
                _friendly[name] = tpl;
                if (PlayableVessel.GS.DumpData)
                    Dumper.DumpTree(tpl, Path.Combine(PlayableVessel.ModDir, "dump"), $"Asset_{_src.Form}_{name}");
            }
            return tpl;
        }

        /// <summary>Spawn a friendly copy at a position. It destroys itself after <paramref name="life"/> seconds.</summary>
        public GameObject Spawn(string name, Vector3 pos, int damage, AttackTypes type = AttackTypes.Spell, float life = 5f)
        {
            var tpl = Friendly(name, damage, type);
            if (tpl == null) return null;
            var go = UObject.Instantiate(tpl, pos, tpl.transform.rotation);
            go.name = name + " (Vessel)";
            foreach (var d in go.GetComponentsInChildren<EnemyDamager>(true)) d.Damage = damage;
            IgnoreHero(go);
            go.SetActive(true);
            if (life > 0f) UObject.Destroy(go, life);
            return go;
        }

        /// <summary>
        /// Boss bullets (EnemyBullet) burst on touching the hero's body or HeroBox. Ours start inside
        /// the vessel, so turn off every collision between the copy and the hero.
        /// </summary>
        public static void IgnoreHero(GameObject go)
        {
            var hero = HeroController.instance;
            if (hero == null) return;
            var heroCols = hero.GetComponentsInChildren<Collider2D>(true);
            foreach (var c in go.GetComponentsInChildren<Collider2D>(true))
                foreach (var h in heroCols)
                    if (c != null && h != null) Physics2D.IgnoreCollision(c, h, true);
        }

        /// <summary>Strip enemy-side damage from an object tree and give it player-side damage instead.</summary>
        public static void MakeFriendly(GameObject root, int damage, AttackTypes type)
        {
            foreach (var dh in root.GetComponentsInChildren<DamageHero>(true))
            {
                var go = dh.gameObject;
                UObject.DestroyImmediate(dh);
                if (go.GetComponent<Collider2D>() != null && go.GetComponent<EnemyDamager>() == null)
                {
                    var d = go.AddComponent<EnemyDamager>();
                    d.Damage = damage;
                    d.AttackType = type;
                }
            }
            // Enemy-only helpers that would react to the hero.
            foreach (var hm in root.GetComponentsInChildren<HealthManager>(true)) UObject.DestroyImmediate(hm);
        }
    }
}
