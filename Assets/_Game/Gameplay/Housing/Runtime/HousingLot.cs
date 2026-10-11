using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// A Lot: a fenced piece of ground with a pad for the Hall, a front lawn and a back lawn. Its own space has the
    /// origin in the middle of the ground, x across, z from the front gate towards the back. A Lot does not know
    /// which map it is on, so the same prefab serves the sandbox and, later, a Crew Quarter. Every Lot of a faction
    /// has the same shape, which is what lets a Hall move from one to another with its lawns as they are.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HousingLot : MonoBehaviour
    {
        [Tooltip("Width and depth of the ground inside the fence, in metres.")]
        [SerializeField] private Vector2 size = new Vector2(36f, 48f);
        [Tooltip("How high above the ground a piece may be set.")]
        [SerializeField, Min(1f)] private float height = 14f;
        [Tooltip("The Hall's pad: its middle and its width and depth, in the Lot's space. Nothing is set down on it.")]
        [SerializeField] private Vector2 padCenter = new Vector2(0f, 4f);
        [SerializeField] private Vector2 padSize = new Vector2(16f, 14f);
        [Tooltip("The space kept clear in front of the Hall's door.")]
        [SerializeField] private Vector2 doorClearCenter = new Vector2(0f, -4.5f);
        [SerializeField] private Vector2 doorClearSize = new Vector2(3f, 3f);
        [Tooltip("Where a player arrives: just inside the front gate.")]
        [SerializeField] private Transform arrival;
        [Tooltip("The Hall is stood here, on the middle of the pad.")]
        [SerializeField] private Transform facadeAnchor;
        [Tooltip("Furnishings are drawn under this.")]
        [SerializeField] private Transform pieceRoot;

        public Vector2 Size => size;
        public Vector2 PadCenter => padCenter;
        public Vector2 PadSize => padSize;
        public Transform Arrival => arrival != null ? arrival : transform;
        public Transform FacadeAnchor => facadeAnchor != null ? facadeAnchor : transform;
        public Transform PieceRoot => pieceRoot != null ? pieceRoot : transform;

        /// <summary>The lawns as the rules see them.</summary>
        public AreaShape LawnShape => new AreaShape
        {
            halfSize = size * 0.5f,
            height = height,
            blocked = new[]
            {
                new Rect(padCenter - padSize * 0.5f, padSize),
                new Rect(doorClearCenter - doorClearSize * 0.5f, doorClearSize),
            },
        };

        /// <summary>Editor builder entry point.</summary>
        public void Configure(Vector2 lotSize, Vector2 padMiddle, Vector2 padExtent, Vector2 doorMiddle, Vector2 doorExtent,
            Transform arrivalPoint, Transform facade, Transform pieces)
        {
            size = lotSize;
            padCenter = padMiddle;
            padSize = padExtent;
            doorClearCenter = doorMiddle;
            doorClearSize = doorExtent;
            arrival = arrivalPoint;
            facadeAnchor = facade;
            pieceRoot = pieces;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(size.x, height, size.y));
            Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(padCenter.x, 0.05f, padCenter.y), new Vector3(padSize.x, 0.1f, padSize.y));
            Gizmos.DrawWireCube(new Vector3(doorClearCenter.x, 0.05f, doorClearCenter.y), new Vector3(doorClearSize.x, 0.1f, doorClearSize.y));
        }
    }
}
