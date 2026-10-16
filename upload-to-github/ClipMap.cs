using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PlayableVessel
{
    /// <summary>How to show one Knight clip on the vessel puppet.</summary>
    internal struct ClipChoice
    {
        public string Clip;
        /// <summary>&gt;0: play at native fps times this and never retime to the Knight's clip length.</summary>
        public float FpsMultiplier;
    }

    /// <summary>
    /// Maps the Knight's animation clip names (from HeroAnimationController and the Knight FSMs) to
    /// boss clip names. Each entry lists candidates; the first that exists in the boss library wins.
    /// A candidate may carry "*N" to play at N times its own speed instead of being stretched to the
    /// Knight's clip length. Clip names come from the dumped libraries (Hollow Knight Boss Anim /
    /// Hollow Knight Prime Anim). Note the HK library spells it "Dstab Stomp", the PV one "DStab Stomp".
    /// </summary>
    internal static class ClipMap
    {
        private static readonly Dictionary<string, string[]> Common = new Dictionary<string, string[]>
        {
            // Standing / looking
            ["Idle"] = new[] { "Idle" }, ["Idle Hurt"] = new[] { "Idle" }, ["Idle Wind"] = new[] { "Idle" },
            ["Lantern Idle"] = new[] { "Idle" },
            // Look up = roar at the sky; look down = kneel (custom clips, see BossSource.BuildCustomClips)
            ["LookUp"] = new[] { "Vessel LookUp" }, ["Lookup"] = new[] { "Vessel LookUp" },
            ["LookUpEnd"] = new[] { "RoarToIdle" }, ["LookUpToIdle"] = new[] { "RoarToIdle" },
            ["LookDown"] = new[] { "Vessel Kneel" }, ["LookDownEnd"] = new[] { "StunToIdle" }, ["LookDownToIdle"] = new[] { "StunToIdle" },
            ["TurnToIdle"] = new[] { "Idle" }, ["Map Idle"] = new[] { "Idle" },
            ["Turn"] = new[] { "Turn" }, ["Map Turn"] = new[] { "Turn" },

            // Moving: the HK boss walks; Pure Vessel has no walk clip, so it uses the custom "Vessel Walk".
            ["Run"] = new[] { "Walk*1.6", "Vessel Walk*1.5" }, ["Walk"] = new[] { "Walk", "Vessel Walk" },
            ["Sprint"] = new[] { "Walk*1.9", "Vessel Walk*1.8" }, ["Lantern Run"] = new[] { "Walk*1.6", "Vessel Walk*1.5" },
            ["Map Walk"] = new[] { "Walk", "Vessel Walk" },
            ["Run To Idle"] = new[] { "Recover" }, ["Dash To Idle"] = new[] { "Dash Recover", "Recover" },

            // Air
            ["Airborne"] = new[] { "Jump*0.8" }, ["Fall"] = new[] { "Jump*0.8" },
            ["Double Jump"] = new[] { "Evade", "Jump" }, ["Walljump"] = new[] { "Jump" },
            ["Wall Slide"] = new[] { "Vessel Cling", "Stun Air" },
            ["Land"] = new[] { "Recover" }, ["HardLand"] = new[] { "DStab Land" }, ["Super Hard Land"] = new[] { "DStab Land" },

            // Dashes
            ["Dash"] = new[] { "Dash*1" }, ["Shadow Dash"] = new[] { "Dash*1" }, ["Shadow Dash Sharp"] = new[] { "Dash*1" },
            ["Dash Down"] = new[] { "Dstab Stomp", "DStab Stomp" }, ["Shadow Dash Down"] = new[] { "Dstab Stomp", "DStab Stomp" },
            ["Shadow Dash Down Sharp"] = new[] { "Dstab Stomp", "DStab Stomp" }, ["Dash Down Land"] = new[] { "DStab Land" },
            ["Back Dash"] = new[] { "Evade" }, ["Back Dash 2"] = new[] { "Evade" }, ["Back Dash 3"] = new[] { "Evade" },
            ["Backdash Land 1"] = new[] { "Evade Recover", "Recover" }, ["Backdash Land 2"] = new[] { "Evade Recover", "Recover" },

            // Nail (side slashes are driven by the combo sequencer; these are fallbacks)
            ["Slash"] = new[] { "Slash1" }, ["SlashAlt"] = new[] { "Slash2" }, ["Wall Slash"] = new[] { "Slash3" },
            ["UpSlash"] = new[] { "Slash3" }, ["DownSlash"] = new[] { "Dstab Stomp", "DStab Stomp" },

            // Nail arts and dream nail
            ["NA Charge"] = new[] { "Slash1 Antic" }, ["NA Big Slash"] = new[] { "Slash3" },
            ["NA Dash Slash"] = new[] { "Dash*1" }, ["NA Cyclone Start"] = new[] { "Slashes", "Slash1" },
            ["NA Cyclone"] = new[] { "Slashes*1.6", "Slash2" }, ["NA Cyclone End"] = new[] { "Slash3 Recover" },
            ["DN Charge"] = new[] { "Slash1 Antic" }, ["DN Start"] = new[] { "Slash1 Antic" },
            ["DN Slash Antic"] = new[] { "Slash1 Antic" }, ["DN Slash"] = new[] { "Slash1" }, ["DN Cancel"] = new[] { "Recover" },

            // Hurt
            ["Recoil"] = new[] { "Stun HitUp" }, ["Stun"] = new[] { "Stun Air" },
            ["Death"] = new[] { "Death Collapse", "Stun Land" }, ["Spike Death"] = new[] { "Death Collapse", "Stun Land" },
            ["Acid Death"] = new[] { "Death Collapse", "Stun Land" }, ["Death Dream"] = new[] { "Tele Out" },

            // Healing / spells (spells are normally replaced by vessel abilities; these cover fallbacks)
            ["Focus"] = new[] { "Focus Antic", "Roar Init" }, ["Focus Get"] = new[] { "Focus Burst", "Roar" },
            ["Focus Get Once"] = new[] { "Focus Burst", "Roar" }, ["Focus End"] = new[] { "RoarToIdle" },
            ["Fireball Antic"] = new[] { "SmallShot Antic" }, ["Fireball1 Cast"] = new[] { "SmallShot" },
            ["Fireball2 Cast"] = new[] { "SmallShot" },
            ["Quake Antic"] = new[] { "DStab Antic" }, ["Quake Fall"] = new[] { "Dstab Stomp", "DStab Stomp" },
            ["Quake Fall 2"] = new[] { "Dstab Stomp", "DStab Stomp" }, ["Quake Land"] = new[] { "DStab Land" },
            ["Quake Land 2"] = new[] { "DStab Land" },
            ["Scream Start"] = new[] { "Roar Antic" }, ["Scream"] = new[] { "Roar" }, ["Scream 2"] = new[] { "Roar" },
            ["Scream End"] = new[] { "RoarToIdle" }, ["Scream End 2"] = new[] { "RoarToIdle" },
            ["Roar Lock"] = new[] { "Counter Block" },

            // Crystal heart
            ["SD Charge Ground"] = new[] { "Dash Antic" }, ["SD Wall Charge"] = new[] { "Dash Antic" },
            ["SD Dash"] = new[] { "Dash*1" }, ["SD Air Brake"] = new[] { "Dash Recover" },
            ["SD Hit Wall"] = new[] { "Stun HitUp" }, ["SD Charge Ground End"] = new[] { "Dash Recover" },

            // Respawns, benches, doors
            ["Hazard Respawn"] = new[] { "Tele In" }, ["Respawn Wake"] = new[] { "Tele In" },
            ["Wake Up Ground"] = new[] { "Stun Land" }, ["Prostrate Rise"] = new[] { "Stun Land" },
            ["Enter"] = new[] { "Idle" }, ["Exit"] = new[] { "Idle" }, ["Exit Door To Idle"] = new[] { "Idle" },
            ["Sit"] = new[] { "Counter Antic" }, ["Sit Idle"] = new[] { "Counter Ready" }, ["Sitting Asleep"] = new[] { "Counter Ready" },
            ["Sit Lean"] = new[] { "Counter Ready" }, ["Get Off"] = new[] { "Counter End" }, ["Wake"] = new[] { "Counter End" },
            ["Sit Fall Asleep"] = new[] { "Counter Ready" }, ["Wake To Sit"] = new[] { "Counter Ready" },
            ["DG Warp Charge"] = new[] { "Tele Out" }, ["DG Warp"] = new[] { "Tele Out" }, ["DG Warp In"] = new[] { "Tele In" },
        };

        private static readonly Dictionary<VesselForm, Dictionary<string, ClipChoice>> Resolved = new Dictionary<VesselForm, Dictionary<string, ClipChoice>>();

        public static ClipChoice Resolve(BossSource src, string heroClip)
        {
            if (!Resolved.TryGetValue(src.Form, out var cache))
                Resolved[src.Form] = cache = new Dictionary<string, ClipChoice>();
            if (string.IsNullOrEmpty(heroClip)) heroClip = "Idle";
            if (cache.TryGetValue(heroClip, out var hit)) return hit;

            ClipChoice? result = null;
            if (Common.TryGetValue(heroClip, out var cands))
            {
                foreach (var c in cands)
                {
                    var choice = Parse(c);
                    if (Usable(src, choice.Clip)) { result = choice; break; }
                }
            }
            if (result == null && Usable(src, heroClip)) result = new ClipChoice { Clip = heroClip };
            if (result == null) result = new ClipChoice { Clip = GuessByKeyword(heroClip, src) };

            cache[heroClip] = result.Value;
            PlayableVessel.Instance.LogDebug($"[{src.Form}] clip '{heroClip}' -> '{result.Value.Clip}'");
            return result.Value;
        }

        private static ClipChoice Parse(string spec)
        {
            int star = spec.IndexOf('*');
            if (star < 0) return new ClipChoice { Clip = spec };
            return new ClipChoice
            {
                Clip = spec.Substring(0, star),
                FpsMultiplier = float.Parse(spec.Substring(star + 1), CultureInfo.InvariantCulture)
            };
        }

        public static bool Usable(BossSource src, string clip)
        {
            var c = src.Clip(clip);
            return c != null && c.frames.Length > 0;
        }

        private static string GuessByKeyword(string heroClip, BossSource src)
        {
            string h = heroClip.ToLowerInvariant();
            string[] keys =
                h.Contains("dash") ? new[] { "Dash" } :
                h.Contains("slash") ? new[] { "Slash1" } :
                h.Contains("fireball") || h.Contains("cast") ? new[] { "SmallShot" } :
                h.Contains("scream") ? new[] { "Roar" } :
                h.Contains("quake") ? new[] { "DStab Land" } :
                h.Contains("focus") ? new[] { "Focus Antic", "Roar Init" } :
                h.Contains("jump") || h.Contains("fall") || h.Contains("air") ? new[] { "Jump" } :
                h.Contains("land") ? new[] { "Recover" } :
                h.Contains("run") || h.Contains("walk") ? new[] { "Walk", "Dash" } :
                h.Contains("death") ? new[] { "Death Collapse", "Stun Land" } :
                new[] { "Idle" };
            return keys.FirstOrDefault(k => Usable(src, k)) ?? "Idle";
        }

        public static void Clear() => Resolved.Clear();
    }
}
