using System;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>Player-facing interface options, persisted per machine in PlayerPrefs.</summary>
    public static class HudPreferences
    {
        private const string Prefix = "kn.hud.";
        private const string MoveMarkerKey = Prefix + "moveMarker";
        private const string TargetRingKey = Prefix + "targetRing";
        private const string ControlsHintKey = Prefix + "controlsHint";
        private const string NameplatesKey = Prefix + "nameplates";
        private const string MinimapRotatesKey = Prefix + "minimapRotates";
        private const string LayoutKey = Prefix + "layout.v1";

        public static event Action Changed;

        public static bool ShowMoveMarker
        {
            get => GetBool(MoveMarkerKey, true);
            set => SetBool(MoveMarkerKey, value);
        }

        public static bool ShowTargetRing
        {
            get => GetBool(TargetRingKey, true);
            set => SetBool(TargetRingKey, value);
        }

        public static bool ShowControlsHint
        {
            get => GetBool(ControlsHintKey, true);
            set => SetBool(ControlsHintKey, value);
        }

        public static bool ShowNameplates
        {
            get => GetBool(NameplatesKey, true);
            set => SetBool(NameplatesKey, value);
        }

        /// <summary>True: the minimap turns with the camera (forward is up). False: fixed north-up.</summary>
        public static bool MinimapRotates
        {
            get => GetBool(MinimapRotatesKey, true);
            set => SetBool(MinimapRotatesKey, value);
        }

        public static string LayoutJson
        {
            get => PlayerPrefs.GetString(LayoutKey, string.Empty);
            set
            {
                if (string.IsNullOrEmpty(value)) PlayerPrefs.DeleteKey(LayoutKey);
                else PlayerPrefs.SetString(LayoutKey, value);
                PlayerPrefs.Save();
            }
        }

        private static bool GetBool(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0;

        private static void SetBool(string key, bool value)
        {
            if (GetBool(key, !value) == value && PlayerPrefs.HasKey(key)) return;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
