using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>M4 step 7 (owner, 5 Nov 2026): an area skill hits every body round the player, and every hit's number reaches the attacker.</summary>
    public sealed class HitDeliveryTests
    {
        [Test]
        public void FortyHitNumbers_AllReachTheAttackersGame_ByTheReliableMessage()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            tw.Connect();
            int id = KnTestWorld.NetworkIdOf(tw.Clients[0]);
            var client = tw.Clients[0].GetExistingSystemManaged<HitNumberClientSystem>();
            Assert.IsNotNull(client, "the client world has the number system");
            for (int i = 0; i < 40; i++)
                Assert.IsTrue(HitNumbers.Send(tw.Server, id, new Vector3(i, 0f, 0f), new DamageResult { DamageDealt = 10 + i, IsCritical = i % 5 == 0 }, true));
            tw.TickUntil(() => client.Received >= 40, 300);
            tw.Tick(10);
            Assert.AreEqual(40, client.Received, "every number, once");
        }

        [Test]
        public void ANumber_KeepsWhatItSays()
        {
            var sent = HitNumbers.Of(new Vector3(1.234f, 2f, -3f), new DamageResult { DamageDealt = 12.5f, ReflectedDamage = 1f, IsCritical = true, IsEvaded = false, ShieldShattered = true }, true);
            DamageResult back = HitNumbers.ResultOf(sent);
            Assert.AreEqual(12.5f, back.DamageDealt, 1e-3);
            Assert.AreEqual(1f, back.ReflectedDamage, 1e-3);
            Assert.IsTrue(back.IsCritical);
            Assert.IsFalse(back.IsEvaded);
            Assert.IsTrue(back.ShieldShattered);
            Assert.AreEqual(1234, sent.XMm);
            Assert.AreNotEqual(0, sent.Flags & HitEventRing.Skill);
        }

        [Test]
        public void EverySkillsHitLabel_FitsTheRing()
        {
            // NGO PlayerSkillAttacks' label: the name, and "(i/n)" for a multi-hit skill
            string[] guids = AssetDatabase.FindAssets("t:SkillDefinitionSO");
            Assert.Greater(guids.Length, 100, "the skills are found");
            foreach (string guid in guids)
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (skill == null) continue;
                int hits = Mathf.Max(1, skill.hitCount);
                string label = hits > 1 ? $"{skill.displayName} ({hits}/{hits})" : skill.displayName;
                Assert.AreEqual(label, HitEventRing.FitLabel(label).ToString(), skill.name + "'s label is cut: " + label);
            }
        }
    }
}
