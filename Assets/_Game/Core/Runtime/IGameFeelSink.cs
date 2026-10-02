using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>A confirmed melee hit as every client sees it (from the server's impact ClientRpc).</summary>
    public struct HitFeel
    {
        public Vector3 Point;
        /// <summary>Flat direction from the target back toward the attacker.</summary>
        public Vector3 TowardAttacker;
        public Transform Attacker;
        /// <summary>The struck entity on this client, or null when it isn't spawned here.</summary>
        public Transform Target;
        public int ComboStep;
        public bool IsCritical;
        public bool AttackerIsLocalPlayer;
        public bool TargetIsLocalPlayer;
    }

    /// <summary>
    /// Client-only "juice" (screen shake, hit flash, squash, dust, sounds). Implemented by the GameFeel assembly and
    /// registered in <see cref="ClientPresentationRegistry.Feel"/>; null on dedicated servers, so callers null-check.
    /// Gameplay code only reports moments; it never decides how they feel.
    /// </summary>
    public interface IGameFeelSink
    {
        void Swing(Transform attacker, int comboStep, bool isLocalPlayer);
        void Hit(in HitFeel hit);
        /// <summary>The local player lost health; <paramref name="fraction01"/> is the loss relative to max health.</summary>
        void LocalPlayerDamaged(float fraction01);
        void MoveCommand(Vector3 point, Vector3 normal);
        void TargetSelected(Transform target, WorldTargetKind kind);
        void RunStarted(Transform actor, bool isLocalPlayer);
        void JumpTakeoff(Transform actor, bool isLocalPlayer);
        void Landed(Transform actor, bool isLocalPlayer);
    }
}
