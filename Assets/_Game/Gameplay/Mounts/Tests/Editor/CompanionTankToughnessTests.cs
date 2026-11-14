using KryticalNova.Core;
using KryticalNova.Hostiles;
using KryticalNova.Mounts;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// How long a TANK Mount Companion holds a same-level Bruiser (day list item 19 phase 4; owner, 14 Nov 2026: the stances grow with the
    /// mount, TANK from 65 % to 70 % less damage and from x1 to x2 HP; the time at 0 % and at 100 % growth is reported). Everything from the real assets and the one damage formula
    /// (<see cref="CombatMath.Resolve"/>): the level 40 Colossus as <see cref="HostileStatCurve"/> builds it (Bruiser), its three attacks with
    /// their weights, multipliers and clip lengths plus the brain's cooldown, against the companion, which defends with its owner's stats (the
    /// curve's reference player of level 40, the gear that level is expected to wear) and has the owner's HP times its growth share, minus
    /// the TANK cost of a blow at that growth (<see cref="CompanionFightRules.TakenScale"/>). The expected damage of a swing is the average over a grid of the hit, crit and
    /// block dice, so the test is exact for the formula and needs no luck.
    /// </summary>
    public sealed class CompanionTankToughnessTests
    {
        const string CurvePath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileStatCurve.asset";
        const string ColossusPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/Species/Hostile_Colossus.asset";
        const string BrainPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileBrainSettings.asset";
        const string MountSettingsPath = "Assets/_Game/Gameplay/Mounts/Data/Assets/MountSettings.asset";
        const int Level = 40;

        [Test]
        public void ATankCompanion_HoldsALevel40Colossus_LongerAsItGrows()
        {
            var curve = AssetDatabase.LoadAssetAtPath<HostileStatCurve>(CurvePath);
            var colossus = AssetDatabase.LoadAssetAtPath<HostileDefinition>(ColossusPath);
            var brain = AssetDatabase.LoadAssetAtPath<HostileBrainSettings>(BrainPath);
            var settings = AssetDatabase.LoadAssetAtPath<MountSettings>(MountSettingsPath);
            Assert.IsNotNull(curve, CurvePath);
            Assert.IsNotNull(colossus, ColossusPath);
            Assert.IsNotNull(brain, BrainPath);
            Assert.IsNotNull(settings, MountSettingsPath);

            float atNew = SecondsToDown(curve, colossus, brain, settings, 0,
                CompanionFightRules.TakenScale(CompanionStance.Tank, settings.CompanionFight.AtGrowth(0)), out float perSecond, out float hp);
            float atHalf = SecondsToDown(curve, colossus, brain, settings, 50,
                CompanionFightRules.TakenScale(CompanionStance.Tank, settings.CompanionFight.AtGrowth(50)), out _, out float hpHalf);
            float atFull = SecondsToDown(curve, colossus, brain, settings, 100,
                CompanionFightRules.TakenScale(CompanionStance.Tank, settings.CompanionFight.AtGrowth(100)), out _, out float hpFull);
            float freeNew = SecondsToDown(curve, colossus, brain, settings, 0, 1f, out _, out _);
            TestContext.WriteLine($"L{Level} Colossus vs TANK: {perSecond:0.#} damage a second before the stance; plain HP {hp:0} / {hpHalf:0} / {hpFull:0} " +
                $"at 0 / 50 / 100 %; down in {atNew:0.0} s at 0 %, {atHalf:0.0} s at 50 %, {atFull:0.0} s at 100 %; FREE at 0 % {freeNew:0.0} s");

            Assert.Greater(atNew, freeNew * 1.5f, "TANK holds longer than FREE even on a new mount");
            Assert.That(atNew, Is.InRange(13f, 20f), "0 %: 65 % less damage, x1 HP (owner: about 15 s from day one)");
            Assert.That(atHalf, Is.InRange(24f, 42f), "50 %: 67.5 % less damage, x1.5 HP (about 32 s)");
            Assert.That(atFull, Is.InRange(40f, 80f), "100 %: 70 % less damage, x2 HP (about 58 s)");
            Assert.Less(atNew, atHalf);
            Assert.Less(atHalf, atFull);
        }

        static float SecondsToDown(HostileStatCurve curve, HostileDefinition colossus, HostileBrainSettings brain, MountSettings settings,
            int growth, float takenScale, out float damagePerSecond, out float companionHp)
        {
            HostileStatCurve.ReferencePlayer owner = curve.ReferenceAt(Level);
            HostileStats them = colossus.StatsFrom(curve, Level);
            var attacker = new CombatStatBlock
            {
                AttackPower = them.Attack, DefensePower = them.Defense, HitRate = them.HitRate, EvasionRate = them.Evasion,
                CriticalRate = them.CriticalRate, Level = Level,
            };
            var defender = new CombatStatBlock
            {
                AttackPower = owner.Attack, DefensePower = owner.Defense, HitRate = owner.HitRate, EvasionRate = owner.Evasion, Level = Level,
            };
            CombatTuning limits = CombatCalculator.Limits(curve.tuning);

            float weights = 0f, damage = 0f, seconds = 0f;
            foreach (HostileDefinition.Attack a in colossus.attacks)
            {
                float w = a.weight;
                weights += w;
                damage += w * ExpectedDamage(attacker, defender, limits, a.damageMultiplier);
                seconds += w * ((a.clip != null ? a.clip.length : 1f) + brain.attackCooldownSeconds);
            }
            float perSwing = damage / weights, swing = seconds / weights;
            damagePerSecond = perSwing / swing;
            companionHp = CompanionRules.MaxHp(owner.MaxHealth, growth, settings.Companion);
            return companionHp / (damagePerSecond * takenScale);
        }

        /// <summary>The average of the formula's damage over a grid of the hit, crit and block dice (2 points apart).</summary>
        static float ExpectedDamage(in CombatStatBlock attacker, in CombatStatBlock defender, in CombatTuning limits, float multiplier)
        {
            var strike = new CombatStrike { SkillMultiplier = multiplier };
            double sum = 0;
            int n = 0;
            for (float hit = 1f; hit < 100f; hit += 2f)
            for (float crit = 1f; crit < 100f; crit += 2f)
            for (float block = 1f; block < 100f; block += 2f)
            {
                DamageResult r = CombatMath.Resolve(attacker, defender, limits, strike,
                    new CombatRolls { Hit = hit, Reflect = 100f, Crit = crit, Shatter = 100f, Block = block });
                sum += r.IsEvaded ? 0f : r.DamageDealt;
                n++;
            }
            return (float)(sum / n);
        }
    }
}
