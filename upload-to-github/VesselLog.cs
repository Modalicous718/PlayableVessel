using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GlobalEnums;
using Modding;
using UnityEngine;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace PlayableVessel
{
    /// <summary>
    /// The mod's own log: Mods/PlayableVessel/logs/PlayableVessel.log (the previous session is kept as
    /// PlayableVessel.prev.log). Records what the mod does in game — settings, room changes, the vessel's
    /// moves, the music menu, the HUD — and, with "DebugHits" on, every swing, what the nail touched, every
    /// hit on an enemy, and swings that hit nothing (with the nearest enemy's position). Errors thrown from
    /// the mod's code are copied here too.
    /// </summary>
    internal static class VesselLog
    {
        private static StreamWriter _w;
        private static readonly object Lock = new object();
        private static bool _hitHooks;

        internal static string Dir => Path.Combine(PlayableVessel.ModDir, "logs");
        internal static string FilePath => Path.Combine(Dir, "PlayableVessel.log");

        public static void Start()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string prev = Path.Combine(Dir, "PlayableVessel.prev.log");
                if (File.Exists(FilePath)) { if (File.Exists(prev)) File.Delete(prev); File.Move(FilePath, prev); }
                _w = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception e)
            {
                PlayableVessel.Instance.LogError("Could not open the mod log: " + e.Message);
                return;
            }
            Write("log", $"Playable Vessel {PlayableVessel.Instance.GetVersion()} — game {Constants.GAME_VERSION}, Unity {Application.unityVersion}");
            Application.logMessageReceived += OnUnityLog;
            USceneManager.activeSceneChanged += (a, b) => Write("scene", $"{a.name} -> {b.name}");
            ApplyHitLogging(PlayableVessel.GS.DebugHits);
        }

        public static void Write(string category, string message)
        {
            if (_w == null) return;
            string line = $"[{DateTime.Now:HH:mm:ss.fff} f{Time.frameCount}] {category,-7} {message}";
            lock (Lock) { try { _w.WriteLine(line); } catch { } }
        }

        /// <summary>One-line snapshot of the hero, for context.</summary>
        public static string Hero()
        {
            var hc = HeroController.instance;
            if (hc == null) return "hero=none";
            var s = hc.cState;
            return $"pos=({hc.transform.position.x:0.00},{hc.transform.position.y:0.00}) facingR={s.facingRight} ground={s.onGround} " +
                   $"input={hc.acceptingInput} ctrlLost={hc.controlReqlinquished} atk={s.attacking} dash={s.dashing} cast={s.casting} " +
                   $"wall={s.wallSliding} recoil={s.recoiling} invuln={s.invulnerable}";
        }

        private static void OnUnityLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (stack == null || !stack.Contains("PlayableVessel")) return;
            Write("ERROR", condition + "\n" + stack);
        }

        // ------------------------------------------------------------ hit logging

        public static void ApplyHitLogging(bool on)
        {
            if (on == _hitHooks) return;
            _hitHooks = on;
            if (on)
            {
                ModHooks.AttackHook += OnAttack;
                ModHooks.SlashHitHook += OnSlashHit;
                On.HealthManager.Hit += OnHmHit;
            }
            else
            {
                ModHooks.AttackHook -= OnAttack;
                ModHooks.SlashHitHook -= OnSlashHit;
                On.HealthManager.Hit -= OnHmHit;
            }
            Write("log", "hit logging " + (on ? "on" : "off"));
        }

        private static int _swing;
        private static int _contacts;

        private static void OnAttack(AttackDirection dir)
        {
            _swing++;
            _contacts = 0;
            var hc = HeroController.instance;
            Write("swing", $"#{_swing} {dir} {Hero()}");
            if (hc != null) hc.StartCoroutine(AfterSwing(_swing));
        }

        /// <summary>When a swing ends without touching anything, say where the nearest enemy was.</summary>
        private static System.Collections.IEnumerator AfterSwing(int id)
        {
            yield return new WaitForSeconds(0.4f);
            if (id != _swing || _contacts > 0) yield break;
            var hc = HeroController.instance;
            if (hc == null) yield break;
            HealthManager best = null;
            float bestD = 8f;
            foreach (var col in Physics2D.OverlapCircleAll(hc.transform.position, 8f, 1 << 11))
            {
                var hm = col.GetComponentInParent<HealthManager>();
                if (hm == null || hm.isDead) continue;
                float d = Vector2.Distance(hm.transform.position, hc.transform.position);
                if (d < bestD) { bestD = d; best = hm; }
            }
            Write("miss", best == null
                ? $"#{id} touched nothing (no enemy within 8)"
                : $"#{id} touched nothing; nearest '{best.name}' dx={best.transform.position.x - hc.transform.position.x:0.00} dy={best.transform.position.y - hc.transform.position.y:0.00} (facingR={hc.cState.facingRight})");
        }

        private static void OnSlashHit(Collider2D other, GameObject slash)
        {
            if (other == null) return;
            _contacts++;
            var hm = other.GetComponentInParent<HealthManager>();
            Write("touch", $"#{_swing} {slash?.name} -> '{other.name}' layer={LayerMask.LayerToName(other.gameObject.layer)}" + (hm != null ? $" enemy='{hm.name}'" : ""));
        }

        private static void OnHmHit(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hit)
        {
            int before = self.hp;
            bool inv = self.IsInvincible;
            orig(self, hit);
            Write("hit", $"'{self.name}' {hit.AttackType} dmg={hit.DamageDealt}x{hit.Multiplier:0.##} from '{hit.Source?.name}' hp {before}->{self.hp}" + (inv ? " (invincible)" : "") + (self.hp == before ? " NO DAMAGE" : ""));
        }
    }
}
