using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// M6.1 (implementation plan s5): every species of the roster on the entity brain. Reads the real assets the builders wrote
    /// (Krytical Nova > Hostiles > Build Hostiles / Apply Brain Data / Build DOTS Assets).
    /// </summary>
    public sealed class RosterTests
    {
        const string SpeciesFolder = "Assets/_Game/Gameplay/Hostiles/Data/Assets/Species";
        const string CurvePath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileStatCurve.asset";
        const int TickRate = HostileTestWorld.TickRate;
        /// <summary>Brain plan s3.8: what the player does not see of a windup = 50 ms half RTT + 2 ticks interpolation + 1 tick.</summary>
        const float UnseenSeconds = 0.05f + 3f / TickRate;

        static List<HostileDefinition> Roster()
        {
            var list = new List<HostileDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:HostileDefinition", new[] { SpeciesFolder }))
                list.Add(AssetDatabase.LoadAssetAtPath<HostileDefinition>(AssetDatabase.GUIDToAssetPath(guid)));
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return list;
        }

        [Test]
        public void EveryDefinitionHasBodyAndBlob()
        {
            List<HostileDefinition> roster = Roster();
            var ids = new List<int>();
            foreach (HostileDefinition d in roster) ids.Add(d.id);
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, ids, "ids 2-11, one species each (1 was the stand-in)");

            var catalog = AssetDatabase.LoadAssetAtPath<HostileBodyCatalog>(HostileBodyCatalog.AssetPath);
            Assert.IsNotNull(catalog, "the server body catalog (Build DOTS Assets)");
            using var w = new HostileTestWorld();
            foreach (HostileDefinition d in roster)
            {
                Assert.IsNotNull(d.prefab, d.name + ": NGO prefab");
                var species = d.prefab.GetComponent<HostileNet>();
                Assert.IsNotNull(species, d.name + ": the prefab's HostileNet");
                HostileNet body = catalog.BodyOf(species);
                Assert.IsNotNull(body, d.name + ": a server body in the catalog");
                Assert.AreSame(d, body.Definition, d.name + ": the body plays the same species");
                Assert.IsNotNull(body.GetComponent<EcsHostileBrain>(), d.name + ": the body's entity brain");

                // the species bake: one ability row per attack, the same id on the brain
                Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack, d.id);
                ConfigureOn(w, h, d, d.StatsFrom(body.Curve != null ? body.Curve : Curve()));
                HostileLife life = w.Em.GetComponentData<HostileLife>(h);
                Assert.AreEqual(d.id, life.Species, d.name + ": id");
                Assert.AreEqual(d.attacks.Length, w.Em.GetBuffer<HostileAbilityRow>(h).Length, d.name + ": a row per attack");
                Assert.AreEqual(d.tier == HostileTier.Bruiser ? 70 : 30, life.Poise, d.name + ": poise normal 30, Bruiser 70");
                Assert.AreEqual(d.behaviour == HostileBehaviour.AttacksOnSight ? 1 : 0, life.Assist, d.name + ": assist = attacks on sight");
                Assert.AreEqual(0, life.Flees, d.name + ": nobody flees by default");
                Assert.AreEqual((byte)d.scoreProfile, life.Profile, d.name + ": score profile");
            }
            Assert.AreEqual(HostileScoreProfile.Skirmisher, Find(roster, 6).scoreProfile, "Wraith (proposal)");
            Assert.AreEqual(HostileScoreProfile.Skirmisher, Find(roster, 7).scoreProfile, "Seraph Unit (proposal)");
            Assert.AreEqual(HostileScoreProfile.Finisher, Find(roster, 10).scoreProfile, "Hellion (proposal)");
            Assert.AreEqual(HostileScoreProfile.Finisher, Find(roster, 11).scoreProfile, "Colossus (proposal)");
            foreach (int brute in new[] { 2, 3, 4, 5, 8, 9 })
                Assert.AreEqual(HostileScoreProfile.Brute, Find(roster, brute).scoreProfile, Find(roster, brute).name + " (proposal)");
        }

        [Test]
        public void BlobRowsEqualCurve()
        {
            HostileStatCurve curve = Curve();
            Assert.GreaterOrEqual(curve.MaxLevel, 145, "levels 1-145");
            using var w = new HostileTestWorld();
            int checkedRows = 0;
            foreach (HostileDefinition d in Roster())
            {
                Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack, d.id);
                foreach (HostileTier tier in new[] { HostileTier.Normal, HostileTier.Bruiser })
                    for (int level = 1; level <= 145; level++)
                    {
                        HostileStats stats = curve.Evaluate(level, tier);
                        ConfigureOn(w, h, d, stats);
                        HostileLife life = w.Em.GetComponentData<HostileLife>(h);
                        string at = $"{d.name} {tier} level {level}";
                        Assert.AreEqual(level, life.Level, at);
                        Assert.AreEqual((byte)tier, life.Tier, at);
                        // H01: Emerald's detection radius = the curve's, rounded up (HostileBrainLink.cs:109)
                        Assert.AreEqual((float)Mathf.Max(1, Mathf.CeilToInt(stats.AggroRadius)), life.AggroRadius, at + ": aggro radius");
                        Assert.AreEqual(HostileRules.AggroRadius(curve.aggroRadius, level), life.AggroRadius, at + ": the brain's rule = the curve");
                        Assert.AreEqual(stats.WalkSpeed, life.Walk, at + ": walk");
                        Assert.AreEqual(stats.RunSpeed, life.Run, at + ": run");
                        Assert.AreEqual(d.attackRange, life.AttackRange, at + ": range");
                        Assert.AreEqual(d.reachAbove, life.ReachAbove, at + ": reach above");
                        checkedRows++;
                    }
            }
            Assert.AreEqual(10 * 2 * 145, checkedRows);
        }

        /// <summary>
        /// Q3 (owner, 6 Nov 2026): light windups 0.25 -> 0.40 s, heavy 0.55 s; NGO's Emerald strikes at the same moment (the clip's
        /// one attack event at the windup); the brain's windup ticks equal the seconds. The plan's "visible >= 0.35 s" holds for the
        /// heavy swings; a light one shows 0.40 - 0.15 = 0.25 s of windup and the tell (Q3 option A, 7 Nov 2026) the rest.
        /// </summary>
        [Test]
        public void WarningAtLeast035()
        {
            using var w = new HostileTestWorld();
            foreach (HostileDefinition d in Roster())
            {
                Entity h = w.Hostile(float3.zero, HostileBehaviourKind.FightsBack, d.id);
                ConfigureOn(w, h, d, default);
                DynamicBuffer<HostileAbilityRow> rows = w.Em.GetBuffer<HostileAbilityRow>(h);
                Assert.AreEqual(3, d.attacks.Length, d.name + ": three attacks (H05)");
                for (int i = 0; i < d.attacks.Length; i++)
                {
                    HostileDefinition.Attack a = d.attacks[i];
                    string at = $"{d.name} attack {i + 1}";
                    Assert.AreEqual(a.heavy ? 0.55f : 0.40f, a.windupSeconds, 1e-5f, at + ": Q3 windup");
                    // Q3 option A: the tell fills a light swing up to 0.35 s on screen (HostileRules.TellTicks)
                    uint tell = HostileRules.TellTicks(rows[i].WindupTicks, 11, 5);
                    Assert.GreaterOrEqual((rows[i].WindupTicks + tell) / (float)TickRate - UnseenSeconds, 0.35f - 1e-4f, at + ": visible warning, the tell included");
                    Assert.AreEqual((uint)math.max(1, (int)math.ceil(a.windupSeconds * TickRate)), rows[i].WindupTicks, at + ": windup ticks");
                    AnimationEvent[] events = AnimationUtility.GetAnimationEvents(a.clip);
                    Assert.AreEqual(1, events.Length, at + ": one strike event on the clip");
                    Assert.AreEqual(a.windupSeconds, events[0].time, 1e-4f, at + ": NGO's strike at the windup");
                }
            }
        }

        static HostileStatCurve Curve()
        {
            var curve = AssetDatabase.LoadAssetAtPath<HostileStatCurve>(CurvePath);
            Assert.IsNotNull(curve, CurvePath);
            return curve;
        }

        static HostileDefinition Find(List<HostileDefinition> roster, int id) => roster.Find(d => d.id == id);

        static void ConfigureOn(HostileTestWorld w, Entity h, HostileDefinition d, HostileStats stats)
        {
            var go = new GameObject("RosterBody");
            try
            {
                var brain = go.AddComponent<EcsHostileBrain>();
                brain.Bind(w.World, h);
                brain.Configure(d, stats);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
