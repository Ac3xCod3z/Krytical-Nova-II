using KryticalNova.World;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Drops each kind of swing push in a row in front of the game camera, in Play (day list item 20): a look at how the grass takes an
    /// arc, a finisher, an area ring, a big-hit wave and a big Hostile's swing, without having to fight. Editor only.
    /// </summary>
    internal static class FoliagePushPreview
    {
        private const string Menu = "Krytical Nova/World/Drop Swing Pushes";

        [MenuItem(Menu)]
        private static void Drop()
        {
            Camera cam = Camera.main;
            if (cam == null || FoliagePushes.Settings == null)
            {
                Debug.LogWarning("[KnSwingPush] Play first: the pushes need the game camera and the feel presenter's settings.");
                return;
            }
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 centre = cam.transform.position + forward * 14f;
            FoliagePushKind[] kinds = { FoliagePushKind.Arc, FoliagePushKind.Finisher, FoliagePushKind.Ring, FoliagePushKind.Wave, FoliagePushKind.Hostile };
            int added = 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                Vector3 at = centre + right * ((i - 2) * 6f);
                at.y = Physics.Raycast(at + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f) ? hit.point.y : at.y;
                float scale = kinds[i] == FoliagePushKind.Ring ? 0.6f : kinds[i] == FoliagePushKind.Hostile ? 2f : 1f;
                if (FoliagePushes.Add(kinds[i], at, forward, scale, local: true)) added++;
            }
            Debug.Log($"[KnSwingPush] dropped {added} of {kinds.Length} pushes 14 m ahead (arc, finisher, ring, wave, Hostile x2), {FoliagePushes.Alive} alive");
        }

        [MenuItem(Menu, true)]
        private static bool CanDrop() => Application.isPlaying;
    }
}
