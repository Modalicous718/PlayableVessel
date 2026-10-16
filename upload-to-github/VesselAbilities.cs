using System;
using System.Collections;
using HutongGames.PlayMaker;
using Modding;
using UnityEngine;
using Random = UnityEngine.Random;
using UObject = UnityEngine.Object;

namespace PlayableVessel
{
    /// <summary>
    /// The vessel's boss moves, rebuilt for the player from the bosses' Control FSM data.
    ///
    ///  Input                    Hollow Knight (Sealed Vessel)        Pure Vessel
    ///  Spell (Vengeful Spirit)  Infection glob barrage               Shadow dagger fan
    ///  Down spell, in the air   Down-stab + erupting plumes          Down-stab + ground swords
    ///  Down spell, on ground    Puppet slams (hop and crush)         Void tendrils
    ///  Up spell                 Chest shot (rise and spray)          Focus blast orbs
    ///  Parry button (Quick Cast by default)  Counter stance, counter slash   same
    ///  Taunt (V / right stick click, Silksong)  Stare up and roar; next hit within 6 s deals 1.5x
    ///  Look up / look down      Roar at the sky / kneel              same
    ///  Dash                     Dash stab (damages on contact)       same
    ///
    /// Spells hook the Knight's own "Spell Control" FSM right after it has checked soul and spell
    /// ownership, so soul cost, quick cast, Spell Twister and spell upgrades all still apply.
    /// </summary>
    public class VesselAbilities : MonoBehaviour
    {
        private enum Spell { Fireball, Quake, Scream }

        private HeroController _hc;
        private Rigidbody2D _rb;
        private Collider2D _body;
        private AudioSource _audio;
        private VesselController _vc;
        private PlayMakerFSM _spellFsm;

        private bool _busy;
        private bool _spellRunning;
        private bool _counterStance;
        private bool _countered;
        private float _parryReadyAt;
        private float _tauntReadyAt;
        private EnemyDamager _dashStab;
        private bool _wasDashing;

        private const int TerrainMask = 1 << 8;

        private bool VesselOn => _vc != null && _vc.Active && PlayableVessel.GS.Enabled;
        private VesselForm Form => _vc.Source.Form;
        private VesselAssets Assets => _vc.Assets;
        private float Face => _hc.cState.facingRight ? 1f : -1f;
        private int Nail => Math.Max(5, PlayerData.instance.GetInt("nailDamage"));
        private bool ShamanStone => PlayerData.instance.GetBool("equippedCharm_19");

        private void Awake()
        {
            _hc = GetComponent<HeroController>();
            _rb = GetComponent<Rigidbody2D>();
            _body = GetComponent<BoxCollider2D>();
            _audio = GetComponent<AudioSource>();
            _vc = GetComponent<VesselController>();
            ModHooks.TakeDamageHook += OnTakeDamage;
            On.HeroController.CanDreamNail += OnCanDreamNail;
            On.HeroController.CanQuickMap += OnCanQuickMap;
            ModHooks.HitInstanceHook += OnHitInstance;
        }

        private void OnDestroy()
        {
            ModHooks.TakeDamageHook -= OnTakeDamage;
            On.HeroController.CanDreamNail -= OnCanDreamNail;
            On.HeroController.CanQuickMap -= OnCanQuickMap;
            ModHooks.HitInstanceHook -= OnHitInstance;
        }

        private void Start()
        {
            HookSpellControl();
            BuildDashStab();
        }

        internal void OnFormChanged() => Abort();

        // ================================================================== spell FSM hooks

        private void HookSpellControl()
        {
            _spellFsm = gameObject.LocateMyFSM("Spell Control");
            if (_spellFsm == null)
            {
                PlayableVessel.Instance.LogError("Spell Control FSM not found; vessel spells disabled.");
                return;
            }
            var wait = _spellFsm.AddState("Vessel Cast");
            wait.AddTransition("FINISHED", "Spell End");
            wait.Actions = new FsmStateAction[0];
            wait.InsertAction(0, new CallbackAction
            {
                Update = a => { if (!_spellRunning) a.SendEvent("FINISHED"); }
            });

            Intercept("Wallside?", Spell.Fireball);
            Intercept("On Ground?", Spell.Quake);
            Intercept("Level Check 3", Spell.Scream);

            // When Quick Cast is the parry button, the quick-cast path is cancelled (normal casting still works).
            _spellFsm.GetState("Can Cast? QC")?.InsertAction(0, new CallbackAction
            {
                Enter = a => { if (ParryTakes(ParryInput.QuickCast)) a.SendEvent("CANCEL"); }
            });
        }

        private void Intercept(string stateName, Spell spell)
        {
            var st = _spellFsm.GetState(stateName);
            if (st == null)
            {
                PlayableVessel.Instance.LogError($"Spell Control has no state '{stateName}'");
                return;
            }
            st.AddTransition("VESSEL CAST", "Vessel Cast");
            st.InsertAction(0, new CallbackAction
            {
                Enter = a =>
                {
                    if (!VesselOn || _busy) return;
                    var cost = _spellFsm.GetInt("MP Cost");
                    _hc.TakeMP(cost != null ? cost.Value : 33);
                    _spellRunning = true;
                    StartCoroutine(RunSpell(spell));
                    a.SendEvent("VESSEL CAST");
                }
            });
        }

        private IEnumerator RunSpell(Spell spell)
        {
            _busy = true;
            VesselLog.Write("move", $"spell {spell} start " + VesselLog.Hero());
            int level = PlayerData.instance.GetInt(spell == Spell.Fireball ? "fireballLevel" : spell == Spell.Quake ? "quakeLevel" : "screamLevel");
            level = Mathf.Clamp(level, 1, 2);
            IEnumerator move;
            switch (spell)
            {
                case Spell.Fireball:
                    move = Form == VesselForm.HollowKnight ? GlobBarrage(level) : DaggerFan(level);
                    break;
                case Spell.Quake:
                    if (Grounded())
                        move = Form == VesselForm.HollowKnight ? PuppetSlams(level) : Tendrils(level);
                    else
                        move = DownStab(level);
                    break;
                default:
                    move = Form == VesselForm.HollowKnight ? ChestShot(level) : FocusBlast(level);
                    break;
            }
            yield return move;
            Finish();
        }

        private void Finish()
        {
            VesselLog.Write("move", "finished, control back");
            _hc.AffectedByGravity(true);
            _hc.cState.invulnerable = false;
            _vc.Release();
            _hc.StartAnimationControl();
            _hc.RegainControl();
            _spellRunning = false; // spells: lets the Spell Control FSM continue to "Spell End"
            _busy = false;
        }

        // ================================================================== helpers

        private void Lock(bool gravity)
        {
            _hc.RelinquishControl();
            _hc.StopAnimationControl();
            _hc.AffectedByGravity(gravity);
            _rb.velocity = gravity ? new Vector2(0f, Mathf.Min(0f, _rb.velocity.y)) : Vector2.zero;
        }

        private Vector2 Feet => new Vector2(_body.bounds.center.x, _body.bounds.min.y);
        private Vector2 Chest => (Vector2)_body.bounds.center + new Vector2(Face * 0.4f, 0.3f);

        private bool Grounded() =>
            Physics2D.Raycast(Feet + Vector2.up * 0.05f, Vector2.down, 0.25f, TerrainMask).collider != null;

        private float? GroundY(float x, float fromY)
        {
            var hit = Physics2D.Raycast(new Vector2(x, fromY), Vector2.down, 12f, TerrainMask);
            return hit.collider != null ? hit.point.y : (float?)null;
        }

        private void Sfx(string clip, float volume = 1f)
        {
            var c = Assets.Clip(clip);
            if (c != null && _audio != null) _audio.PlayOneShot(c, volume);
        }

        private static void Shake(string ev)
        {
            var cam = GameCameras.instance;
            if (cam != null && cam.cameraShakeFSM != null) cam.cameraShakeFSM.SendEvent(ev);
        }

        /// <summary>Clone a boss child effect (visual only) at a position; mirrored to the vessel's facing.</summary>
        private GameObject Effect(string name, Vector3 pos, float life, float scale = 1f, bool faceFlip = true)
        {
            var src = Assets.Source(name);
            if (src == null) return null;
            var go = UObject.Instantiate(src);
            VesselAssets.MakeFriendly(go, 0, AttackTypes.Generic);
            foreach (var d in go.GetComponentsInChildren<EnemyDamager>(true)) UObject.Destroy(d);
            go.transform.position = pos;
            // Boss child effects are authored in boss space, where the art faces +x (right).
            var ls = src.transform.localScale * scale;
            if (faceFlip) ls.x *= Face;
            go.transform.localScale = ls;
            go.SetActive(true);
            UObject.Destroy(go, life);
            return go;
        }

        /// <summary>
        /// Clone a boss effect and place it exactly where the boss would have it, relative to the
        /// vessel puppet: same offset, size and angle, scaled down to the vessel and mirrored to its facing.
        /// </summary>
        private GameObject BossEffect(string name, float life, float extraScale = 1f)
        {
            var src = Assets.Source(name);
            var boss = _vc.Source.Boss.transform;
            if (src == null || _vc.Puppet == null) return null;
            float s = _vc.VisualScale;

            Vector3 local = boss.InverseTransformPoint(src.transform.position); // boss space, art facing right
            Vector3 lossy = src.transform.lossyScale, bl = boss.lossyScale;
            var scale = new Vector3(lossy.x / bl.x, lossy.y / bl.y, 1f) * (s * extraScale);
            float rot = src.transform.eulerAngles.z;

            var go = UObject.Instantiate(src);
            VesselAssets.MakeFriendly(go, 0, AttackTypes.Generic);
            foreach (var d in go.GetComponentsInChildren<EnemyDamager>(true)) UObject.Destroy(d);
            go.transform.position = _vc.Puppet.transform.position + new Vector3(local.x * s * Face, local.y * s, -0.01f);
            go.transform.localScale = new Vector3(scale.x * Face, scale.y, 1f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Face > 0 ? rot : -rot);
            go.SetActive(true);
            UObject.Destroy(go, life);
            return go;
        }

        private int SpellDamage(float lvl1, float lvl2, int level)
        {
            float d = level >= 2 ? lvl2 : lvl1;
            if (ShamanStone) d *= 1.33f;
            return Mathf.Max(1, Mathf.RoundToInt(d));
        }

        private static void Fling(GameObject go, float angleDeg, float speed)
        {
            if (go == null) return;
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb == null) return;
            float r = angleDeg * Mathf.Deg2Rad;
            rb.velocity = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * speed;
        }

        /// <summary>Angle measured from "forward" (the vessel's facing), counter-clockwise.</summary>
        private float Forward(float deg) => Face > 0 ? deg : 180f - deg;

        // ================================================================== Hollow Knight: glob barrage

        private IEnumerator GlobBarrage(int level)
        {
            bool air = !Grounded();
            Lock(gravity: false);
            _vc.PlaySequence(("SmallShot Antic", 0.2f));
            Sfx("hollow_knight_bulging_start_scream", 0.6f);
            yield return new WaitForSeconds(0.2f);

            _vc.Hold("SmallShot");
            BossEffect("Burst SmallShot", 0.5f);
            int count = level >= 2 ? 8 : 5;
            int dmg = SpellDamage(7, 11, level);
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);
                var glob = Assets.Spawn("Shot HK Glob", Chest + Random.insideUnitCircle * 0.2f, dmg, AttackTypes.Spell, 4f);
                Fling(glob, Forward(Mathf.Lerp(65f, 15f, t)), Random.Range(20f, 26f));
                yield return new WaitForSeconds(0.06f);
            }
            yield return new WaitForSeconds(0.1f);
            _vc.PlaySequence(("SmallShot Recover", 0.2f));
            if (air) _hc.AffectedByGravity(true);
            yield return new WaitForSeconds(0.2f);
        }

        // ================================================================== Pure Vessel: shadow dagger fan

        private IEnumerator DaggerFan(int level)
        {
            Lock(gravity: false);
            _vc.PlaySequence(("DartShoot Antic", 0.25f));
            Sfx("mage_lord_projectile_charge", 0.7f);
            yield return new WaitForSeconds(0.25f);

            _vc.Hold("DartShoot");
            int count = level >= 2 ? 10 : 6;
            int dmg = SpellDamage(6, 9, level);
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);
                float ang = Forward(Mathf.Lerp(25f, -15f, t));
                var dart = Assets.Spawn("Shot HK Shadow", Chest, dmg, AttackTypes.Spell, 2.5f);
                Fling(dart, ang, 35f);
                if (dart != null) dart.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                Sfx("radiance_sword_create_circular", 0.4f);
                yield return new WaitForSeconds(0.045f);
            }
            _vc.PlaySequence(("SmallShot Recover", 0.2f));
            yield return new WaitForSeconds(0.2f);
        }

        // ================================================================== Down-stab with plumes (both forms)

        private IEnumerator DownStab(int level)
        {
            Lock(gravity: false);
            _vc.PlaySequence(("DStab Antic", 0.22f));
            Sfx("brkn_wand_down_stab_prepare");
            _rb.velocity = new Vector2(0f, 6f);
            yield return new WaitForSeconds(0.22f);

            // Stomp: plunge until the ground is hit, damaging anything below on the way.
            _hc.cState.invulnerable = true;
            _vc.Hold(_vc.Pick("Dstab Stomp", "DStab Stomp"));
            Sfx("brkn_wand_down_stab_dash");
            int fallDmg = Nail;
            float t = 0f;
            var hitAlready = new System.Collections.Generic.HashSet<HealthManager>();
            while (!Grounded() && t < 1.5f)
            {
                _rb.velocity = new Vector2(Face * 4f, -60f);
                HitBoxOnce(Feet + Vector2.up * 0.3f, new Vector2(1.6f, 1.6f), fallDmg, AttackTypes.Nail, hitAlready, 270f);
                t += Time.deltaTime;
                yield return null;
            }
            _rb.velocity = Vector2.zero;

            // Impact.
            Vector2 feet = Feet;
            _vc.PlaySequence(("DStab Land", 0.35f));
            Sfx("false_knight_strike_ground");
            Shake("AverageShake");
            BossEffect("Slam Effect", 1f);
            EnemyDamager.HitCircle(feet + Vector2.up * 0.5f, 3.2f, SpellDamage(20, 32, level), AttackTypes.Spell, gameObject);
            StartCoroutine(PlumeWave(feet, level));
            yield return new WaitForSeconds(0.35f);
            _hc.cState.invulnerable = false;
        }

        private IEnumerator PlumeWave(Vector2 origin, int level)
        {
            bool pv = Form == VesselForm.PureVessel;
            string plume = pv ? "HK Plume Prime" : "HK Plume";
            float spacing = pv ? 3.7f : 2.6f;
            int pairs = level >= 2 ? 7 : 4;
            int dmg = SpellDamage(12, 18, level);
            float yOff = _vc.Source.PlumeYOffset;
            Sfx(pv ? "gg_pure_vessel_ground_swords_prepare" : "hollow_knight_firewalls_produce", 0.7f);
            for (int i = 1; i <= pairs; i++)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = origin.x + side * spacing * i;
                    float? gy = GroundY(x, origin.y + 2f);
                    if (gy == null) continue;
                    Assets.Spawn(plume, new Vector3(x, gy.Value + yOff, 0.01f), dmg, AttackTypes.Spell, 3.5f);
                }
                yield return new WaitForSeconds(0.12f);
            }
            if (pv) Sfx("gg_pure_vessel_ground_swords_appear", 0.7f);
        }

        // ================================================================== Hollow Knight: puppet slams

        private IEnumerator PuppetSlams(int level)
        {
            Lock(gravity: false);
            _vc.PlaySequence(("Puppet Antic", 0.25f));
            yield return new WaitForSeconds(0.25f);
            int slams = level >= 2 ? 4 : 3;
            int dmg = SpellDamage(15, 24, level);
            for (int i = 0; i < slams; i++)
            {
                _vc.Hold(i == 0 ? "Puppet Up" : "Puppet ReUp");
                Sfx("false_knight_jump", 0.8f);
                float t = 0f;
                while (t < 0.32f)
                {
                    _rb.velocity = new Vector2(Face * 9f, Mathf.Lerp(22f, 2f, t / 0.32f));
                    t += Time.deltaTime;
                    yield return null;
                }
                _vc.Hold("Puppet Down");
                t = 0f;
                while (!Grounded() && t < 1f)
                {
                    _rb.velocity = new Vector2(Face * 5f, -45f);
                    t += Time.deltaTime;
                    yield return null;
                }
                _rb.velocity = Vector2.zero;
                _vc.Hold("Puppet Slam");
                _hc.cState.invulnerable = true;
                Sfx("false_knight_land");
                Sfx("hollow_knight_bungling_bounce_" + Random.Range(1, 4), 0.8f);
                Shake("AverageShake");
                BossEffect("PuppetSlam Effect", 1f);
                EnemyDamager.HitCircle(Feet + Vector2.up * 0.6f, 2.8f, dmg, AttackTypes.Spell, gameObject);
                yield return new WaitForSeconds(0.15f);
                _hc.cState.invulnerable = false;
            }
            _vc.PlaySequence(("Puppet End", 0.25f));
            yield return new WaitForSeconds(0.25f);
        }

        // ================================================================== Pure Vessel: tendrils

        private IEnumerator Tendrils(int level)
        {
            Lock(gravity: false);
            _vc.PlaySequence(("SmallShot Antic", 0.25f));
            Sfx("gg_pure_vessel_tendril_antic");
            yield return new WaitForSeconds(0.25f);

            var src = Assets.Source("Tendrils");
            GameObject tendrils = null;
            if (src != null)
            {
                tendrils = UObject.Instantiate(src);
                VesselAssets.MakeFriendly(tendrils, SpellDamage(18, 30, level), AttackTypes.Spell);
                // Place them exactly as on the boss: their offset from the boss's origin, at the vessel's scale,
                // mirrored to its facing (boss space: +x is forward). The puppet's origin is the boss's origin.
                var boss = _vc.Source.Boss.transform;
                Vector3 local = boss.InverseTransformPoint(src.transform.position);
                Vector3 rel = new Vector3(src.transform.lossyScale.x / boss.lossyScale.x, src.transform.lossyScale.y / boss.lossyScale.y, 1f);
                float s = _vc.VisualScale;
                Vector3 origin = _vc.Puppet != null ? _vc.Puppet.transform.position : transform.position;
                tendrils.transform.position = origin + new Vector3(local.x * s * Face, local.y * s, -0.01f);
                tendrils.transform.localScale = new Vector3(rel.x * s * Face, rel.y * s, 1f);
                VesselLog.Write("move", $"tendrils at {tendrils.transform.position} scale={tendrils.transform.localScale} facingR={_hc.cState.facingRight}");
                foreach (Transform c in tendrils.transform) c.gameObject.SetActive(false);
                tendrils.SetActive(true);
                UObject.Destroy(tendrils, 2f);
            }
            void Show(string child)
            {
                var t = tendrils != null ? tendrils.transform.Find(child) : null;
                if (t != null) t.gameObject.SetActive(true);
            }

            _vc.Hold("SmallShot");
            Show("ST1"); Show("ST2");
            BossEffect("Burst SmallShot", 0.5f);
            Sfx("dark_spell_get");
            yield return new WaitForSeconds(level >= 2 ? 0.25f : 0.35f);
            Sfx("gg_pure_vessel_tendril_burst");
            Show("T1");
            yield return new WaitForSeconds(0.1f);
            Show("T Hit");
            Shake("EnemyKillShake");
            yield return new WaitForSeconds(0.15f);
            Show("T2");
            yield return new WaitForSeconds(0.2f);
            Show("T3");
            yield return new WaitForSeconds(0.2f);
            if (tendrils != null) UObject.Destroy(tendrils);
            _vc.PlaySequence(("SmallShot Recover", 0.2f));
            yield return new WaitForSeconds(0.2f);
        }

        // ================================================================== Hollow Knight: chest shot

        private IEnumerator ChestShot(int level)
        {
            Lock(gravity: false);
            _vc.PlaySequence(("ChestShot Antic", 0.25f));
            float t = 0f;
            while (t < 0.25f)
            {
                _rb.velocity = new Vector2(0f, 9f);
                t += Time.deltaTime;
                yield return null;
            }
            _rb.velocity = Vector2.zero;

            BossEffect("Burst ChestShot", 0.6f);
            Sfx("hollow_knight_bulging_start_scream");
            Shake("AverageShake");
            _vc.Hold("ChestShot");
            float dur = level >= 2 ? 1.5f : 0.9f;
            int dmg = SpellDamage(6, 9, level);
            t = 0f;
            bool left = false;
            while (t < dur)
            {
                var a = Assets.Spawn("Shot HK Spurt", Chest, dmg, AttackTypes.Spell, 4f);
                var rb = a != null ? a.GetComponent<Rigidbody2D>() : null;
                if (rb != null) rb.velocity = new Vector2((left ? -1f : 1f) * Random.Range(3f, 25f), Random.Range(15f, 22f));
                left = !left;
                if (Random.value < 0.35f)
                    Fling(Assets.Spawn("Shot HK Spurt", Chest, dmg, AttackTypes.Spell, 4f), Random.Range(235f, 325f), Random.Range(15f, 20f));
                _rb.velocity = Vector2.zero;
                yield return new WaitForSeconds(0.08f);
                t += 0.08f;
            }
            _vc.PlaySequence(("Puppet End", 0.2f));
            _hc.AffectedByGravity(true);
            yield return new WaitForSeconds(0.2f);
        }

        // ================================================================== Pure Vessel: focus blast

        private IEnumerator FocusBlast(int level)
        {
            Lock(gravity: false);
            _vc.Hold("Focus Antic");
            Sfx("ghost_absorb");
            Sfx("gg_pure_vessel_focusing");
            Effect("Lines Anim", _body.bounds.center, 0.6f, 0.5f);
            yield return new WaitForSeconds(level >= 2 ? 0.3f : 0.4f);

            // Small orbs along the ground around the vessel, then the big burst on the vessel itself.
            int n = level >= 2 ? 6 : 4;
            int small = SpellDamage(12, 18, level);
            for (int i = 0; i < n; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                float x = _body.bounds.center.x + side * (3f + 3f * (i / 2));
                float? gy = GroundY(x, _body.bounds.center.y + 1f);
                float y = (gy ?? _body.bounds.min.y) + 1.2f;
                var blast = Assets.Spawn("Focus Blasts/HK Prime Blast/Blast", new Vector3(x, y, 0f), small, AttackTypes.Spell, 1.8f);
                if (blast != null) blast.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
            }
            Sfx("gg_pure_vessel_big_orb_appear");
            var orb = BossEffect("Focus Blast", 1f);
            yield return new WaitForSeconds(0.45f);

            _vc.PlaySequence(("Focus Burst", 0.4f));
            Sfx("gg_pure_vessel_big_orb_explode");
            Shake("BigShake");
            EnemyDamager.HitCircle(_body.bounds.center, 4.5f, SpellDamage(25, 40, level), AttackTypes.Spell, gameObject);
            if (orb != null) UObject.Destroy(orb, 0.3f);
            yield return new WaitForSeconds(0.4f);
        }

        // ================================================================== Counter / parry

        private void Update()
        {
            if (!VesselOn) return;

            if (_busy && (_hc.cState.dead || _hc.cState.transitioning || _hc.cState.hazardDeath))
            {
                Abort();
                return;
            }

            // Dash stab hits each enemy once per dash.
            bool dashing = _hc.cState.dashing || _hc.cState.shadowDashing;
            if (dashing && !_wasDashing && _dashStab != null) _dashStab.ResetHits();
            _wasDashing = dashing;
            if (_dashStab != null) _dashStab.Damage = Nail;
            if (_dashBox != null) _dashBox.enabled = dashing; // only exists while dashing
            SizeDashStab();

            if (_busy || GameManager.instance.isPaused) return;
            bool parry = ParryPressed();
            bool taunt = TauntPressed();
            if (!parry && !taunt) return;
            if (!_hc.acceptingInput || _hc.cState.dashing || _hc.cState.attacking || _hc.cState.casting || _hc.cState.focusing) return;

            if (parry && Time.time >= _parryReadyAt)
                StartCoroutine(Run(Counter()));
            else if (taunt && Grounded() && Time.time >= _tauntReadyAt)
                StartCoroutine(Run(Taunt()));
        }

        private static bool KeyDown(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return false;
            try { return Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), keyName, true)); }
            catch { return false; }
        }

        private bool ParryPressed()
        {
            var actions = InputHandler.Instance.inputActions;
            switch (PlayableVessel.GS.ParryButton)
            {
                case ParryInput.QuickCast: if (actions.quickCast.WasPressed) return true; break;
                case ParryInput.DreamNail: if (actions.dreamNail.WasPressed) return true; break;
                case ParryInput.QuickMap: if (actions.quickMap.WasPressed) return true; break;
                case ParryInput.KeyboardKeyOnly:
                    if (string.IsNullOrEmpty(PlayableVessel.GS.ParryKey)) return KeyDown("G");
                    break;
            }
            return KeyDown(PlayableVessel.GS.ParryKey);
        }

        /// <summary>Silksong's taunt button: V on keyboard, right stick click on a controller.</summary>
        private static bool TauntPressed()
        {
            if (KeyDown(PlayableVessel.GS.TauntKey)) return true;
            if (!PlayableVessel.GS.TauntOnRightStick) return false;
            var dev = InControl.InputManager.ActiveDevice;
            return dev != null && dev.RightStickButton.WasPressed;
        }

        // The button chosen for parry loses its normal use while playing as the vessel.
        private bool ParryTakes(ParryInput button) => VesselOn && PlayableVessel.GS.ParryButton == button;

        private bool OnCanDreamNail(On.HeroController.orig_CanDreamNail orig, HeroController self) =>
            !ParryTakes(ParryInput.DreamNail) && orig(self);

        private bool OnCanQuickMap(On.HeroController.orig_CanQuickMap orig, HeroController self) =>
            !ParryTakes(ParryInput.QuickMap) && orig(self);

        /// <summary>The Knight's own nail hits also use up the taunt bonus (Silksong applies it to nail attacks).</summary>
        private HitInstance OnHitInstance(Fsm owner, HitInstance hit)
        {
            if (VesselOn && hit.AttackType == AttackTypes.Nail && TauntBuff.Active
                && owner?.GameObject != null && owner.GameObject.transform.IsChildOf(transform))
                hit.Multiplier *= TauntBuff.Take();
            return hit;
        }

        private IEnumerator Run(IEnumerator move)
        {
            _busy = true;
            VesselLog.Write("move", $"{move.GetType().Name.Split('>')[0].TrimStart('<')} start " + VesselLog.Hero());
            yield return move;
            Finish();
        }

        private IEnumerator Counter()
        {
            Lock(gravity: true);
            _countered = false;
            _vc.PlaySequence(("Counter Antic", 0.12f));
            yield return new WaitForSeconds(0.12f);

            _counterStance = true;
            _vc.Hold("Counter Ready");
            Sfx("hornet_parry_prepare");
            BossEffect("Counter Flash", 0.4f);
            float t = 0f;
            while (t < 0.6f && !_countered)
            {
                _rb.velocity = new Vector2(0f, _rb.velocity.y);
                t += Time.deltaTime;
                yield return null;
            }
            _counterStance = false;

            if (!_countered)
            {
                _vc.PlaySequence(("Counter End", 0.2f));
                yield return new WaitForSeconds(0.2f);
                _parryReadyAt = Time.time + 0.5f;
                yield break;
            }

            // Blocked: punish with the counter slash.
            _hc.cState.invulnerable = true;
            GameManager.instance.FreezeMoment(1);
            Sfx("hornet_parry_prepare");
            _vc.PlaySequence(("Counter Block", 0.15f));
            yield return new WaitForSeconds(0.15f);

            _vc.PlaySequence(("Slash2", 0.1f), ("Slash2 Recover", 0.25f));
            Sfx("ruin_shield_sentry_sword_1");
            Shake("EnemyKillShake");
            int dmg = Mathf.Max(15, Mathf.RoundToInt(Nail * 2.5f));
            var hit = new System.Collections.Generic.HashSet<HealthManager>();
            t = 0f;
            while (t < 0.14f)
            {
                _rb.velocity = new Vector2(Face * 32f, 0f);
                float k = VesselController.SizeFactor;
                HitBoxOnce((Vector2)_body.bounds.center + new Vector2(Face * 2.2f * k, 0.1f + 0.6f * (k - 1f)), new Vector2(4.6f * k, 2.8f * k), dmg, AttackTypes.Nail, hit, Face > 0 ? 0f : 180f);
                t += Time.deltaTime;
                yield return null;
            }
            _rb.velocity = Vector2.zero;
            yield return new WaitForSeconds(0.25f);
            _parryReadyAt = Time.time + 0.3f;
            StartCoroutine(InvulnFor(0.3f));
        }

        private IEnumerator InvulnFor(float secs)
        {
            _hc.cState.invulnerable = true;
            yield return new WaitForSeconds(secs);
            if (!_busy) _hc.cState.invulnerable = false;
        }

        private int OnTakeDamage(ref int hazardType, int damage)
        {
            if (!VesselOn || damage <= 0) return damage;
            if (_counterStance && hazardType == 1)
            {
                _countered = true;
                VesselLog.Write("move", "counter: blocked a hit");
                return 0;
            }
            if (_counterStance && _countered) return 0;
            // Any other hit cancels a running move so the normal hurt reaction can take over.
            if (_busy && !_hc.cState.invulnerable && hazardType == 1) Abort();
            return damage;
        }

        // ================================================================== Taunt (Silksong)

        /// <summary>
        /// Silksong's taunt, done the vessel's way: it stares up and roars. Like Silksong, a taunt
        /// makes the next hit within 6 seconds deal 1.5x damage. Purely a taunt: no damage of its own.
        /// </summary>
        private IEnumerator Taunt()
        {
            Lock(gravity: true);
            _vc.PlaySequence(("Roar Antic", 0.3f));
            yield return new WaitForSeconds(0.3f);
            _vc.Hold("Roar");
            Sfx(Assets.Clip("hollow_knight_scream_v1") != null ? "hollow_knight_scream_v1" : "gg_pure_vessel_armour_burst", 0.8f);
            Shake("AverageShake");
            Effect("Pt Mist Burst", _body.bounds.center, 1.5f, 1f, faceFlip: false);
            TauntBuff.Grant();
            _hc.GetComponent<SpriteFlash>()?.flashWhitePulse();
            yield return new WaitForSeconds(0.75f);
            _vc.PlaySequence(("RoarToIdle", 0.2f));
            yield return new WaitForSeconds(0.2f);
            _tauntReadyAt = Time.time + 0.5f;
        }

        // ================================================================== Dash stab

        private BoxCollider2D _dashBox;

        /// <summary>Dash stab hitbox in front of the Knight (Knight-local -x is forward), grown with the vessel's size.</summary>
        private void SizeDashStab()
        {
            if (_dashBox == null) return;
            float k = VesselController.SizeFactor;
            _dashBox.size = new Vector2(2.2f * k, 1.3f * k);
            _dashBox.offset = new Vector2(-1.0f * k, -0.3f + 0.5f * (k - 1f));
        }

        private void BuildDashStab()
        {
            var go = new GameObject("Vessel Dash Stab");
            go.layer = 17; // Attack
            go.transform.SetParent(transform, false);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            _dashBox = box;
            SizeDashStab();
            _dashStab = go.AddComponent<EnemyDamager>();
            _dashStab.AttackType = AttackTypes.Nail;
            _dashStab.Damage = Nail;
            _dashStab.Gate = () => VesselOn && (_hc.cState.dashing || _hc.cState.shadowDashing);
        }

        // ================================================================== shared

        private void HitBoxOnce(Vector2 center, Vector2 size, int damage, AttackTypes type, System.Collections.Generic.HashSet<HealthManager> already, float dir)
        {
            foreach (var col in Physics2D.OverlapBoxAll(center, size, 0f, (1 << 11) | (1 << 17) | (1 << 19)))
            {
                var hm = col.GetComponentInParent<HealthManager>();
                if (hm == null || hm.isDead || !already.Add(hm)) continue;
                hm.Hit(new HitInstance
                {
                    Source = gameObject,
                    AttackType = type,
                    DamageDealt = damage,
                    Direction = dir,
                    MagnitudeMultiplier = 1f,
                    Multiplier = TauntBuff.Take(),
                    SpecialType = SpecialTypes.None
                });
            }
        }

        /// <summary>Stop whatever move is running and hand everything back to the game.</summary>
        internal void Abort()
        {
            if (_busy) VesselLog.Write("move", "aborted " + VesselLog.Hero());
            StopAllCoroutines();
            _counterStance = false;
            _countered = false;
            if (_hc == null) return;
            _hc.cState.invulnerable = false;
            _hc.AffectedByGravity(true);
            _vc?.Release();
            if (_busy && !_hc.cState.dead && !_hc.cState.transitioning)
            {
                _hc.StartAnimationControl();
                _hc.RegainControl();
            }
            _spellRunning = false; // lets the Spell Control FSM leave "Vessel Cast"
            _busy = false;
        }
    }
}
