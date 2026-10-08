using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The outermost points of a weapon model, in its own space: whichever way the weapon is turned, the first of it
    /// to touch the ground is one of these. <see cref="WeaponVisualMount"/> uses them to keep a long weapon out of
    /// the ground without lifting it further than its real shape asks. Sits on the weapon's model prefab
    /// (Items/Data/Assets/Prefabs/Weapons), written by Krytical Nova ▸ Items ▸ 8 from the mesh, because a mesh
    /// cannot be read in a build. A model without them falls back on the corners of its box, which holds it a
    /// little higher than it needs to be.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponReach : MonoBehaviour
    {
        public Vector3[] points;

        private void OnDrawGizmosSelected()
        {
            if (points == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.31f, 0.89f, 0.94f);
            foreach (Vector3 point in points) Gizmos.DrawWireSphere(point, 0.008f);
        }
    }
}
