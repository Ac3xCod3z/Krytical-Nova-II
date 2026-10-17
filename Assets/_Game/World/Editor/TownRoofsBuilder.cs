using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The town's roofs, solid where they are seen and walkable, and nothing walkable inside a building (owner,
    /// 17 Oct 2026, for Wallstride: "make building roofs solid, runnable and walkable").
    ///
    /// Synty's buildings come with rough convex collision shells: on 173 of the town's 319 building pieces the solid
    /// top was more than 0.4 m off the roof that is drawn, so a player on a roof sank into it or stood on air. Each
    /// building piece in the town is given an exact collider made from the mesh it draws, and its own shells are
    /// switched off on the scene's copy (the vendor prefabs are untouched). Section buildings also have floors inside
    /// them, which baked into NavMesh islands no one can reach; a not-walkable volume inside each piece, stopping
    /// just under its top, keeps floors and the ground indoors out of the NavMesh while the roof stays in.
    /// </summary>
    public static class TownRoofsBuilder
    {
        private const float MinimumHeight = 2.5f;   // lower than this is a prop or a low wall, not a building
        private const float IndoorInset = 0.6f;     // the volume stays this far inside the walls (awnings, sills)
        private const float UnderTheTop = 0.35f;    // and stops this far under the top, so the roof bakes
        private const int NotWalkable = 1;

        [MenuItem("Krytical Nova/Zone/Solid Roofs and No Indoor NavMesh (Open Scene)")]
        private static void Menu()
        {
            Apply(out int exact, out int volumes);
            Zone01Setup.BakeNavMesh();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Roofs] {exact} building piece(s) given an exact collider, {volumes} indoor volume(s); NavMesh rebaked.");
        }

        /// <summary>Exact colliders and indoor volumes on every building piece under the town root. Safe to run again.</summary>
        public static void Apply(out int exact, out int volumes)
        {
            exact = volumes = 0;
            GameObject town = GameObject.Find(StarterTownBuilder.TownRootName);
            if (town == null) { Debug.LogWarning("[Roofs] No town in the open scene."); return; }
            foreach (MeshRenderer renderer in town.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsBuildingPiece(renderer, out Mesh mesh)) continue;
                if (MakeExact(renderer.gameObject, mesh)) exact++;
                if (KeepIndoorsOut(renderer.gameObject, mesh)) volumes++;
            }
            EditorSceneManager.MarkSceneDirty(town.scene);
        }

        private static bool IsBuildingPiece(MeshRenderer renderer, out Mesh mesh)
        {
            mesh = null;
            if (!renderer.name.StartsWith("SM_Bld_") || renderer.bounds.size.y < MinimumHeight) return false;
            if (renderer.name.Contains("Gate")) return false; // a gate is walked through
            var filter = renderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
            return mesh != null;
        }

        /// <summary>A collider that is the drawn mesh; the piece's own collision shells on this copy are switched off.</summary>
        private static bool MakeExact(GameObject piece, Mesh mesh)
        {
            MeshCollider own = null;
            var others = new List<Collider>();
            foreach (Collider collider in piece.GetComponents<Collider>())
            {
                if (collider is MeshCollider meshCollider && meshCollider.sharedMesh == mesh && !meshCollider.convex) own = meshCollider;
                else others.Add(collider);
            }
            bool changed = false;
            if (own == null)
            {
                own = Undo.AddComponent<MeshCollider>(piece);
                own.sharedMesh = mesh;
                own.convex = false;
                changed = true;
            }
            foreach (Collider other in others)
            {
                if (!other.enabled) continue;
                Undo.RecordObject(other, "Exact roof collider");
                other.enabled = false;
                changed = true;
            }
            return changed;
        }

        /// <summary>Not walkable inside the piece, from below its floor to just under its roof deck.</summary>
        private static bool KeepIndoorsOut(GameObject piece, Mesh mesh)
        {
            Bounds local = mesh.bounds;
            Vector3 scale = piece.transform.lossyScale;
            float insetX = IndoorInset / Mathf.Max(0.01f, Mathf.Abs(scale.x)), insetZ = IndoorInset / Mathf.Max(0.01f, Mathf.Abs(scale.z));
            float under = UnderTheTop / Mathf.Max(0.01f, Mathf.Abs(scale.y)), below = 0.5f / Mathf.Max(0.01f, Mathf.Abs(scale.y));
            // The deck, not the highest point: parapets, antennas and rooftop props stand above the roof that is walked.
            // A piece open on top (a lattice frame, a roofless shell) has no inside: what is in it is open to the sky.
            var volume = piece.GetComponent<NavMeshModifierVolume>();
            if (!DeckHeight(piece, mesh, local, insetX, insetZ, out float deck))
            {
                if (volume == null) return false;
                Undo.DestroyObjectImmediate(volume);
                return true;
            }
            if (volume == null) volume = Undo.AddComponent<NavMeshModifierVolume>(piece);
            else Undo.RecordObject(volume, "Indoor volume");
            float bottom = local.min.y - below, top = deck - under;
            var size = new Vector3(Mathf.Max(0.1f, local.size.x - 2f * insetX), Mathf.Max(0.1f, top - bottom), Mathf.Max(0.1f, local.size.z - 2f * insetZ));
            var center = new Vector3(local.center.x, (top + bottom) * 0.5f, local.center.z);
            bool changed = volume.area != NotWalkable || volume.size != size || volume.center != center;
            volume.area = NotWalkable;
            volume.size = size;
            volume.center = center;
            return changed;
        }

        /// <summary>
        /// The piece's roof deck in its own space: rays straight down onto its exact collider across the inside of its
        /// footprint, and the height most of them meet (the lower quarter, so props and parapets do not count).
        /// </summary>
        private static bool DeckHeight(GameObject piece, Mesh mesh, Bounds local, float insetX, float insetZ, out float deck)
        {
            deck = local.max.y;
            MeshCollider own = null;
            foreach (MeshCollider collider in piece.GetComponents<MeshCollider>())
                if (collider.sharedMesh == mesh && !collider.convex) own = collider;
            if (own == null) return true;
            Physics.SyncTransforms();
            Transform t = piece.transform;
            var heights = new List<float>(DeckSamples * DeckSamples);
            for (int i = 0; i < DeckSamples; i++)
                for (int j = 0; j < DeckSamples; j++)
                {
                    float u = (i + 0.5f) / DeckSamples, v = (j + 0.5f) / DeckSamples;
                    var inside = new Vector3(Mathf.Lerp(local.min.x + insetX, local.max.x - insetX, u), local.max.y + 1f,
                        Mathf.Lerp(local.min.z + insetZ, local.max.z - insetZ, v));
                    Vector3 from = t.TransformPoint(inside);
                    if (own.Raycast(new Ray(from, Vector3.down), out RaycastHit hit, local.size.y * Mathf.Abs(t.lossyScale.y) + 2f))
                        heights.Add(t.InverseTransformPoint(hit.point).y);
                }
            if (heights.Count < DeckSamples * DeckSamples * ClosedShare) return false; // open on top
            heights.Sort();
            deck = heights[heights.Count / 4];
            return true;
        }

        private const float ClosedShare = 0.6f;

        private const int DeckSamples = 5;
    }
}
