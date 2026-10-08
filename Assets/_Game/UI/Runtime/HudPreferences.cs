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
        private static readonly string PetChatterKey = Prefix + "petChatter";
        private static readonly string MinimapRotatesKey = Prefix + "minimapRotates";
        private static readonly string ScreenShakeKey = Prefix + "screenShake";
        private static readonly string MenuThemeKey = Prefix + "menuTheme";
        private static readonly string DamageNumbersKey = Prefix + "damageNumbers";
        private static readonly string RefuseTradesKey = Prefix + "refuse.trade";
        private static readonly string RefuseContactsKey = Prefix + "refuse.contact";
        private static readonly string RefuseCrewKey = Prefix + "refuse.crew";
        private static readonly string RefuseSquadKey = Prefix + "refuse.squad";
        private static readonly string RefuseCalloutsKey = Prefix + "refuse.callout";
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

        /// <summary>The bubbles a pet reacts with: its lines, or its emoji. Off = pets stay quiet.</summary>
        public static bool PetChatter
        {
            get => GetBool(PetChatterKey, true);
            set => SetBool(PetChatterKey, value);
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

        /// <summary>The numbers that float off a hit.</summary>
        public static bool DamageNumbers
        {
            get => GetBool(DamageNumbersKey, true);
            set => SetBool(DamageNumbersKey, value);
        }

        /// <summary>A request to trade is declined for the player, without being shown.</summary>
        public static bool RefuseTrades
        {
            get => GetBool(RefuseTradesKey, false);
            set => SetBool(RefuseTradesKey, value);
        }

        /// <summary>A request to become a contact is declined for the player, without being shown.</summary>
        public static bool RefuseContacts
        {
            get => GetBool(RefuseContactsKey, false);
            set => SetBool(RefuseContactsKey, value);
        }

        /// <summary>An invitation to a Crew is declined for the player, without being shown.</summary>
        public static bool RefuseCrewInvites
        {
            get => GetBool(RefuseCrewKey, false);
            set => SetBool(RefuseCrewKey, value);
        }

        /// <summary>An invitation to a Squad is declined for the player, without being shown.</summary>
        public static bool RefuseSquadInvites
        {
            get => GetBool(RefuseSquadKey, false);
            set => SetBool(RefuseSquadKey, value);
        }

        /// <summary>A Callout (a duel request) is declined for the player, without being shown.</summary>
        public static bool RefuseCallouts
        {
            get => GetBool(RefuseCalloutsKey, false);
            set => SetBool(RefuseCalloutsKey, value);
        }

        /// <summary>Every option of the Gameplay page that is kept here back to what the game ships with.</summary>
        public static void ResetGameplayToDefaults()
        {
            foreach (string key in new[] { DamageNumbersKey, RefuseTradesKey, RefuseContactsKey, RefuseCrewKey, RefuseSquadKey, RefuseCalloutsKey })
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Which faction's skin the menus wear: one picked at random each start of the game, or always the same.</summary>
        public static MenuTheme MenuTheme
        {
            get => (MenuTheme)Mathf.Clamp(PlayerPrefs.GetInt(MenuThemeKey, (int)MenuTheme.Rotate), 0, 3);
            set
            {
                if (MenuTheme == value) return;
                PlayerPrefs.SetInt(MenuThemeKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
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

        /// <summary>
        /// Every option of the Interface page back to what the game ships with. The HUD layout, waypoints and the
        /// chat's size are not options: they are left as they are.
        /// </summary>
        public static void ResetToDefaults()
        {
            foreach (string key in new[]
                     {
                         MoveMarkerKey, TargetRingKey, ControlsHintKey, NameplatesKey, OwnNamecardKey, PetChatterKey, MinimapRotatesKey,
                         ScreenShakeKey, MenuThemeKey, SnapToGridKey, GridSizeKey, InterfaceScaleKey
                     })
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Changed?.Invoke();
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
