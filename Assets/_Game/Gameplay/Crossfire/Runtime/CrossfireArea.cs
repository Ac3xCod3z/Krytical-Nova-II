using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Crossfire
{
    /// <summary>
    /// A box of a Crossfire battlefield (owner's DAY list item 17; World-Atlas "The shape every battlefield shares"): a faction's
    /// <b>Muster Yard</b>, where players gather during the join window and the Muster, or the <b>Field</b>, the open ground the gates lead
    /// to. Together they are the war area: at the war's end the server sends everyone inside it back where they came from. Placed in the
    /// map by hand (or a builder); the server reads it, nothing is networked.
    /// </summary>
    public sealed class CrossfireArea : MonoBehaviour
    {
        public enum AreaKind : byte { MusterYard = 0, Field = 1 }

        public AreaKind kind = AreaKind.MusterYard;
        [Tooltip("The box, in this object's space (centre at its position). A yard is about 50 x 10 x 40 m; the field about 200 m across.")]
        public Vector3 size = new Vector3(50f, 10f, 40f);

        static readonly List<CrossfireArea> s_All = new List<CrossfireArea>();

        /// <summary>Every enabled area in the loaded maps.</summary>
        public static IReadOnlyList<CrossfireArea> All => s_All;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        void OnEnable() => Track(this);
        void OnDisable() => Untrack(this);

        /// <summary>Counts an area in (done by its OnEnable in play; EditMode tests call it themselves).</summary>
        public static void Track(CrossfireArea area) { if (area != null && !s_All.Contains(area)) s_All.Add(area); }

        public static void Untrack(CrossfireArea area) => s_All.Remove(area);

        /// <summary>The point (world, metres) is inside the box.</summary>
        public bool Contains(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            Vector3 half = size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>The point is in a Muster Yard (<paramref name="yardsOnly"/>) or anywhere in the war area.</summary>
        public static bool AnyContains(Vector3 world, bool yardsOnly)
        {
            for (int i = 0; i < s_All.Count; i++)
            {
                CrossfireArea a = s_All[i];
                if (a == null || (yardsOnly && a.kind != AreaKind.MusterYard)) continue;
                if (a.Contains(world)) return true;
            }
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = kind == AreaKind.MusterYard ? new Color(1f, 0.8f, 0.2f, 0.6f) : new Color(1f, 0.3f, 0.2f, 0.4f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
