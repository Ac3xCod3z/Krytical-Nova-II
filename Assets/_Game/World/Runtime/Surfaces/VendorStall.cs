using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.World.Surfaces
{
    /// <summary>
    /// Vendor stalls are never climbed (owner, 22 Oct 2026: "we're not supposed to be able to climb any of the vendor
    /// stalls ... it doesn't make sense to climb them"). A stall is told by what it is for, not by its name: a structure
    /// no taller than a stall (a canopy, a booth, a kiosk, a watch post, an open shop) with someone the player talks to
    /// under it or beside it. Whole buildings are taller and stay walls, whoever stands at their door; an item lying on
    /// the ground is not someone to talk to. Wallstride's climb and the surface graph's bake both ask here.
    /// </summary>
    public static class VendorStall
    {
        /// <summary>Taller than this (metres) is a building, not a stall.</summary>
        public const float TallestStall = 6.5f;
        /// <summary>How far round the structure (metres, along the ground) its vendor may stand.</summary>
        public const float VendorReach = 3f;

        private static readonly Collider[] Near = new Collider[32];

        /// <summary>True when <paramref name="surface"/> is a vendor's stall.</summary>
        public static bool Is(Collider surface)
        {
            if (surface == null || surface is TerrainCollider) return false;
            Bounds bounds = surface.bounds;
            if (bounds.size.y > TallestStall) return false;
            bounds.Expand(new Vector3(VendorReach * 2f, 1f, VendorReach * 2f));
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, Near, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider other = Near[i];
                if (other == null || other == surface) continue;
                IInteractable someone = other.GetComponentInParent<IInteractable>();
                if (someone == null || someone is IInstantInteractable) continue; // a drop on the ground is no vendor
                if (someone is Component c && bounds.Contains(c.transform.position)) return true;
            }
            return false;
        }
    }
}
