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

    /// <summary>A player's death as every client sees it (from the server's death ClientRpc).</summary>
    public struct DeathFeel
    {
        /// <summary>The body's root when the blow landed.</summary>
        public Vector3 Point;
        /// <summary>Flat direction the blow travelled: the way the body is thrown.</summary>
        public Vector3 Direction;
        public Transform Target;
        /// <summary>Who dealt the blow on this client, or null (a GM kill, or the killer is not spawned here).</summary>
        public Transform Killer;
        public bool IsCritical;
        public bool VictimIsLocalPlayer;
        public bool KillerIsLocalPlayer;
    }

    /// <summary>
    /// Client-only "juice" (screen shake, hit flash, squash, dust, auras, trails, sounds). Implemented by the GameFeel
    /// assembly and registered in <see cref="ClientPresentationRegistry.Feel"/>; null on dedicated servers, so callers
    /// null-check. Gameplay code only reports moments; it never decides how they look or feel. Skills are named by
    /// their <c>skillId</c>, which the GameFeel side maps to its own effects.
    /// </summary>
    public interface IGameFeelSink
    {
        /// <param name="seconds">How long the blade keeps cutting (drives the weapon trail); 0 uses a default.</param>
        void Swing(Transform attacker, int comboStep, bool isLocalPlayer, float seconds = 0f);
        void Hit(in HitFeel hit);
        /// <summary>The local player lost health; <paramref name="fraction01"/> is the loss relative to max health.</summary>
        void LocalPlayerDamaged(float fraction01);
        void MoveCommand(Vector3 point, Vector3 normal);
        void TargetSelected(Transform target, WorldTargetKind kind);
        void RunStarted(Transform actor, bool isLocalPlayer);
        void RunStopped(Transform actor);
        void JumpTakeoff(Transform actor, bool isLocalPlayer);
        void Landed(Transform actor, bool isLocalPlayer);
        /// <summary>The actor left the ground (true) or is back on it, including after an aborted jump (false).</summary>
        void Airborne(Transform actor, bool airborne);

        /// <summary>A skill was cast (attack or buff), on every peer.</summary>
        /// <param name="areaRadius">Metres around the caster an area attack reaches; 0 for every other skill.</param>
        /// <param name="areaSeconds">Seconds from the cast until the area attack lands.</param>
        void SkillCast(Transform caster, string skillId, bool isAttack, bool isLocalPlayer, float areaRadius = 0f, float areaSeconds = 0f);
        /// <summary>The actor began holding a charged skill's key; the charge fills over <paramref name="seconds"/>.</summary>
        void ChargeStarted(Transform actor, string skillId, float seconds, bool isLocalPlayer);
        /// <summary>The hold ended: released into the cast (<paramref name="completed"/>) or let go early.</summary>
        void ChargeEnded(Transform actor, string skillId, bool completed);
        /// <summary>A skill's hit landed at <paramref name="point"/>, on every peer.</summary>
        void SkillHit(Transform caster, string skillId, Vector3 point);
        /// <summary>A timed buff took hold. It lasts until <see cref="BuffEnded"/>; <paramref name="duration"/> is its full length.</summary>
        void BuffStarted(Transform actor, string skillId, float duration);
        void BuffEnded(Transform actor, string skillId);
        /// <summary>A consumable took effect on the actor.</summary>
        void Consumed(Transform actor, bool restoredHealth, bool restoredChi);

        /// <summary>A pet reached its next growth stage, on every peer. <paramref name="glow"/> is the stage's colour.</summary>
        /// <param name="ownerIsLocalPlayer">The pet belongs to the local player: screen and sound feedback play too.</param>
        void PetEvolved(Transform pet, Color glow, bool ownerIsLocalPlayer);

        /// <summary>A character was killed, on every peer. A critical death throws the body.</summary>
        void Death(in DeathFeel death);
        /// <summary>The dying body hit the ground.</summary>
        /// <param name="involvesLocalPlayer">The local player is the one who died or the one who killed.</param>
        void BodyLanded(Transform actor, bool critical, bool involvesLocalPlayer);
        /// <summary>A dead character is back on its feet at a spawn point.</summary>
        void Respawned(Transform actor, bool isLocalPlayer);

        /// <summary>An attempt at the forge ended for this character.</summary>
        /// <param name="outcome">0 success, 1 fail, 2 a fail a Failsafe absorbed, 3 the item broke.</param>
        void GearWorked(Transform actor, int outcome, bool isLocalPlayer);

        /// <summary>A request at the Herald ended for this character: an Ascension attempt, or a Standing bought.</summary>
        /// <param name="outcome">0 a level gained, 1 nothing changed, 2 a level lost, 3 a slip the Nova held.</param>
        /// <param name="newTier">The character reached a new Nova tier: the big moment.</param>
        void NovaWorked(Transform actor, int outcome, bool newTier, bool isLocalPlayer);
    }
}
