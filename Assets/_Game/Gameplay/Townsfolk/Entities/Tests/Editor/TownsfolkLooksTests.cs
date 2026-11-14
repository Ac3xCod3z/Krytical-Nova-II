using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Townsfolk.Editor;
using NUnit.Framework;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Townsfolk.Tests
{
    /// <summary>
    /// Plan evals "same person on every client" and "faction cities only wear their faction; Sky Concord mixes all three", on the real
    /// creation database; and the ghost lint (one prefab, interpolated, never owned) plus the built assets.
    /// </summary>
    public sealed class TownsfolkLooksTests
    {
        const string DatabasePath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterCreationDatabase.asset";
        CharacterCreationDatabase m_Db;
        readonly List<Object> m_Made = new();

        [SetUp]
        public void SetUp()
        {
            m_Db = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(DatabasePath);
            Assert.IsNotNull(m_Db, DatabasePath);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made) if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear();
        }

        TownsfolkCast Cast(byte id, params FactionId[] factions)
        {
            var cast = ScriptableObject.CreateInstance<TownsfolkCast>();
            cast.id = id;
            var list = new List<TownsfolkCast.FactionWeight>();
            foreach (FactionId f in factions) list.Add(new TownsfolkCast.FactionWeight { faction = f, weight = 1f });
            cast.factions = list.ToArray();
            m_Made.Add(cast);
            return cast;
        }

        static string Key(in CharacterRecipe r) => r.faction + "/" + r.lineage + "/" + string.Join(",", r.parts ?? new ushort[0]) + "/" + r.skinTone + "/" + r.build;

        [Test]
        public void SameSeedSameLook()
        {
            TownsfolkCast cast = Cast(1, FactionId.Hegemony, FactionId.Null, FactionId.Covenant);
            for (int look = 0; look < 16; look++)
            {
                Assert.IsTrue(TownsfolkLooks.Recipe(cast, m_Db, look, out CharacterRecipe a, out BodyType ba, out _), "look " + look);
                Assert.IsTrue(TownsfolkLooks.Recipe(cast, m_Db, look, out CharacterRecipe b, out BodyType bb, out _));
                Assert.AreEqual(Key(a), Key(b), "look " + look + " is the same person both times");
                Assert.AreEqual(ba, bb);
            }
            Assert.AreEqual(TownsfolkLooks.LookIndex(1234u, 16), TownsfolkLooks.LookIndex(1234u, 16));
        }

        [Test]
        public void HomeCityWearsOnlyItsFactionAndConcordMixes()
        {
            foreach (FactionId only in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
            {
                TownsfolkCast home = Cast(10, only);
                for (int look = 0; look < 24; look++)
                {
                    Assert.IsTrue(TownsfolkLooks.Recipe(home, m_Db, look, out CharacterRecipe r, out _, out FactionId f));
                    Assert.AreEqual(only, f);
                    Assert.AreEqual(only, r.faction, "a " + only + " home city's look " + look);
                }
            }
            TownsfolkCast concord = Cast(1, FactionId.Hegemony, FactionId.Null, FactionId.Covenant);
            var seen = new HashSet<FactionId>();
            for (int look = 0; look < 32; look++)
                if (TownsfolkLooks.Recipe(concord, m_Db, look, out _, out _, out FactionId f)) seen.Add(f);
            Assert.AreEqual(3, seen.Count, "Sky Concord mixes all three factions");
        }

        [Test]
        public void GhostPrefabIsInterpolatedAndNeverOwned()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TownsfolkEntitiesAssetsBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "run Krytical Nova ▸ Townsfolk ▸ Build Assets");
            var ghost = prefab.GetComponent<GhostAuthoringComponent>();
            Assert.IsNotNull(ghost);
            Assert.IsFalse(ghost.HasOwner, "townsfolk are never owned (PlayerGhost stays the only owned ghost)");
            Assert.AreEqual(GhostMode.Interpolated, ghost.DefaultGhostMode);
            Assert.AreEqual(GhostModeMask.Interpolated, ghost.SupportedGhostModes);
            Assert.IsNotNull(prefab.GetComponent<TownsfolkGhostAuthoring>());
        }

        [Test]
        public void EveryTownsfolkAssetTypeHasItsOwnScriptFile()
        {
            // 14 Nov 2026 Unity run: TownsfolkCast and TownsfolkViewAssets shared a file with another class, so Unity saved their assets
            // with no script and Resources.Load gave null. A ScriptableObject's class must be the only one named like its file.
            foreach (System.Type t in typeof(TownsfolkSettings).Assembly.GetTypes())
            {
                if (!typeof(ScriptableObject).IsAssignableFrom(t) || t.IsAbstract || t.Namespace == null || !t.Namespace.StartsWith("KryticalNova.Townsfolk")) continue;
                var instance = ScriptableObject.CreateInstance(t);
                try { Assert.IsNotNull(UnityEditor.MonoScript.FromScriptableObject(instance), t.Name + " needs a file named " + t.Name + ".cs"); }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void BuiltAssetsHoldTenPersonalitiesAndTheSkyConcordMix()
        {
            var world = Resources.Load<TownsfolkWorldAssets>(TownsfolkWorldAssets.ResourcePath);
            Assert.IsNotNull(world, "run Krytical Nova ▸ Townsfolk ▸ Build Assets");
            Assert.AreEqual(10, world.personalities.Length);
            var ids = new HashSet<byte>();
            foreach (TownsfolkPersonality p in world.personalities) Assert.IsTrue(ids.Add(p.id), "personality ids are unique: " + p.name);
            TownLifeArea area = world.areas[0];
            int day = 0, night = 0;
            foreach (TownLifeArea.MixRow row in area.mix) { day += row.day; night += row.night; }
            Assert.AreEqual(80, day, "owner 14 Nov 2026 (busier crowd): 80 by day (was 48)");
            Assert.AreEqual(35, night, "and 35 at night (was 20)");
            var view = Resources.Load<TownsfolkViewAssets>(TownsfolkViewAssets.ResourcePath);
            Assert.IsNotNull(view);
            Assert.IsNotNull(view.body);
            Assert.IsNotNull(view.masculine);
            Assert.IsNotNull(view.database);
        }
    }
}
