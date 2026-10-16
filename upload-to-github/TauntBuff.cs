using UnityEngine;

namespace PlayableVessel
{
    /// <summary>
    /// Silksong's taunt reward (HeroController.SilkTaunted / DamageEnemies): for 6 seconds after a
    /// taunt, the next hit deals 1.5x damage, and that hit uses the bonus up.
    /// </summary>
    internal static class TauntBuff
    {
        public const float Duration = 6f;      // Silksong: TAUNT_EFFECT_DURATION
        public const float DamageMult = 1.5f;  // Silksong: TAUNT_EFFECT_DAMAGE_MULT

        private static float _until;

        public static bool Active => Time.time < _until;

        public static void Grant() => _until = Time.time + Duration;

        public static void Clear() => _until = 0f;

        /// <summary>Damage multiplier for the hit being dealt now (consumes the bonus if active).</summary>
        public static float Take()
        {
            if (!Active) return 1f;
            _until = 0f;
            return DamageMult;
        }
    }
}
