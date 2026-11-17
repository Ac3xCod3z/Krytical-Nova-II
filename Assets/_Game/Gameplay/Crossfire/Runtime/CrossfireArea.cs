using System.Collections.Generic;
using KryticalNova.Factions;
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
        [Tooltip("A Muster Yard: the faction whose Rally Point it is (one per faction; owner, 16 Nov 2026). The Transit Row port lands that faction's called players here. None: nobody is landed here.")]
        public FactionId faction = FactionId.None;
        [Tooltip("A Muster Yard: where a player the Transit Row port sends lands, and the way they face (on the ground). Put it on the ground inside the box. Empty: the box's centre, facing as the box does.")]
        public Transform landing;
        [Tooltip("A Muster Yard: where its side's dead get up during a war (the yard's shrine), on the ground inside the box. Empty: the landing.")]
        public Transform respawn;
        [Tooltip("A Muster Yard: the stand points round its shrine where the dead get up (The Crucible: Front, Left, Rear, Right). Each next one of its side's dead takes the next stand, a little apart on a small ring there; the shrine itself is never stood in. Empty: the respawn spot (or the landing).")]
        public Transform[] respawnStands = new Transform[0];

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

        /// <summary>Where a player put on this yard lands (world, metres) and faces (degrees about up).</summary>
        public void Landing(out Vector3 at, out float yawDegrees)
        {
            Transform t = landing != null ? landing : transform;
            at = t.position;   // the centre is always inside the box, so the player counts as in the yard (a floor point would sit on its edge)
            yawDegrees = t.eulerAngles.y;
        }

        /// <summary>
        /// Where the <paramref name="index"/>-th of this side's dead gets up (world, metres) and faces: the respawn spot (or the landing), each
        /// next one a little apart on a ring so bodies do not stand in each other. Always inside the box, so the player is still in the war.
        /// </summary>
        public void RespawnSpot(int index, float spacing, int spots, out Vector3 at, out float yawDegrees)
        {
            Transform t = respawn != null ? respawn : (landing != null ? landing : transform);
            int stands = CountStands();
            if (stands > 0)
            {
                // the stands take turns (Front, Left, Rear, Right, Front...), each with its own share of the spread's spots
                index = Mathf.Max(0, index);
                t = StandAt(index % stands);
                index /= stands;
                spots = Mathf.Max(1, spots / stands);
            }
            Vector3 offset = CrossfireRules.RespawnSpread(index, spacing, spots);
            at = t.position + offset;
            yawDegrees = t.eulerAngles.y;
            if (!Contains(at)) at = t.position - offset;   // a spot past the box's edge: the same spot on the other side of the stand
            if (!Contains(at)) at = t.position;            // still outside: no spread rather than a spot outside the war
        }

        int CountStands()
        {
            int n = 0;
            if (respawnStands != null)
                for (int i = 0; i < respawnStands.Length; i++)
                    if (respawnStands[i] != null) n++;
            return n;
        }

        /// <summary>The <paramref name="k"/>-th stand that is set (empty slots are skipped).</summary>
        Transform StandAt(int k)
        {
            for (int i = 0; i < respawnStands.Length; i++)
            {
                if (respawnStands[i] == null) continue;
                if (k-- == 0) return respawnStands[i];
            }
            return transform;
        }

        /// <summary>The Rally Point (Muster Yard) of <paramref name="faction"/> in the loaded maps; false when it has none.</summary>
        public static bool TryRallyPoint(FactionId faction, out CrossfireArea yard)
        {
            yard = null;
            if (faction == FactionId.None) return false;
            for (int i = 0; i < s_All.Count; i++)
            {
                CrossfireArea a = s_All[i];
                if (a == null || a.kind != AreaKind.MusterYard || a.faction != faction) continue;
                yard = a;
                return true;
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
