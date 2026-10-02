using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Excavator bucket destroyer for Quick Destruction.
    ///
    /// NO RIGIDBODY REQUIRED. Put this on the bucket's ArticulationBody link - the same
    /// GameObject that carries the link's Collider. It never reads Collision.rigidbody,
    /// which Unity leaves null for an articulation link.
    ///
    /// The bucket keeps damaging the wall for as long as it pushes, and only while the
    /// contact is genuinely forceful. Quick Destruction's own collision damage answers
    /// OnCollisionEnter, which fires once when a contact begins - right for a bullet, silent
    /// for a bucket that leans into a wall for two seconds.
    ///
    /// To calibrate: tick Log Contact Force, drive the bucket into a wall the way the game
    /// will, read the peak force in the Console, set Min Contact Force below it, untick.
    /// </summary>
    [AddComponentMenu("Quick Destruction/Excavator Bucket Damage")]
    public class ExcavatorBucketDamage : MonoBehaviour
    {
        [Tooltip("Contact force in newtons the bucket must reach before it does any damage. " +
                 "Below this it only shoves the object around.")]
        public float MinContactForce = 100f;

        [Tooltip("Health damage per kilonewton of force above the threshold. Raise it until a " +
                 "solid swing takes the chunk you want.")]
        public float DamagePerKilonewton = 200f;

        [Tooltip("Print the measured contact force to the Console, to set the threshold from " +
                 "real numbers instead of guesswork. Turn off once calibrated.")]
        public bool LogContactForce;

        // Seconds between damage events, so one long contact does not fire every physics step.
        // The target profile's own Damage Cooldown applies on top of this anyway.
        const float HitInterval = 0.1f;

        float m_nextHitTime;
        float m_radius = 0.6f;
        ArticulationBody m_link;
        float m_speedBeforeSolve;

        float LinkSpeed
        {
            get
            {
#if UNITY_6000_0_OR_NEWER
            return m_link.linearVelocity.magnitude;
#else
                return m_link.velocity.magnitude;
#endif
            }
        }

        void Start()
        {
            // The size of the hole this tool opens is the size of the tool. Reading it from
            // the bucket's own collider keeps one more number off the inspector and is a
            // better default than any fixed figure could be. Searched in children too, so it
            // still works when the collider sits under the link rather than on it.
            Collider bucket = GetComponentInChildren<Collider>();
            if (bucket != null)
                m_radius = Mathf.Max(0.1f, bucket.bounds.extents.magnitude);

            m_link = GetComponentInParent<ArticulationBody>();

            // Unity only raises collision events for colliders that belong to a body. An
            // ArticulationBody counts; a bare mesh child of the arm does not, and would fail
            // silently. This is the one setup mistake worth naming out loud.
            if (m_link == null && GetComponentInParent<Rigidbody>() == null)
                Debug.LogWarning($"[{name}] is not on an ArticulationBody or Rigidbody, so " +
                                 "Unity will not send it collision events. Move it onto the " +
                                 "bucket's articulation link.", this);
        }

        void FixedUpdate()
        {
            // Sampled before the solver runs. Collision callbacks arrive after it, so the
            // difference between this and the speed then is what the wall took out of the
            // bucket during this step.
            if (m_link != null) m_speedBeforeSolve = LinkSpeed;
        }

        void OnCollisionEnter(Collision collision) => Hit(collision);

        void OnCollisionStay(Collision collision) => Hit(collision);

        void Hit(Collision collision)
        {
            if (collision == null || collision.contactCount == 0) return;

            ContactPoint contact = collision.GetContact(0);
            float force = MeasureForce(collision, contact.point);
            if (force < MinContactForce || Time.time < m_nextHitTime) return;

            DestructibleRoot root = collision.collider.GetComponentInParent<DestructibleRoot>();
            if (root == null) return;

            // Unity reports the contact normal from either side; Quick Destruction wants the
            // outward surface direction, pointing back towards the bucket, so freed pieces
            // leave the hole instead of being driven into the wall.
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, transform.position - contact.point) < 0f) normal = -normal;

            m_nextHitTime = Time.time + HitInterval;
            root.ApplyDamage(new DamageContext(
                (force - MinContactForce) * 0.001f * DamagePerKilonewton,
                contact.point, normal, m_radius));
        }

        /// <summary>
        /// How hard the bucket is pushing, in newtons, read from whichever channel actually
        /// carries the signal.
        ///
        /// Collision.impulse is the direct answer and is used whenever Unity fills it in. It
        /// is not guaranteed for articulation contacts, so two physical fallbacks follow: the
        /// speed the wall took out of the bucket this step, and - when a stiff hydraulic
        /// drive holds the bucket so rigidly that it never slows down - the strain in the
        /// joint itself, converted from torque to force across the lever arm to the contact.
        /// </summary>
        float MeasureForce(Collision collision, Vector3 point)
        {
            float dt = Time.fixedDeltaTime;
            float fromImpulse = collision.impulse.magnitude / dt;
            float fromMomentum = 0f;
            float fromJoint = 0f;

            if (m_link != null)
            {
                float speedLost = m_speedBeforeSolve - LinkSpeed;
                if (speedLost > 0f) fromMomentum = m_link.mass * speedLost / dt;

                ArticulationReducedSpace joint = m_link.jointForce;
                float torque = 0f;
                for (int i = 0; i < joint.dofCount; i++) torque += Mathf.Abs(joint[i]);
                // jointForce on a revolute joint is a torque. Divided by the distance from
                // the joint to the contact it is the force available at the bucket tip.
                float lever = Mathf.Max(0.1f, Vector3.Distance(m_link.transform.position, point));
                fromJoint = torque / lever;
            }

            if (LogContactForce)
                Debug.Log($"[{name}] impulse {fromImpulse:0} N | momentum {fromMomentum:0} N | " +
                          $"joint {fromJoint:0} N | threshold {MinContactForce:0} N", this);

            if (fromImpulse > 1f) return fromImpulse;
            if (fromMomentum > 1f) return fromMomentum;
            return fromJoint;
        }
    }
}
