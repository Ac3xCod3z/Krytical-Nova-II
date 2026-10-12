using System;
using KryticalNova.Audio;
using KryticalNova.Core;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>What became of a press that was on Cadence.</summary>
    public enum CadenceFate : byte
    {
        /// <summary>Not on Cadence: there was nothing to keep.</summary>
        None,
        /// <summary>Kept for its skill: the cast has not begun yet (or never will).</summary>
        Kept,
        /// <summary>Spent on a cast, which hit harder.</summary>
        Spent,
        /// <summary>The cast came too long after the press, or another press took its place.</summary>
        Lost,
    }

    /// <summary>One skill key pressed in a fight, as the server judged it. For the GM's Cadence readout.</summary>
    public struct CadencePress
    {
        public string Skill;
        /// <summary>Milliseconds from the nearest beat: negative is early, positive late.</summary>
        public float ErrorMs;
        /// <summary>Milliseconds between the press and the server hearing of it.</summary>
        public float HeardAfterMs;
        public CadenceVerdict Verdict;
        public CadenceFate Fate;
        /// <summary>Where in <see cref="Track"/> the press fell, in beats since the track's first.</summary>
        public double Beats;
        public MusicTrack Track;
        /// <summary>Game time (<see cref="Time.time"/>) the server heard of it.</summary>
        public float At;
    }

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
        /// <summary>How many presses are remembered for the GM's readout.</summary>
        public const int HistorySize = 16;
        private const string ReadoutKey = "kn.gm.cadenceReadout";

        [SerializeField] private CadenceSettings settings;

        private PlayerSkillHotbar hotbar;
        private PlayerCombatSkills skills;
        private PlayerCombatState combat;
        // Where the game is decided: the skill an on-beat press is kept for, since when, and which press it was.
        private string keptSkill;
        private float keptAt = float.NegativeInfinity;
        private int keptPress = -1;
        // The last presses, newest last written, and what they add up to.
        private readonly CadencePress[] history = new CadencePress[HistorySize];
        private int written;
        private double errorSum, errorSquares;

        public CadenceSettings Settings => settings;

        /// <summary>Where the game is decided: what became of the last press, in words (GM).</summary>
        public string LastPress { get; private set; } = "No skill key pressed in a fight yet.";

        // ---- the tally, where the game is decided (GM readout). Counted since the session began or the last reset.
        /// <summary>Presses judged on the beat.</summary>
        public int PressesOn { get; private set; }
        /// <summary>Presses judged fairly, but off the beat.</summary>
        public int PressesOff { get; private set; }
        /// <summary>Presses that could not be judged: no beat playing, not in a fight, too old.</summary>
        public int PressesRefused { get; private set; }
        /// <summary>Casts that hit harder for a press on the beat.</summary>
        public int CastsOnCadence { get; private set; }
        /// <summary>How many presses are in the history, at most <see cref="HistorySize"/>.</summary>
        public int HistoryCount => Mathf.Min(written, HistorySize);
        /// <summary>The average distance from the beat of every press that was judged, in ms: positive is late.</summary>
        public float AverageErrorMs => PressesOn + PressesOff > 0 ? (float)(errorSum / (PressesOn + PressesOff)) : 0f;
        /// <summary>How far those presses scatter round their average, in ms.</summary>
        public float SpreadMs
        {
            get
            {
                int count = PressesOn + PressesOff;
                if (count < 2) return 0f;
                double mean = errorSum / count;
                return (float)Math.Sqrt(Math.Max(0.0, errorSquares / count - mean * mean));
            }
        }

        /// <summary>A press of the history: 0 is the latest, 1 the one before.</summary>
        public CadencePress Press(int back) => history[((written - 1 - back) % HistorySize + HistorySize) % HistorySize];

        /// <summary>Starts the tally and the history again.</summary>
        public void ResetTally()
        {
            PressesOn = PressesOff = PressesRefused = CastsOnCadence = 0;
            written = 0;
            keptPress = -1;
            errorSum = errorSquares = 0.0;
            LastPress = "No skill key pressed in a fight yet.";
        }

        /// <summary>GM: every press and what the server made of it goes to the log.</summary>
        public static bool Logging { get; set; }

        /// <summary>GM: the Cadence readout is on the screen. Kept between sessions on this machine.</summary>
        public static bool ReadoutShown
        {
            get => PlayerPrefs.GetInt(ReadoutKey, 0) != 0;
            set
            {
                if (ReadoutShown == value) return;
                PlayerPrefs.SetInt(ReadoutKey, value ? 1 : 0);
                PlayerPrefs.Save();
                ReadoutChanged?.Invoke();
            }
        }

        public static event Action ReadoutChanged;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Logging = false;
            ReadoutChanged = null;
        }

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
            ResetTally();
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
            LetGo();
            SkillDefinitionSO skill = skills != null ? skills.GetSkill(skillId) : null;
            if (skill == null || !skill.IsAttack || skill.IsCharged) return;

            MusicConductor conductor = MusicConductor.Instance;
            double now = conductor != null ? conductor.Now : 0.0;
            double error = 0.0, beats = 0.0, position = 0.0;
            MusicTrack track = null;
            bool hasBeat = conductor != null && conductor.TryLocate(pressedAt, out track, out _, out position);
            if (hasBeat)
            {
                double first = conductor.FirstBeatOf(track);
                error = BeatMath.ErrorSeconds(position, first, track.BeatSeconds);
                beats = BeatMath.Beats(position, first, track.BeatSeconds);
            }
            CadenceVerdict verdict = CadenceRules.Judge(settings, combat != null && combat.InCombat, hasBeat, now, pressedAt, error);

            var press = new CadencePress
            {
                Skill = skill.displayName, ErrorMs = (float)(error * 1000.0), HeardAfterMs = (float)((now - pressedAt) * 1000.0),
                Verdict = verdict, Fate = verdict == CadenceVerdict.OnCadence ? CadenceFate.Kept : CadenceFate.None,
                Beats = beats, Track = track, At = Time.time,
            };
            int slot = written % HistorySize;
            history[slot] = press;
            written++;

            if (verdict == CadenceVerdict.OnCadence)
            {
                keptSkill = skill.skillId;
                keptAt = Time.time;
                keptPress = slot;
                PressesOn++;
            }
            else if (verdict == CadenceVerdict.OffBeat) PressesOff++;
            else PressesRefused++;
            if (verdict == CadenceVerdict.OnCadence || verdict == CadenceVerdict.OffBeat)
            {
                errorSum += press.ErrorMs;
                errorSquares += (double)press.ErrorMs * press.ErrorMs;
            }

            LastPress = hasBeat
                ? $"{skill.displayName}: {error * 1000.0:+0;-0;0} ms from the beat, heard of {(now - pressedAt) * 1000.0:0} ms later: {Words(verdict)}."
                : $"{skill.displayName}: {Words(verdict)}.";
            if (Logging) Debug.Log($"[Cadence] {name}: {LastPress}");
        }

        /// <summary>A kept press that was never spent is lost when another press takes its place.</summary>
        private void LetGo()
        {
            if (keptSkill != null && keptPress >= 0 && history[keptPress].Fate == CadenceFate.Kept) history[keptPress].Fate = CadenceFate.Lost;
            keptSkill = null;
            keptPress = -1;
        }

        /// <summary>The verdict in the words a GM tool shows.</summary>
        public static string Words(CadenceVerdict verdict)
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
            int press = keptPress;
            keptSkill = null;
            keptPress = -1;
            if (!settings.enabled) return 1f;
            if (!CadenceRules.StillKept(Time.time, keptAt, settings.keepSeconds))
            {
                if (press >= 0) history[press].Fate = CadenceFate.Lost;
                LastPress = $"{skill.displayName}: was on Cadence, but the cast came more than {settings.keepSeconds:0.#} s after the press.";
                if (Logging) Debug.Log($"[Cadence] {name}: {LastPress}");
                return 1f;
            }

            if (press >= 0) history[press].Fate = CadenceFate.Spent;
            CastsOnCadence++;
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
