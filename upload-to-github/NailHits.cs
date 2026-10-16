using System.Collections;
using System.Collections.Generic;
using GlobalEnums;
using HutongGames.PlayMaker;
using Modding;
using UnityEngine;

namespace PlayableVessel
{
    /// <summary>
    /// Makes sure nail swings damage what they touch. The game deals nail damage from a PlayMaker FSM
    /// ("damages_enemy") on each slash; in testing that FSM saw the contacts but never dealt damage (see
    /// the mod log). So: when a slash touches an enemy, wait two physics steps, and if the game hasn't hit
    /// that enemy with this swing, deal the same hit ourselves (same damage, direction and attack type as the
    /// FSM's own variables). Each enemy is hit at most once per swing either way.
    ///
    /// With "DebugHits" on, it also traces the damages_enemy and Superdash FSMs' state changes into the log.
    /// </summary>
    internal class NailHits : MonoBehaviour
    {
        private HeroController _hc;
        private int _swing;
        private readonly HashSet<HealthManager> _hitThisSwing = new HashSet<HealthManager>();
        private readonly HashSet<HealthManager> _pending = new HashSet<HealthManager>();

        private static readonly string[] Slashes = { "Slash", "AltSlash", "UpSlash", "DownSlash", "WallSlash" };

        private void Awake()
        {
            _hc = GetComponent<HeroController>();
            ModHooks.AttackHook += OnAttack;
            ModHooks.SlashHitHook += OnSlashHit;
            On.HealthManager.Hit += OnHmHit;
        }

        private void Start()
        {
            if (PlayableVessel.GS.DebugHits) Trace();
        }

        private float _nextCheck;
        private bool _reported;

        /// <summary>
        /// The game reads nail damage into each slash once, when the Knight is created (Attacks / "Set Slash
        /// Damage"). If that ran before the save's data was ready the slashes keep damageDealt = 0 and the game
        /// cancels every hit. Ask it to read the value again whenever a slash shows 0.
        /// </summary>
        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            var attacks = transform.Find("Attacks");
            var slash = attacks != null ? attacks.Find("Slash") : null;
            var fsm = slash != null ? slash.gameObject.LocateMyFSM("damages_enemy") : null;
            var dmg = fsm != null ? fsm.FsmVariables.FindFsmInt("damageDealt") : null;
            if (dmg == null || dmg.Value > 0 || PlayerData.instance == null || PlayerData.instance.GetInt("nailDamage") <= 0) return;
            var setter = attacks.gameObject.LocateMyFSM("Set Slash Damage");
            if (setter == null) return;
            setter.SendEvent("UPDATE NAIL DAMAGE");
            if (!_reported) { _reported = true; VesselLog.Write("nail", $"slash damage was 0 (nailDamage={PlayerData.instance.GetInt("nailDamage")}); asked the game to update it"); }
        }

        private void OnDestroy()
        {
            ModHooks.AttackHook -= OnAttack;
            ModHooks.SlashHitHook -= OnSlashHit;
            On.HealthManager.Hit -= OnHmHit;
        }

        private static bool On_ => PlayableVessel.GS.Enabled && PlayableVessel.GS.NailHitFallback;

        private void OnAttack(AttackDirection dir)
        {
            _swing++;
            _hitThisSwing.Clear();
            _pending.Clear();
        }

        /// <summary>Record hits the game itself makes with a nail slash, so we never double up.</summary>
        private void OnHmHit(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hit)
        {
            if (hit.AttackType == AttackTypes.Nail && hit.Source != null && IsSlash(hit.Source)) _hitThisSwing.Add(self);
            orig(self, hit);
        }

        private bool IsSlash(GameObject go) => go.transform.IsChildOf(transform) && System.Array.IndexOf(Slashes, go.name) >= 0;

        private void OnSlashHit(Collider2D other, GameObject slash)
        {
            if (!On_ || other == null || slash == null) return;
            var hm = other.GetComponentInParent<HealthManager>();
            if (hm == null || hm.isDead || _hitThisSwing.Contains(hm) || !_pending.Add(hm)) return;
            StartCoroutine(Fallback(hm, slash, _swing));
        }

        private IEnumerator Fallback(HealthManager hm, GameObject slash, int swing)
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            _pending.Remove(hm);
            if (swing != _swing || hm == null || hm.isDead || _hitThisSwing.Contains(hm)) yield break;

            var fsm = slash.LocateMyFSM("damages_enemy");
            var v = fsm != null ? fsm.FsmVariables : null;
            int dmg = v?.FindFsmInt("damageDealt")?.Value ?? PlayerData.instance.GetInt("nailDamage");
            if (dmg <= 0) dmg = Mathf.Max(5, PlayerData.instance.GetInt("nailDamage"));
            float dir = v?.FindFsmFloat("direction")?.Value ?? (_hc.cState.facingRight ? 0f : 180f);
            float mult = v?.FindFsmFloat("Multiplier")?.Value ?? 1f;
            float mag = v?.FindFsmFloat("magnitudeMult")?.Value ?? 1f;

            _hitThisSwing.Add(hm);
            VesselLog.Write("nail", $"fallback hit '{hm.name}' dmg={dmg} dir={dir} (game's own hit did not land)");
            hm.Hit(new HitInstance
            {
                Source = slash,
                AttackType = AttackTypes.Nail,
                DamageDealt = dmg,
                Direction = dir,
                Multiplier = mult <= 0f ? 1f : mult,
                MagnitudeMultiplier = mag <= 0f ? 1f : mag,
                SpecialType = SpecialTypes.None
            });
        }

        // ------------------------------------------------------------ tracing

        /// <summary>Log every state the slash damage FSMs and the Crystal Heart FSM go through.</summary>
        private void Trace()
        {
            var attacks = transform.Find("Attacks");
            if (attacks != null)
                foreach (var name in Slashes)
                {
                    var s = attacks.Find(name);
                    var fsm = s != null ? s.gameObject.LocateMyFSM("damages_enemy") : null;
                    if (fsm != null) TraceFsm(fsm, name, f =>
                    {
                        var col = f.FsmVariables.FindFsmGameObject("Collider")?.Value;
                        return $"collider='{(col != null ? col.name : "-")}' layer={(col != null ? LayerMask.LayerToName(col.layer) : "-")} dmg={f.FsmVariables.FindFsmInt("damageDealt")?.Value}";
                    });
                }
            var sd = gameObject.LocateMyFSM("Superdash");
            if (sd != null) TraceFsm(sd, "Superdash", f => VesselLog.Hero());
            VesselLog.Write("trace", "tracing damages_enemy + Superdash FSMs");
        }

        private static void TraceFsm(PlayMakerFSM fsm, string label, System.Func<PlayMakerFSM, string> detail)
        {
            foreach (var st in fsm.FsmStates)
            {
                if (st.Name == "Idle" && label != "Superdash") continue; // too frequent, says nothing
                string stateName = st.Name;
                st.InsertAction(0, new CallbackAction
                {
                    Enter = a => VesselLog.Write("fsm", $"{label}.{stateName} {detail(fsm)}")
                });
            }
        }
    }
}
