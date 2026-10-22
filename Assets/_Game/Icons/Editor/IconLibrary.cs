using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>What an icon is for. A skill is matched to icons of its own use.</summary>
    public enum IconUse
    {
        Any = 0,
        Attack = 1,
        Buff = 2,
        Debuff = 3,
        Passive = 4,
        Movement = 5,
        Item = 6,
    }

    /// <summary>What an icon is given to.</summary>
    public enum IconTargetKind
    {
        Skill = 0,
        MovementSkill = 1,
        Item = 2,
        /// <summary>A passive of the Arsenal that is not a skill asset (Wallstride, Waterstride): its settings hold the icon.</summary>
        Passive = 3,
    }

    /// <summary>The factions an icon suits.</summary>
    [Flags]
    public enum IconFactions
    {
        None = 0,
        Hegemony = 1,
        Null = 2,
        Covenant = 4,
        All = Hegemony | Null | Covenant,
    }

    /// <summary>What a skill or an item is looking for in an icon.</summary>
    public struct IconRequest
    {
        public IconUse use;
        public IconFactions faction;
        public WeaponClassId weaponClass;
        public string race;
        public List<string> keywords;
    }

    /// <summary>
    /// The record of every icon the game takes from the icon packs: which sets the packs hold, what each icon is
    /// good for (race, faction, class or weapon line, use, and words for what it shows), and which icon every skill
    /// and item has been given. Icons are found for a skill from these tags, and no icon is ever given twice. The
    /// packs' own files are only read: what the game shows is a treated copy (<see cref="IconBaker"/>).
    /// Edit it in Krytical Nova ▸ Icons ▸ Icon Library.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Icons/Icon Library", fileName = "IconLibrary")]
    public sealed class IconLibrary : ScriptableObject
    {
        public const string AssetPath = "Assets/_Game/Icons/Data/Assets/IconLibrary.asset";

        /// <summary>One set of a pack: a folder of numbered icons painted as a family.</summary>
        [Serializable]
        public sealed class Set
        {
            [Tooltip("The set's name: pack and category, for example 'Fantasy/Swordman Skill'.")]
            public string id;
            [Tooltip("The folder the set's pictures are read from. Never written to.")]
            public string folder;
            [Tooltip("Square = a full painting that becomes a rounded tile (skills). Cut Out = an object on a clear ground that keeps its outline (items).")]
            public ComicIconShape shape;
            public int count;

            [Header("What every icon of the set is, unless the icon says otherwise")]
            public IconUse use;
            public IconFactions factions;
            [Tooltip("The classes (weapon lines) the set is drawn for. Empty = any.")]
            public List<WeaponClassId> weaponClasses = new List<WeaponClassId>();
            [Tooltip("The lineages the set suits (Civilian, Soldier, Robot, Skeleton, Elf, Viking). Empty = any.")]
            public List<string> races = new List<string>();
            public List<string> keywords = new List<string>();

            [Header("Colour")]
            [Tooltip("On when a class wears a set painted in another colour: the set's own colour is moved round the colour wheel.")]
            public bool moveHue;
            [Tooltip("The colour the set is painted in, in degrees on the colour wheel (0 red, 120 green, 240 blue).")]
            [Range(0f, 360f)] public float hueFrom;
            [Tooltip("The colour it is moved to.")]
            [Range(0f, 360f)] public float hueTo;
            [Tooltip("How far from the set's own colour a hue may be and still be moved, in degrees. Skin, metal and other colours outside it are left alone.")]
            [Range(0f, 180f)] public float hueBand = 45f;
        }

        /// <summary>The tags of one icon. Only icons somebody has looked at have one; the rest go by their set.</summary>
        [Serializable]
        public sealed class Icon
        {
            public string set;
            public int number;
            [Tooltip("On = this icon has a use of its own; off = the set's.")]
            public bool ownUse;
            public IconUse use;
            [Tooltip("None = the set's.")]
            public IconFactions factions;
            [Tooltip("Empty = the set's.")]
            public List<WeaponClassId> weaponClasses = new List<WeaponClassId>();
            public List<string> races = new List<string>();
            [Tooltip("Words a skill is matched on: what kind of move it suits (sweep, single, triple, heavy, leap, shield, speed ...).")]
            public List<string> keywords = new List<string>();
            [Tooltip("What the picture shows, in plain words.")]
            public string shows;
        }

        /// <summary>One skill or item and the icon it has been given.</summary>
        [Serializable]
        public sealed class Assignment
        {
            public IconTargetKind kind;
            [Tooltip("The skill's id, the movement skill's id (Run, Jump) or the item asset's name.")]
            public string target;
            public string set;
            public int number;
            [Tooltip("Picked by a person: suggestions never replace it.")]
            public bool chosen;
        }

        public List<Set> sets = new List<Set>();
        public List<Icon> icons = new List<Icon>();
        public List<Assignment> assignments = new List<Assignment>();

        // ---------------------------------------------------------------- looking things up

        public Set FindSet(string id)
        {
            foreach (Set set in sets)
                if (set != null && set.id == id) return set;
            return null;
        }

        public Icon FindIcon(string set, int number)
        {
            foreach (Icon icon in icons)
                if (icon != null && icon.number == number && icon.set == set) return icon;
            return null;
        }

        /// <summary>The icon's own tags, made if it has none yet.</summary>
        public Icon TagsFor(string set, int number)
        {
            Icon icon = FindIcon(set, number);
            if (icon != null) return icon;
            icon = new Icon { set = set, number = number };
            icons.Add(icon);
            return icon;
        }

        public Assignment AssignmentOf(IconTargetKind kind, string target)
        {
            foreach (Assignment a in assignments)
                if (a != null && a.kind == kind && a.target == target) return a;
            return null;
        }

        /// <summary>Who has this icon, or null when it is free.</summary>
        public Assignment UserOf(string set, int number)
        {
            foreach (Assignment a in assignments)
                if (a != null && a.number == number && a.set == set) return a;
            return null;
        }

        // ---------------------------------------------------------------- what an icon is

        public IconUse UseOf(Set set, Icon icon) => icon != null && icon.ownUse ? icon.use : set != null ? set.use : IconUse.Any;

        public IconFactions FactionsOf(Set set, Icon icon) =>
            icon != null && icon.factions != IconFactions.None ? icon.factions : set != null ? set.factions : IconFactions.None;

        public bool SuitsClass(Set set, Icon icon, WeaponClassId weaponClass, out bool drawnForAClass)
        {
            List<WeaponClassId> classes = icon != null && icon.weaponClasses.Count > 0 ? icon.weaponClasses : set != null ? set.weaponClasses : null;
            drawnForAClass = classes != null && classes.Count > 0;
            return drawnForAClass && classes.Contains(weaponClass);
        }

        // ---------------------------------------------------------------- giving icons out

        /// <summary>
        /// Gives a skill or an item an icon. Refused (with the reason) when the icon is not in the library or another
        /// skill or item already has it: no icon is ever used twice.
        /// </summary>
        public bool Assign(IconTargetKind kind, string target, string set, int number, bool chosen, out string refusal)
        {
            refusal = null;
            Set owner = FindSet(set);
            if (owner == null) { refusal = $"There is no set '{set}' in the library."; return false; }
            if (number < 1 || (owner.count > 0 && number > owner.count)) { refusal = $"'{set}' has no icon {number}."; return false; }
            Assignment user = UserOf(set, number);
            if (user != null && (user.kind != kind || user.target != target))
            {
                refusal = $"{set} {number:00} is already the icon of {user.target}.";
                return false;
            }
            Assignment mine = AssignmentOf(kind, target);
            if (mine == null)
            {
                mine = new Assignment { kind = kind, target = target };
                assignments.Add(mine);
            }
            mine.set = set;
            mine.number = number;
            mine.chosen = chosen;
            return true;
        }

        public bool Unassign(IconTargetKind kind, string target)
        {
            Assignment mine = AssignmentOf(kind, target);
            return mine != null && assignments.Remove(mine);
        }

        /// <summary>
        /// The free icons that suit a request, best first. An icon must be of the right use; one drawn for the
        /// skill's own class comes before one drawn for no class, and one drawn for another class comes last.
        /// </summary>
        public List<(Set set, int number, int score)> Suggest(in IconRequest request, int most)
        {
            var found = new List<(Set set, int number, int score)>();
            foreach (Set set in sets)
            {
                if (set == null || set.count <= 0) continue;
                for (int number = 1; number <= set.count; number++)
                {
                    if (UserOf(set.id, number) != null) continue;
                    Icon icon = FindIcon(set.id, number);
                    if (!Score(set, icon, request, out int score)) continue;
                    found.Add((set, number, score));
                }
            }
            found.Sort((a, b) =>
            {
                if (a.score != b.score) return b.score.CompareTo(a.score);
                int bySet = string.CompareOrdinal(a.set.id, b.set.id);
                return bySet != 0 ? bySet : a.number.CompareTo(b.number);
            });
            if (most > 0 && found.Count > most) found.RemoveRange(most, found.Count - most);
            return found;
        }

        /// <summary>How well an icon suits a request; false when it does not suit at all.</summary>
        public bool Score(Set set, Icon icon, in IconRequest request, out int score)
        {
            score = 0;
            IconUse use = UseOf(set, icon);
            if (request.use != IconUse.Any)
            {
                if (use == request.use) score += 40;
                else if (use == IconUse.Any) score += 10;
                else return false;
            }

            if (request.weaponClass != WeaponClassId.None)
            {
                if (SuitsClass(set, icon, request.weaponClass, out bool drawnForAClass)) score += 30;
                else if (!drawnForAClass) score += 5;
                else score -= 20;
            }

            IconFactions factions = FactionsOf(set, icon);
            if (request.faction != IconFactions.None)
            {
                if (factions == IconFactions.None || factions == IconFactions.All) score += 5;
                else if ((factions & request.faction) != 0) score += 15;
                else score -= 10;
            }

            if (!string.IsNullOrEmpty(request.race) && (Has(icon?.races, request.race) || Has(set.races, request.race))) score += 10;

            if (request.keywords != null)
            {
                foreach (string word in request.keywords)
                {
                    if (string.IsNullOrEmpty(word)) continue;
                    if (Has(icon?.keywords, word) || Has(set.keywords, word)) score += 8;
                    else if (icon != null && !string.IsNullOrEmpty(icon.shows)
                             && icon.shows.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) score += 4;
                }
            }
            return true;
        }

        private static bool Has(List<string> words, string word)
        {
            if (words == null) return false;
            foreach (string w in words)
                if (string.Equals(w, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------------------------------------------------------------- what a skill asks for

        // The move each of a class's ten slots is: the words its icon is matched on.
        private static readonly string[][] SlotWords =
        {
            new string[0],
            new[] { "sweep", "area" }, new[] { "single" }, new[] { "triple" }, new[] { "heavy", "single" }, new[] { "flurry", "triple" },
            new[] { "single", "bright" }, new[] { "triple", "bright" }, new[] { "leap", "area" }, new[] { "stance" }, new[] { "guard", "shield" },
        };

        private static readonly HashSet<string> SmallWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "and", "the", "of", "to", "in", "on", "for", "by", "with", "at", "as", "is", "it", "its", "or", "one", "per", "from", "that",
        };

        public static IconFactions FactionsOf(WeaponClassId weaponClass)
        {
            switch (WeaponClassRules.FactionOf(weaponClass))
            {
                case FactionId.Hegemony: return IconFactions.Hegemony;
                case FactionId.Null: return IconFactions.Null;
                case FactionId.Covenant: return IconFactions.Covenant;
                default: return IconFactions.All;
            }
        }

        public static IconRequest RequestFor(SkillDefinitionSO skill)
        {
            var request = new IconRequest
            {
                use = skill.IsAttack ? IconUse.Attack : skill.executionType == SkillExecutionType.Passive ? IconUse.Passive : IconUse.Buff,
                weaponClass = skill.weaponClass,
                faction = FactionsOf(skill.weaponClass),
                keywords = new List<string>(),
            };
            int slot = (int)skill.classSlot;
            if (slot > 0 && slot < SlotWords.Length) request.keywords.AddRange(SlotWords[slot]);
            AddWords(request.keywords, skill.displayName);
            AddWords(request.keywords, skill.description);
            return request;
        }

        public static IconRequest RequestFor(MovementSkillData skill)
        {
            var request = new IconRequest { use = IconUse.Movement, faction = IconFactions.All, keywords = new List<string>() };
            AddWords(request.keywords, skill.displayName);
            AddWords(request.keywords, skill.description);
            return request;
        }

        /// <summary>A passive of the Strides tab (Wallstride, Waterstride): a movement icon found by its words.</summary>
        public static IconRequest RequestForPassive(string displayName, string words)
        {
            var request = new IconRequest { use = IconUse.Movement, faction = IconFactions.All, keywords = new List<string>() };
            AddWords(request.keywords, displayName);
            AddWords(request.keywords, words);
            return request;
        }

        public static IconRequest RequestForItem(string displayName, string description)
        {
            var request = new IconRequest { use = IconUse.Item, faction = IconFactions.All, keywords = new List<string>() };
            AddWords(request.keywords, displayName);
            AddWords(request.keywords, description);
            return request;
        }

        private static void AddWords(List<string> into, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (string raw in text.Split(new[] { ' ', ',', '.', ':', ';', '(', ')', '-', '/', '\'', '\n', '\r', '\t', '%', '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string word = raw.Trim().ToLowerInvariant();
                if (word.Length < 3 || SmallWords.Contains(word) || char.IsDigit(word[0]) || into.Contains(word)) continue;
                into.Add(word);
            }
        }

        // ---------------------------------------------------------------- checking

        /// <summary>Every icon that two targets share, as a message each. An empty list is what is wanted.</summary>
        public List<string> Duplicates()
        {
            var problems = new List<string>();
            for (int i = 0; i < assignments.Count; i++)
            {
                for (int j = i + 1; j < assignments.Count; j++)
                {
                    Assignment a = assignments[i], b = assignments[j];
                    if (a == null || b == null) continue;
                    if (a.kind == b.kind && a.target == b.target) problems.Add($"{a.target} is in the library twice.");
                    else if (a.number == b.number && a.set == b.set) problems.Add($"{a.set} {a.number:00} is the icon of both {a.target} and {b.target}.");
                }
            }
            return problems;
        }
    }
}
