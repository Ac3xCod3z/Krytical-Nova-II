using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Shared movement tuning; never stores a player's current movement mode.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Movement Settings")]
    public sealed class MovementSettings : ScriptableObject
    {
        private const float MinimumPositive = 0.01f;
        private const float DefaultWalkSpeed = 5f;
        private const float DefaultAcceleration = 25f;
        private const float DefaultTurnSpeed = 540f;
        private const float DefaultStoppingDistance = 0.2f;

        [Tooltip("Base walk speed in world units per second.")]
        [Min(MinimumPositive)] public float walkSpeed = DefaultWalkSpeed;
        [Min(MinimumPositive)] public float acceleration = DefaultAcceleration;
        [Tooltip("Degrees per second while turning toward the path.")]
        [Min(MinimumPositive)] public float turnSpeed = DefaultTurnSpeed;
        [Min(0f)] public float stoppingDistance = DefaultStoppingDistance;

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(MinimumPositive, walkSpeed);
            acceleration = Mathf.Max(MinimumPositive, acceleration);
            turnSpeed = Mathf.Max(MinimumPositive, turnSpeed);
            stoppingDistance = Mathf.Max(0f, stoppingDistance);
        }
    }
}
