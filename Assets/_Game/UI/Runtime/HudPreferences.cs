using System;
using System.Globalization;
using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// Player-facing interface options, persisted in PlayerPrefs. One set per machine, or per local profile when
    /// several copies of the game run side by side (a Multiplayer Play Mode clone, a build started with "-profile").
    /// </summary>
    public static class HudPreferences
    {
        private static readonly string Prefix = BuildPrefix();
        private static readonly string MoveMarkerKey = Prefix + "moveMarker";
        private static readonly string TargetRingKey = Prefix + "targetRing";
        private static readonly string ControlsHintKey = Prefix + "controlsHint";
        private static readonly string NameplatesKey = Prefix + "nameplates";
        private static readonly string OwnNamecardKey = Prefix + "ownNamecard";
        private static readonly string MinimapRotatesKey = Prefix + "minimapRotates";
        private static readonly string ScreenShakeKey = Prefix + "screenShake";
        private static readonly string LayoutKey = Prefix + "layout.v1";
        private static readonly string SnapToGridKey = Prefix + "layout.snap";
        private static readonly string GridSizeKey = Prefix + "layout.grid";
        private static readonly string InterfaceScaleKey = Prefix + "scale";
        private static readonly string WaypointKey = Prefix + "waypoint.";
        private static readonly string ChatWidthKey = Prefix + "chat.width";
        private static readonly string ChatHeightKey = Prefix + "chat.height";

        private static string BuildPrefix()
        {
            string profile = NetworkConnectionManager.LocalProfile();
            return string.IsNullOrEmpty(profile) ? "kn.hud." : "kn.hud." + profile + ".";
        }

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

        /// <summary>Names over heads: NPC nameplates and player namecards.</summary>
        public static bool ShowNameplates
        {
            get => GetBool(NameplatesKey, true);
            set => SetBool(NameplatesKey, value);
        }

        /// <summary>The namecard over the player's own head. Other players see it whatever this says.</summary>
        public static bool ShowOwnNamecard
        {
            get => GetBool(OwnNamecardKey, true);
            set => SetBool(OwnNamecardKey, value);
        }

        /// <summary>True: the minimap turns with the camera (forward is up). False: fixed north-up.</summary>
        public static bool MinimapRotates
        {
            get => GetBool(MinimapRotatesKey, true);
            set => SetBool(MinimapRotatesKey, value);
        }

        /// <summary>Camera shake, FOV kicks and screen pulses from GameFeel (accessibility / comfort toggle).</summary>
        public static bool ScreenShake
        {
            get => GetBool(ScreenShakeKey, true);
            set => SetBool(ScreenShakeKey, value);
        }

        /// <summary>The layout editor lines panels up with a grid and with each other while they are dragged.</summary>
        public static bool SnapToGrid
        {
            get => GetBool(SnapToGridKey, true);
            set => SetBool(SnapToGridKey, value);
        }

        public const int DefaultGridSize = 8;
        public static readonly int[] GridSizes = { 4, 8, 16, 32 };

        /// <summary>Grid cell of the layout editor, in panel units.</summary>
        public static int GridSize
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(GridSizeKey, DefaultGridSize), GridSizes[0], GridSizes[GridSizes.Length - 1]);
            set
            {
                if (GridSize == value) return;
                PlayerPrefs.SetInt(GridSizeKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public const float MinInterfaceScale = 0.8f;
        public const float MaxInterfaceScale = 1.3f;

        /// <summary>Size of the whole in-game interface: 1 is the authored size.</summary>
        public static float InterfaceScale
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(InterfaceScaleKey, 1f), MinInterfaceScale, MaxInterfaceScale);
            set
            {
                value = Mathf.Clamp(value, MinInterfaceScale, MaxInterfaceScale);
                if (Mathf.Approximately(InterfaceScale, value)) return;
                PlayerPrefs.SetFloat(InterfaceScaleKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
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

        /// <summary>The player's own map marker for a zone (see <see cref="MapWaypoint"/>).</summary>
        public static bool TryGetWaypoint(string zone, out Vector3 world)
        {
            world = default;
            string[] parts = PlayerPrefs.GetString(WaypointKey + zone, string.Empty).Split(';');
            return parts.Length == 3
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out world.x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out world.y)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out world.z);
        }

        public static void SetWaypoint(string zone, Vector3? world)
        {
            if (world.HasValue)
            {
                Vector3 w = world.Value;
                PlayerPrefs.SetString(WaypointKey + zone, string.Format(CultureInfo.InvariantCulture, "{0:0.##};{1:0.##};{2:0.##}", w.x, w.y, w.z));
            }
            else PlayerPrefs.DeleteKey(WaypointKey + zone);
            PlayerPrefs.Save();
        }

        /// <summary>The size the player dragged the chat window to, in panel units. False until they resize it.</summary>
        public static bool TryGetChatSize(out Vector2 size)
        {
            size = new Vector2(PlayerPrefs.GetFloat(ChatWidthKey, 0f), PlayerPrefs.GetFloat(ChatHeightKey, 0f));
            return size.x > 0f && size.y > 0f;
        }

        public static void SetChatSize(Vector2 size)
        {
            PlayerPrefs.SetFloat(ChatWidthKey, size.x);
            PlayerPrefs.SetFloat(ChatHeightKey, size.y);
            PlayerPrefs.Save();
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
