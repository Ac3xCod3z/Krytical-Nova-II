using System.Collections;
using KryticalNova.Core;
using KryticalNova.Hostiles;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace KryticalNova.Persistence.Entities.Tests.Play
{
    /// <summary>
    /// M5/M6 Hostiles on the entity netcode with the game's real species prefabs (implementation plan s0: B70-B89). B70 is seam 3:
    /// a client view of a Hostile.
    /// </summary>
    public class HostileSmokeTests
    {
        const string SpeciesPath = "Assets/_Game/Gameplay/Hostiles/Prefabs/Hostile_Bonewalker.prefab";

        [UnityTest]
        public IEnumerator B70_AHostileView_NeverDecidesOrWakes_AndShowsWhatItsGhostSays()
        {
            // NGO's client copy of a Hostile (HostileNet.OnNetworkSpawn's client branch, AttackClientRpc, OnNetLevelChanged), driven by a ghost
            LogAssert.ignoreFailingMessages = true;   // a species body off the NavMesh may complain; this test reads HostileNet only
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SpeciesPath);
            Assert.IsNotNull(prefab, SpeciesPath);
            GameObject body = Object.Instantiate(prefab, new Vector3(0f, -500f, 0f), Quaternion.identity);
            try
            {
                var net = body.GetComponent<HostileNet>();
                net.BindView(12);                      // before its Start, as the view factory will
                for (int i = 0; i < 3; i++) yield return null;

                Assert.IsTrue(net.IsView);
                Assert.AreEqual(12, net.Level, "the ghost's level");
                CollectionAssert.Contains(HostileNet.Active, net, "nameplates, minimap and Autopilot see it");
                Assert.AreEqual(default(HostileStats), net.Stats, "never woken: no numbers decided here");
                net.SetLevel(30);
                Assert.AreEqual(12, net.Level, "a view never decides its level");

                int levels = 0;
                net.LevelChanged += () => levels++;
                net.ShowLevelFromGhost(14);
                Assert.AreEqual(14, net.Level);
                Assert.AreEqual(1, levels);

                int started = -1;
                bool heavySeen = false;
                net.AttackStarted += (index, windup, heavy) => { started = index; heavySeen = heavy; };
                const int third = 2;
                net.ShowAttackFromGhost(third);
                Assert.AreEqual(third, started, "the ghost's attack starts here");
                Assert.AreEqual(net.Definition.attacks.Length > third && net.Definition.attacks[third].heavy, heavySeen, "heavy as the species says");

                net.ShowStateFromGhost(new HostileAnimState { Dead = true, DeathIndex = 1 });
                net.ShowStateFromGhost(default);   // back from the pool: stands up
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Object.Destroy(body);
            }
            yield return null;
        }

        // ------------------------------------------------------------------ M5.6 (HIGH): blows, death, rewards on a server body

        const string BodyPath = "Assets/_Game/Gameplay/Hostiles/Prefabs/ServerBody/HostileBody_Bonewalker.prefab";
        const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        readonly System.Collections.Generic.List<GameObject> m_Made = new System.Collections.Generic.List<GameObject>();
        KryticalNova.Core.INetRoleAdapter m_OldAdapter;
        KryticalNova.Core.ISaveStore m_OldStore;

        void Begin()
        {
            LogAssert.ignoreFailingMessages = true;   // bodies without a NetworkManager print their NGO stubs' lines
            m_OldAdapter = KryticalNova.Core.NetRole.Adapter;
            KryticalNova.Core.NetRole.Install(new KryticalNova.Networking.Entities.BodyRoleNetRoleAdapter(null, () => KryticalNova.Core.NetRoleKind.ServerBody));
            KryticalNova.Player.Entities.Presentation.GhostBodyRegistry.Clear();
            m_OldStore = KryticalNova.Persistence.ServerSave.Store;
            KryticalNova.Persistence.ServerSave.Store = new KryticalNova.Core.MemorySaveStore();
            HostileSpawner.AuthorityOverride = () => true;   // this process decides the Hostiles (the entity netcode's server)
        }

        void End()
        {
            foreach (GameObject go in m_Made) if (go != null) Object.Destroy(go);
            m_Made.Clear();
            HostileSpawner.AuthorityOverride = null;
            KryticalNova.Core.NetRole.Install(m_OldAdapter);
            KryticalNova.Player.Entities.Presentation.GhostBodyRegistry.Clear();
            KryticalNova.Persistence.ServerSave.Store = m_OldStore;
            LogAssert.ignoreFailingMessages = false;
        }

        GameObject Player(ulong key, Vector3 at)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab, PlayerPrefabPath);
            GameObject body = KryticalNova.Player.Entities.Bodies.PlayerBodyAssembler.Create(prefab, KryticalNova.Core.NetRoleKind.ServerBody,
                new KryticalNova.Core.PlayerKey(key), key, false, at, 0f);
            m_Made.Add(body);
            return body;
        }

        HostileNet Bonewalker(Vector3 at, Vector3 facing)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
            Assert.IsNotNull(prefab, BodyPath + " (Krytical Nova > Hostiles > Build DOTS Assets)");
            GameObject body = Object.Instantiate(prefab, at, Quaternion.LookRotation(facing, Vector3.up));
            m_Made.Add(body);
            return body.GetComponent<HostileNet>();
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnityTest]
        public IEnumerator B73_TheWallRule_AMeleeSwingMissesAPlayerUpAWall_AndHitsInAClearLine()
        {
            // H07 / H08 (NGO HostileBrainLink.Strike:241-286): above reachAbove misses; on a wall only a clear straight line within range lands
            Begin();
            try
            {
                HostileNet hostile = Bonewalker(Vector3.zero, Vector3.forward);
                GameObject p = Player(731, new Vector3(0f, 0f, 1f));
                yield return Frames(30);
                Assert.IsTrue(hostile.Entity.IsAlive, "the body woke");
                var target = p.GetComponent<CombatEntity>();
                Assert.IsTrue(HostileStrikeRules.TryRoll(hostile.Entity, hostile.Definition, 0, target, hostile.transform, out _), "on the ground in reach: lands");

                // above what the species reaches (not on a wall): the swing meets nothing
                p.transform.SetPositionAndRotation(new Vector3(0f, hostile.Definition.reachAbove + 1f, 1f), Quaternion.identity);
                Assert.IsFalse(HostileStrikeRules.TryRoll(hostile.Entity, hostile.Definition, 0, target, hostile.transform, out _), "above reachAbove: misses");

                // on a wall 2 m up, beside it, a clear line from the eyes: lands
                p.transform.SetPositionAndRotation(new Vector3(0f, 2f, 1f), Quaternion.Euler(-90f, 0f, 0f));
                Assert.IsTrue(HostileStrikeRules.OnAWall(p.transform), "the case under test: up a wall");
                Assert.IsTrue(HostileStrikeRules.TryRoll(hostile.Entity, hostile.Definition, 0, target, hostile.transform, out _), "clear line within range: lands");

                // the same with a ledge in the way: misses
                var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m_Made.Add(ledge);
                ledge.transform.position = new Vector3(0f, 1.6f, 0.5f);
                ledge.transform.localScale = new Vector3(2f, 0.1f, 0.3f);
                Physics.SyncTransforms();
                Assert.IsFalse(HostileStrikeRules.TryRoll(hostile.Entity, hostile.Definition, 0, target, hostile.transform, out _), "a ledge in the way: misses");
            }
            finally { End(); }
        }

        [UnityTest]
        public IEnumerator B74_TheBridgesBlow_TakesExactlyItsDamage_AndADeathToTheHostile_CostsTheXpOnce()
        {
            // H06 + H09: the brain's swing rolled on the real body (HostileStrikeBridge), applied as M4.9 does; the real Hostile is the attacker
            Begin();
            try
            {
                HostileNet hostile = Bonewalker(Vector3.zero, Vector3.forward);
                GameObject p = Player(741, new Vector3(0f, 0f, 1f));
                yield return Frames(30);
                var target = p.GetComponent<CombatEntity>();
                var resources = p.GetComponent<KryticalNova.Progression.PlayerResources>();
                var progression = p.GetComponent<PlayerCharacterProgression>();
                var death = p.GetComponent<KryticalNova.Player.PlayerDeathMotor>();

                bool rolled = false;
                KryticalNova.Combat.Entities.HostileStrike strike = default;
                for (int i = 0; i < 20 && !rolled; i++)
                {
                    rolled = KryticalNova.Hostiles.Entities.HostileStrikeBridge.TryStrike(hostile, 0, target, Vector3.zero, false, Unity.Entities.Entity.Null, out strike);
                    if (rolled && strike.Result.IsEvaded) rolled = false;   // an evaded blow takes nothing: roll again for one that lands
                }
                Assert.IsTrue(rolled, "a blow that landed");
                int key = KryticalNova.Hostiles.Entities.HostileStrikeBridge.AttackerKey(hostile);
                Assert.AreEqual(key, strike.Attacker);
                Assert.AreSame(hostile.Entity.CombatStats, KryticalNova.Combat.Entities.HostileAttackers.Of(key), "the real Hostile strikes");
                float before = resources.Health;
                Assert.IsTrue(KryticalNova.Combat.Entities.HostileStrikes.Apply(target, strike, KryticalNova.Combat.Entities.HostileAttackers.Of(key)));
                Assert.AreEqual(before - strike.Result.DamageDealt, resources.Health, 1e-3, "HP falls by exactly the rolled damage");

                // the killing blow: 5 % of the level's XP once (NGO HostileKillRewards with the real body's level gap settings)
                progression.SetExperience(progression.ExperienceRequired / 2);
                int held = progression.Experience;
                int loss = LevelGapRules.DeathLoss(hostile.LevelGap, progression.ExperienceRequired, held);
                Assert.Greater(loss, 0, "planned: a loss to see");
                var lethal = strike;
                float rest = resources.Health + 10f;
                lethal.Result = new DamageResult { DamageDealt = rest, RawPhysicalDamage = rest, MitigatedPhysicalDamage = rest };
                Assert.IsTrue(KryticalNova.Combat.Entities.HostileStrikes.Apply(target, lethal, KryticalNova.Combat.Entities.HostileAttackers.Of(key)));
                Assert.IsTrue(death.IsDead, "down");
                Assert.AreEqual(hostile.transform, death.ServerBodyKiller, "killed by the Hostile body");
                Assert.AreEqual(held - loss, progression.Experience, "the loss, once");
                Assert.IsFalse(KryticalNova.Combat.Entities.HostileStrikes.Apply(target, strike, KryticalNova.Combat.Entities.HostileAttackers.Of(key)), "the dead take no more");
                Assert.AreEqual(held - loss, progression.Experience);
            }
            finally { End(); }
        }

        static DamageResult Hit(float damage) => new DamageResult { DamageDealt = damage, RawPhysicalDamage = damage, MitigatedPhysicalDamage = damage };

        [UnityTest]
        public IEnumerator B75_TheFirstHit_HoldsTheKill_NotTheMostDamageNorTheLastBlow()
        {
            // H10 + H11: A hits first for 1, B does the rest and kills it: the kill is A's (NGO HostileNet tag, HostileKillRewards credit)
            Begin();
            HostileKill? paid = null;
            System.Action<HostileKill> seen = k => paid = k;
            HostileKillRewards.Rewarded += seen;
            try
            {
                HostileNet hostile = Bonewalker(Vector3.zero, Vector3.forward);
                GameObject a = Player(751, new Vector3(0f, 0f, 1.5f));
                GameObject b = Player(752, new Vector3(1.5f, 0f, 0f));
                yield return Frames(30);
                hostile.Entity.TakeDamage(Hit(1f), a.GetComponent<CharacterCombatStats>());
                Assert.AreEqual(a.GetComponent<PlayerCharacterProgression>(), hostile.TagHolder, "A's first hit holds it");
                hostile.Entity.TakeDamage(Hit(hostile.Entity.CurrentHealth * 0.6f), b.GetComponent<CharacterCombatStats>());
                Assert.AreEqual(a.GetComponent<PlayerCharacterProgression>(), hostile.TagHolder, "B's bigger hit does not take it");
                hostile.Entity.TakeDamage(Hit(hostile.Entity.CurrentHealth + 10f), b.GetComponent<CharacterCombatStats>());
                Assert.IsFalse(hostile.Entity.IsAlive, "dead");
                Assert.IsTrue(paid.HasValue, "paid out");
                Assert.AreEqual(a.GetComponent<PlayerCharacterProgression>(), paid.Value.Credited, "credited to the first hit, not the last blow");
            }
            finally
            {
                HostileKillRewards.Rewarded -= seen;
                End();
            }
        }

        [UnityTest]
        public IEnumerator B76_B77_AKillPays_NgosXpForTheLevelGap_AndItsDropsAtTheCorpse()
        {
            // H11 (XP = HostileKillRewards.ExperienceFor at the gap, alone, no boost) + H12 (drop chance and gear at the gap, at the corpse)
            Begin();
            HostileKill? paid = null;
            System.Action<HostileKill> seen = k => paid = k;
            HostileKillRewards.Rewarded += seen;
            try
            {
                HostileNet hostile = Bonewalker(new Vector3(2f, 0f, 3f), Vector3.forward);
                GameObject a = Player(761, new Vector3(2f, 0f, 4.5f));
                yield return Frames(30);
                var progression = a.GetComponent<PlayerCharacterProgression>();
                progression.SetLevel(Mathf.Max(1, hostile.Level));   // an even fight: a gap of 0, and the XP stays inside the level
                yield return null;
                int level = progression.CurrentLevel;
                int gap = LevelGapRules.Gap(hostile.Level, level);
                int expected = HostileKillRewards.ExperienceFor(hostile.Stats, hostile.LevelGap, gap, 0f);
                Assert.Greater(expected, 0, "planned: XP to see");
                Assert.Less(progression.Experience + expected, progression.ExperienceRequired, "planned: no level-up in between");
                int before = progression.Experience;
                Vector3 corpse = hostile.transform.position;

                hostile.Entity.TakeDamage(Hit(hostile.Entity.CurrentHealth + 10f), a.GetComponent<CharacterCombatStats>());
                Assert.IsFalse(hostile.Entity.IsAlive);
                Assert.AreEqual(level, progression.CurrentLevel);
                Assert.AreEqual(before + expected, progression.Experience, "exactly NGO's XP for the gap");
                Assert.IsTrue(paid.HasValue);
                Assert.AreEqual(progression, paid.Value.Credited);
                Assert.AreEqual(LevelGapRules.DropPercent(hostile.LevelGap, gap), paid.Value.DropPercent, 1e-4, "NGO's drop chance at the gap");
                Assert.AreEqual(LevelGapRules.GearDrops(hostile.LevelGap, gap), paid.Value.GearDrops);
                Assert.AreEqual(corpse, paid.Value.Position, "at the corpse");
                Assert.AreEqual(hostile.Definition.id, paid.Value.SpeciesId);
            }
            finally
            {
                HostileKillRewards.Rewarded -= seen;
                End();
            }
        }
    }
}
