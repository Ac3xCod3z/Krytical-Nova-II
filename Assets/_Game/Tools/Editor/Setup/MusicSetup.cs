using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Audio;
using KryticalNova.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The music the game keeps time to (Cadence, design 17). The sound-scout skill bakes the picked tracks into
    /// <see cref="MusicFolder"/> and writes what it measured of each (beats a minute, first beat, first beat of the
    /// bar) and which zone plays which into its music.json; this makes the <see cref="MusicTrack"/> and
    /// <see cref="ZoneMusic"/> assets from that, and puts the conductor and the player of a zone's fight music into
    /// the open scene. Safe to run again: the measured numbers are written each time, a volume or a fade tuned on
    /// an asset is kept. The same manifest names the menus' music (<see cref="MenuMusic"/>) and each zone's
    /// walking-about music (<see cref="ZoneMusic.explore"/>); the music-gen skill's bake.py writes those.
    /// </summary>
    public static class MusicSetup
    {
        public const string MusicFolder = "Assets/_Game/Audio/Music";
        public const string DataFolder = "Assets/_Game/Audio/Data/Assets";
        private const string ManifestPath = ".claude/skills/sound-scout/music.json";
        private const string PlayerName = "Combat Music";
        private const string ClickName = "Beat Click (GM)";
        private const string ExploreName = "Explore Music";
        public static string MenuMusicPath => $"{DataFolder}/MenuMusic.asset";

        [Serializable]
        private sealed class Manifest
        {
            public List<TrackRow> tracks = new List<TrackRow>();
            public List<ZoneRow> zones = new List<ZoneRow>();
            public MenuRow menu;
        }

        [Serializable]
        private sealed class MenuRow
        {
            public string title, titleOutro;
            public string selectHegemony, selectNull, selectCovenant;
            public string coatHegemony, coatNull, coatCovenant;
        }

        [Serializable]
        private sealed class TrackRow
        {
            public string name;
            public string title;
            public string file;
            public double bpm;
            public double first_beat;
            public int bar_beat;
            public int beats_per_bar = 4;
        }

        [Serializable]
        private sealed class ZoneRow
        {
            public string name;
            public List<string> tracks = new List<string>();
            public List<string> explore = new List<string>();
        }

        public static string TrackPath(string name) => $"{DataFolder}/MusicTrack_{name}.asset";
        public static string ZonePath(string name) => $"{DataFolder}/ZoneMusic_{name}.asset";

        /// <summary>Makes or updates every track and zone asset the manifest lists. Returns how many tracks have their clip.</summary>
        [MenuItem("Krytical Nova/Setup/Music", priority = 13)]
        public static int Build()
        {
            if (!File.Exists(ManifestPath))
            {
                Debug.LogWarning($"[Music] No {ManifestPath}: bake music with the sound-scout skill first (music_bake.py).");
                return 0;
            }
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            AssetDatabase.Refresh();
            AudioSetup.ApplyImportSettings();
            WeatherVfxBuilder.EnsureFolder(DataFolder);

            int ready = 0;
            var byName = new Dictionary<string, MusicTrack>();
            foreach (TrackRow row in manifest.tracks)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{MusicFolder}/{row.file}");
                if (clip == null) Debug.LogWarning($"[Music] {row.name}: no clip at {MusicFolder}/{row.file}.");
                else ready++;

                var track = AssetDatabase.LoadAssetAtPath<MusicTrack>(TrackPath(row.name));
                if (track == null)
                {
                    track = ScriptableObject.CreateInstance<MusicTrack>();
                    AssetDatabase.CreateAsset(track, TrackPath(row.name));
                }
                track.clip = clip;
                track.title = string.IsNullOrEmpty(row.title) ? row.name : row.title;
                track.beatsPerMinute = row.bpm;
                track.firstBeatSeconds = row.first_beat;
                track.barBeat = Mathf.Max(0, row.bar_beat);
                track.beatsPerBar = Mathf.Max(1, row.beats_per_bar);
                EditorUtility.SetDirty(track);
                byName[row.name] = track;
            }

            foreach (ZoneRow row in manifest.zones)
            {
                var zone = AssetDatabase.LoadAssetAtPath<ZoneMusic>(ZonePath(row.name));
                if (zone == null)
                {
                    zone = ScriptableObject.CreateInstance<ZoneMusic>();
                    AssetDatabase.CreateAsset(zone, ZonePath(row.name));
                }
                zone.combat = Tracks(byName, row.tracks, row.name);
                zone.explore = Tracks(byName, row.explore, row.name);
                EditorUtility.SetDirty(zone);
            }

            if (manifest.menu != null && !string.IsNullOrEmpty(manifest.menu.title))
            {
                var menu = AssetDatabase.LoadAssetAtPath<MenuMusic>(MenuMusicPath);
                if (menu == null)
                {
                    menu = ScriptableObject.CreateInstance<MenuMusic>();
                    AssetDatabase.CreateAsset(menu, MenuMusicPath);
                }
                MenuRow m = manifest.menu;
                menu.title = Track(byName, m.title);
                menu.titleOutro = Track(byName, m.titleOutro);
                menu.selectHegemony = Track(byName, m.selectHegemony);
                menu.selectNull = Track(byName, m.selectNull);
                menu.selectCovenant = Track(byName, m.selectCovenant);
                menu.coatHegemony = Track(byName, m.coatHegemony);
                menu.coatNull = Track(byName, m.coatNull);
                menu.coatCovenant = Track(byName, m.coatCovenant);
                EditorUtility.SetDirty(menu);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Music] {ready} of {manifest.tracks.Count} track(s) have their clip; {manifest.zones.Count} zone(s).");
            return ready;
        }

        private static MusicTrack[] Tracks(Dictionary<string, MusicTrack> byName, List<string> names, string zone)
        {
            var list = new List<MusicTrack>();
            if (names == null) return list.ToArray();
            foreach (string name in names)
            {
                if (byName.TryGetValue(name, out MusicTrack track)) list.Add(track);
                else Debug.LogWarning($"[Music] Zone {zone} names a track the manifest does not have: {name}.");
            }
            return list.ToArray();
        }

        private static MusicTrack Track(Dictionary<string, MusicTrack> byName, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (byName.TryGetValue(name, out MusicTrack track)) return track;
            Debug.LogWarning($"[Music] The menu names a track the manifest does not have: {name}.");
            return null;
        }

        /// <summary>
        /// Gives the open scene its fight music: the conductor beside the world's clock (it rides the same networked
        /// object) and, under it, what plays the music on a player's own game. Returns false when the scene has no
        /// World Environment or the zone has no music asset.
        /// </summary>
        public static bool EnsureInScene(string zoneName)
        {
            var music = AssetDatabase.LoadAssetAtPath<ZoneMusic>(ZonePath(zoneName));
            var env = UnityEngine.Object.FindFirstObjectByType<WorldEnvironmentNetwork>(FindObjectsInactive.Include);
            if (music == null || env == null)
            {
                Debug.LogWarning($"[Music] Not wired: {(music == null ? "no " + ZonePath(zoneName) : "the scene has no World Environment")}.");
                return false;
            }

            GameObject host = env.gameObject;
            if (!host.TryGetComponent(out MusicConductor conductor)) conductor = host.AddComponent<MusicConductor>();
            var conductorSo = new SerializedObject(conductor);
            conductorSo.FindProperty("music").objectReferenceValue = music;
            conductorSo.ApplyModifiedPropertiesWithoutUndo();

            AudioMixer mixer = AudioSetup.EnsureMixer();
            AudioMixerGroup[] groups = mixer != null ? mixer.FindMatchingGroups("Music") : new AudioMixerGroup[0];
            AudioMixerGroup group = groups.Length > 0 ? groups[0] : null;

            Transform playerAt = host.transform.Find(PlayerName);
            if (playerAt == null)
            {
                playerAt = new GameObject(PlayerName).transform;
                playerAt.SetParent(host.transform, false);
            }
            Transform clickAt = playerAt.Find(ClickName);
            if (clickAt == null)
            {
                clickAt = new GameObject(ClickName).transform;
                clickAt.SetParent(playerAt, false);
            }
            AudioSource source = Flat(playerAt.gameObject, group, 1f);
            AudioSource click = Flat(clickAt.gameObject, group, 0.5f);

            if (!playerAt.TryGetComponent(out CombatMusicPlayer player)) player = playerAt.gameObject.AddComponent<CombatMusicPlayer>();
            var playerSo = new SerializedObject(player);
            playerSo.FindProperty("conductor").objectReferenceValue = conductor;
            playerSo.FindProperty("source").objectReferenceValue = source;
            playerSo.FindProperty("clickSource").objectReferenceValue = click;
            playerSo.ApplyModifiedPropertiesWithoutUndo();

            // The walking-about music beside it; it plays nothing in a zone that has none.
            Transform exploreAt = playerAt.Find(ExploreName);
            if (exploreAt == null)
            {
                exploreAt = new GameObject(ExploreName).transform;
                exploreAt.SetParent(playerAt, false);
            }
            AudioSource exploreSource = Flat(exploreAt.gameObject, group, 0f);
            if (!exploreAt.TryGetComponent(out ExploreMusicPlayer explore)) explore = exploreAt.gameObject.AddComponent<ExploreMusicPlayer>();
            var exploreSo = new SerializedObject(explore);
            exploreSo.FindProperty("conductor").objectReferenceValue = conductor;
            exploreSo.FindProperty("source").objectReferenceValue = exploreSource;
            exploreSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(host.scene);
            return true;
        }

        /// <summary>A source that plays in the ears, not in the world, and only when told to.</summary>
        private static AudioSource Flat(GameObject on, AudioMixerGroup group, float volume)
        {
            if (!on.TryGetComponent(out AudioSource source)) source = on.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = volume;
            source.priority = 0;
            source.outputAudioMixerGroup = group;
            return source;
        }
    }
}
