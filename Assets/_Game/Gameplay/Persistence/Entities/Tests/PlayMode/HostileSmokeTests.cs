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
        // ------------------------------------------------------------------ M6.2 zones on the server only

        const string ZonePath = "Assets/_Game/World/Zones/Zone01/HostileZones/HZ_mob-nw.asset";   // Zone01's Blightborn zone (level 4, 12, r 30)
        const string BlightbornPath = "Assets/_Game/Gameplay/Hostiles/Prefabs/Hostile_Blightborn.prefab";
        const string SettingsPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileSettings.asset";
        static readonly Vector3 ZoneCentre = new Vector3(600f, 0f, 600f);   // away from every other test's bodies

        /// <summary>Flat walkable ground for the zone's spawn spots (a NavMesh of its own) and a floor for the player body.</summary>
        UnityEngine.AI.NavMeshDataInstance Ground()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            m_Made.Add(floor);
            floor.transform.position = ZoneCentre;
            floor.transform.localScale = new Vector3(30f, 1f, 30f);   // 300 m
            var source = new UnityEngine.AI.NavMeshBuildSource
            {
                shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
                size = new Vector3(300f, 0.2f, 300f),
                transform = Matrix4x4.TRS(ZoneCentre + Vector3.down * 0.1f, Quaternion.identity, Vector3.one),
                area = 0,
            };
            UnityEngine.AI.NavMeshData data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(UnityEngine.AI.NavMesh.GetSettingsByIndex(0),
                new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource> { source },
                new Bounds(ZoneCentre, new Vector3(310f, 20f, 310f)), Vector3.zero, Quaternion.identity);
            return UnityEngine.AI.NavMesh.AddNavMeshData(data);
        }

        /// <summary>A Hostile Zone as Zone01 has it (its definition, the game's settings, the NGO species prefab), switched off until enabled.</summary>
        HostileZone Zone()
        {
            var go = new GameObject("M6.2 zone");
            go.SetActive(false);
            go.transform.position = ZoneCentre;
            m_Made.Add(go);
            var zone = go.AddComponent<HostileZone>();
            var so = new UnityEditor.SerializedObject(zone);
            so.FindProperty("definition").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<HostileZoneDefinition>(ZonePath);
            so.FindProperty("settings").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<HostileSettings>(SettingsPath);
            so.FindProperty("hostilePrefab").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BlightbornPath).GetComponent<HostileNet>();
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsNotNull(zone.Definition, ZonePath);
            return zone;
        }

        /// <summary>The server bodies stand in for the species prefabs, as HostileEntitiesSetup installs them.</summary>
        static void UseServerBodies()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<HostileBodyCatalog>(HostileBodyCatalog.AssetPath);
            Assert.IsNotNull(catalog, "Build DOTS Assets");
            HostilePool.BodyOf = species => catalog.BodyOf(species);
        }

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float end = Time.time + seconds;
            while (!done() && Time.time < end) yield return null;
        }

        static void EndZone(HostileZone zone, UnityEngine.AI.NavMeshDataInstance ground)
        {
            HostileSpawner.AuthorityOverride = () => true;
            if (zone != null) { zone.Clear(); Object.DestroyImmediate(zone.gameObject); }   // with authority: its bodies go back to the pool
            ground.Remove();
            HostileZone.PlayersOverride = null;
            HostilePool.BodyOf = null;
        }

        static void AllClearOf(HostileZone zone, Vector3 player, float clearance)
        {
            foreach (HostileNet h in HostileNet.Active)
            {
                if (h == null || h.Zone != zone) continue;
                Vector3 d = h.transform.position - player; d.y = 0f;
                Assert.GreaterOrEqual(d.magnitude, clearance - 1e-3f, h.name + " spawned within the clearance of a player (H16)");
                Vector3 c = h.transform.position - zone.Centre; c.y = 0f;
                Assert.LessOrEqual(c.magnitude, zone.Radius + 1e-3f, h.name + " inside its zone");
            }
        }

        [UnityTest]
        public IEnumerator B80_AZone_RunsOnTheServerOnly_WakesForAServerPlayerBody_AndSleepHealsAndCalms()
        {
            // H15 (NGO HostileZone.Tick, 0.5 s): awake while a player is within radius + wake margin (30 + 40); asleep ends fights and heals.
            // The players are the entity server's player bodies (HostileZone.PlayersOverride, M6.2); bodies are server bodies (HostilePool.BodyOf).
            Begin();
            UnityEngine.AI.NavMeshDataInstance ground = Ground();
            HostileZone zone = null;
            try
            {
                UseServerBodies();
                GameObject p = Player(801, ZoneCentre + new Vector3(0f, 0f, 30f + 40f + 15f));
                HostileZone.PlayersOverride = list => list.Add(p.transform);
                zone = Zone();
                float margin = zone.Definition.wakeMargin;
                Assert.AreEqual(40f, margin, "Zone01's wake margin");

                // a client of the entity netcode: never decides the Hostiles, so its zones spawn nothing (no offline NGO Hostiles)
                HostileSpawner.AuthorityOverride = () => false;
                zone.gameObject.SetActive(true);
                yield return Until(() => false, 1.5f);
                Assert.AreEqual(0, zone.Capacity, "a client's zone never begins");
                Assert.AreEqual(0, zone.Alive);

                // the server: the zone begins, asleep while the player is beyond radius + margin
                HostileSpawner.AuthorityOverride = () => true;
                yield return Until(() => false, 1.5f);
                Assert.AreEqual(zone.Definition.maxAlive, zone.Capacity, "the server's zone began");
                Assert.IsFalse(zone.IsAwake, "nobody within radius + margin");
                Assert.AreEqual(0, zone.Alive, "an asleep zone fills nothing");

                // the player walks to 1 m inside the wake margin: the zone wakes and fills (4 a tick)
                p.transform.position = ZoneCentre + new Vector3(0f, 0f, zone.Radius + margin - 1f);
                yield return Until(() => zone.IsAwake && zone.Alive == zone.Capacity, 5f);
                Assert.IsTrue(zone.IsAwake, "woken by the server's player body");
                Assert.AreEqual(zone.Capacity, zone.Alive, "every slot filled");
                HostileNet one = null;
                foreach (HostileNet h in HostileNet.Active)
                {
                    if (h == null || h.Zone != zone) continue;
                    Assert.IsNotNull(h.GetComponent<KryticalNova.Hostiles.Entities.EcsHostileBrain>(), h.name + ": a server body (the entity brain)");
                    one ??= h;
                }
                AllClearOf(zone, p.transform.position, 3f);

                // a fight, then the player walks away: asleep, the fight is over and the hurt Hostile is whole again
                one.Entity.TakeDamage(Hit(one.Entity.MaxHealth * 0.5f), p.GetComponent<CharacterCombatStats>());
                Assert.Less(one.Entity.CurrentHealth, one.Entity.MaxHealth, "hurt");
                Assert.IsNotNull(one.TagHolder, "the hit tagged it");
                p.transform.position = ZoneCentre + new Vector3(0f, 0f, zone.Radius + margin + 15f);
                yield return Until(() => !zone.IsAwake, 3f);
                Assert.IsFalse(zone.IsAwake, "asleep again");
                Assert.AreEqual(one.Entity.MaxHealth, one.Entity.CurrentHealth, 1e-3, "sleep heals (H15)");
                Assert.IsNull(one.TagHolder, "and ends the fight: the tag is released");
                // the entity brain itself falling asleep (BrainActive off, the fight dropped): EditMode ZoneSleepTests
            }
            finally
            {
                EndZone(zone, ground);
                End();
            }
        }

        [UnityTest]
        public IEnumerator B81_ARespawn_ComesAfterNgosTime_AtLevel4_AndNeverWithin3mOfAPlayer()
        {
            // H16: Normal 8 + 0.12 s a level (Blightborn level 4: 8.48 s), due at the zone's next 0.5 s tick; spawn clearance 3 m.
            Begin();
            UnityEngine.AI.NavMeshDataInstance ground = Ground();
            HostileZone zone = null;
            try
            {
                UseServerBodies();
                GameObject p = Player(811, ZoneCentre);   // in the middle of the zone: every spot must keep clear of it
                HostileZone.PlayersOverride = list => list.Add(p.transform);
                zone = Zone();
                zone.gameObject.SetActive(true);
                yield return Until(() => zone.IsAwake && zone.Alive == zone.Capacity, 5f);
                Assert.AreEqual(zone.Capacity, zone.Alive, "full");
                AllClearOf(zone, p.transform.position, 3f);

                HostileNet victim = null;
                foreach (HostileNet h in HostileNet.Active) if (h != null && h.Zone == zone) { victim = h; break; }
                Assert.AreEqual(4, victim.Level, "Blightborn at its level");
                Assert.AreEqual(8.48f, victim.Stats.RespawnSeconds, 1e-3f, "NGO's respawn time at level 4");
                float killedAt = Time.time;
                victim.Entity.TakeDamage(Hit(victim.Entity.CurrentHealth + 10f), p.GetComponent<CharacterCombatStats>());
                Assert.IsFalse(victim.Entity.IsAlive, "killed");
                Assert.AreEqual(zone.Capacity - 1, zone.Alive, "a slot is empty");

                yield return Until(() => zone.Alive == zone.Capacity, 8.48f + 3f);
                float took = Time.time - killedAt;
                Assert.AreEqual(zone.Capacity, zone.Alive, "the slot came back");
                Assert.GreaterOrEqual(took, 8.48f - 0.02f, "never before NGO's time");
                Assert.LessOrEqual(took, 8.48f + 0.5f + 0.2f, "at the zone tick after it is due (0.5 s), plus a frame");
                AllClearOf(zone, p.transform.position, 3f);
            }
            finally
            {
                EndZone(zone, ground);
                End();
            }
        }

        // ------------------------------------------------------------------ M6.3 GM commands on the entity server

        /// <summary>A Quantum Console command of HostileCommands, run as the console runs it (the methods are the console's, not public).</summary>
        static string Gm(string method, params object[] args)
        {
            System.Reflection.MethodInfo m = typeof(KryticalNova.DevTools.HostileCommands).GetMethod(method,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(m, "HostileCommands." + method);
            return (string)m.Invoke(null, args);
        }

        static System.Collections.Generic.List<HostileNet> GmSpawned()
        {
            var list = new System.Collections.Generic.List<HostileNet>();
            foreach (HostileNet h in HostileNet.Active) if (h != null && h.GmSpawned) list.Add(h);
            return list;
        }

        [UnityTest]
        public IEnumerator B82_TheGmCommands_RunOnTheEntityServer_ForThePickedPlayer_AndAFrozenHostileStandsStillTakingHits()
        {
            // H20: gm.hostile.* / gm.zone.* keep their names and act on HostileNet bodies; on the entity server the species come from the
            // server body catalog (HostileSpawner.SpeciesOverride) and the player is the picked connection's server body (GmTarget, M7.4).
            Begin();
            UnityEngine.AI.NavMeshDataInstance ground = Ground();
            HostileZone zone = null;
            KryticalNova.DevTools.GmTarget.Saved oldTarget = KryticalNova.DevTools.GmTarget.Save();
            try
            {
                UseServerBodies();
                var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<HostileBodyCatalog>(HostileBodyCatalog.AssetPath);
                HostileSpawner.SpeciesOverride = () =>
                {
                    var species = new System.Collections.Generic.List<HostileNet>();
                    foreach (HostileBodyCatalog.Entry e in catalog.entries) species.Add(e.species);
                    return species;
                };
                KryticalNova.Core.PlayerKeys.Reset();
                KryticalNova.Core.PlayerKey key = KryticalNova.Core.PlayerKeys.Mint(821);
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                GameObject p = KryticalNova.Player.Entities.Bodies.PlayerBodyAssembler.Create(prefab, KryticalNova.Core.NetRoleKind.ServerBody,
                    key, 821, false, ZoneCentre + new Vector3(40f, 0f, 0f), 0f);
                m_Made.Add(p);
                HostileZone.PlayersOverride = list => list.Add(p.transform);
                Assert.IsTrue(KryticalNova.DevTools.GmTarget.TrySetTarget(821, out string error), error);
                yield return Frames(5);

                StringAssert.Contains("Bonewalker", Gm("List"), "gm.hostile.list: the catalog's species");
                StringAssert.StartsWith("Spawned 3 Bonewalker at level 12", Gm("Spawn", "Bonewalker", 3, 12, ""));
                System.Collections.Generic.List<HostileNet> made = GmSpawned();
                Assert.AreEqual(3, made.Count, "three, GM-spawned");
                foreach (HostileNet h in made)
                {
                    Assert.AreEqual(12, h.Level, "at the level asked");
                    Assert.IsNotNull(h.GetComponent<KryticalNova.Hostiles.Entities.EcsHostileBrain>(), "server bodies");
                }
                yield return Frames(10);

                // freeze: every brain stops; a frozen one takes a hit and stays where it stood, still frozen
                StringAssert.EndsWith("frozen.", Gm("Freeze", "on"));
                foreach (HostileNet h in made) Assert.IsTrue(h.Brain.Frozen, h.name + " frozen");
                HostileNet struck = made[0];
                Vector3 stood = struck.transform.position;
                float hp = struck.Entity.CurrentHealth;
                struck.Entity.TakeDamage(Hit(5f), p.GetComponent<CharacterCombatStats>());
                yield return Frames(30);
                Assert.AreEqual(hp - 5f, struck.Entity.CurrentHealth, 1e-3, "it takes the hit");
                Assert.IsTrue(struck.Brain.Frozen, "and stays frozen");
                Assert.Less(Vector3.Distance(stood, struck.transform.position), 0.01f, "standing still");
                StringAssert.EndsWith("released.", Gm("Freeze", "off"));
                foreach (HostileNet h in made) Assert.IsFalse(h.Brain.Frozen, h.name + " released");

                // health, info, kill (the nearest), the brain lines, who fights whom
                StringAssert.Contains("HP 10/", Gm("Health", "10"));
                StringAssert.Contains("species 2", Gm("Info"));
                StringAssert.StartsWith("Killed 1", Gm("Kill", 0f));
                int dead = 0;
                foreach (HostileNet h in made) if (!h.Entity.IsAlive) dead++;
                Assert.AreEqual(1, dead, "one dead");
                Gm("BrainLines", "on");
                Assert.IsTrue(KryticalNova.Hostiles.Entities.HostileBrainDiag.Enabled, "gm.hostile.brain on");
                Gm("BrainLines", "off");
                Assert.IsFalse(KryticalNova.Hostiles.Entities.HostileBrainDiag.Enabled, "gm.hostile.brain off");
                Assert.IsNotNull(Gm("Slots"), "gm.hostile.slots answers");
                StringAssert.StartsWith("Removed ", Gm("Clear", 0f));
                Assert.AreEqual(0, GmSpawned().Count, "gm.hostile.clear: every GM-spawned one gone");

                // the zone commands: held awake, it fills; emptied; filled again at once; held asleep
                zone = Zone();
                zone.gameObject.SetActive(true);
                yield return Frames(5);
                string id = zone.Definition.id;
                StringAssert.Contains("held awake", Gm("ZoneSleep", id, "off"));
                yield return Until(() => zone.IsAwake && zone.Alive == zone.Capacity, 5f);
                Assert.AreEqual(zone.Capacity, zone.Alive, "gm.zone.sleep off: awake and full");
                StringAssert.Contains("emptied", Gm("ZoneClear", id, 60f));
                Assert.AreEqual(0, zone.Alive, "gm.zone.clear: empty");
                StringAssert.Contains($"{zone.Capacity} brought back", Gm("ZoneFill", id));
                Assert.AreEqual(zone.Capacity, zone.Alive, "gm.zone.fill: back now");
                StringAssert.Contains("held asleep", Gm("ZoneSleep", id, "on"));
                yield return Until(() => !zone.IsAwake, 3f);
                Assert.IsFalse(zone.IsAwake, "gm.zone.sleep on: asleep with a player inside");
                Gm("ZoneSleep", id, "auto");
            }
            finally
            {
                Gm("Freeze", "off");
                KryticalNova.DevTools.GmTarget.Restore(oldTarget);
                KryticalNova.Hostiles.Entities.HostileBrainDiag.On = null;
                HostileSpawner.SpeciesOverride = null;
                foreach (HostileNet h in GmSpawned()) HostileSpawner.Remove(h);
                EndZone(zone, ground);
                KryticalNova.Core.PlayerKeys.Reset();
                End();
            }
        }

        // ------------------------------------------------------------------ M6.4 consumers

        [UnityTest]
        public IEnumerator B83_WhatAPlayerSeesAndPicks_LeavesOutTheHiddenServerBody_AndTakesTheView()
        {
            // H22: nameplates, minimap, Autopilot, the pointer and spirits read HostileNet.Active / IHostile; in Play DOTS here the hidden
            // server body and the client's view stand in one process, at one spot. The pool marks the body (HostilePool.BodyOf) and they skip it.
            Begin();
            UnityEngine.AI.NavMeshDataInstance ground = Ground();
            HostileNet body = null, view = null;
            try
            {
                var species = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SpeciesPath).GetComponent<HostileNet>();
                UseServerBodies();
                body = HostilePool.Take(species, ZoneCentre, Quaternion.identity);
                Assert.IsTrue(body.IsServerBody, "taken through the server body catalog: the hidden body");
                Assert.IsTrue(((KryticalNova.Core.IHiddenServerBody)body).IsHiddenServerBody, "and says so to Core (pointer, spirits)");
                Assert.IsNotNull(body.GetComponent<KryticalNova.Hostiles.Entities.EcsHostileBrain>());
                HostilePool.BodyOf = null;
                view = HostilePool.Take(species, ZoneCentre, Quaternion.identity);
                Assert.IsFalse(view.IsServerBody, "the species itself (NGO, or a client view): shown");
                Assert.IsFalse(((KryticalNova.Core.IHiddenServerBody)view).IsHiddenServerBody);
                yield return Frames(3);
                CollectionAssert.Contains(HostileNet.Active, body, "the server's code still sees its body (zones, GM)");
                CollectionAssert.Contains(HostileNet.Active, view);

                // the pointer: a ray through both capsules picks the view, never the hidden body in front of it
                Physics.SyncTransforms();
                var pointerObject = new GameObject("M6.4 pointer");   // the client's own pointer (a server body has none)
                m_Made.Add(pointerObject);
                pointerObject.transform.position = ZoneCentre + new Vector3(0f, 0f, -6f);
                var pointer = pointerObject.AddComponent<KryticalNova.Player.PointAndClickPlayer>();
                body.transform.position = ZoneCentre + new Vector3(0f, 0f, -0.3f);   // a little nearer the camera than the view
                Physics.SyncTransforms();
                var pick = typeof(KryticalNova.Player.PointAndClickPlayer).GetMethod("TryPickPointerHit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.IsNotNull(pick, "PointAndClickPlayer.TryPickPointerHit");
                object[] args = { new Ray(ZoneCentre + new Vector3(0f, 1f, -10f), Vector3.forward), null };
                Assert.IsTrue((bool)pick.Invoke(pointer, args), "the ray picks something");
                var hit = (RaycastHit)args[1];
                Assert.AreSame(view, hit.collider.GetComponentInParent<HostileNet>(), "the view, not the hidden body");
            }
            finally
            {
                HostilePool.BodyOf = null;
                if (body != null) HostilePool.Return(body);
                if (view != null) HostilePool.Return(view);
                ground.Remove();
                End();
            }
        }

    }
}
