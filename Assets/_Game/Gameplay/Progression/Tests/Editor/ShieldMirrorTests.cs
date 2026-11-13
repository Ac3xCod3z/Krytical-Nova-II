using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Progression.Tests
{
    /// <summary>Build 17 item 9.2: the server's shield left travels in the buff list and every view's copy shows it falling.</summary>
    public sealed class ShieldMirrorTests
    {
        private SkillDefinitionSO shieldSkill, plainSkill;

        [SetUp]
        public void SetUp()
        {
            shieldSkill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            shieldSkill.skillId = "test_aegis";
            shieldSkill.isAbsorptionShield = true;
            plainSkill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            plainSkill.skillId = "test_stance";
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(shieldSkill);
            Object.DestroyImmediate(plainSkill);
        }

        [Test]
        public void TheServerSendsTheShieldLeftRoundedUp()
        {
            var aegis = new ActiveBuff(shieldSkill, 1, 1000f, 30f, 0f, 0f, 0f) { CurrentShieldCapacity = 412.2f };
            Assert.AreEqual(413, BuffMirror.ShieldOf(aegis));
            Assert.AreEqual(-1, BuffMirror.ShieldOf(new ActiveBuff(plainSkill, 1, 0f, 30f, 0f, 0f, 0f)), "not a shield");
        }

        [Test]
        public void AViewCopyFallsHitByHit()
        {
            var copy = new ActiveBuff(shieldSkill, 1, 1000f, 30f, 0f, 0f, 0f);
            Assert.IsTrue(BuffMirror.SetShield(copy, 700));
            Assert.AreEqual(700f, copy.CurrentShieldCapacity);
            Assert.IsTrue(BuffMirror.SetShield(copy, 250));
            Assert.AreEqual(250f, copy.CurrentShieldCapacity);
            Assert.IsFalse(BuffMirror.SetShield(copy, 250), "same number: nothing changes");
        }

        [Test]
        public void ANonShieldOrNoNumberIsLeftAlone()
        {
            var stance = new ActiveBuff(plainSkill, 1, 0f, 30f, 0f, 0f, 0f);
            Assert.IsFalse(BuffMirror.SetShield(stance, 100));
            var aegis = new ActiveBuff(shieldSkill, 1, 1000f, 30f, 0f, 0f, 0f);
            Assert.IsFalse(BuffMirror.SetShield(aegis, -1));
            Assert.AreEqual(1000f, aegis.CurrentShieldCapacity);
        }
    }
}
