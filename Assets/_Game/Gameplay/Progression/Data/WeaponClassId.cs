namespace KryticalNova.Progression
{
    /// <summary>
    /// The nine weapon classes, three per faction in the order offense, defense, ranged. The numeric value is saved
    /// and sent over the network: never renumber, only append. A character's class is the class of the weapon it
    /// holds, so it can change at any time.
    /// </summary>
    public enum WeaponClassId : byte
    {
        None = 0,
        // Hegemony
        Monoblade = 1,
        Glaive = 2,
        Carbine = 3,
        // Null
        Harvester = 4,
        ServoClaws = 5,
        ArcCaster = 6,
        // Covenant
        Longaxe = 7,
        Claymore = 8,
        Warbow = 9
    }

    public enum WeaponClassRole : byte
    {
        Offense = 0,
        Defense = 1,
        Ranged = 2
    }

    /// <summary>The skills every class has. Ranks and unlocks belong to the slot, so they carry across classes.</summary>
    public enum ClassSkillSlot : byte
    {
        None = 0,
        S1 = 1,
        S2 = 2,
        S3 = 3,
        S4 = 4,
        S5 = 5,
        S6 = 6,
        S7 = 7,
        S8 = 8,
        Stance = 9,
        Guard = 10,
        /// <summary>
        /// The single hit and the multi hit of level 60 (owner, 11 Oct 2026). S6 and S7 were those until then; they
        /// are now the level 80 pair, which like the Zenith technique waits for the Prime grounds.
        /// </summary>
        Lv60Single = 11,
        Lv60Multi = 12,
        /// <summary>
        /// The two-hit attacks, one per tier (owner, 16 Oct 2026): levels 1 and 30 from the Instructor, levels 60 and
        /// 80 from a Skill Chip like the single and the multi hit of those levels.
        /// </summary>
        Lv1TwoHit = 13,
        Lv30TwoHit = 14,
        Lv60TwoHit = 15,
        Lv80TwoHit = 16
    }

    public static class WeaponClasses
    {
        public const int Count = 9;
        public const int SlotCount = 16;
        /// <summary>Classes per faction; a faction's classes are consecutive ids.</summary>
        public const int PerFaction = 3;

        public static readonly WeaponClassId[] All =
        {
            WeaponClassId.Monoblade, WeaponClassId.Glaive, WeaponClassId.Carbine,
            WeaponClassId.Harvester, WeaponClassId.ServoClaws, WeaponClassId.ArcCaster,
            WeaponClassId.Longaxe, WeaponClassId.Claymore, WeaponClassId.Warbow
        };

        public static readonly ClassSkillSlot[] Slots =
        {
            ClassSkillSlot.S1, ClassSkillSlot.S2, ClassSkillSlot.S3, ClassSkillSlot.S4, ClassSkillSlot.S5,
            ClassSkillSlot.S6, ClassSkillSlot.S7, ClassSkillSlot.S8, ClassSkillSlot.Stance, ClassSkillSlot.Guard,
            ClassSkillSlot.Lv60Single, ClassSkillSlot.Lv60Multi,
            ClassSkillSlot.Lv1TwoHit, ClassSkillSlot.Lv30TwoHit, ClassSkillSlot.Lv60TwoHit, ClassSkillSlot.Lv80TwoHit
        };

        public static bool IsValid(WeaponClassId id) => id >= WeaponClassId.Monoblade && id <= WeaponClassId.Warbow;

        public static WeaponClassRole RoleOf(WeaponClassId id) =>
            IsValid(id) ? (WeaponClassRole)(((int)id - 1) % PerFaction) : WeaponClassRole.Offense;

        /// <summary>0 = Hegemony, 1 = Null, 2 = Covenant; -1 for None. Matches FactionId minus one.</summary>
        public static int FactionIndexOf(WeaponClassId id) => IsValid(id) ? ((int)id - 1) / PerFaction : -1;

        public static bool IsRanged(WeaponClassId id) => IsValid(id) && RoleOf(id) == WeaponClassRole.Ranged;

        public static string DisplayName(WeaponClassId id)
        {
            switch (id)
            {
                case WeaponClassId.Monoblade: return "Monoblade";
                case WeaponClassId.Glaive: return "Glaive";
                case WeaponClassId.Carbine: return "Carbine";
                case WeaponClassId.Harvester: return "Harvester";
                case WeaponClassId.ServoClaws: return "Servo Claws";
                case WeaponClassId.ArcCaster: return "Arc Caster";
                case WeaponClassId.Longaxe: return "Longaxe";
                case WeaponClassId.Claymore: return "Claymore";
                case WeaponClassId.Warbow: return "Warbow";
                default: return string.Empty;
            }
        }

        /// <summary>Reads a class from user text ("carbine", "servo claws", "ArcCaster"). False when nothing matches.</summary>
        public static bool TryParse(string text, out WeaponClassId id)
        {
            id = WeaponClassId.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string wanted = text.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            foreach (var candidate in All)
            {
                if (!string.Equals(candidate.ToString(), wanted, System.StringComparison.OrdinalIgnoreCase)) continue;
                id = candidate;
                return true;
            }
            return false;
        }

        public static string RoleName(WeaponClassRole role)
        {
            switch (role)
            {
                case WeaponClassRole.Defense: return "Defense";
                case WeaponClassRole.Ranged: return "Ranged";
                default: return "Offense";
            }
        }
    }
}
