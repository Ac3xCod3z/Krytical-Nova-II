using System.Collections.Generic;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>
    /// The stat curve against the game's own damage formula: a player built the way the curve expects fights a
    /// Hostile carrying the curve's numbers, through <see cref="CombatCalculator"/>, at every level.
    /// </summary>
    public sealed class HostileStatCurveTests
    {
        private HostileStatCurve curve;
        private readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            curve = ScriptableObject.CreateInstance<HostileStatCurve>();
            made.Add(curve);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private CharacterCombatStats Sheet(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go.AddComponent<CharacterCombatStats>();
        }

        /// <summary>A stat sheet that reads exactly what the curve says its reference player has.</summary>
        private CharacterCombatStats ReferencePlayer(int level)
        {
            HostileStatCurve.ReferencePlayer reference = curve.ReferenceAt(level);
            var asHostile = new HostileStats
            {
                Attack = reference.Attack, Defense = reference.Defense, HitRate = reference.HitRate,
                Evasion = reference.Evasion, CriticalRate = 0f
            };
            CharacterCombatStats sheet = Sheet("Player " + level);
            asHostile.ApplyTo(sheet);
            return sheet;
        }

        private CharacterCombatStats Hostile(HostileStats stats)
        {
            CharacterCombatStats sheet = Sheet("Hostile " + stats.Level);
            stats.ApplyTo(sheet);
            return sheet;
        }

        // A hit that lands, is not reflected, is not critical and shatters nothing.
        private static DamageResult PlainHit(CharacterCombatStats attacker, CharacterCombatStats defender) =>
            CombatCalculator.CalculateAttackInternal(attacker, defender, 1f, 0f, 100f, 100f, 100f, false, 0f, false, 0f);

        [Test]
        public void AppliedStatsReadBackAsGiven()
        {
            HostileStats stats = curve.Evaluate(50);
            CharacterCombatStats sheet = Hostile(stats);
            Assert.AreEqual(stats.Attack, sheet.GetStatValue(StatType.AttackPower), 0.01f);
            Assert.AreEqual(stats.Defense, sheet.GetStatValue(StatType.DefensePower), 0.01f);
            Assert.AreEqual(stats.HitRate, sheet.GetStatValue(StatType.HitRate), 0.01f);
            Assert.AreEqual(stats.Evasion, sheet.GetStatValue(StatType.EvasionRate), 0.01f);
            Assert.AreEqual(stats.CriticalRate, sheet.GetStatValue(StatType.CriticalRate), 0.01f);
        }

        [Test]
        public void ApplyingTwiceDoesNotStack()
        {
            HostileStats stats = curve.Evaluate(30);
            CharacterCombatStats sheet = Hostile(stats);
            stats.ApplyTo(sheet);
            Assert.AreEqual(stats.Attack, sheet.GetStatValue(StatType.AttackPower), 0.01f);
        }

        [Test]
        public void AnEvenLevelKillTakesFourToSixHitsAtEveryLevel()
        {
            for (int level = 1; level <= curve.levelCap; level++)
            {
                HostileStats stats = curve.Evaluate(level);
                DamageResult hit = PlainHit(ReferencePlayer(level), Hostile(stats));
                Assert.IsFalse(hit.IsEvaded, $"level {level}: a roll of 0 must land");
                float hits = stats.MaxHealth / hit.DamageDealt;
                Assert.That(hits, Is.InRange(4f, 6f), $"level {level}: {stats.MaxHealth} HP at {hit.DamageDealt} a hit");
            }
        }

        [Test]
        public void AHostileNeedsAboutAsManyHitsAsTheCurveAsksFor()
        {
            for (int level = 1; level <= curve.levelCap; level += 3)
            {
                HostileStats stats = curve.Evaluate(level);
                DamageResult hit = PlainHit(Hostile(stats), ReferencePlayer(level));
                float hits = curve.ReferenceAt(level).MaxHealth / hit.DamageDealt;
                Assert.That(hits, Is.InRange(curve.hitsToDie * 0.9f, curve.hitsToDie * 1.1f), $"level {level}");
            }
        }

        [Test]
        public void BothSidesLandAsOftenAsTheCurveAsksFor()
        {
            for (int level = 1; level <= curve.levelCap; level += 3)
            {
                HostileStats stats = curve.Evaluate(level);
                CharacterCombatStats player = ReferencePlayer(level), hostile = Hostile(stats);
                Assert.AreEqual(curve.playerHitChance * 100f, PlainHit(player, hostile).HitChance, 2f, $"player, level {level}");
                Assert.AreEqual(curve.hostileHitChance * 100f, PlainHit(hostile, player).HitChance, 2f, $"Hostile, level {level}");
            }
        }

        [Test]
        public void ABruiserIsTougherAndPaysMore()
        {
            HostileStats normal = curve.Evaluate(12), bruiser = curve.Evaluate(12, HostileTier.Bruiser);
            Assert.AreEqual(normal.MaxHealth * 8f, bruiser.MaxHealth, normal.MaxHealth * 0.01f);
            Assert.Greater(bruiser.Attack, normal.Attack);
            Assert.Greater(bruiser.Defense, normal.Defense);
            Assert.AreEqual(normal.Experience * 6f, bruiser.Experience, 6f);
            Assert.Less(bruiser.RespawnSeconds, bruiser.RespawnSecondsMax);
        }

        [Test]
        public void DefenseNeverSwallowsTheWholeHit()
        {
            curve.tiers = new[] { new HostileStatCurve.TierMultipliers { tier = HostileTier.Bruiser, health = 1f, damage = 1f, defense = 50f, hitRate = 1f, evasion = 1f, experience = 1f, creds = 1f } };
            HostileStats stats = curve.Evaluate(40, HostileTier.Bruiser);
            Assert.Less(stats.Defense, curve.ReferenceAt(40).Attack);
        }

        [Test]
        public void NumbersRiseWithLevelAndPrimeLevelsGoOn()
        {
            HostileStats previous = curve.Evaluate(1);
            for (int level = 2; level <= curve.MaxLevel; level++)
            {
                HostileStats stats = curve.Evaluate(level);
                Assert.GreaterOrEqual(stats.MaxHealth, previous.MaxHealth, $"health, level {level}");
                Assert.GreaterOrEqual(stats.Attack, previous.Attack, $"attack, level {level}");
                previous = stats;
            }
            Assert.Greater(curve.Evaluate(curve.MaxLevel).MaxHealth, curve.Evaluate(curve.levelCap).MaxHealth);
        }

        [Test]
        public void BehaviourNumbersFollowThePlan()
        {
            Assert.AreEqual(8f, curve.Evaluate(1).RespawnSeconds, 0.2f);
            Assert.AreEqual(21.44f, curve.Evaluate(112).RespawnSeconds, 0.01f);
            Assert.AreEqual(12f, curve.Evaluate(112).AggroRadius, 0.01f);
            Assert.AreEqual(4.2f + 0.006f * 50, curve.Evaluate(50).RunSpeed, 0.001f);
        }
    }
}
