using System.Globalization;
using KryticalNova.World;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for testing the sky features (rainbow, moon phases, 22° halo, heat shimmer, lens
    /// flare, lightning). The overrides only change this machine's presentation (nothing is networked), but like the
    /// other world commands they are limited to the host/offline player. sky.reset restores natural behaviour.
    /// </summary>
    public static class SkyCommands
    {
        private const string NoWorld = "No WorldEnvironmentNetwork in the scene.";
        private const string NoPresenter = "No EnvironmentPresenter in the scene.";
        private const string NoAuthority = "Only the host/offline player can use sky commands.";

        private static EnvironmentPresenter cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => cached = null;

        private static bool TryGetPresenter(out EnvironmentPresenter presenter, out string error)
        {
            presenter = null;
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) { error = NoWorld; return false; }
            if (!env.HasWorldAuthority) { error = NoAuthority; return false; }
            if (cached == null) cached = Object.FindFirstObjectByType<EnvironmentPresenter>();
            presenter = cached;
            error = presenter == null ? NoPresenter : null;
            return presenter != null;
        }

        private static bool IsOff(string arg) => arg == "off" || arg == "auto" || arg == "natural";

        [Command("sky.rainbow", "Force the rainbow: sky.rainbow [0-1|off]. Needs a sun below ~42° to be visible.")]
        private static string Rainbow(string value = "1")
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            value = value.Trim().ToLowerInvariant();
            if (IsOff(value)) { p.SetRainbowOverride(-1f); return "Rainbow back to natural (after rain)."; }
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float s)) return "Usage: sky.rainbow [0-1|off]";
            s = Mathf.Clamp01(s);
            p.SetRainbowOverride(s);
            var env = WorldEnvironmentNetwork.Instance;
            float sunElevation = env != null && env.DayCycle != null ? env.DayCycle.SunDirection(env.TimeOfDay01).y : 0f;
            string hint = sunElevation <= 0f ? " (the sun is down: try time.set 8 or 17)"
                        : sunElevation > 0.67f ? " (the sun is too high, the bow is below the horizon: try time.set 8 or 17)" : "";
            return $"Rainbow forced to {s:0.00}. Look away from the sun.{hint}";
        }

        [Command("sky.moonphase", "Force the moon phase: sky.moonphase <0-1|new|first|full|last|off> (0 new, 0.5 full).")]
        private static string MoonPhase(string value)
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            value = value.Trim().ToLowerInvariant();
            float phase;
            switch (value)
            {
                case "new": phase = 0f; break;
                case "waxingcrescent": case "crescent": phase = 0.125f; break;
                case "first": case "firstquarter": phase = 0.25f; break;
                case "gibbous": case "waxinggibbous": phase = 0.375f; break;
                case "full": phase = 0.5f; break;
                case "waninggibbous": phase = 0.625f; break;
                case "last": case "lastquarter": phase = 0.75f; break;
                case "waningcrescent": phase = 0.875f; break;
                default:
                    if (IsOff(value)) { p.SetMoonPhaseOverride(-1f); return "Moon phase follows the world clock again."; }
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out phase)) return "Usage: sky.moonphase <0-1|new|first|full|last|off>";
                    break;
            }
            p.SetMoonPhaseOverride(phase);
            return $"Moon phase {PhaseName(phase)} ({phase:0.00}, {EnvironmentPresenter.MoonIllumination(phase):P0} lit). Night: time.midnight";
        }

        [Command("sky.moon.next", "Step the moon to the next of the 8 phases.")]
        private static string MoonNext()
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            // Step from the forced phase when there is one: MoonPhase01 only catches up on the presenter's next frame.
            float current = p.MoonPhaseOverride >= 0f ? p.MoonPhaseOverride : p.MoonPhase01;
            float next = (Mathf.Floor(current * 8f + 0.01f) + 1f) / 8f;
            return MoonPhase(Mathf.Repeat(next, 1f).ToString("0.###", CultureInfo.InvariantCulture));
        }

        [Command("sky.halo", "Force the 22° halo: sky.halo [sun|moon|both|off] [strength 0-1].")]
        private static string Halo(string which = "sun", float strength = 0.6f)
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            which = which.Trim().ToLowerInvariant();
            strength = Mathf.Clamp01(strength);
            switch (which)
            {
                case "sun": p.SetHaloOverride(strength, 0f); return $"Sun halo {strength:0.00}. Needs the sun up (time.noon).";
                case "moon": p.SetHaloOverride(0f, strength); return $"Moon halo {strength:0.00}. Needs night and a bright moon (time.midnight, sky.moonphase full).";
                case "both": p.SetHaloOverride(strength, strength); return $"Sun and moon halos {strength:0.00}.";
                default:
                    if (!IsOff(which)) return "Usage: sky.halo [sun|moon|both|off] [strength]";
                    p.SetHaloOverride(-1f, -1f);
                    return "Halo back to natural (rare: thin high cloud, roughly 1 day in 15).";
            }
        }

        [Command("sky.shimmer", "Heat shimmer multiplier: sky.shimmer <mult|off> (1 = clear midday, 2-3 = exaggerated).")]
        private static string Shimmer(string value)
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            value = value.Trim().ToLowerInvariant();
            if (IsOff(value)) { p.SetShimmerOverride(-1f); return "Shimmer back to natural (weather and time driven)."; }
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float m) || m < 0f) return "Usage: sky.shimmer <mult|off>";
            p.SetShimmerOverride(m);
            return $"Heat shimmer ×{m:0.##}.";
        }

        [Command("sky.flare", "Sun lens flare strength: sky.flare <0-4|off>.")]
        private static string Flare(string value)
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            value = value.Trim().ToLowerInvariant();
            if (IsOff(value)) { p.SetFlareOverride(-1f); return "Lens flare back to the inspector strength."; }
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float s) || s < 0f) return "Usage: sky.flare <0-4|off>";
            p.SetFlareOverride(Mathf.Min(s, 4f));
            return $"Lens flare strength {Mathf.Min(s, 4f):0.##}.";
        }

        [Command("sky.lightning", "Strike lightning near you right now (local flash + bolt).")]
        private static string Lightning()
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            if (p.Lightning == null) return "The presenter has no LightningController.";
            p.Lightning.ForceStrike();
            return "Lightning!";
        }

        [Command("sky.reset", "Clear every sky override.")]
        private static string Reset()
        {
            if (!TryGetPresenter(out var p, out var error)) return error;
            p.ClearOverrides();
            return "Sky overrides cleared.";
        }

        [Command("sky.status", "Print time, day, moon phase and the rainbow / halo / moisture levels.")]
        private static string Status()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            if (cached == null) cached = Object.FindFirstObjectByType<EnvironmentPresenter>();
            if (cached == null) return NoPresenter;
            float h = env.TimeOfDay01 * 24f;
            float phase = cached.MoonPhase01;
            var halo = cached.HaloStrength;
            string weather = env.ToWeather != null ? env.ToWeather.name : "none";
            return $"Day {env.State.DayCount(env.Now)}  {(int)h:00}:{(int)((h % 1f) * 60f):00}  weather {weather}  cirrus {env.Weather.cirrus:0.00}\n" +
                   $"Moon {PhaseName(phase)} ({phase:0.00}, {EnvironmentPresenter.MoonIllumination(phase):P0} lit)\n" +
                   $"Rainbow {cached.RainbowStrength:0.00}  moisture {cached.SkyMoisture:0.00}  halo sun {halo.x:0.00} moon {halo.y:0.00}";
        }

        private static string PhaseName(float phase)
        {
            string[] names = { "new", "waxing crescent", "first quarter", "waxing gibbous", "full", "waning gibbous", "last quarter", "waning crescent" };
            return names[Mathf.RoundToInt(Mathf.Repeat(phase, 1f) * 8f) % 8];
        }
    }
}
