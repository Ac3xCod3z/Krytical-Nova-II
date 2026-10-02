using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>Ordered list of weather profiles; the index is what goes over the network (one byte).</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Weather Catalog")]
    public sealed class WeatherCatalog : ScriptableObject
    {
        public const int MaxProfiles = 255;

        [SerializeField] private List<WeatherProfile> profiles = new List<WeatherProfile>();

        public int Count => profiles.Count;

        public WeatherProfile Get(int index)
        {
            if (profiles.Count == 0) return null;
            return profiles[Mathf.Clamp(index, 0, profiles.Count - 1)];
        }

        public int IndexOf(WeatherProfile profile) => profiles.IndexOf(profile);

        /// <summary>Case-insensitive lookup by asset name, with or without the "WP_" prefix.</summary>
        public int IndexOf(string name)
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (p == null) continue;
                if (string.Equals(p.name, name, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.name, "WP_" + name, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        public void SetProfiles(List<WeatherProfile> list)
        {
            profiles = list;
            OnValidate();
        }

        private void OnValidate()
        {
            if (profiles.Count > MaxProfiles) profiles.RemoveRange(MaxProfiles, profiles.Count - MaxProfiles);
        }
    }
}
