using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools.Crucible
{
    /// <summary>
    /// Pictures of the placed Crucible from fixed viewpoints (Edit mode, Zone01 open), for checking an import by eye against
    /// Fable's renders. Writes PNGs to the folder named by -crucibleShots (default Library/CrucibleShots). Not a lighting proof:
    /// the day cycle only runs in Play.
    /// </summary>
    public static class CrucibleShots
    {
        static readonly (string name, Vector3 eye, Vector3 at)[] Views =
        {
            ("01_heart_overview", new Vector3(0f, 95f, 150f), new Vector3(0f, -7f, 0f)),
            ("02_covenant_yard", new Vector3(-100f, 22f, 30f), new Vector3(-145f, 2f, 0f)),
            ("03_hegemony_gate_out", new Vector3(80f, 8f, 0f), new Vector3(118f, 4f, 0f)),
            ("04_null_yard", new Vector3(0f, 25f, 100f), new Vector3(0f, 2f, 145f)),
            ("05_sealed_north", new Vector3(0f, 12f, -70f), new Vector3(0f, 8f, -118f)),
            ("06_dais_close", new Vector3(18f, -3f, 18f), new Vector3(0f, -6f, 0f)),
            ("07_panorama_out", new Vector3(0f, 30f, 0f), new Vector3(400f, 40f, 0f)),
        };

        [MenuItem("Krytical Nova/World/Crucible/Take Check Pictures")]
        public static void TakeMenu() => Debug.Log(Take(null));

        public static void TakeBatch()
        {
            string folder = null;
            string[] a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-crucibleShots") folder = a[i + 1];
            Debug.Log(Take(folder));
            EditorApplication.Exit(0);
        }

        public static string Take(string folder)
        {
            folder = string.IsNullOrEmpty(folder) ? "Library/CrucibleShots" : folder;
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.OpenScene(CrucibleBuilder.Zone01Path, OpenSceneMode.Single);
            GameObject root = null;
            foreach (GameObject r in scene.GetRootGameObjects()) if (r.name == CrucibleBuilder.RootName) root = r;
            if (root == null) return "FAIL: no Crucible root in Zone01";
            var camGo = new GameObject("[Crucible check camera]") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1200f;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            int n = 0;
            foreach (var v in Views)
            {
                Vector3 eye = root.transform.TransformPoint(v.eye), at = root.transform.TransformPoint(v.at);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(folder, v.name + ".png"), tex.EncodeToPNG());
                n++;
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            return $"[Crucible] {n} check pictures in {Path.GetFullPath(folder)}";
        }
    }
}
