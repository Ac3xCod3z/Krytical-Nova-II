using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using KryticalNova.Wallet;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The owner's Creds and progression on the entity netcode (M7/M8 system 0, owner Q1: "just like the current game"): the server's body is
    /// read into <see cref="OwnerProgress"/> (owner only), and the owner's own view body takes it the way NGO's owner took the wallet's and the
    /// progression's network variables, so the Pack and Character windows show the server's numbers and a level-up plays its feel there.
    /// </summary>
    public static class OwnerProgressBridge
    {
        /// <summary>What the server's <paramref name="body"/> holds now; null when it has neither part.</summary>
        public static OwnerProgress? Read(GameObject body)
        {
            if (body == null) return null;
            var wallet = body.GetComponent<IWallet>();
            var progression = body.GetComponent<PlayerCharacterProgression>();
            if (wallet == null && progression == null) return null;
            ProgressionState s = progression != null ? progression.State : default;
            return new OwnerProgress
            {
                Creds = wallet != null ? wallet.Creds : 0, Level = s.level, Experience = s.experience, StatPoints = s.statPoints,
                SkillPoints = s.skillPoints, Renown = s.renown, Strength = s.strength, Vitality = s.vitality, Agility = s.agility, Spirit = s.spirit,
            };
        }

        public static bool Same(in OwnerProgress a, in OwnerProgress b) =>
            a.Creds == b.Creds && a.Level == b.Level && a.Experience == b.Experience && a.StatPoints == b.StatPoints && a.SkillPoints == b.SkillPoints
            && a.Renown == b.Renown && a.Strength == b.Strength && a.Vitality == b.Vitality && a.Agility == b.Agility && a.Spirit == b.Spirit;

        /// <summary>The owner's view <paramref name="body"/> shows <paramref name="p"/>. Level 0 = nothing from the server yet: left as it is.</summary>
        public static void Mirror(GameObject body, in OwnerProgress p)
        {
            if (body == null || p.Level < 1) return;
            body.GetComponent<IWalletMirror>()?.MirrorCreds(p.Creds);
            body.GetComponent<PlayerCharacterProgression>()?.MirrorState(new ProgressionState
            {
                level = p.Level, experience = p.Experience, statPoints = p.StatPoints, skillPoints = p.SkillPoints, renown = p.Renown,
                strength = p.Strength, vitality = p.Vitality, agility = p.Agility, spirit = p.Spirit,
            });
        }
    }

    /// <summary>Server: keeps each player ghost's <see cref="OwnerProgress"/> equal to its body; writes only what changed.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class OwnerProgressServerSystem : SystemBase
    {
        /// <summary>Ghosts rewritten (tests).</summary>
        public int Writes { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || !em.Exists(entry.Ghost) || !em.HasComponent<OwnerProgress>(entry.Ghost)) continue;
                OwnerProgress? now = OwnerProgressBridge.Read(entry.Body);
                if (!now.HasValue || OwnerProgressBridge.Same(now.Value, em.GetComponentData<OwnerProgress>(entry.Ghost))) continue;
                em.SetComponentData(entry.Ghost, now.Value);
                Writes++;
            }
        }
    }

    /// <summary>Client: the own view body takes the server's <see cref="OwnerProgress"/> when it changes (other players' bodies get none).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class OwnerProgressClientSystem : SystemBase
    {
        readonly Dictionary<GameObject, OwnerProgress> m_Shown = new Dictionary<GameObject, OwnerProgress>();

        /// <summary>Times the own body took a new state (tests).</summary>
        public int Mirrored { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!entry.IsLocal || !ReferenceEquals(entry.World, World) || entry.Body == null) continue;
                if (!em.Exists(entry.Ghost) || !em.HasComponent<OwnerProgress>(entry.Ghost)) continue;
                OwnerProgress p = em.GetComponentData<OwnerProgress>(entry.Ghost);
                if (p.Level < 1 || (m_Shown.TryGetValue(entry.Body, out OwnerProgress had) && OwnerProgressBridge.Same(had, p))) continue;
                m_Shown[entry.Body] = p;
                OwnerProgressBridge.Mirror(entry.Body, p);
                Mirrored++;
            }
        }
    }
}
