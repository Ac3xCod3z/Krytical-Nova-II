using System;
using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// How the player likes the camera to answer the mouse, persisted in PlayerPrefs like the other options (one set
    /// per machine, or per local profile). Read by <see cref="PointAndClickPlayer"/> every time the camera is turned;
    /// set from the Settings window's Gameplay page.
    /// </summary>
    public static class CameraPreferences
    {
        public const float MinSensitivity = 0.3f;
        public const float MaxSensitivity = 2f;
        public const float MinZoomSpeed = 0.3f;
        public const float MaxZoomSpeed = 2f;
        /// <summary>The nearest the furthest distance can be brought in: this share of the rig's own.</summary>
        public const float MinFurthest = 0.4f;

        private static readonly string Prefix = BuildPrefix();
        private static readonly string SensitivityKey = Prefix + "sensitivity";
        private static readonly string InvertKey = Prefix + "invertY";
        private static readonly string ZoomSpeedKey = Prefix + "zoomSpeed";
        private static readonly string FurthestKey = Prefix + "furthest";

        private static string BuildPrefix()
        {
            string profile = NetworkConnectionManager.LocalProfile();
            return string.IsNullOrEmpty(profile) ? "kn.camera." : "kn.camera." + profile + ".";
        }

        public static event Action Changed;

        /// <summary>How fast the camera turns with the mouse: 1 is the speed the game ships with.</summary>
        public static float Sensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
            set
            {
                value = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
                if (Mathf.Approximately(Sensitivity, value)) return;
                PlayerPrefs.SetFloat(SensitivityKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>Dragging the mouse up tilts the camera down instead of up.</summary>
        public static bool InvertVertical
        {
            get => PlayerPrefs.GetInt(InvertKey, 0) != 0;
            set
            {
                if (InvertVertical == value) return;
                PlayerPrefs.SetInt(InvertKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>How fast the mouse wheel moves the camera in and out: 1 is the speed the game ships with.</summary>
        public static float ZoomSpeed
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(ZoomSpeedKey, 1f), MinZoomSpeed, MaxZoomSpeed);
            set
            {
                value = Mathf.Clamp(value, MinZoomSpeed, MaxZoomSpeed);
                if (Mathf.Approximately(ZoomSpeed, value)) return;
                PlayerPrefs.SetFloat(ZoomSpeedKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>How far the camera may pull back, as a share of the furthest the game allows (1 is all of it).</summary>
        public static float Furthest
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(FurthestKey, 1f), MinFurthest, 1f);
            set
            {
                value = Mathf.Clamp(value, MinFurthest, 1f);
                if (Mathf.Approximately(Furthest, value)) return;
                PlayerPrefs.SetFloat(FurthestKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static void ResetToDefaults()
        {
            foreach (string key in new[] { SensitivityKey, InvertKey, ZoomSpeedKey, FurthestKey }) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ what the options do to the rig's numbers

        /// <summary>What a movement of the mouse turns the camera by, with the player's options: x to the side, y up.</summary>
        public static Vector2 Turn(Vector2 mouse, float rigSensitivity, bool rigInverted)
            => Turn(mouse, rigSensitivity, rigInverted, Sensitivity, InvertVertical);

        public static Vector2 Turn(Vector2 mouse, float rigSensitivity, bool rigInverted, float sensitivity, bool invert)
        {
            Vector2 turn = mouse * (rigSensitivity * sensitivity);
            // Not inverted, dragging the mouse up tilts the camera down over the character (the rig's own way).
            if (rigInverted == invert) turn.y = -turn.y;
            return turn;
        }

        /// <summary>The furthest the camera pulls back, in metres, between the rig's nearest and furthest.</summary>
        public static float FurthestDistance(float rigNearest, float rigFurthest) => FurthestDistance(rigNearest, rigFurthest, Furthest);

        public static float FurthestDistance(float rigNearest, float rigFurthest, float share)
            => Mathf.Max(rigNearest, rigFurthest * Mathf.Clamp(share, MinFurthest, 1f));
    }
}
