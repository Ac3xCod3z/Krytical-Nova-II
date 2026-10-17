using System.Collections.Generic;
using System.Linq;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the skill kits of the nine weapon classes and the class catalog. Every class has the same sixteen slots
    /// (S1 to S8, Stance, Guard, the level 60 pair and the four two-hit attacks). The Monoblade's skills, built and tuned by <see cref="BladeSkillBuilder"/>,
    /// are the template: each other class gets a copy per slot with its own name, animation set, reach and stance
    /// stat. Names come from _Docs/Reference/12Sky/04_Weapon_Classes.md.
    /// Re-runnable: a class skill that already exists keeps its numbers and its animation (so hand tuning survives);
    /// only its name, class, slot and description are refreshed.
    /// </summary>
    public static class ClassSkillBuilder
    {
        private const string SkillRoot = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills";
        private const string MonobladeFolder = SkillRoot + "/BladeSkills";
        public const string CatalogPath = "Assets/_Game/Gameplay/Items/Data/Assets/WeaponClassCatalog.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        private const string GreatSword = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Big_Sword/";
        private const string Bow = "Assets/Synty/AnimationBowCombat/Animations/Polygon/Neutral/Standing/Shoot/";

        // The Monoblade skill in each slot, in slot order.
        private static readonly string[] MonobladeIds =
        {
            "FuryStrike", "HaeseoStroke", "GentlemansPen", "DevilsStrike", "FlashOfRage",
            "SunraysStroke", "PhantomThunder", "ThunderousDragon", "LightningFlash", "AzureWall",
            BladeSkillBuilder.Lv60SingleId, BladeSkillBuilder.Lv60MultiId,
            BladeSkillBuilder.Lv1TwoHitId, BladeSkillBuilder.Lv30TwoHitId, BladeSkillBuilder.Lv60TwoHitId, BladeSkillBuilder.Lv80TwoHitId
        };

        private enum ClipSet
        {
            /// <summary>The template's own clips (Grruzam katana).</summary>
            Katana,
            /// <summary>Grruzam great-sword: the two-handed melee classes.</summary>
            GreatSword,
            /// <summary>Synty Bow Combat.</summary>
            Bow
        }

        private sealed class ClassSeed
        {
            public WeaponClassId id;
            public ClipSet clips;
            public string summary;
            public string[] names; // slot order: S1..S8, Stance, Guard, the level 60 single and multi hit, the two-hit attacks of levels 1, 30, 60 and 80
        }

        // Clips per slot (S1..S8, Stance, Guard, level 60 single, level 60 multi, the four two-hit attacks). Null keeps the template's clip.
        private static readonly string[] GreatSwordClips =
        {
            GreatSword + "3_Skills/M_Big_Sword@Skill_H.FBX",
            GreatSword + "2_Attacks/3__Dash_Attack/M_Big_Sword@Dash_Attack_ver_B.FBX",
            GreatSword + "2_Attacks/0__3Combos/M_Big_Sword@Attack_3Combo_ALL_Inplace.FBX",
            GreatSword + "2_Attacks/5__Upper_Attack/M_Big_Sword@UpperAttack_ZeroHeight.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_B.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_A.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_G_ALL.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_D.FBX",
            GreatSword + "1_Movements/0__Intro/M_Big_Sword@Intro.FBX",
            null,
            GreatSword + "3_Skills/M_Big_Sword@Skill_C.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_E.FBX",
            // The two-hit tier: clips no skill used yet.
            GreatSword + "2_Attacks/3__Dash_Attack/M_Big_Sword@Dash_Attack_ver_A.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_F.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_I.FBX",
            GreatSword + "3_Skills/M_Big_Sword@Skill_K.FBX"
        };

        private static readonly string[] BowClips =
        {
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Leaping_Neut.fbx",
            null,
            null,
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            Bow + "A_POLY_BOW_Stand_Shoot_Reload_Neut.fbx",
            // The two-hit tier: the pack has no two-shot clip, so each tier takes another of its bow grips.
            Bow + "Cmp/A_POLY_BOW_Cmp_Stand_Shoot_Reload_Neut.fbx",
            Bow + "Lng/A_POLY_BOW_Lng_Stand_Shoot_Reload_Neut.fbx",
            Bow + "Rcv/A_POLY_BOW_Rcv_Stand_Shoot_Reload_Neut.fbx",
            Bow + "Rcv/A_POLY_BOW_Rcv_Stand_Shoot_Leaping_Neut.fbx"
        };

        private static readonly ClassSeed[] Classes =
        {
            new ClassSeed
            {
                id = WeaponClassId.Monoblade, clips = ClipSet.Katana, summary = "A single-edged blade. Fast, clean cuts on the front line.",
                names = new[] { "Perimeter Cut", "Clean Cut", "Signature Series", "Severance", "Hostile Takeover", "Zero Hour", "Liquidation", "Market Crash", "Overclock", "Firewall", "Margin Call", "Short Squeeze", "Double Entry", "Split Decision", "Countersign", "Double Jeopardy" }
            },
            new ClassSeed
            {
                id = WeaponClassId.Glaive, clips = ClipSet.GreatSword, summary = "A polearm that holds a line and controls the space around it.",
                names = new[] { "Riot Sweep", "Suppression Thrust", "Compliance Combo", "Curfew", "Cease and Desist", "Lockstep", "Full Enforcement", "Martial Law", "Bulwark Protocol", "Shield Field", "Injunction", "Crackdown", "Baton Pair", "Summons", "Double Warrant", "Due Process" }
            },
            new ClassSeed
            {
                id = WeaponClassId.Carbine, clips = ClipSet.Katana, summary = "A firearm. Picks targets apart from range.",
                names = new[] { "Spray Pattern", "Clean Shot", "Triple Tap", "Headhunter", "Burst Contract", "Longshot", "Terminal Burst", "Orbital Call", "Target Lock", "Reactive Plating", "Deadeye", "Suppressing Fire", "Double Tap", "Controlled Pair", "Crossfire", "Twin Verdict" }
            },
            new ClassSeed
            {
                id = WeaponClassId.Harvester, clips = ClipSet.GreatSword, summary = "A reaping blade. Wide, heavy swings that cut down whatever is close.",
                names = new[] { "Reaping Cycle", "Culling Stroke", "Threshing Combo", "Scrap Sentence", "Hydraulic Flurry", "Extinction Blow", "Kill Loop", "Terminal Harvest", "Kill Protocol", "Null Shell", "Shear Line", "Combine Pass", "Twin Reap", "Bale and Bind", "Gleaning Pass", "Final Yield" }
            },
            new ClassSeed
            {
                id = WeaponClassId.ServoClaws, clips = ClipSet.Katana, summary = "Powered claws. Close, relentless and hard to move.",
                names = new[] { "Shockwave Stomp", "Piston Jab", "Servo Combo", "Compactor", "Overdrive Cycle", "Core Breach", "Three-Phase Rend", "Zero Point", "Lockdown Plating", "Faraday Skin", "Ram Drive", "Ratchet Rush", "One-Two Piston", "Crankshaft", "Twin Clamp", "Dual Overload" }
            },
            new ClassSeed
            {
                id = WeaponClassId.ArcCaster, clips = ClipSet.Katana, summary = "An energy projector. Burns targets down from a distance.",
                names = new[] { "Arc Flare", "Lance Shot", "Tri-Bolt", "Ion Spike", "Cascade Fire", "Sunspot", "Chain Storm", "Extinction Beam", "Capacitor", "Static Veil", "Flashpoint", "Arc Volley", "Twin Spark", "Dipole", "Alternating Current", "Double Discharge" }
            },
            new ClassSeed
            {
                id = WeaponClassId.Longaxe, clips = ClipSet.GreatSword, summary = "A two-handed axe. Slow to raise, final when it lands.",
                names = new[] { "Reaping Arc", "Hewing Blow", "Clan Rhythm", "Oathbreaker", "Frostbite Flurry", "Skysplitter", "Wolfpack", "Northfall", "Blood Oath", "Ancestor's Ward", "Bonecleaver", "Raven's Feast", "Two Ravens", "Brother's Bite", "Twinblood", "Fenris Jaws" }
            },
            new ClassSeed
            {
                id = WeaponClassId.Claymore, clips = ClipSet.GreatSword, summary = "A great sword. The clan's shield as much as its blade.",
                names = new[] { "Shieldwall Sweep", "Steadfast Cut", "Three Oaths", "Bulwark Strike", "Hearthguard Flurry", "Dawnguard", "Highguard Trinity", "Kingsfall", "Shieldwall", "Stoneheart", "Oathkeeper", "Vanguard's Tide", "Oath and Answer", "Twin Vows", "Shield and Sword", "Kinsguard" }
            },
            new ClassSeed
            {
                id = WeaponClassId.Warbow, clips = ClipSet.Bow, summary = "A war bow. Light on its feet, deadly at range.",
                names = new[] { "Arrow Rain", "Hunter's Mark", "Three Feathers", "Heartseeker", "Volley", "Moonshot", "Stormflight", "Skyfall", "Windwalk", "Barkskin", "Falcon's Eye", "Swiftwind", "Two Feathers", "Paired Flight", "Twin Talons", "Wind and Thunder" }
            },
        };

        // Ranged single-target reach by rank (melee is 2). Area skills keep their radius.
        private const float RangedMinReach = 8f, RangedMaxReach = 12f;
        private const float StanceMin = 2f, StanceMax = 20f;

        [MenuItem("Krytical Nova/Progression/Build Class Skills")]
        public static void Build()
        {
            var templates = new SkillDefinitionSO[WeaponClasses.SlotCount];
            for (int i = 0; i < MonobladeIds.Length; i++)
            {
                templates[i] = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>($"{MonobladeFolder}/Skill_{MonobladeIds[i]}.asset");
                if (templates[i] == null)
                {
                    Debug.LogError($"[ClassSkills] Monoblade skill '{MonobladeIds[i]}' not found. Run 'Krytical Nova/Progression/Build Blade Skills' first.");
                    return;
                }
            }

            var entries = new List<WeaponClassCatalog.Entry>();
            int created = 0;
            foreach (var seed in Classes)
            {
                var role = WeaponClasses.RoleOf(seed.id);
                var skills = new SkillDefinitionSO[WeaponClasses.SlotCount];
                for (int i = 0; i < WeaponClasses.SlotCount; i++)
                {
                    var slot = WeaponClasses.Slots[i];
                    SkillDefinitionSO skill;
                    string feel = BorrowedFeel(slot);
                    if (seed.id == WeaponClassId.Monoblade)
                    {
                        skill = templates[i];
                        skill.presentationId = feel ?? string.Empty;
                    }
                    else
                    {
                        skill = LoadOrClone(seed, slot, templates[i], out bool isNew);
                        if (isNew)
                        {
                            created++;
                            ApplyClassShape(skill, seed, role, slot, i);
                        }
                        skill.skillId = $"{seed.id}_{slot}";
                        skill.presentationId = feel ?? templates[i].skillId;
                        skill.description = Describe(seed.id, role, slot);
                        // What ranks and levels a slot asks is the same in every class: it follows the Monoblade's when that changes.
                        skill.requiredCharacterLevel = templates[i].requiredCharacterLevel;
                        skill.requiresSkillBook = templates[i].requiresSkillBook;
                    }
                    skill.displayName = seed.names[i];
                    skill.weaponClass = seed.id;
                    skill.classSlot = slot;
                    // A Stance of rank 20 or more is given to the Squad too (owner, 16 Oct 2026; SquadSettings).
                    skill.sharesWithSquad = slot == ClassSkillSlot.Stance;
                    EditorUtility.SetDirty(skill);
                    skills[i] = skill;
                }

                entries.Add(new WeaponClassCatalog.Entry
                {
                    id = seed.id, displayName = WeaponClasses.DisplayName(seed.id), faction = WeaponClassRules.FactionOf(seed.id),
                    role = role, summary = seed.summary, stanceSummary = $"{seed.names[8]}: {StanceEffect(role)}", skills = skills
                });
            }

            var catalog = AssetDatabase.LoadAssetAtPath<WeaponClassCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WeaponClassCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var shared = SharedSkills();
            NameSharedSkills(shared);
            catalog.SetContents(entries.ToArray(), shared);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            bool wired = WirePlayerPrefab(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ClassSkills] {entries.Count} classes, {entries.Count * WeaponClasses.SlotCount} class skills ({created} new), " +
                      $"{catalog.SharedSkills.Count} shared skills in {CatalogPath}.{(wired ? " Player prefab given its PlayerWeaponClass." : string.Empty)}");
        }

        /// <summary>The skills every class can use: what the player prefab lists that is not a class skill.</summary>
        private static SkillDefinitionSO[] SharedSkills()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var combatSkills = prefab != null ? prefab.GetComponent<PlayerCombatSkills>() : null;
            if (combatSkills == null) return System.Array.Empty<SkillDefinitionSO>();
            return combatSkills.AvailableSkills.Where(s => s != null && !s.IsClassSkill && System.Array.IndexOf(MonobladeIds, s.skillId) < 0).ToArray();
        }

        /// <summary>
        /// What the shared skills are called and say. They are the same assets for every class, so every faction
        /// reads the same names. Ids, asset names and effect ids keep their first names (SpiritualRage, HeavenlyShield).
        /// </summary>
        private static readonly Dictionary<string, (string Name, string Description)> SharedSkillText =
            new Dictionary<string, (string, string)>
            {
                ["SpiritualRage"] = ("Redline",
                    "Hold to push past the redline. Your next hit deals greatly increased damage with a higher Critical rate " +
                    "(the first hit of a multi-hit skill). A three-hit skill pressed on the beat keeps the charge for all " +
                    "three hits, each a Critical, until one misses. The charge fades if unused."),
                ["HeavenlySerenity"] = ("Second Wind",
                    "Take a knee and catch your breath, restoring HP and Flux every second for 20s. Higher ranks restore more " +
                    "each second. Interrupted by taking damage or moving."),
                ["HeavenlyShield"] = ("Aegis",
                    "Raises a barrier worth 111% - 150% of your HP. Physical damage hits the barrier first; once it breaks, " +
                    "damage reaches your HP. Elemental damage goes straight through it."),
                ["HeavenlyBlow"] = ("Killer Instinct",
                    "Sharpens your eye for the fatal opening, raising your Critical chance by 12% - 75%."),
                ["GarudasPrayer"] = ("Hot Streak",
                    "Fortune runs your way: Luck is raised by 13% - 100% (500 Luck becomes 1000 at max rank). " +
                    "Luck improves your odds at the Armorer's forge."),
                ["HeavenlyRepulsion"] = ("Backlash",
                    "1% - 10% chance to throw a physical hit back at the attacker in full and take none of it " +
                    "(no effect on Elemental damage; the returned damage ignores Aegis)."),
                ["HeavenlyDestruction"] = ("Shatterpoint",
                    "While active, every attack has a 1% - 20% chance to shatter the enemy's Aegis and strip one of their buffs."),
                ["ReverseFlow"] = ("Iron Nerve",
                    "Steels you against being locked down: Lockup resistance is raised by 1% - 20%."),
            };

        /// <summary>The display name of a shared skill by id, or null when it has none in the table.</summary>
        public static string SharedSkillName(string skillId) =>
            SharedSkillText.TryGetValue(skillId, out var text) ? text.Name : null;

        /// <summary>Writes the table above onto the shared skill assets. Safe to run again.</summary>
        public static int NameSharedSkills(IEnumerable<SkillDefinitionSO> shared)
        {
            int named = 0;
            foreach (var skill in shared)
            {
                if (skill == null || !SharedSkillText.TryGetValue(skill.skillId, out var text)) continue;
                // Where it comes from, in the same terms: an Instructor, a Skill Chip, or a Skill Chip / Dossier.
                bool crafted = skill.requiredCraftingArchives != null && skill.requiredCraftingArchives.Length > 0;
                string source = !skill.requiresSkillBook ? "Instructor"
                    : $"Level {skill.requiredCharacterLevel} Skill Chip{(crafted ? " or Dossier" : string.Empty)}";
                if (skill.displayName == text.Name && skill.description == text.Description && skill.acquisitionNote == source) continue;
                skill.displayName = text.Name;
                skill.description = text.Description;
                skill.acquisitionNote = source;
                EditorUtility.SetDirty(skill);
                named++;
            }
            return named;
        }

        /// <summary>Names the shared skills without rebuilding the class kits.</summary>
        public static int NameSharedSkills()
        {
            int named = NameSharedSkills(SharedSkills());
            if (named > 0) AssetDatabase.SaveAssets();
            return named;
        }

        private static SkillDefinitionSO LoadOrClone(ClassSeed seed, ClassSkillSlot slot, SkillDefinitionSO template, out bool isNew)
        {
            string folder = $"{SkillRoot}/{seed.id}Skills";
            WeatherVfxBuilder.EnsureFolder(folder);
            string path = $"{folder}/Skill_{seed.id}_{slot}.asset";
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(path);
            isNew = skill == null;
            if (!isNew) return skill;
            skill = Object.Instantiate(template); // every number, the icon and the hit timing of the Monoblade's slot
            AssetDatabase.CreateAsset(skill, path);
            return skill;
        }

        /// <summary>What differs from the Monoblade for a new class skill: its animation, its reach and its stance stat.</summary>
        private static void ApplyClassShape(SkillDefinitionSO skill, ClassSeed seed, WeaponClassRole role, ClassSkillSlot slot, int slotIndex)
        {
            string[] clips = seed.clips == ClipSet.GreatSword ? GreatSwordClips : seed.clips == ClipSet.Bow ? BowClips : null;
            string clipPath = clips != null ? clips[slotIndex] : null;
            if (clipPath != null)
            {
                var clip = LoadClip(clipPath);
                if (clip != null && clip != skill.castClip)
                {
                    skill.castClip = clip;
                    // The Monoblade's hit moments were measured on its own clips; a new clip starts from the defaults.
                    if (skill.IsAttack)
                    {
                        bool area = skill.targetType == SkillTargetType.AreaOfEffect;
                        skill.hitTimesNormalized = BladeSkillBuilder.DefaultHitTimes(skill.hitCount, area);
                        skill.castLiftCurve = slot == ClassSkillSlot.S8 ? BladeSkillBuilder.BakeLift(clip) : new AnimationCurve();
                    }
                }
            }

            if (role == WeaponClassRole.Ranged && skill.IsAttack && skill.targetType == SkillTargetType.SingleTarget)
            {
                skill.minRange = RangedMinReach;
                skill.maxRange = RangedMaxReach;
            }

            if (slot == ClassSkillSlot.Stance)
            {
                switch (role)
                {
                    case WeaponClassRole.Defense:
                        skill.rankScaledModifiers = new[] { new RankScaledModifier(StatType.DefensePower, ModifierType.PercentAdd, StanceMin, StanceMax) };
                        break;
                    case WeaponClassRole.Ranged:
                        skill.rankScaledModifiers = new[]
                        {
                            new RankScaledModifier(StatType.EvasionRate, ModifierType.PercentAdd, StanceMin, StanceMax),
                            new RankScaledModifier(StatType.MoveSpeedPercent, ModifierType.Flat, StanceMin, StanceMax),
                        };
                        break;
                    default:
                        skill.rankScaledModifiers = new[] { new RankScaledModifier(StatType.AttackPower, ModifierType.PercentAdd, StanceMin, StanceMax) };
                        break;
                }
            }
        }

        /// <summary>
        /// The hit feel a slot without one of its own borrows, by Monoblade id; null when it has its own. The level 60
        /// pair borrows the level 80 pair's; the two-hit attacks borrow the single hit of their level.
        /// </summary>
        private static string BorrowedFeel(ClassSkillSlot slot)
        {
            switch (slot)
            {
                case ClassSkillSlot.Lv60Single: return "SunraysStroke";
                case ClassSkillSlot.Lv60Multi: return "PhantomThunder";
                case ClassSkillSlot.Lv1TwoHit: return "HaeseoStroke";
                case ClassSkillSlot.Lv30TwoHit: return "DevilsStrike";
                case ClassSkillSlot.Lv60TwoHit: return "SunraysStroke";
                case ClassSkillSlot.Lv80TwoHit: return "SunraysStroke";
                default: return null;
            }
        }

        private static string StanceEffect(WeaponClassRole role)
        {
            switch (role)
            {
                case WeaponClassRole.Defense: return "raises Defense";
                case WeaponClassRole.Ranged: return "raises Evasion and Move Speed";
                default: return "raises Attack Power";
            }
        }

        private static string Describe(WeaponClassId id, WeaponClassRole role, ClassSkillSlot slot)
        {
            bool ranged = role == WeaponClassRole.Ranged;
            string hit = ranged ? "shot" : "strike";
            string hits = ranged ? "shots" : "strikes";
            switch (slot)
            {
                case ClassSkillSlot.S1: return $"Hits every enemy around you. Hold the key to keep {(ranged ? "firing" : "striking")}.";
                case ClassSkillSlot.S2: return $"A single {hit} at your target.";
                case ClassSkillSlot.S3: return $"Three quick {hits} at your target.";
                case ClassSkillSlot.S4: return $"A heavier single {hit} for a seasoned {WeaponClasses.DisplayName(id)}.";
                case ClassSkillSlot.S5: return $"A three-{hit} flurry at your target.";
                case ClassSkillSlot.S6: return $"A single {hit} that lands with extra force.";
                case ClassSkillSlot.S7: return $"Three {hits} that each land with extra force.";
                case ClassSkillSlot.Lv60Single: return $"A hard single {hit} that lands with some extra force.";
                case ClassSkillSlot.Lv60Multi: return $"Three hard {hits} that each land with some extra force.";
                case ClassSkillSlot.Lv1TwoHit: return $"Two quick {hits} at your target.";
                case ClassSkillSlot.Lv30TwoHit: return $"Two heavier {hits} for a seasoned {WeaponClasses.DisplayName(id)}.";
                case ClassSkillSlot.Lv60TwoHit: return $"Two hard {hits} that each land with some extra force.";
                case ClassSkillSlot.Lv80TwoHit: return $"Two {hits} that each land with extra force.";
                case ClassSkillSlot.S8: return $"Zenith technique. Crashes down on every enemy around you. Hold the key to keep {(ranged ? "firing" : "striking")}.";
                case ClassSkillSlot.Stance:
                    return role == WeaponClassRole.Defense ? "Sets your stance, raising your Defense."
                        : ranged ? "Lightens your step, raising Evasion and Move Speed."
                        : "Quickens your weapon, raising all damage you deal.";
                default: return "Hardens your guard, raising Elemental Defense.";
            }
        }

        /// <summary>What a class calls the skill in a slot, or null.</summary>
        public static string SkillName(WeaponClassId id, ClassSkillSlot slot)
        {
            int index = System.Array.IndexOf(WeaponClasses.Slots, slot);
            foreach (ClassSeed seed in Classes)
                if (seed.id == id) return index >= 0 && index < seed.names.Length ? seed.names[index] : null;
            return null;
        }

        private static AnimationClip LoadClip(string fbxPath)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Debug.LogWarning($"[ClassSkills] No animation clip found in {fbxPath}; the Monoblade's clip stays as a stand-in.");
            return clip;
        }

        /// <summary>The player needs the component that works out its class from the weapon in hand.</summary>
        private static bool WirePlayerPrefab(WeaponClassCatalog catalog)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var component = root.GetComponent<PlayerWeaponClass>();
                bool added = component == null;
                if (added) component = root.AddComponent<PlayerWeaponClass>();
                var so = new SerializedObject(component);
                var property = so.FindProperty("catalog");
                if (!added && property.objectReferenceValue == catalog) return false;
                property.objectReferenceValue = catalog;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
