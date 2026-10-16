using System.Collections.Generic;
using UnityEngine;

namespace PlayableVessel
{
    /// <summary>
    /// Player-owned hitbox. Every physics step, while this object's colliders are enabled, it
    /// queries for enemy colliders overlapping it and calls HealthManager.Hit on each enemy once
    /// (or again after <see cref="RehitInterval"/>). Using queries instead of trigger callbacks means
    /// boss attack objects can be reused as-is without touching the layer collision matrix.
    /// </summary>
    public class EnemyDamager : MonoBehaviour
    {
        public int Damage = 10;
        public AttackTypes AttackType = AttackTypes.Spell;
        /// <summary>Hit direction in degrees (0 right, 90 up, 180 left, 270 down). Negative = away from this object.</summary>
        public float Direction = -1f;
        /// <summary>Seconds before the same enemy can be hit again; 0 = once per activation.</summary>
        public float RehitInterval = 0f;
        public float MagnitudeMultiplier = 1f;
        /// <summary>Optional: only hit while this returns true (e.g. while the hero is dashing).</summary>
        public System.Func<bool> Gate;

        private const int EnemyMask = (1 << 11) | (1 << 17) | (1 << 19); // Enemies, Attack (some breakables), Interactive Object
        private readonly Dictionary<HealthManager, float> _hitTimes = new Dictionary<HealthManager, float>();
        private readonly List<Collider2D> _results = new List<Collider2D>(16);
        private Collider2D[] _colliders;
        private ContactFilter2D _filter;

        private void Awake()
        {
            _colliders = GetComponents<Collider2D>();
            _filter = new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = EnemyMask };
        }

        private void OnEnable() => _hitTimes.Clear();

        /// <summary>Forget who was hit, so a reused hitbox can hit everyone again.</summary>
        public void ResetHits() => _hitTimes.Clear();

        private void FixedUpdate()
        {
            if (Gate != null && !Gate()) return;
            if (_colliders == null || _colliders.Length == 0) _colliders = GetComponents<Collider2D>();
            foreach (var col in _colliders)
            {
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                _results.Clear();
                col.OverlapCollider(_filter, _results);
                foreach (var other in _results) TryHit(other);
            }
        }

        // Bullets that physically bump into an enemy (and burst) still deal their damage.
        private void OnCollisionEnter2D(Collision2D c)
        {
            if (Gate == null || Gate()) TryHit(c.collider);
        }

        private void TryHit(Collider2D other)
        {
            if (other == null) return;
            var hm = other.GetComponentInParent<HealthManager>();
            if (hm == null || hm.isDead) return;
            if (HeroController.instance != null && hm.gameObject == HeroController.instance.gameObject) return;

            if (_hitTimes.TryGetValue(hm, out float last))
            {
                if (RehitInterval <= 0f || Time.time - last < RehitInterval) return;
            }
            _hitTimes[hm] = Time.time;

            float dir = Direction;
            if (dir < 0f) dir = hm.transform.position.x >= transform.position.x ? 0f : 180f;

            hm.Hit(new HitInstance
            {
                Source = gameObject,
                AttackType = AttackType,
                CircleDirection = false,
                DamageDealt = Damage,
                Direction = dir,
                IgnoreInvulnerable = false,
                MagnitudeMultiplier = MagnitudeMultiplier,
                MoveAngle = 0f,
                MoveDirection = false,
                Multiplier = TauntBuff.Take(),
                SpecialType = SpecialTypes.None,
                IsExtraDamage = false
            });

            // Boss bullets pass through enemies physically; make them burst on the enemy they hit.
            var bullet = GetComponent<EnemyBullet>();
            if (bullet != null) bullet.OrbitShieldHit(hm.transform);
        }

        /// <summary>One-shot area damage without a persistent object.</summary>
        public static void HitCircle(Vector2 center, float radius, int damage, AttackTypes type, GameObject source)
        {
            var seen = new HashSet<HealthManager>();
            foreach (var col in Physics2D.OverlapCircleAll(center, radius, EnemyMask))
            {
                var hm = col.GetComponentInParent<HealthManager>();
                if (hm == null || hm.isDead || !seen.Add(hm)) continue;
                if (HeroController.instance != null && hm.gameObject == HeroController.instance.gameObject) continue;
                hm.Hit(new HitInstance
                {
                    Source = source,
                    AttackType = type,
                    DamageDealt = damage,
                    Direction = hm.transform.position.x >= center.x ? 0f : 180f,
                    MagnitudeMultiplier = 1f,
                    Multiplier = TauntBuff.Take(),
                    SpecialType = SpecialTypes.None
                });
            }
        }
    }
}
