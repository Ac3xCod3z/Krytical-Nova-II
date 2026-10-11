#if KN_RECORDER
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KryticalNova.UI.Guide;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.Video;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Records the moving picture of a Guide topic (menu Krytical Nova ▸ Guide ▸ Record Clip): a few seconds of the
    /// running game, silent, 30 frames a second, written to UI/Guide/Clips/GC_&lt;topic&gt;.mp4 and handed to the
    /// topic in the Guide library. The Game view is recorded, as the player sees it (the main camera alone comes out
    /// at the upscaler's low render size, so it is only there for a caller that asks). Needs Play mode and the Unity
    /// Recorder package (editor only: nothing of it is in a build).
    ///
    /// A clip is encoded once for the game (11 Oct 2026). The take is recorded as a master at a high bitrate outside
    /// the project, then cut to the part that matters (<see cref="Crop"/>) and encoded by ffmpeg at a set quality
    /// (<see cref="Quality"/>); the game plays that file as it is, with no second encode on import. On a PC without
    /// ffmpeg the master itself becomes the clip, recorded at <see cref="PlainBitRate"/>, and the topic's focus cuts
    /// the picture on the page as before. <see cref="GuideClipStaging"/> stages and records every topic.
    /// </summary>
    public sealed class GuideClipRecorder : EditorWindow
    {
        public const string ClipFolder = "Assets/_Game/UI/Guide/Clips";
        /// <summary>Where the takes are kept as recorded: outside the project, so nothing imports them.</summary>
        public const string MasterFolder = "Temp/GuideClips";
        private const int Width = 1280, Height = 720, FrameRate = 30;

        /// <summary>Megabits a second of the take when ffmpeg makes the clip from it: far more than the clip keeps.</summary>
        public const float MasterBitRate = 40f;
        /// <summary>Megabits a second of the clip itself on a PC without ffmpeg.</summary>
        public const float PlainBitRate = 6f;
        /// <summary>
        /// The clip's quality as x264 counts it (lower is sharper and larger; 18 is near lossless to the eye, 23 its
        /// default). 27 since 11 Oct 2026, the owner's choice of three shown side by side: the smallest, in which nothing could
        /// be told apart on the page from 21, at under a third of the size.
        /// </summary>
        public const int Quality = 27;
        /// <summary>A clip is never wider or taller than this: the page shows it at half that, and large at about this.</summary>
        public const int LargestWidth = 1280, LargestHeight = 1000;
        /// <summary>A clip of the world (the whole picture, nothing cut) is no wider than this: it has no small text to keep sharp.</summary>
        public const int LargestWorldWidth = 960;

        /// <summary>
        /// The part of the take the clip keeps, as shares of the picture (x and y its top left corner); no size keeps
        /// all of it. Set while the take is being recorded, used once when it ends.
        /// </summary>
        public static Rect Crop;

        private static RecorderController controller;
        private static RecorderControllerSettings controllerSettings;
        private static string recordingTopic, recordingPath, masterPath;
        private static int recordingWidth, recordingHeight;
        // The recorder switches the Game view to the clip's size and leaves it there: the size it had is put back.
        private static int gameViewSizeBefore = -1;

        private int topicIndex;
        private float seconds = 8f;

        public static bool IsRecording => controller != null && controller.IsRecording();
        public static string ClipPath(string topicId) => $"{ClipFolder}/GC_{topicId}.mp4";
        /// <summary>What the last take came to: its size and how it was made, or why there is none.</summary>
        public static string LastResult { get; private set; } = string.Empty;

        [MenuItem("Krytical Nova/Guide/Record Clip")]
        private static void Open() => GetWindow<GuideClipRecorder>(true, "Record Guide Clip");

        private void OnGUI()
        {
            var library = AssetDatabase.LoadAssetAtPath<GuideLibrary>(HudSetup.GuideLibraryPath);
            if (library == null)
            {
                EditorGUILayout.HelpBox("No Guide library at " + HudSetup.GuideLibraryPath, MessageType.Warning);
                return;
            }
            var ids = new List<string>();
            foreach (GuideTopic topic in library.Topics) ids.Add(topic.id);
            if (ids.Count == 0) return;
            topicIndex = EditorGUILayout.Popup("Topic", Mathf.Clamp(topicIndex, 0, ids.Count - 1), ids.ToArray());
            seconds = EditorGUILayout.Slider("Seconds", seconds, 3f, 15f);
            EditorGUILayout.HelpBox(Application.isPlaying
                ? "Set the scene up, press Record, and play it out. The clip is handed to the topic when the time is up. " +
                  "Guide ▸ Record Staged Clips does the setting up for the topics it knows."
                : "Enter Play mode to record.", MessageType.Info);
            if (Ffmpeg() == null)
                EditorGUILayout.HelpBox("ffmpeg was not found on this PC: the clip is kept as recorded, larger and uncut.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || IsRecording))
                if (GUILayout.Button(IsRecording ? "Recording..." : "Record")) Record(ids[topicIndex], seconds, true);
        }

        /// <summary>Starts recording the topic's clip. False (and a reason in the console) when it cannot.</summary>
        public static bool Record(string topicId, float seconds, bool withInterface, int width = Width, int height = Height)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[GuideClip] Enter Play mode to record."); return false; }
            if (IsRecording) { Debug.LogWarning("[GuideClip] A clip is being recorded already."); return false; }
            var library = AssetDatabase.LoadAssetAtPath<GuideLibrary>(HudSetup.GuideLibraryPath);
            if (library == null || library.Find(topicId) == null) { Debug.LogWarning($"[GuideClip] The Guide has no topic '{topicId}'."); return false; }

            Directory.CreateDirectory(ClipFolder);
            Directory.CreateDirectory(MasterFolder);
            gameViewSizeBefore = GameViewSize(-1);
            recordingTopic = topicId;
            recordingPath = ClipPath(topicId);
            masterPath = $"{MasterFolder}/GC_{topicId}_take.mp4";
            recordingWidth = width;
            recordingHeight = height;
            if (File.Exists(masterPath)) File.Delete(masterPath);
            Crop = default;
            LastResult = string.Empty;

            controllerSettings = CreateInstance<RecorderControllerSettings>();
            var movie = CreateInstance<MovieRecorderSettings>();
            movie.name = "Guide Clip";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom,
                TargetBitRate = Ffmpeg() != null ? MasterBitRate : PlainBitRate
            };
            movie.CaptureAudio = false;
            if (withInterface) movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
            else movie.ImageInputSettings = new CameraInputSettings { Source = ImageSource.MainCamera, OutputWidth = width, OutputHeight = height, CaptureUI = false };
            // The recorder adds the extension itself.
            movie.OutputFile = masterPath.Substring(0, masterPath.Length - ".mp4".Length);
            controllerSettings.AddRecorderSettings(movie);
            controllerSettings.SetRecordModeToTimeInterval(0f, seconds);
            controllerSettings.FrameRate = FrameRate;
            controllerSettings.CapFrameRate = true;

            controller = new RecorderController(controllerSettings);
            controller.PrepareRecording();
            if (!controller.StartRecording())
            {
                Debug.LogWarning("[GuideClip] The recorder did not start.");
                controller = null;
                return false;
            }
            EditorApplication.update += Watch;
            Debug.Log($"[GuideClip] Recording {seconds:0.#} s for '{topicId}'.");
            return true;
        }

        /// <summary>
        /// The game plays a clip as it was made: Unity does not encode it again on import, and it has no sound track.
        /// (Until 11 Oct 2026 every clip was encoded a second time at the Low bitrate, which is what made them soft.)
        /// </summary>
        public static void AsRecorded(string clipPath)
        {
            if (!(AssetImporter.GetAtPath(clipPath) is VideoClipImporter importer)) return;
            VideoImporterTargetSettings settings = importer.defaultTargetSettings;
            if (!settings.enableTranscoding && !importer.importAudio) return;
            settings.enableTranscoding = false;
            importer.defaultTargetSettings = settings;
            importer.importAudio = false;
            importer.SaveAndReimport();
        }

        /// <summary>Every clip in the folder is played as recorded. Returns how many were changed.</summary>
        [MenuItem("Krytical Nova/Guide/Play Every Clip As Recorded")]
        public static int AllAsRecorded()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:VideoClip", new[] { ClipFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is VideoClipImporter importer)) continue;
                if (!importer.defaultTargetSettings.enableTranscoding && !importer.importAudio) continue;
                AsRecorded(path);
                changed++;
            }
            Debug.Log($"[GuideClip] {changed} clip(s) set to play as recorded.");
            return changed;
        }

        private static string ffmpegPath;
        private static bool ffmpegLooked;

        /// <summary>Where ffmpeg is on this PC, or null: on the path, or where winget puts it.</summary>
        public static string Ffmpeg()
        {
            if (ffmpegLooked) return ffmpegPath;
            ffmpegLooked = true;
            var folders = new List<string>((System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator));
            string local = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            folders.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
            string packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packages))
                foreach (string package in Directory.GetDirectories(packages, "*FFmpeg*"))
                    foreach (string bin in Directory.GetDirectories(package, "bin", SearchOption.AllDirectories))
                        folders.Add(bin);
            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                try
                {
                    string candidate = Path.Combine(folder.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return ffmpegPath = candidate;
                }
                catch (System.ArgumentException) { }
            }
            return ffmpegPath = null;
        }

        /// <summary>
        /// Makes the clip from the take: the part asked for, no larger than the page can show, at the set quality.
        /// False when ffmpeg is not there or fails; the caller then keeps the take.
        /// </summary>
        private static bool Encode(string take, string clip, Rect crop, int width, int height)
        {
            string ffmpeg = Ffmpeg();
            if (ffmpeg == null) return false;
            bool whole = crop.width < 0.01f || crop.height < 0.01f || (crop.width > 0.999f && crop.height > 0.999f);
            // H.264 wants even sizes.
            int w = Even(whole ? width : crop.width * width), h = Even(whole ? height : crop.height * height);
            int x = whole ? 0 : Mathf.Clamp(Mathf.RoundToInt(crop.x * width), 0, width - w), y = whole ? 0 : Mathf.Clamp(Mathf.RoundToInt(crop.y * height), 0, height - h);
            float fit = Mathf.Min(1f, Mathf.Min((float)(whole ? LargestWorldWidth : LargestWidth) / w, (float)LargestHeight / h));
            int outWidth = Even(w * fit), outHeight = Even(h * fit);
            string filter = string.Format(CultureInfo.InvariantCulture, "crop={0}:{1}:{2}:{3},scale={4}:{5}:flags=lanczos", w, h, x, y, outWidth, outHeight);
            string made = $"{MasterFolder}/GC_{recordingTopic}_clip.mp4";
            if (File.Exists(made)) File.Delete(made);
            var start = new System.Diagnostics.ProcessStartInfo(ffmpeg,
                $"-v error -y -i \"{take}\" -vf \"{filter}\" -an -c:v libx264 -preset veryslow -crf {Quality} -profile:v high -pix_fmt yuv420p -g {FrameRate * 2} -movflags +faststart \"{made}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit(120000);
                if (process.ExitCode != 0 || !File.Exists(made))
                {
                    Debug.LogWarning("[GuideClip] ffmpeg could not make the clip: " + errors);
                    return false;
                }
            }
            File.Copy(made, clip, true);
            File.Delete(made);
            LastResult = $"{outWidth}x{outHeight} by ffmpeg";
            return true;
        }

        private static int Even(float value) => Mathf.Max(2, Mathf.RoundToInt(value) & ~1);

        /// <summary>The Game view's chosen size (its place in the size list); a value of 0 or more chooses that one first.</summary>
        private static int GameViewSize(int choose)
        {
            System.Type type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            System.Reflection.PropertyInfo index = type?.GetProperty("selectedSizeIndex",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Object[] views = type != null ? Resources.FindObjectsOfTypeAll(type) : null;
            if (index == null || views == null || views.Length == 0) return -1;
            if (choose >= 0) index.SetValue(views[0], choose);
            return (int)index.GetValue(views[0]);
        }

        /// <summary>When the time is up: the clip is made from the take, imported to play as it is, and the topic gets it.</summary>
        private static void Watch()
        {
            if (controller != null && controller.IsRecording() && Application.isPlaying) return;
            EditorApplication.update -= Watch;
            controller?.StopRecording();
            controller = null;
            if (gameViewSizeBefore >= 0) GameViewSize(gameViewSizeBefore);
            gameViewSizeBefore = -1;
            // The size is the one asked for: the recorder's own settings can be gone by now.
            int width = recordingWidth, height = recordingHeight;
            if (controllerSettings != null) DestroyImmediate(controllerSettings);
            controllerSettings = null;
            if (!File.Exists(masterPath))
            {
                LastResult = "nothing was recorded";
                Debug.LogWarning($"[GuideClip] Nothing was written to {masterPath}.");
                return;
            }
            bool cut = Encode(masterPath, recordingPath, Crop, width, height);
            if (!cut)
            {
                File.Copy(masterPath, recordingPath, true);
                LastResult = $"{width}x{height} as recorded";
            }
            AssetDatabase.ImportAsset(recordingPath, ImportAssetOptions.ForceSynchronousImport);
            AsRecorded(recordingPath);
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(recordingPath);
            var library = AssetDatabase.LoadAssetAtPath<GuideLibrary>(HudSetup.GuideLibraryPath);
            GuideTopic topic = library != null ? library.Find(recordingTopic) : null;
            if (clip == null || topic == null)
            {
                Debug.LogWarning($"[GuideClip] {recordingPath} was written but could not be handed to '{recordingTopic}'.");
                return;
            }
            topic.clip = clip;
            // A clip that was cut to its part is shown whole; one kept as recorded is cut on the page.
            topic.focus = cut ? default : Crop;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            LastResult += $", {new FileInfo(recordingPath).Length / 1024} KB, {clip.length:0.0} s";
            Debug.Log($"[GuideClip] '{recordingTopic}' has its clip: {recordingPath} ({LastResult}).");
        }
    }
}
#endif
