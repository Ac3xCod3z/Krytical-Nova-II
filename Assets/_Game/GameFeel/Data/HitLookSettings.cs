using System;
using KryticalNova.Factions;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>What kind of hit it was, as far as its look goes. Each kind has one colour ramp, always the same.</summary>
    public enum HitKind : byte { Normal = 0, Elemental = 1, Critical = 2, Shattered = 3, Reflected = 4 }

    /// <summary>Which of a weapon class's three words a hit shows, if any.</summary>
    public enum HitWord : byte { None = 0, Heavy = 1, Critical = 2, Skill = 3 }

    /// <summary>The look of a critical hit on another player. One is in use; the others are kept to choose from.</summary>
    public enum PvpCritStyle : byte { SplashPanel = 0, ImpactFrame = 1, FactionStrike = 2 }

    /// <summary>How one weapon class's hits look: the shape of its impact and the words it hits with.</summary>
    [Serializable]
    public sealed class WeaponHitLook
    {
        public WeaponClassId weaponClass;
        [Tooltip("The mark an ordinary hit leaves: this class's own shape, drawn in the colours of the kind of hit.")]
        public GameObject lightImpact;
        [Tooltip("The same shape for a heavy hit: a combo finisher, a skill, a critical hit.")]
        public GameObject heavyImpact;
        [Tooltip("The word of a heavy hit that is not critical.")]
        public GameObject heavyWord;
        [Tooltip("The word of a critical hit.")]
        public GameObject criticalWord;
        [Tooltip("The word of the class's signature skills (slots 4, 6 and 8).")]
        public GameObject skillWord;
        [Tooltip("The colour of this class's blade trail and cast slash: what is swung, not what it does to the target.")]
        public Color swing = Color.white;
    }

    /// <summary>One look of the critical hit on another player.</summary>
    [Serializable]
    public sealed class PvpCritLook
    {
        public PvpCritStyle style;
        [Tooltip("The panel that bursts at the hit: turned to the camera. One for all, or one per faction (Hegemony, Null, Covenant) when the look speaks in the attacker's faction's words.")]
        public GameObject[] panels = Array.Empty<GameObject>();
        [Tooltip("Lines of speed drawn through the hit, turned to the camera. Optional.")]
        public GameObject lines;
        [Tooltip("The colour of those lines.")]
        public Color linesColour = Color.white;
        [Tooltip("The panel is drawn in the attacker's faction's colours instead of its own.")]
        public bool factionColours;
        [Tooltip("The panel's own colours when it does not take the faction's.")]
        public ComicRamp ramp;
        [Tooltip("Size of the panel: 1 is the size it was built at (about three metres across).")]
        [Range(0.5f, 3f)] public float scale = 1f;
    }

    /// <summary>
    /// How hits look, in one place. The <b>shape</b> of an impact and the <b>word</b> and lettering say which weapon
    /// class struck; the <b>colour</b> says what kind of hit it was, and a kind always has the same colours, so a
    /// fight between many players stays readable. A critical hit on another player has a look of its own, used for
    /// nothing else. Made by ComicFxBuilder (Krytical Nova ▸ GameFeel ▸ Build Comic Effects); every number here can
    /// be changed on the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Game Feel/Hit Look Settings")]
    public sealed class HitLookSettings : ScriptableObject
    {
        [Header("Colour: one ramp per kind of hit")]
        public ComicRamp normal = new ComicRamp(Color.white, Hex("FFC93C"), Hex("FF9E66"), Hex("14122B"));
        [Tooltip("A telling share of the damage was Elemental (it passes an Aegis).")]
        public ComicRamp elemental = new ComicRamp(Hex("EFFFFF"), Hex("4FE3F0"), Hex("4C52BF"), Hex("14122B"));
        public ComicRamp critical = new ComicRamp(Hex("FFF3C4"), Hex("F03A2E"), Hex("FFC93C"), Hex("14122B"));
        [Tooltip("The blow broke a barrier (Shatterpoint).")]
        public ComicRamp shattered = new ComicRamp(Color.white, Hex("ADE0F2"), Hex("1F6ECC"), Hex("14122B"));
        [Tooltip("The blow was thrown back (Backlash).")]
        public ComicRamp reflected = new ComicRamp(Color.white, Hex("E04FB8"), Hex("7A4FE0"), Hex("14122B"));

        [Header("Shape and word: one look per weapon class")]
        public WeaponHitLook[] classes = Array.Empty<WeaponHitLook>();
        [Tooltip("For a hit by something that has no weapon class: a creature, a GM command.")]
        public WeaponHitLook unarmed = new WeaponHitLook();
        [Tooltip("Every impact mark is drawn this much larger or smaller than it was built.")]
        [Range(0.5f, 2f)] public float markScale = 1f;
        [Tooltip("Every word of a hit is drawn this much larger or smaller than it was built.")]
        [Range(0.5f, 2f)] public float wordScale = 1f;
        [Tooltip("A mark and a word are drawn this many metres nearer the camera than the hit itself, so the body that was hit does not hide them.")]
        [Min(0f)] public float towardCamera = 0.35f;

        [Header("Words")]
        [Tooltip("Metres above the point of the hit a word appears.")]
        [Min(0f)] public float wordHeight = 0.8f;
        [Tooltip("Metres a word is moved sideways at random, so two in a row do not sit on each other.")]
        [Min(0f)] public float wordScatter = 0.35f;
        [Tooltip("One attacker shows at most one word in this many seconds. A critical hit's word is never held back by an ordinary one.")]
        [Min(0f)] public float wordGapSeconds = 0.45f;
        [Tooltip("Words of fights the local player is not part of: at most this many on screen at once.")]
        [Min(0)] public int othersWords = 5;
        [Tooltip("Words of fights the local player is not part of are only shown this close to the camera (metres).")]
        [Min(1f)] public float othersWordDistance = 26f;
        [Tooltip("Seconds a word counts as on screen.")]
        [Min(0.1f)] public float wordSeconds = 0.6f;
        [Tooltip("Impact marks are only drawn this close to the camera (metres).")]
        [Min(1f)] public float impactDistance = 48f;
        [Tooltip("The word shown where a blow is thrown back (Backlash), and where a barrier breaks (Shatterpoint).")]
        public GameObject reflectedWord;
        public GameObject shatteredWord;

        [Header("A critical hit on another player (used for nothing else)")]
        public PvpCritStyle pvpCritStyle = PvpCritStyle.ImpactFrame;
        public PvpCritLook[] pvpCrit = Array.Empty<PvpCritLook>();
        [Tooltip("Seconds both fighters' poses hold as it lands, on the screens of the two of them.")]
        [Range(0f, 0.3f)] public float pvpCritStopSeconds = 0.14f;
        [Tooltip("How much of the screen feedback the one who was hit gets (the attacker gets all of it).")]
        [Range(0f, 1f)] public float pvpCritVictimStrength = 0.6f;
        [Tooltip("One attacker shows this at most once in this many seconds (a three-hit skill can land three critical hits).")]
        [Min(0f)] public float pvpCritGapSeconds = 0.3f;

        [Header("Colours of the factions (looks that speak in the attacker's faction's colours; a level gained)")]
        public ComicRamp hegemony = new ComicRamp(Color.white, Hex("4FE3F0"), Hex("1F6ECC"), Hex("14122B"));
        public ComicRamp nullFaction = new ComicRamp(Color.white, Hex("D8343C"), Hex("7A4FE0"), Hex("14122B"));
        public ComicRamp covenant = new ComicRamp(Color.white, Hex("E8B04A"), Hex("3F8F5A"), Hex("14122B"));

        [Header("Pool: made ready before any fight, so nothing is created during one")]
        [Tooltip("Copies of each impact mark kept ready.")]
        [Min(1)] public int warmImpacts = 4;
        [Tooltip("Copies of each word kept ready.")]
        [Min(1)] public int warmWords = 2;
        [Tooltip("Copies of the spark of a basic attack kept ready. The local player's own are made when none is left; anyone else's are then left out.")]
        [Min(1)] public int warmSparks = 12;
        [Tooltip("Copies of every other pooled effect of the game feel kept ready.")]
        [Min(1)] public int warmOthers = 2;
        [Tooltip("How many copies are made a frame while the pool is being filled.")]
        [Min(1)] public int warmPerFrame = 6;

        public ComicRamp Ramp(HitKind kind)
        {
            switch (kind)
            {
                case HitKind.Elemental: return elemental;
                case HitKind.Critical: return critical;
                case HitKind.Shattered: return shattered;
                case HitKind.Reflected: return reflected;
                default: return normal;
            }
        }

        public ComicRamp Ramp(FactionId faction)
        {
            switch (faction)
            {
                case FactionId.Hegemony: return hegemony;
                case FactionId.Null: return nullFaction;
                case FactionId.Covenant: return covenant;
                default: return normal;
            }
        }

        /// <summary>The look of a weapon class, or the unarmed look when the class has none (a creature, no weapon).</summary>
        public WeaponHitLook Look(WeaponClassId weaponClass)
        {
            for (int i = 0; i < classes.Length; i++)
                if (classes[i] != null && classes[i].weaponClass == weaponClass) return classes[i];
            return unarmed;
        }

        public PvpCritLook PvpCrit() => PvpCrit(pvpCritStyle);

        public PvpCritLook PvpCrit(PvpCritStyle style)
        {
            for (int i = 0; i < pvpCrit.Length; i++)
                if (pvpCrit[i] != null && pvpCrit[i].style == style) return pvpCrit[i];
            return pvpCrit.Length > 0 ? pvpCrit[0] : null;
        }

        private static Color Hex(string rgb) => ColorUtility.TryParseHtmlString("#" + rgb, out Color colour) ? colour : Color.white;
    }

    /// <summary>The rules of a hit's look, free of scene state so they can be tested.</summary>
    public static class HitLooks
    {
        /// <summary>
        /// The kind a hit is shown as. A hit can be several things at once: the rarest wins, so the colour that is
        /// seen is the most telling one. Thrown back, then a barrier broken, then critical, then Elemental.
        /// </summary>
        public static HitKind KindOf(bool critical, bool reflected, bool shattered, bool elemental)
        {
            if (reflected) return HitKind.Reflected;
            if (shattered) return HitKind.Shattered;
            if (critical) return HitKind.Critical;
            return elemental ? HitKind.Elemental : HitKind.Normal;
        }

        /// <summary>True for a hit that gets the heavy impact mark instead of the light one.</summary>
        public static bool IsHeavy(bool critical, int comboStep, HitKind kind) =>
            critical || comboStep >= 3 || kind == HitKind.Shattered || kind == HitKind.Reflected;

        /// <summary>The class skills whose hits carry the class's skill word: the heavy single, the bonus single and the leap.</summary>
        public static bool IsSignature(ClassSkillSlot slot) => slot == ClassSkillSlot.S4 || slot == ClassSkillSlot.S6 || slot == ClassSkillSlot.S8;

        /// <summary>
        /// The word a hit shows. A critical hit shows the critical word; a signature skill's hit the skill word; any
        /// other heavy hit (a combo finisher, another skill) the heavy word; a light hit none.
        /// </summary>
        public static HitWord WordFor(bool critical, int comboStep, ClassSkillSlot skill)
        {
            if (critical) return HitWord.Critical;
            if (IsSignature(skill)) return HitWord.Skill;
            return comboStep >= 3 ? HitWord.Heavy : HitWord.None;
        }

        /// <summary>
        /// May this attacker show a word now? One word per <paramref name="gapSeconds"/>; a critical word is only
        /// held back by another critical word, never by an ordinary one.
        /// </summary>
        public static bool GapAllows(HitWord word, float now, float lastWordTime, HitWord lastWord, float gapSeconds)
        {
            if (word == HitWord.None) return false;
            if (now - lastWordTime >= gapSeconds) return true;
            return word == HitWord.Critical && lastWord != HitWord.Critical;
        }

        /// <summary>
        /// May a word be shown at all? The local player's own fights always show theirs. Others' are shown only near
        /// the camera and only while fewer than <paramref name="othersWords"/> of them are up.
        /// </summary>
        public static bool BudgetAllows(bool localPlayerInvolved, float distanceToCamera, int othersWordsUp, int othersWords, float othersWordDistance)
        {
            if (localPlayerInvolved) return true;
            return distanceToCamera <= othersWordDistance && othersWordsUp < othersWords;
        }

        /// <summary>
        /// A critical hit by one player on another: the hit with the look of its own. A blow thrown back is not one
        /// (no damage was done), and neither is a hit on oneself (a GM preview).
        /// </summary>
        public static bool IsPvpCrit(bool critical, bool reflected, bool attackerIsPlayer, bool targetIsPlayer, bool sameActor) =>
            critical && !reflected && attackerIsPlayer && targetIsPlayer && !sameActor;
    }
}
