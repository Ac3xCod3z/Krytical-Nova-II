using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>Forwards collision events from the intact collider object to the root,
    /// so collision impulses can act as a damage source.</summary>
    [AddComponentMenu("")]
    public class DestructibleCollisionRelay : MonoBehaviour
    {
        public DestructibleRoot Root;

        void OnCollisionEnter(Collision collision)
        {
            if (Root != null) Root.ReportIntactCollision(collision);
        }
    }
}
