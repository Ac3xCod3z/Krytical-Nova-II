using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Brains.Tests
{
    /// <summary>
    /// A plain world with the Brain's systems, ticked by hand: the test writes the tick and the sensed players (the job of the mind's
    /// feeder in the game). No netcode needed: the Brain reads only <see cref="BrainTick"/>.
    /// </summary>
    public sealed class BrainTestWorld : IDisposable
    {
        public const int TickRate = 30;
        public readonly World World;
        public uint Tick;
        readonly SystemHandle m_Sense, m_Clock;
        readonly EntityQuery m_TickQuery, m_SenseQuery, m_ConfigQuery;

        public BrainTestWorld(uint worldSeed = 0x4B4E)
        {
            World = new World("BrainTestWorld");
            var settings = ScriptableObject.CreateInstance<BrainSettings>();
            settings.worldSeed = worldSeed;
            BrainConfig config = BrainConfig.From(settings, TickRate);
            UnityEngine.Object.DestroyImmediate(settings);
            World.EntityManager.CreateSingleton(config);
            World.EntityManager.CreateSingleton(new BrainTick());
            m_Sense = World.CreateSystem<SenseBuildSystem>();
            m_Clock = World.CreateSystem<ThinkClockSystem>();
            m_TickQuery = World.EntityManager.CreateEntityQuery(typeof(BrainTick));
            m_SenseQuery = World.EntityManager.CreateEntityQuery(typeof(SenseWorld));
            m_ConfigQuery = World.EntityManager.CreateEntityQuery(typeof(BrainConfig));
        }

        public EntityManager Em => World.EntityManager;
        public BrainConfig Config => m_ConfigQuery.GetSingleton<BrainConfig>();

        /// <summary>A brain at <paramref name="at"/> with its stable id and dice.</summary>
        public Entity Brain(float3 at, uint stableId)
        {
            EntityManager em = Em;
            Entity e = em.CreateEntity(typeof(BrainTag), typeof(BrainActive), typeof(BrainClock), typeof(BrainDice), typeof(LocalTransform));
            em.AddBuffer<BrainEvent>(e);
            em.SetComponentData(e, LocalTransform.FromPosition(at));
            em.SetComponentData(e, new BrainClock { StableId = stableId });
            em.SetComponentData(e, new BrainDice { Rng = BrainDiceRules.Create(stableId, Config.WorldSeed) });
            return e;
        }

        /// <summary>The sense singleton (its containers are shared handles: changes through this copy are the world's).</summary>
        public SenseWorld Sense => m_SenseQuery.GetSingleton<SenseWorld>();

        public void SetPlayers(params float3[] feet)
        {
            SenseWorld sense = Sense;
            sense.Players.Clear();
            for (int i = 0; i < feet.Length; i++)
                sense.Players.Add(new SensedPlayer { GhostId = 100 + i, Feet = feet[i], Flags = SensedFlags.Alive });
        }

        /// <summary>Runs the Brain's systems for <paramref name="ticks"/> ticks (the first tick is 1).</summary>
        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                Tick++;
                m_TickQuery.SetSingleton(new BrainTick { Tick = Tick });
                m_Sense.Update(World.Unmanaged);
                m_Clock.Update(World.Unmanaged);
            }
        }

        public BrainClock Clock(Entity brain) => Em.GetComponentData<BrainClock>(brain);

        public void Dispose()
        {
            if (World.IsCreated) World.Dispose();
        }
    }
}
