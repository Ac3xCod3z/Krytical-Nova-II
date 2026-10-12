using KryticalNova.Audio;
using KryticalNova.Core;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Cadence (design 17): in a fight, an attack skill whose key goes down on the beat of the fight music hits a
    /// little harder, once. The player's own game hears the beat, so it says when the key went down, in the zone's
    /// shared clock; the server reads its own beat at that moment, decides, and keeps the press for that skill's
    /// next cast (which may be a moment later: a swing still playing, a walk into reach). Off the beat nothing is
    /// lost. A held key that repeats, Autopilot, buffs and charged skills never earn it: only the key going down.
    /// Offline (the unspawned scene avatar) the local copy does both halves.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCadence : NetworkBehaviour
    {
        // While the music is still coming in at the start of a fight there is no beat a player could be on yet.
        private const float HeardFrom = 0.5f;

        [SerializeField] private CadenceSettings settings;

        private PlayerSkillHotbar hotbar;
        private PlayerCombatSkills skills;
        private PlayerCombatState combat;
        // Where the game is decided: the skill an on-beat press is kept for, and since when.
        private string keptSkill;
        private float keptAt = float.NegativeInfinity;

        public CadenceSettings Settings => settings;

        /// <summary>Where the game is decided: what became of the last press, in words (GM).</summary>
        public string LastPress { get; private set; } = "No skill key pressed in a fight yet.";

        /// <summary>GM: every press and what the server made of it goes to the log.</summary>
        public static bool Logging { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Logging = false;

        private bool Decides => !IsSpawned || IsServer;
        private bool IsMine => !IsSpawned || IsOwner;

        private void Awake()
        {
            hotbar = GetComponent<PlayerSkillHotbar>();
            skills = GetComponent<PlayerCombatSkills>();
            combat = GetComponent<PlayerCombatState>();
        }

        private void OnEnable()
        {
            if (hotbar != null) hotbar.SkillKeyPressed += OnSkillKey;
        }

        private void OnDisable()
        {
            if (hotbar != null) hotbar.SkillKeyPressed -= OnSkillKey;
            keptSkill = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            keptSkill = null; // whatever the offline avatar had earned does not come along into a session
        }

        // ------------------------------------------------------------------ the owner presses

        private void OnSkillKey(SkillDefinitionSO skill)
        {
            if (!IsMine || settings == null || !settings.enabled) return;
            if (skill == null || !skill.IsAttack || skill.IsCharged) return;
            MusicConductor conductor = MusicConductor.Instance;
            // No beat for this player (out of a fight, between two tracks, a zone with no fight music): nothing to say.
            if (conductor == null || !CombatBeat.HasBeat || CombatBeat.Live01 < HeardFrom) return;

            // The moment of the press on the shared clock, as this player heard the beat.
            double pressedAt = conductor.Now - CombatBeat.ClockLagSeconds;
            if (Decides) ServerNotePress(skill.skillId, pressedAt);
            else PressRpc(skill.skillId, pressedAt);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PressRpc(string skillId, double pressedAt) => ServerNotePress(skillId, pressedAt);

        // ------------------------------------------------------------------ the server decides

        private void ServerNotePress(string skillId, double pressedAt)
        {
            // Every press is the latest word: one off the beat lets go of an earlier one that was on it.
            keptSkill = null;
            SkillDefinitionSO skill = skills != null ? skills.GetSkill(skillId) : null;
            if (skill == null || !skill.IsAttack || skill.IsCharged) return;

            MusicConductor conductor = MusicConductor.Instance;
            double now = conductor != null ? conductor.Now : 0.0;
            double error = 0.0;
            bool hasBeat = conductor != null && conductor.TryBeatError(pressedAt, out error);
            CadenceVerdict verdict = CadenceRules.Judge(settings, combat != null && combat.InCombat, hasBeat, now, pressedAt, error);
            if (verdict == CadenceVerdict.OnCadence)
            {
                keptSkill = skill.skillId;
                keptAt = Time.time;
            }

            LastPress = hasBeat
                ? $"{skill.displayName}: {error * 1000.0:+0;-0;0} ms from the beat, heard of {(now - pressedAt) * 1000.0:0} ms later: {Words(verdict)}."
                : $"{skill.displayName}: {Words(verdict)}.";
            if (Logging) Debug.Log($"[Cadence] {name}: {LastPress}");
        }

        private static string Words(CadenceVerdict verdict)
        {
            switch (verdict)
            {
                case CadenceVerdict.OnCadence: return "on Cadence";
                case CadenceVerdict.OffBeat: return "off the beat";
                case CadenceVerdict.Stale: return "refused, the press is too old or not yet due";
                case CadenceVerdict.NoBeat: return "no beat was playing";
                case CadenceVerdict.NotInAFight: return "not in a fight";
                default: return "Cadence is off";
            }
        }

        /// <summary>
        /// Server or offline, as a cast of <paramref name="skill"/> begins: what its damage is multiplied by. More
        /// than 1 when the skill's key went down on the beat a moment ago; the press is spent by asking.
        /// </summary>
        public float ServerTake(SkillDefinitionSO skill)
        {
            if (!Decides || settings == null || skill == null || keptSkill == null || keptSkill != skill.skillId) return 1f;
            keptSkill = null;
            if (!settings.enabled) return 1f;
            if (!CadenceRules.StillKept(Time.time, keptAt, settings.keepSeconds))
            {
                LastPress = $"{skill.displayName}: was on Cadence, but the cast came more than {settings.keepSeconds:0.#} s after the press.";
                if (Logging) Debug.Log($"[Cadence] {name}: {LastPress}");
                return 1f;
            }

            LastPress += $" Spent on this cast: +{settings.bonusPercent:0.#} % damage.";
            if (Logging) Debug.Log($"[Cadence] {name}: {skill.displayName} cast on Cadence, +{settings.bonusPercent:0.#} % damage.");
            if (IsSpawned && !IsOwnedByServer) EarnedRpc(skill.skillId);
            else ShowEarned(skill.skillId);
            return settings.Multiplier;
        }

        /// <summary>Whether a cast on Cadence hits this victim harder (other players only when the settings say so).</summary>
        public bool AppliesTo(IDamageable victim)
        {
            if (settings == null || victim == null) return false;
            return settings.againstPlayers || victim.Transform.GetComponentInParent<PlayerCadence>() == null;
        }

        // ------------------------------------------------------------------ the owner is told

        [Rpc(SendTo.Owner)]
        private void EarnedRpc(string skillId) => ShowEarned(skillId);

        // Only the player who earned it is shown it, as with damage numbers.
        private void ShowEarned(string skillId)
        {
            SkillDefinitionSO skill = skills != null ? skills.GetSkill(skillId) : null;
            ClientPresentationRegistry.Feedback?.ShowFloatingText(transform.position, "ON CADENCE", "crit");
            if (skills != null && skill != null && settings != null)
                skills.Announce($"On Cadence: {skill.displayName} +{settings.bonusPercent:0.#} % damage.");
        }
    }
}
