using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Factions;
using KryticalNova.Housing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools.Housing
{
    /// <summary>
    /// Pictures for choosing by: each prop, furnishing, Facade or room drawn alone on a plain ground with a figure
    /// the height of a person (1.8 m) beside it, one PNG a subject, numbered in the order given. The pictures are
    /// written outside the project and put together into labelled sheets afterwards; nothing here is game content.
    /// </summary>
    public static class HousingSheets
    {
        public const float PersonHeight = 1.8f;
        private const int Pixels = 512;
        private static readonly Color Backdrop = new Color(0.13f, 0.14f, 0.17f, 1f);
        private static readonly Color GroundColour = new Color(0.24f, 0.26f, 0.3f);
        private static readonly Color PersonColour = new Color(1f, 0.55f, 0.1f);

        private sealed class Rig : IDisposable
        {
            public Scene scene;
            public Camera camera;
            public RenderTexture target;
            public Material ground, person;
            public readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

            public void Dispose()
            {
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                foreach (UnityEngine.Object thing in made) if (thing != null) UnityEngine.Object.DestroyImmediate(thing);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Rig MakeRig(int width, int height)
        {
            var rig = new Rig { scene = EditorSceneManager.NewPreviewScene() };
            rig.target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var holder = new GameObject("Sheet Camera");
            SceneManager.MoveGameObjectToScene(holder, rig.scene);
            rig.camera = holder.AddComponent<Camera>();
            rig.camera.scene = rig.scene;
            rig.camera.cameraType = CameraType.Game;
            rig.camera.clearFlags = CameraClearFlags.SolidColor;
            rig.camera.backgroundColor = Backdrop;
            rig.camera.fieldOfView = 30f;
            rig.camera.allowHDR = false;
            var data = holder.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = true;

            Light(rig.scene, new Vector3(48f, -30f, 0f), 1.25f, new Color(1f, 0.97f, 0.92f), shadows: true);
            Light(rig.scene, new Vector3(20f, 140f, 0f), 0.55f, new Color(0.78f, 0.86f, 1f), shadows: false);
            Light(rig.scene, new Vector3(-10f, 200f, 0f), 0.35f, Color.white, shadows: false);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            rig.ground = new Material(lit);
            rig.ground.SetColor("_BaseColor", GroundColour);
            rig.ground.SetFloat("_Smoothness", 0f);
            rig.person = new Material(lit);
            rig.person.SetColor("_BaseColor", PersonColour);
            rig.person.SetFloat("_Smoothness", 0f);
            rig.made.Add(rig.ground);
            rig.made.Add(rig.person);
            return rig;
        }

        private static void Light(Scene scene, Vector3 euler, float intensity, Color colour, bool shadows)
        {
            var holder = new GameObject("Sheet Light");
            SceneManager.MoveGameObjectToScene(holder, scene);
            holder.transform.rotation = Quaternion.Euler(euler);
            var light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = colour;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        // A figure the height of a person: a body and a head, nothing more.
        private static GameObject Person(Rig rig, Vector3 feet)
        {
            var figure = new GameObject("Person");
            SceneManager.MoveGameObjectToScene(figure, rig.scene);
            figure.transform.position = feet;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(figure.transform, false);
            body.transform.localScale = new Vector3(0.42f, (PersonHeight - 0.26f) * 0.5f, 0.28f);
            body.transform.localPosition = new Vector3(0f, (PersonHeight - 0.26f) * 0.5f, 0f);
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(figure.transform, false);
            head.transform.localScale = Vector3.one * 0.26f;
            head.transform.localPosition = new Vector3(0f, PersonHeight - 0.13f, 0f);
            foreach (Renderer renderer in figure.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = rig.person;
            foreach (Collider collider in figure.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
            return figure;
        }

        private static GameObject Ground(Rig rig, Vector3 middle, float span)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, rig.scene);
            ground.transform.position = new Vector3(middle.x, -0.05f, middle.z);
            ground.transform.localScale = new Vector3(span, 0.1f, span);
            ground.GetComponent<Renderer>().sharedMaterial = rig.ground;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            return ground;
        }

        private static Bounds BoundsOf(GameObject subject)
        {
            bool any = false;
            Bounds box = default;
            foreach (Renderer renderer in subject.GetComponentsInChildren<Renderer>())
            {
                if (!HousingLotBuilder.IsMesh(renderer)) continue;
                if (any) box.Encapsulate(renderer.bounds); else box = renderer.bounds;
                any = true;
            }
            return any ? box : new Bounds(subject.transform.position, Vector3.one);
        }

        // Looks at a box from the front and a little to the side and above, far enough to hold all of it.
        private static void Frame(Camera camera, Bounds box, float yawDegrees, float pitchDegrees, float margin = 1.15f)
        {
            Vector3 direction = Quaternion.Euler(pitchDegrees, yawDegrees, 0f) * Vector3.forward;
            float radius = Mathf.Max(box.extents.magnitude, 0.2f) * margin;
            float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.transform.position = box.center - direction * distance;
            camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            camera.nearClipPlane = Mathf.Max(0.05f, distance - radius * 2f);
            camera.farClipPlane = distance + radius * 4f + 50f;
        }

        private static void Shoot(Rig rig, string file)
        {
            var request = new RenderPipeline.StandardRequest { destination = rig.target };
            if (RenderPipeline.SupportsRenderRequest(rig.camera, request)) RenderPipeline.SubmitRenderRequest(rig.camera, request);
            else { rig.camera.targetTexture = rig.target; rig.camera.Render(); rig.camera.targetTexture = null; }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rig.target;
            var read = new Texture2D(rig.target.width, rig.target.height, TextureFormat.RGB24, false);
            read.ReadPixels(new Rect(0, 0, rig.target.width, rig.target.height), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, read.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(read);
        }

        private static string Numbered(string folder, int index) => Path.Combine(folder, (index + 1).ToString("000") + ".png");

        /// <summary>
        /// Draws each of a list of prefabs alone, with a person beside it, and writes its size beside the pictures
        /// (sizes.tsv: number, name, width, height, depth). <paramref name="turn"/> turns each model first.
        /// </summary>
        public static int Props(string folder, IReadOnlyList<string> prefabPaths, float turn = 0f, Material wear = null)
        {
            var sizes = new System.Text.StringBuilder();
            using (Rig rig = MakeRig(Pixels, Pixels))
            {
                for (int n = 0; n < prefabPaths.Count; n++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[n]);
                    if (prefab == null) continue;
                    GameObject subject = UnityEngine.Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(subject, rig.scene);
                    subject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, turn, 0f));
                    if (wear != null)
                        foreach (Renderer renderer in subject.GetComponentsInChildren<Renderer>())
                            if (HousingLotBuilder.IsMesh(renderer)) renderer.sharedMaterial = wear;
                    Bounds box = BoundsOf(subject);
                    subject.transform.position -= new Vector3(box.center.x, box.min.y, box.center.z);
                    box = BoundsOf(subject);
                    One(rig, box, Numbered(folder, n));
                    sizes.Append(n + 1).Append('\t').Append(prefab.name).Append('\t').Append(box.size.x.ToString("0.00")).Append('\t')
                        .Append(box.size.y.ToString("0.00")).Append('\t').Append(box.size.z.ToString("0.00")).Append('\n');
                    UnityEngine.Object.DestroyImmediate(subject);
                }
            }
            File.WriteAllText(Path.Combine(folder, "sizes.tsv"), sizes.ToString());
            return prefabPaths.Count;
        }

        // The subject is in the scene, standing on the origin: a ground under it, a person at its front left, a picture.
        private static void One(Rig rig, Bounds box, string file, float yaw = 205f, float pitch = 22f)
        {
            GameObject person = Person(rig, new Vector3(box.min.x - 0.6f, 0f, box.min.z - 0.4f));
            Bounds all = box;
            all.Encapsulate(new Bounds(person.transform.position + Vector3.up * PersonHeight * 0.5f, new Vector3(0.5f, PersonHeight, 0.5f)));
            GameObject ground = Ground(rig, all.center, Mathf.Max(all.size.x, all.size.z) * 3f + 4f);
            Frame(rig.camera, all, yaw, pitch);
            Shoot(rig, file);
            UnityEngine.Object.DestroyImmediate(person);
            UnityEngine.Object.DestroyImmediate(ground);
        }

        /// <summary>Draws every furnishing of a faction as it is set down in the game: held to the measure, turned, with a person.</summary>
        public static int Furnishings(string folder, FactionId faction)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FurnishingCatalog>(FurnishingCatalogBuilder.CatalogPath);
            var own = new List<FurnishingDefinition>();
            catalog.ForFaction(faction, own);
            var names = new System.Text.StringBuilder();
            using (Rig rig = MakeRig(Pixels, Pixels))
            {
                for (int n = 0; n < own.Count; n++)
                {
                    GameObject subject = HomeView.Spawn(own[n], null, 0, pickable: false);
                    if (subject == null) continue;
                    SceneManager.MoveGameObjectToScene(subject, rig.scene);
                    Bounds box = BoundsOf(subject);
                    subject.transform.position -= new Vector3(box.center.x, box.min.y, box.center.z);
                    box = BoundsOf(subject);
                    // What hangs on a wall is looked at from its front, which is its +z.
                    One(rig, box, Numbered(folder, n), own[n].mount == FurnishingMount.Wall ? 160f : 205f);
                    names.Append(n + 1).Append('\t').Append(own[n].DisplayName).Append('\t').Append(box.size.x.ToString("0.00")).Append('\t')
                        .Append(box.size.y.ToString("0.00")).Append('\t').Append(box.size.z.ToString("0.00")).Append('\t')
                        .Append(own[n].kind).Append('\t').Append(own[n].modelScale.ToString("0.00")).Append('\t').Append(own[n].blocks ? "blocks" : "-").Append('\n');
                    UnityEngine.Object.DestroyImmediate(subject);
                }
            }
            File.WriteAllText(Path.Combine(folder, "sizes.tsv"), names.ToString());
            return own.Count;
        }

        /// <summary>
        /// Draws Facades as they stand on a Lot's pad: the pad, the door's place marked on it, a person at the door.
        /// Two pictures each: from the gate, and from above.
        /// </summary>
        public static int Facades(string folder, IReadOnlyList<HallLookDefinition> looks, Vector2 padSize)
        {
            using (Rig rig = MakeRig(Pixels * 2, Pixels))
            {
                for (int n = 0; n < looks.Count; n++)
                {
                    var anchor = new GameObject("Anchor");
                    SceneManager.MoveGameObjectToScene(anchor, rig.scene);
                    GameObject facade = PlayerHousing.StandFacade(looks[n], anchor.transform);
                    if (facade == null) { UnityEngine.Object.DestroyImmediate(anchor); continue; }
                    GameObject pad = Ground(rig, Vector3.zero, 1f);
                    pad.transform.localScale = new Vector3(padSize.x, 0.12f, padSize.y);
                    pad.GetComponent<Renderer>().sharedMaterial = rig.person;
                    GameObject person = Person(rig, new Vector3(0f, 0f, -padSize.y * 0.5f - 0.6f));
                    GameObject ground = Ground(rig, Vector3.zero, 90f);
                    ground.transform.position += Vector3.down * 0.05f;

                    Bounds box = BoundsOf(facade);
                    box.Encapsulate(new Bounds(Vector3.zero, new Vector3(padSize.x, 0.2f, padSize.y + 2f)));
                    Frame(rig.camera, box, 20f, 14f, 1.0f); // from the gate: the camera looks towards +z
                    Shoot(rig, Path.Combine(folder, (n + 1).ToString("000") + "a.png"));
                    Frame(rig.camera, box, 0f, 89f, 1.0f);
                    Shoot(rig, Path.Combine(folder, (n + 1).ToString("000") + "b.png"));

                    UnityEngine.Object.DestroyImmediate(anchor);
                    UnityEngine.Object.DestroyImmediate(pad);
                    UnityEngine.Object.DestroyImmediate(person);
                    UnityEngine.Object.DestroyImmediate(ground);
                }
            }
            return looks.Count;
        }

        /// <summary>
        /// Draws the room inside a Hall wearing a set of Finishes, seen as the game shows it: from above the front
        /// wall, looking in, with the ceiling and the near walls out of the way. A second picture looks up from the
        /// floor to show the ceiling.
        /// </summary>
        public static void Room(string file, HousingSettings settings, FactionId faction, HallLookDefinition wall, HallLookDefinition floor, HallLookDefinition ceiling)
        {
            using (Rig rig = MakeRig(Pixels * 2, Pixels))
            {
                HallRoom room = HallRoom.Create(Vector3.zero, Quaternion.identity, settings, faction);
                SceneManager.MoveGameObjectToScene(room.gameObject, rig.scene);
                room.Wear(HomeLook.Floor, floor);
                room.Wear(HomeLook.Wall, wall);
                room.Wear(HomeLook.Ceiling, ceiling);
                GameObject person = Person(rig, new Vector3(0f, 0f, -room.Size.y * 0.25f));

                // The front wall and the ceiling are what the game would have taken out of the way.
                Hide(room.transform, "Wall Front");
                Hide(room.transform, "Ceiling Finish");
                Hide(room.transform, "Surround");
                var box = new Bounds(new Vector3(0f, room.Height * 0.4f, 0f), new Vector3(room.Size.x, room.Height * 0.8f, room.Size.y));
                Frame(rig.camera, box, 0f, 32f, 0.82f);
                Shoot(rig, file);

                Show(room.transform, "Ceiling Finish");
                rig.camera.fieldOfView = 70f;
                rig.camera.transform.position = new Vector3(0f, 1.6f, -room.Size.y * 0.5f + 1.2f);
                rig.camera.transform.rotation = Quaternion.Euler(-28f, 0f, 0f);
                rig.camera.nearClipPlane = 0.1f;
                rig.camera.farClipPlane = 200f;
                Shoot(rig, file.Replace(".png", "_up.png"));
                rig.camera.fieldOfView = 30f;

                UnityEngine.Object.DestroyImmediate(person);
                UnityEngine.Object.DestroyImmediate(room.gameObject);
            }
        }

        private static void Hide(Transform room, string child)
        {
            Transform found = room.Find(child);
            if (found == null) return;
            foreach (Renderer renderer in found.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        private static void Show(Transform room, string child)
        {
            Transform found = room.Find(child);
            if (found == null) return;
            foreach (Renderer renderer in found.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = ShadowCastingMode.On;
        }
    }
}
