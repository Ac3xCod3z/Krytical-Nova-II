using System.Collections.Generic;
using System.Linq;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The nine weapon classes: three per faction, sixteen skills each with the names from the class doc, ranks shared by
    /// slot, and a Discipline that must belong to the character's faction.
    /// Run "Krytical Nova/Progression/Build Class Skills" after changing a class.
    /// </summary>
    public sealed class WeaponClassTests
    {
        private const string CatalogPath = "Assets/_Game/Gameplay/Items/Data/Assets/WeaponClassCatalog.asset";

        // _Docs/Reference/12Sky/04_Weapon_Classes.md section 3, slot order S1..S8, Stance, Guard, the level 60 single hit, the level 60 multi hit,
        // the two-hit attacks of levels 1, 30, 60 and 80.
        private static readonly Dictionary<WeaponClassId, string[]> Names = new Dictionary<WeaponClassId, string[]>
        {
            { WeaponClassId.Monoblade, new[] { "Perimeter Cut", "Clean Cut", "Signature Series", "Severance", "Hostile Takeover", "Zero Hour", "Liquidation", "Market Crash", "Overclock", "Firewall", "Margin Call", "Short Squeeze", "Double Entry", "Split Decision", "Countersign", "Double Jeopardy" } },
            { WeaponClassId.Glaive, new[] { "Riot Sweep", "Suppression Thrust", "Compliance Combo", "Curfew", "Cease and Desist", "Lockstep", "Full Enforcement", "Martial Law", "Bulwark Protocol", "Shield Field", "Injunction", "Crackdown", "Baton Pair", "Summons", "Double Warrant", "Due Process" } },
            { WeaponClassId.Carbine, new[] { "Spray Pattern", "Clean Shot", "Triple Tap", "Headhunter", "Burst Contract", "Longshot", "Terminal Burst", "Orbital Call", "Target Lock", "Reactive Plating", "Deadeye", "Suppressing Fire", "Double Tap", "Controlled Pair", "Crossfire", "Twin Verdict" } },
            { WeaponClassId.Harvester, new[] { "Reaping Cycle", "Culling Stroke", "Threshing Combo", "Scrap Sentence", "Hydraulic Flurry", "Extinction Blow", "Kill Loop", "Terminal Harvest", "Kill Protocol", "Null Shell", "Shear Line", "Combine Pass", "Twin Reap", "Bale and Bind", "Gleaning Pass", "Final Yield" } },
            { WeaponClassId.ServoClaws, new[] { "Shockwave Stomp", "Piston Jab", "Servo Combo", "Compactor", "Overdrive Cycle", "Core Breach", "Three-Phase Rend", "Zero Point", "Lockdown Plating", "Faraday Skin", "Ram Drive", "Ratchet Rush", "One-Two Piston", "Crankshaft", "Twin Clamp", "Dual Overload" } },
            { WeaponClassId.ArcCaster, new[] { "Arc Flare", "Lance Shot", "Tri-Bolt", "Ion Spike", "Cascade Fire", "Sunspot", "Chain Storm", "Extinction Beam", "Capacitor", "Static Veil", "Flashpoint", "Arc Volley", "Twin Spark", "Dipole", "Alternating Current", "Double Discharge" } },
            { WeaponClassId.Longaxe, new[] { "Reaping Arc", "Hewing Blow", "Clan Rhythm", "Oathbreaker", "Frostbite Flurry", "Skysplitter", "Wolfpack", "Northfall", "Blood Oath", "Ancestor's Ward", "Bonecleaver", "Raven's Feast", "Two Ravens", "Brother's Bite", "Twinblood", "Fenris Jaws" } },
            { WeaponClassId.Claymore, new[] { "Shieldwall Sweep", "Steadfast Cut", "Three Oaths", "Bulwark Strike", "Hearthguard Flurry", "Dawnguard", "Highguard Trinity", "Kingsfall", "Shieldwall", "Stoneheart", "Oathkeeper", "Vanguard's Tide", "Oath and Answer", "Twin Vows", "Shield and Sword", "Kinsguard" } },
            { WeaponClassId.Warbow, new[] { "Arrow Rain", "Hunter's Mark", "Three Feathers", "Heartseeker", "Volley", "Moonshot", "Stormflight", "Skyfall", "Windwalk", "Barkskin", "Falcon's Eye", "Swiftwind", "Two Feathers", "Paired Flight", "Twin Talons", "Wind and Thunder" } },
        };

        // ---------------------------------------------------------------- rules (no assets)

        [Test]
        public void EachFactionHasAnOffenseDefenseAndRangedClass()
        {
            Assert.AreEqual(WeaponClassId.Monoblade, WeaponClassRules.ClassOf(FactionId.Hegemony, WeaponClassRole.Offense));
            Assert.AreEqual(WeaponClassId.Glaive, WeaponClassRules.ClassOf(FactionId.Hegemony, WeaponClassRole.Defense));
            Assert.AreEqual(WeaponClassId.Carbine, WeaponClassRules.ClassOf(FactionId.Hegemony, WeaponClassRole.Ranged));
            Assert.AreEqual(WeaponClassId.Harvester, WeaponClassRules.ClassOf(FactionId.Null, WeaponClassRole.Offense));
            Assert.AreEqual(WeaponClassId.ServoClaws, WeaponClassRules.ClassOf(FactionId.Null, WeaponClassRole.Defense));
            Assert.AreEqual(WeaponClassId.ArcCaster, WeaponClassRules.ClassOf(FactionId.Null, WeaponClassRole.Ranged));
            Assert.AreEqual(WeaponClassId.Longaxe, WeaponClassRules.ClassOf(FactionId.Covenant, WeaponClassRole.Offense));
            Assert.AreEqual(WeaponClassId.Claymore, WeaponClassRules.ClassOf(FactionId.Covenant, WeaponClassRole.Defense));
            Assert.AreEqual(WeaponClassId.Warbow, WeaponClassRules.ClassOf(FactionId.Covenant, WeaponClassRole.Ranged));
            foreach (var id in WeaponClasses.All)
                Assert.AreEqual(id, WeaponClassRules.ClassOf(WeaponClassRules.FactionOf(id), WeaponClasses.RoleOf(id)));
            Assert.AreEqual(FactionId.None, WeaponClassRules.FactionOf(WeaponClassId.None));
        }

        [Test]
        public void ADisciplineFromAnotherFactionFallsBackToTheOffenseClass()
        {
            Assert.AreEqual(WeaponClassId.Glaive, WeaponClassRules.Normalize(WeaponClassId.Glaive, FactionId.Hegemony));
            Assert.AreEqual(WeaponClassId.Monoblade, WeaponClassRules.Normalize(WeaponClassId.Warbow, FactionId.Hegemony));
            // A character saved before classes existed has none.
            Assert.AreEqual(WeaponClassId.Harvester, WeaponClassRules.Normalize(WeaponClassId.None, FactionId.Null));
            Assert.AreEqual(WeaponClassId.None, WeaponClassRules.Normalize(WeaponClassId.Monoblade, FactionId.None));
        }

        [Test]
        public void ClassNamesParseFromWhatAPlayerTypes()
        {
            Assert.IsTrue(WeaponClasses.TryParse("carbine", out var a));
            Assert.AreEqual(WeaponClassId.Carbine, a);
            Assert.IsTrue(WeaponClasses.TryParse("Servo Claws", out var b));
            Assert.AreEqual(WeaponClassId.ServoClaws, b);
            Assert.IsTrue(WeaponClasses.TryParse("arc_caster", out var c));
            Assert.AreEqual(WeaponClassId.ArcCaster, c);
            Assert.IsFalse(WeaponClasses.TryParse("none", out _));
            Assert.IsFalse(WeaponClasses.TryParse("lute", out _));
        }

        [Test]
        public void ClassSkillsShareARankKeyBySlotAndSharedSkillsKeepTheirOwn()
        {
            var one = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            var two = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            var other = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            var shared = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            try
            {
                one.skillId = "FuryStrike"; one.weaponClass = WeaponClassId.Monoblade; one.classSlot = ClassSkillSlot.S1;
                two.skillId = "Glaive_S1"; two.weaponClass = WeaponClassId.Glaive; two.classSlot = ClassSkillSlot.S1;
                other.skillId = "Glaive_S2"; other.weaponClass = WeaponClassId.Glaive; other.classSlot = ClassSkillSlot.S2;
                shared.skillId = "HeavenlySerenity";
                Assert.AreEqual(one.RankKey, two.RankKey, "Same slot, different class");
                Assert.AreNotEqual(two.RankKey, other.RankKey, "Different slot");
                Assert.AreEqual("HeavenlySerenity", shared.RankKey);
                Assert.IsFalse(shared.IsClassSkill);

                two.presentationId = "FuryStrike";
                Assert.AreEqual("FuryStrike", two.PresentationId);
                Assert.AreEqual("FuryStrike", one.PresentationId, "No borrowed feel: its own ID");
            }
            finally
            {
                foreach (var skill in new[] { one, two, other, shared }) Object.DestroyImmediate(skill);
            }
        }

        // ---------------------------------------------------------------- the built catalog

        private static WeaponClassCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponClassCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "WeaponClassCatalog.asset (build the class skills)");
            return catalog;
        }

        [Test]
        public void TheCatalogHasNineClassesOfTwelveNamedSkills()
        {
            var catalog = LoadCatalog();
            Assert.AreEqual(WeaponClasses.Count, catalog.Classes.Count);
            var ids = new HashSet<string>();
            foreach (var weaponClass in WeaponClasses.All)
            {
                Assert.IsTrue(catalog.TryGet(weaponClass, out var entry), weaponClass.ToString());
                Assert.AreEqual(WeaponClassRules.FactionOf(weaponClass), entry.faction, weaponClass.ToString());
                Assert.AreEqual(WeaponClasses.RoleOf(weaponClass), entry.role, weaponClass.ToString());
                Assert.AreEqual(WeaponClasses.SlotCount, entry.skills.Length, weaponClass.ToString());
                for (int i = 0; i < WeaponClasses.SlotCount; i++)
                {
                    var skill = entry.skills[i];
                    Assert.IsNotNull(skill, $"{weaponClass} {WeaponClasses.Slots[i]}");
                    Assert.AreEqual(Names[weaponClass][i], skill.displayName, $"{weaponClass} {WeaponClasses.Slots[i]}");
                    Assert.AreEqual(weaponClass, skill.weaponClass, skill.name);
                    Assert.AreEqual(WeaponClasses.Slots[i], skill.classSlot, skill.name);
                    Assert.IsTrue(ids.Add(skill.skillId), $"Skill ID '{skill.skillId}' twice");
                    Assert.AreSame(skill, entry.SkillIn(WeaponClasses.Slots[i]));
                }
            }
            Assert.AreEqual(WeaponClasses.Count * WeaponClasses.SlotCount, ids.Count);
        }

        [Test]
        public void EverySlotHasTheSameShapeInEveryClass()
        {
            var catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGet(WeaponClassId.Monoblade, out var template));
            foreach (var entry in catalog.Classes)
            {
                for (int i = 0; i < WeaponClasses.SlotCount; i++)
                {
                    var a = template.skills[i];
                    var b = entry.skills[i];
                    // What ranks are spent against must match, or a shared rank would mean different things.
                    Assert.AreEqual(a.maxRank, b.maxRank, b.name);
                    Assert.AreEqual(a.requiredCharacterLevel, b.requiredCharacterLevel, b.name);
                    Assert.AreEqual(a.requiresSkillBook, b.requiresSkillBook, b.name);
                    Assert.AreEqual(a.skillPointsToLearn, b.skillPointsToLearn, b.name);
                    Assert.AreEqual(a.totalSkillPointsToMax, b.totalSkillPointsToMax, b.name);
                    Assert.AreEqual(a.IsAttack, b.IsAttack, b.name);
                    Assert.AreEqual(a.RankKey, b.RankKey, b.name);
                }
            }
        }

        [Test]
        public void RangedClassesReachFurtherAndStancesFollowTheRole()
        {
            var catalog = LoadCatalog();
            foreach (var entry in catalog.Classes)
            {
                var single = entry.SkillIn(ClassSkillSlot.S2);
                if (entry.role == WeaponClassRole.Ranged) Assert.GreaterOrEqual(single.minRange, 8f, single.name);
                else Assert.LessOrEqual(single.maxRange, 3f, single.name);

                var stance = entry.SkillIn(ClassSkillSlot.Stance);
                var stats = stance.rankScaledModifiers.Select(m => m.statType).ToList();
                StatType expected = entry.role == WeaponClassRole.Defense ? StatType.DefensePower
                    : entry.role == WeaponClassRole.Ranged ? StatType.EvasionRate : StatType.AttackPower;
                CollectionAssert.Contains(stats, expected, stance.name);
            }
        }

        [Test]
        public void AFactionsSkillListFitsTheUnlockMaskAndIsStable()
        {
            var catalog = LoadCatalog();
            foreach (var faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
            {
                var skills = catalog.SkillsFor(faction);
                Assert.LessOrEqual(skills.Length, 64, $"{faction}: skill unlocks are mirrored as a 64-bit mask");
                Assert.AreEqual(catalog.SharedSkills.Count + WeaponClasses.PerFaction * WeaponClasses.SlotCount, skills.Length, faction.ToString());
                Assert.AreSame(skills, catalog.SkillsFor(faction), "The same list every time: ranks are mirrored by position in it");
                Assert.AreEqual(skills.Length, skills.Select(s => s.skillId).Distinct().Count(), $"{faction}: a skill is listed twice");
                foreach (var skill in skills)
                    Assert.IsTrue(!skill.IsClassSkill || WeaponClassRules.BelongsTo(skill.weaponClass, faction), $"{skill.name} in {faction}'s list");
            }
        }
    }
}
