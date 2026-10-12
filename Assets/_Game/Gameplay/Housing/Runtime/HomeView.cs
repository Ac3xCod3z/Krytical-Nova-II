using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Housing
{
    /// <summary>A furnishing as it stands in the world: which piece of the Hall it is.</summary>
    [DisallowMultipleComponent]
    public sealed class FurnishingPiece : MonoBehaviour
    {
        public uint Uid { get; set; }
        public FurnishingDefinition Furnishing { get; set; }
    }

    /// <summary>
    /// Draws one area of a Hall: a model for every piece the authority holds, kept in step as edits come back.
    /// It shows and never decides. Pieces are on their own layer, so the player's clicks and the camera's
    /// see-through fade leave them alone; each carries one box to be picked by, not the model's own colliders.
    /// </summary>
    public sealed class HomeView : MonoBehaviour
    {
        public const string FurnishingLayerName = "Furnishing";
        private const int IgnoreRaycastLayer = 2;

        private readonly Dictionary<uint, FurnishingPiece> shown = new Dictionary<uint, FurnishingPiece>();
        private IHomeAuthority authority;
        private HomeArea area;
        private Transform root;

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

        public void Bind(IHomeAuthority homeAuthority, HomeArea homeArea, Transform pieceRoot)
        {
            Unbind();
            authority = homeAuthority;
            area = homeArea;
            root = pieceRoot != null ? pieceRoot : transform;
            if (authority == null) return;
            authority.Changed += OnChanged;
            Rebuild();
        }

        private void Unbind()
        {
            if (authority != null) authority.Changed -= OnChanged;
            authority = null;
            foreach (FurnishingPiece piece in shown.Values)
                if (piece != null) Destroy(piece.gameObject);
            shown.Clear();
        }

        private void OnDestroy() => Unbind();

        public FurnishingPiece Find(uint uid) => shown.TryGetValue(uid, out FurnishingPiece piece) ? piece : null;

        /// <summary>Shows or hides one piece (the Arrange tool hides the piece it is carrying).</summary>
        public void SetVisible(uint uid, bool visible)
        {
            FurnishingPiece piece = Find(uid);
            if (piece != null) piece.gameObject.SetActive(visible);
        }

        private void Rebuild()
        {
            foreach (FurnishingPiece piece in shown.Values)
                if (piece != null) Destroy(piece.gameObject);
            shown.Clear();
            foreach (PlacedPiece piece in authority.Home.Area(area)) Show(piece);
            RefreshPoses();
        }

        private void OnChanged(HomeArea changedArea, ushort request, IReadOnlyList<HomeChange> changes)
        {
            if (changedArea != area) return;
            foreach (HomeChange change in changes)
            {
                if (change.hadBefore && !change.hasAfter)
                {
                    if (shown.TryGetValue(change.before.u, out FurnishingPiece gone) && gone != null) Destroy(gone.gameObject);
                    shown.Remove(change.before.u);
                }
                else if (change.hasAfter && !change.hadBefore) Show(change.after);
            }
            RefreshPoses();
        }

        private void Show(in PlacedPiece piece)
        {
            FurnishingDefinition furnishing = authority.Catalog != null ? authority.Catalog.Get(piece.i) : null;
            if (furnishing == null || shown.ContainsKey(piece.u)) return; // an item this build does not know stays in the save, undrawn
            GameObject model = Spawn(furnishing, root, FurnishingLayer, pickable: true);
            if (model == null) return;
            var view = model.AddComponent<FurnishingPiece>();
            view.Uid = piece.u;
            view.Furnishing = furnishing;
            shown[piece.u] = view;
        }

        // What stands on a moved piece moves with it, so every pose is worked out again: a Hall is a few hundred pieces.
        private void RefreshPoses()
        {
            List<PlacedPiece> pieces = authority.Home.Area(area);
            for (int n = 0; n < pieces.Count; n++)
            {
                if (!shown.TryGetValue(pieces[n].u, out FurnishingPiece view) || view == null) continue;
                HousingLayout.Pose(pieces, pieces[n], out Vector3 position, out Quaternion rotation);
                Transform t = view.transform;
                t.SetPositionAndRotation(root.TransformPoint(position), root.rotation * rotation);
                t.localScale = Vector3.one * pieces[n].Scale;
            }
        }

        /// <summary>
        /// A furnishing's model, ready to stand in the world: the vendor prefab without its colliders, on the given
        /// layer, with one box to pick it by when asked. Also what the Arrange tool's preview is made of.
        /// </summary>
        public static GameObject Spawn(FurnishingDefinition furnishing, Transform parent, int layer, bool pickable)
        {
            if (furnishing == null || furnishing.prefab == null) return null;
            var holder = new GameObject(furnishing.name);
            holder.transform.SetParent(parent, false);
            GameObject model = Instantiate(furnishing.prefab, holder.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                if (!furnishing.castsShadow) renderer.shadowCastingMode = ShadowCastingMode.Off;
            SetLayer(holder.transform, layer);

            if (pickable)
            {
                var box = holder.AddComponent<BoxCollider>();
                box.center = furnishing.boundsCenter;
                // A rug or a painting is nearly flat: give it enough thickness to be clicked.
                box.size = Vector3.Max(furnishing.boundsSize, Vector3.one * 0.06f);
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
