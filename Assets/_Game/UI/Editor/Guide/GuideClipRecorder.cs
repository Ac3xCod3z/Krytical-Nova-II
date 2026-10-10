#if KN_RECORDER
using System.Collections.Generic;
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
    /// running game, silent, 960 by 540 (larger when asked, for a clip of which only a part is shown) at 30 frames a second, written to UI/Guide/Clips/GC_&lt;topic&gt;.mp4 and
    /// handed to the topic in the Guide library. With the interface, the Game view is recorded as the player sees
    /// it; without, the main camera alone, so no faction's HUD is in the picture. Needs Play mode and the Unity
    /// Recorder package (editor only: nothing of it is in a build).
    /// </summary>
    public sealed class GuideClipRecorder : EditorWindow
    {
        public const string ClipFolder = "Assets/_Game/UI/Guide/Clips";
        private const int Width = 960, Height = 540, FrameRate = 30;

        private static RecorderController controller;
        private static RecorderControllerSettings controllerSettings;
        private static string recordingTopic, recordingPath;
        // The recorder switches the Game view to the clip's size and leaves it there: the size it had is put back.
        private static int gameViewSizeBefore = -1;

        private int topicIndex;
        private float seconds = 8f;
        private bool withInterface = true;

        public static bool IsRecording => controller != null && controller.IsRecording();
        public static string ClipPath(string topicId) => $"{ClipFolder}/GC_{topicId}.mp4";

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
            withInterface = EditorGUILayout.Toggle(new GUIContent("With the interface", "Off: the main camera alone, with no HUD in the picture."), withInterface);
            EditorGUILayout.HelpBox(Application.isPlaying
                ? "Set the scene up, press Record, and play it out. The clip is handed to the topic when the time is up."
                : "Enter Play mode to record.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || IsRecording))
                if (GUILayout.Button(IsRecording ? "Recording..." : "Record")) Record(ids[topicIndex], seconds, withInterface);
        }

        /// <summary>Starts recording the topic's clip. False (and a reason in the console) when it cannot.</summary>
        public static bool Record(string topicId, float seconds, bool withInterface, int width = Width, int height = Height)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[GuideClip] Enter Play mode to record."); return false; }
            if (IsRecording) { Debug.LogWarning("[GuideClip] A clip is being recorded already."); return false; }
            var library = AssetDatabase.LoadAssetAtPath<GuideLibrary>(HudSetup.GuideLibraryPath);
            if (library == null || library.Find(topicId) == null) { Debug.LogWarning($"[GuideClip] The Guide has no topic '{topicId}'."); return false; }

            Directory.CreateDirectory(ClipFolder);
            gameViewSizeBefore = GameViewSize(-1);
            recordingTopic = topicId;
            recordingPath = ClipPath(topicId);

            controllerSettings = CreateInstance<RecorderControllerSettings>();
            var movie = CreateInstance<MovieRecorderSettings>();
            movie.name = "Guide Clip";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Medium
            };
            movie.CaptureAudio = false;
            if (withInterface) movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
            else movie.ImageInputSettings = new CameraInputSettings { Source = ImageSource.MainCamera, OutputWidth = width, OutputHeight = height, CaptureUI = false };
            // The recorder adds the extension itself.
            movie.OutputFile = recordingPath.Substring(0, recordingPath.Length - ".mp4".Length);
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
        /// A build carries Unity's own copy of a clip, not the file as recorded: that copy is made at a low bitrate
        /// and without a sound track, which is less than half the size and still sharp enough to read a window.
        /// </summary>
        public static void Slim(string clipPath)
        {
            if (!(AssetImporter.GetAtPath(clipPath) is VideoClipImporter importer)) return;
            VideoImporterTargetSettings settings = importer.defaultTargetSettings;
            if (settings.enableTranscoding && settings.bitrateMode == VideoBitrateMode.Low && !importer.importAudio) return;
            settings.enableTranscoding = true;
            settings.codec = VideoCodec.H264;
            settings.bitrateMode = VideoBitrateMode.Low;
            settings.resizeMode = VideoResizeMode.OriginalSize;
            settings.spatialQuality = VideoSpatialQuality.HighSpatialQuality;
            importer.defaultTargetSettings = settings;
            importer.importAudio = false;
            importer.SaveAndReimport();
        }

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

        /// <summary>When the time is up: the file is imported, slimmed for the build, and the topic gets it.</summary>
        private static void Watch()
        {
            if (controller != null && controller.IsRecording() && Application.isPlaying) return;
            EditorApplication.update -= Watch;
            controller?.StopRecording();
            controller = null;
            if (gameViewSizeBefore >= 0) GameViewSize(gameViewSizeBefore);
            gameViewSizeBefore = -1;
            if (controllerSettings != null) DestroyImmediate(controllerSettings);
            controllerSettings = null;
            if (!File.Exists(recordingPath))
            {
                Debug.LogWarning($"[GuideClip] Nothing was written to {recordingPath}.");
                return;
            }
            AssetDatabase.ImportAsset(recordingPath, ImportAssetOptions.ForceSynchronousImport);
            Slim(recordingPath);
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(recordingPath);
            var library = AssetDatabase.LoadAssetAtPath<GuideLibrary>(HudSetup.GuideLibraryPath);
            GuideTopic topic = library != null ? library.Find(recordingTopic) : null;
            if (clip == null || topic == null)
            {
                Debug.LogWarning($"[GuideClip] {recordingPath} was written but could not be handed to '{recordingTopic}'.");
                return;
            }
            topic.clip = clip;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            Debug.Log($"[GuideClip] '{recordingTopic}' has its clip: {recordingPath} ({new FileInfo(recordingPath).Length / 1024} KB, {clip.length:0.0} s).");
        }
    }
}
#endif
