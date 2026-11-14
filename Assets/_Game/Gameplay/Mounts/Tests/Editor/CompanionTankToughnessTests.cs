using KryticalNova.Core;
using KryticalNova.Hostiles;
using KryticalNova.Mounts;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// How long a TANK Mount Companion holds a same-level Bruiser (day list item 19 phase 4; owner, 14 Nov 2026: "its defense buff should
    /// allow it to be tougher", aim about 15-20 s at level 40). Everything from the real assets and the one damage formula
    /// (<see cref="CombatMath.Resolve"/>): the level 40 Colossus as <see cref="HostileStatCurve"/> builds it (Bruiser), its three attacks with
    /// their weights, multipliers and clip lengths plus the brain's cooldown, against the companion, which defends with its owner's stats (the
    /// curve's reference player of level 40, the gear that level is expected to wear) and has the owner's HP times its growth share, minus
    /// <see cref="MountSettings.companionTankDefenceBuff"/>. The expected damage of a swing is the average over a grid of the hit, crit and
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
        public void ATankCompanion_HoldsALevel40Colossus_15To25Seconds_WhenNew_AndLongerFullyGrown()
        {
            var curve = AssetDatabase.LoadAssetAtPath<HostileStatCurve>(CurvePath);
            var colossus = AssetDatabase.LoadAssetAtPath<HostileDefinition>(ColossusPath);
            var brain = AssetDatabase.LoadAssetAtPath<HostileBrainSettings>(BrainPath);
            var settings = AssetDatabase.LoadAssetAtPath<MountSettings>(MountSettingsPath);
            Assert.IsNotNull(curve, CurvePath);
            Assert.IsNotNull(colossus, ColossusPath);
            Assert.IsNotNull(brain, BrainPath);
            Assert.IsNotNull(settings, MountSettingsPath);

            float tank = CompanionFightRules.TakenScale(CompanionStance.Tank, settings.CompanionFight);
            float atNew = SecondsToDown(curve, colossus, brain, settings, 0, tank, out float perSecond, out float hp);
            float atFull = SecondsToDown(curve, colossus, brain, settings, 100, tank, out _, out _);
            float freeNew = SecondsToDown(curve, colossus, brain, settings, 0, 1f, out _, out _);
            TestContext.WriteLine($"L{Level} Colossus vs TANK (buff {settings.companionTankDefenceBuff:0.##}): {perSecond:0.#} damage a second before the buff, " +
                $"companion HP {hp:0} new; down in {atNew:0.0} s new, {atFull:0.0} s fully grown; with no buff {freeNew:0.0} s");

            Assert.That(atNew, Is.InRange(15f, 25f), "a new mount holds a same-level Bruiser for a real while (owner: about 15-20 s)");
            Assert.Greater(atFull, atNew, "growth makes it tougher");
            Assert.Less(freeNew, 15f, "without the TANK buff it would not: the buff is what holds it");
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
