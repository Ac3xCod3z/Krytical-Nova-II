using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Every collider in a sphere, with no cap (owner, 5 Nov 2026: an area skill hits every target in range). The
    /// buffer is reused; when a query fills it, it doubles and the query runs again, so a crowd is never cut short.
    /// </summary>
    public static class AreaOverlap
    {
        public static int All(Vector3 center, float radius, int layers, QueryTriggerInteraction triggers, ref Collider[] buffer)
        {
            if (buffer == null || buffer.Length == 0) buffer = new Collider[64];
            int count = Physics.OverlapSphereNonAlloc(center, radius, buffer, layers, triggers);
            while (count >= buffer.Length)
            {
                buffer = new Collider[buffer.Length * 2];
                count = Physics.OverlapSphereNonAlloc(center, radius, buffer, layers, triggers);
            }
            return count;
        }
    }
}
