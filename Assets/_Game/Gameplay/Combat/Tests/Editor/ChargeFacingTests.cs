using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Combat.Tests
{
    /// <summary>
    /// Owner, 22 Oct 2026: always turn to face whoever you aim at; 7 Nov 2026: a self-buff (Redline) never turns the body
    /// or the mount. Review, 8 Nov 2026: a targeted support skill (an ally heal, a 0-damage debuff) still faces its target.
    /// </summary>
    public sealed class ChargeFacingTests
    {
        private static SkillDefinitionSO Skill(SkillTargetType target, float damage)
        {
            var skill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            skill.targetType = target;
            skill.maxDamagePercent = damage;
            return skill;
        }

        [TestCase(SkillTargetType.SingleTarget, 120f, true, TestName = "A single-target strike faces its target")]
        [TestCase(SkillTargetType.SingleTarget, 0f, true, TestName = "A targeted heal or debuff faces its target")]
        [TestCase(SkillTargetType.Self, 0f, false, TestName = "A self-buff turns nothing")]
        [TestCase(SkillTargetType.Party, 0f, false, TestName = "A party skill turns nothing")]
        [TestCase(SkillTargetType.AreaOfEffect, 120f, false, TestName = "An area strike aims at nothing")]
        public void TheCharge_FacesOnlyWhatItIsAimedAt(SkillTargetType target, float damage, bool faces)
        {
            SkillDefinitionSO skill = Skill(target, damage);
            try { Assert.AreEqual(faces, PlayerSkillAttacks.ChargeFacesTarget(skill)); }
            finally { Object.DestroyImmediate(skill); }
        }
    }
}
