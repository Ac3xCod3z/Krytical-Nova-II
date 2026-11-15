using System;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// How temperatures are shown (owner, 15 Nov 2026: "im in america we use fahrenheit not celsius"): degrees Fahrenheit by default,
    /// Celsius as an option (Settings ▸ Accessibility ▸ UNITS), kept on this PC. The game works in Celsius everywhere; this is for
    /// display only.
    /// </summary>
    public static class TemperatureUnits
    {
        const string Key = "kn.units.celsius";

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Changed = null;

        /// <summary>True: show °C; false (the default): show °F.</summary>
        public static bool Celsius
        {
            get => PlayerPrefs.GetInt(Key, 0) != 0;
            set
            {
                if (value == Celsius) return;
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static float ToFahrenheit(float celsius) => celsius * 9f / 5f + 32f;

        /// <summary>A Celsius value in the chosen unit, as a number.</summary>
        public static float Shown(float celsius) => Celsius ? celsius : ToFahrenheit(celsius);

        /// <summary>"46°F" (or "8°C"); <paramref name="unit"/> false gives "46°".</summary>
        public static string Format(float celsius, bool unit = true) =>
            Mathf.RoundToInt(Shown(celsius)) + (unit ? (Celsius ? "°C" : "°F") : "°");
    }
}
