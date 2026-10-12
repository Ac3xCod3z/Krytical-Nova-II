using System.Collections.Generic;
using KryticalNova.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace KryticalNova.Housing
{
    /// <summary>A furnishing as it stands in the world: which piece of the Hall it is.</summary>
    [DisallowMultipleComponent]
    public sealed class FurnishingPiece : MonoBehaviour
    {
        public uint Uid { get; set; }
        public FurnishingDefinition Furnishing { get; set; }
        /// <summary>Its light, when it is a lamp.</summary>
        public Light Lamp { get; set; }
        /// <summary>The frame drawn round it while the cursor is on it or it is picked.</summary>
        public LineRenderer Frame { get; set; }

        private bool carried, cutAway;

        /// <summary>Hidden because the Arrange tool is carrying it (its preview is shown in its place).</summary>
        public bool Carried { get => carried; set { carried = value; Apply(); } }
        /// <summary>Hidden because it hangs on a wall the camera is looking through.</summary>
        public bool CutAway { get => cutAway; set { if (cutAway == value) return; cutAway = value; Apply(); } }

        private void Apply()
        {
            bool show = !carried && !cutAway;
            if (gameObject.activeSelf != show) gameObject.SetActive(show);
        }
    }

    /// <summary>
    /// Draws one area of a Hall: a model for every piece the authority holds, kept in step as edits come back.
    /// It shows and never decides. A piece carries a box the Arrange tool picks it by (a trigger: the player's own
    /// clicks pass through it to the ground), and, when it is something that stands in the way, a solid box and a
    /// hole in the walkable ground the size of what a walker would bump into. Lamps are lit a few at a time, the
    /// ones nearest the camera; what hangs on a wall the camera looks through is not drawn.
    /// </summary>
    public sealed class HomeView : MonoBehaviour
    {
        public const string FurnishingLayerName = "Furnishing";
        private const int IgnoreRaycastLayer = 2;
        private const float LightInterval = 0.25f;
        private const float FrameLift = 0.04f, FrameWidth = 0.06f, FramePad = 0.05f;
        private const float CrownGap = 0.5f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly Dictionary<uint, FurnishingPiece> shown = new Dictionary<uint, FurnishingPiece>();
        private readonly List<FurnishingPiece> lamps = new List<FurnishingPiece>();
        private readonly List<FurnishingPiece> hung = new List<FurnishingPiece>();
        private IHomeAuthority authority;
        private HomeArea area;
        private Transform root;
        private MaterialPropertyBlock frameBlock;
        private float nextLightCheck;

        /// <summary>The layer furnishings are on (Ignore Raycast until the project has the layer).</summary>
        public static int FurnishingLayer
        {
            get
            {
                int layer = LayerMask.NameToLayer(FurnishingLayerName);
                return layer >= 0 ? layer : IgnoreRaycastLayer;
            }
        }

        public HomeArea Area => area;
        public Transform Root => root;
        public int Count => shown.Count;

        /// <summary>
        /// Starts drawing an area. With <paramref name="fill"/> off nothing is made yet: whoever binds it then runs
        /// <see cref="FillOverFrames"/>, which is how a Hall is made ready behind the curtain of the trip to it.
        /// </summary>
        public void Bind(IHomeAuthority homeAuthority, HomeArea homeArea, Transform pieceRoot, bool fill = true)
        {
            Unbind();
            authority = homeAuthority;
            area = homeArea;
            root = pieceRoot != null ? pieceRoot : transform;
            if (authority == null) return;
            authority.Changed += OnChanged;
            if (fill) Rebuild();
        }

        /// <summary>Makes the area's pieces a few a frame, so that a full Hall does not arrive as one long frame.</summary>
        public System.Collections.IEnumerator FillOverFrames(int perFrame)
        {
            if (authority == null) yield break;
            Clear();
            var pieces = new List<PlacedPiece>(authority.Home.Area(area));
            perFrame = Mathf.Max(1, perFrame);
            for (int n = 0; n < pieces.Count; n++)
            {
                if (authority == null) yield break;
                Show(pieces[n]);
                if ((n + 1) % perFrame == 0)
                {
                    RefreshPoses();
                    yield return null;
                }
            }
            RefreshPoses();
        }

        private void Unbind()
        {
            if (authority != null) authority.Changed -= OnChanged;
            authority = null;
            Clear();
        }

        private void Clear()
        {
            foreach (FurnishingPiece piece in shown.Values)
                if (piece != null) Destroy(piece.gameObject);
            shown.Clear();
            lamps.Clear();
            hung.Clear();
        }

        private void OnDestroy() => Unbind();

        // Switched off while its player is in the other part of the Hall: nothing of it is lit meanwhile.
        private void OnDisable()
        {
            for (int n = 0; n < lamps.Count; n++)
                if (lamps[n] != null && lamps[n].Lamp != null) lamps[n].Lamp.enabled = false;
        }

        public FurnishingPiece Find(uint uid) => shown.TryGetValue(uid, out FurnishingPiece piece) ? piece : null;

        /// <summary>Shows or hides one piece (the Arrange tool hides the piece it is carrying).</summary>
        public void SetVisible(uint uid, bool visible)
        {
            FurnishingPiece piece = Find(uid);
            if (piece != null) piece.Carried = !visible;
        }

        private void Rebuild()
        {
            Clear();
            foreach (PlacedPiece piece in authority.Home.Area(area)) Show(piece);
            RefreshPoses();
        }

        private void OnChanged(HomeArea changedArea, ushort request, IReadOnlyList<HomeChange> changes)
        {
            if (changedArea != area) return;
            foreach (HomeChange change in changes)
            {
                if (change.hadBefore && !change.hasAfter) Forget(change.before.u);
                else if (change.hasAfter && !change.hadBefore) Show(change.after);
            }
            RefreshPoses();
        }

        private void Forget(uint uid)
        {
            if (!shown.TryGetValue(uid, out FurnishingPiece gone)) return;
            shown.Remove(uid);
            lamps.Remove(gone);
            hung.Remove(gone);
            if (gone != null) Destroy(gone.gameObject);
        }

        private void Show(in PlacedPiece piece)
        {
            FurnishingDefinition furnishing = authority.Catalog != null ? authority.Catalog.Get(piece.i) : null;
            if (furnishing == null || shown.ContainsKey(piece.u)) return; // an item this build does not know stays in the save, undrawn
            GameObject model = Spawn(furnishing, root, FurnishingLayer, pickable: true, solid: true);
            if (model == null) return;
            var view = model.AddComponent<FurnishingPiece>();
            view.Uid = piece.u;
            view.Furnishing = furnishing;
            shown[piece.u] = view;

            if (furnishing.lights)
            {
                view.Lamp = AddLamp(model.transform, furnishing, authority.Settings);
                lamps.Add(view);
            }
            if (furnishing.mount == FurnishingMount.Wall && HomeAreas.IsInside(area)) hung.Add(view);
        }

        /// <summary>Every pose worked out again from the Hall as it is: what stands on a moved piece moved with it.</summary>
        public void RefreshPoses()
        {
            if (authority != null) ShowPoses(authority.Home.Area(area));
        }

        /// <summary>
        /// Draws the pieces where a list says they are. The Arrange tool uses it to show a move before it is asked
        /// for; <see cref="RefreshPoses"/> puts everything back where the Hall has it.
        /// </summary>
        public void ShowPoses(List<PlacedPiece> pieces)
        {
            for (int n = 0; n < pieces.Count; n++)
            {
                if (!shown.TryGetValue(pieces[n].u, out FurnishingPiece view) || view == null) continue;
                HousingLayout.Pose(pieces, pieces[n], out Vector3 position, out Quaternion rotation);
                Transform t = view.transform;
                t.SetPositionAndRotation(root.TransformPoint(position), root.rotation * rotation);
                t.localScale = Vector3.one * pieces[n].Scale;
            }
        }

        // ------------------------------------------------------------------ frames

        /// <summary>Draws or removes the frame round a piece's foot.</summary>
        public void SetFrame(uint uid, bool on, Color colour)
        {
            FurnishingPiece piece = Find(uid);
            if (piece == null) return;
            if (!on)
            {
                if (piece.Frame != null) piece.Frame.enabled = false;
                return;
            }
            if (piece.Frame == null) piece.Frame = MakeFrame(piece);
            if (piece.Frame == null) return;
            piece.Frame.enabled = true;
            frameBlock ??= new MaterialPropertyBlock();
            frameBlock.SetColor(BaseColorId, colour);
            piece.Frame.SetPropertyBlock(frameBlock);
        }

        private LineRenderer MakeFrame(FurnishingPiece piece)
        {
            Material material = authority != null && authority.Settings != null ? authority.Settings.ghostMaterial : null;
            if (material == null) return null;
            var holder = new GameObject("Frame");
            holder.layer = IgnoreRaycastLayer;
            holder.transform.SetParent(piece.transform, false);
            var line = holder.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthMultiplier = FrameWidth;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.TransformZ;
            holder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // the line lies flat on the ground

            Bounds box = piece.Furnishing.LocalBounds;
            float y = box.min.y + FrameLift;
            Vector3 min = box.min - new Vector3(FramePad, 0f, FramePad), max = box.max + new Vector3(FramePad, 0f, FramePad);
            // In the holder's turned space: x stays x, the piece's z is the holder's y, the piece's y is the holder's -z.
            line.positionCount = 4;
            line.SetPosition(0, new Vector3(min.x, min.z, -y));
            line.SetPosition(1, new Vector3(max.x, min.z, -y));
            line.SetPosition(2, new Vector3(max.x, max.z, -y));
            line.SetPosition(3, new Vector3(min.x, max.z, -y));
            return line;
        }

        // ------------------------------------------------------------------ lamps, and walls looked through

        private void LateUpdate()
        {
            if (authority == null) return;
            Camera view = Camera.main;
            if (view == null) return;
            Vector3 eye = view.transform.position;

            for (int n = 0; n < hung.Count; n++)
            {
                FurnishingPiece piece = hung[n];
                if (piece == null) continue;
                Transform t = piece.transform;
                // Its face looks out from the wall: seen from behind, the camera is outside that wall.
                piece.CutAway = Vector3.Dot(eye - t.position, t.forward) < 0f;
            }

            if (Time.unscaledTime < nextLightCheck) return;
            nextLightCheck = Time.unscaledTime + LightInterval;
            LightNearest(eye);
        }

        private void LightNearest(Vector3 eye)
        {
            HousingSettings settings = authority.Settings;
            int most = settings == null ? 0 : HomeAreas.IsInside(area) ? settings.interiorLights : WorldClock.IsNight ? settings.lawnLights : 0;
            // The few nearest the camera: a short list, picked by taking the nearest unlit one that many times.
            for (int n = 0; n < lamps.Count; n++)
                if (lamps[n] != null && lamps[n].Lamp != null) lamps[n].Lamp.enabled = false;
            for (int lit = 0; lit < most; lit++)
            {
                FurnishingPiece best = null;
                float bestDistance = float.MaxValue;
                for (int n = 0; n < lamps.Count; n++)
                {
                    FurnishingPiece lamp = lamps[n];
                    if (lamp == null || lamp.Lamp == null || lamp.Lamp.enabled || !lamp.gameObject.activeInHierarchy) continue;
                    float distance = (lamp.transform.position - eye).sqrMagnitude;
                    if (distance < bestDistance) { bestDistance = distance; best = lamp; }
                }
                if (best == null) break;
                best.Lamp.enabled = true;
            }
        }

        private static Light AddLamp(Transform piece, FurnishingDefinition furnishing, HousingSettings settings)
        {
            var holder = new GameObject("Lamp");
            holder.transform.SetParent(piece, false);
            holder.transform.localPosition = furnishing.lightOffset;
            var lamp = holder.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            lamp.range = settings != null ? settings.pieceLightRange : 7f;
            lamp.intensity = settings != null ? settings.pieceLightIntensity : 9f;
            lamp.color = settings != null ? settings.LightColorFor(furnishing.faction) : Color.white;
            lamp.enabled = false;
            return lamp;
        }

        // ------------------------------------------------------------------ making a piece

        /// <summary>
        /// A furnishing's model, ready to stand in the world: the prefab without its own colliders, held to the
        /// world's measure, on the given layer. <paramref name="pickable"/> gives it the box the Arrange tool picks
        /// it by; <paramref name="solid"/> makes it stand in the way when it is the kind of thing that does. The
        /// Arrange tool's preview is made here too, with neither.
        /// </summary>
        public static GameObject Spawn(FurnishingDefinition furnishing, Transform parent, int layer, bool pickable, bool solid = false)
        {
            if (furnishing == null || furnishing.prefab == null) return null;
            var holder = new GameObject(furnishing.name);
            holder.transform.SetParent(parent, false);
            GameObject model = Instantiate(furnishing.prefab, holder.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(furnishing.modelEuler);
            model.transform.localScale = furnishing.prefab.transform.localScale * Mathf.Max(0.01f, furnishing.modelScale);

            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                if (!furnishing.castsShadow) renderer.shadowCastingMode = ShadowCastingMode.Off;
            SetLayer(holder.transform, layer);

            if (pickable)
            {
                var box = holder.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = furnishing.boundsCenter;
                // A rug or a painting is nearly flat: give it enough thickness to be clicked.
                box.size = Vector3.Max(furnishing.boundsSize, Vector3.one * 0.06f);
            }

            if (solid && furnishing.blocks)
            {
                // On the piece itself, so that the camera's see-through fade finds the model under the collider.
                Bounds part = furnishing.Solid, whole = furnishing.LocalBounds;
                var body = holder.AddComponent<BoxCollider>();
                body.center = part.center;
                body.size = part.size;
                // What spreads above head height (a tree's crown) is solid too, but is walked under.
                if (whole.max.y > part.max.y + CrownGap)
                {
                    var crown = holder.AddComponent<BoxCollider>();
                    crown.center = new Vector3(whole.center.x, (part.max.y + whole.max.y) * 0.5f, whole.center.z);
                    crown.size = new Vector3(whole.size.x, whole.max.y - part.max.y, whole.size.z);
                }
                // The walkable ground is cut away under it, so a walker goes round and a click behind it finds a way.
                var obstacle = holder.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = part.center;
                obstacle.size = part.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            }
            return holder;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int n = 0; n < t.childCount; n++) SetLayer(t.GetChild(n), layer);
        }
    }
}
