using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Server: every player ghost's public HP equal to its body's (NGO's PlayerResources netHealth / netMaxHealth, read by everyone; M4.2a),
    /// in thousandths as <see cref="OwnerFlux"/>. The server body decides; nothing of it is predicted.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VitalsServerSystem : SystemBase
    {
        /// <summary>Ghosts rewritten (tests).</summary>
        public int Writes { get; private set; }

        /// <summary>A health value as the ghost carries it (thousandths, never below 0).</summary>
        public static int ToMilli(float value) => value > 0f ? Mathf.RoundToInt(value * 1000f) : 0;

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerVitals>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out CombatEntity entity)) continue;
                var now = new PlayerVitals { HealthMilli = ToMilli(entity.CurrentHealth), MaxHealthMilli = ToMilli(entity.MaxHealth) };
                PlayerVitals had = em.GetComponentData<PlayerVitals>(entry.Ghost);
                if (had.HealthMilli == now.HealthMilli && had.MaxHealthMilli == now.MaxHealthMilli) continue;
                em.SetComponentData(entry.Ghost, now);
                Writes++;
            }
        }
    }

    /// <summary>Client: another player's view shows the server's HP for them (its nameplate and target frame read its CombatEntity).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VitalsClientSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                // the own view's HP comes from OwnerFlux into its PlayerResources (M3); CombatEntity.MirrorHealth leaves such a body alone
                if (entry.IsLocal || !ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerVitals>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out CombatEntity entity)) continue;
                PlayerVitals v = em.GetComponentData<PlayerVitals>(entry.Ghost);
                entity.MirrorHealth(v.HealthMilli / 1000f, v.MaxHealthMilli / 1000f);
            }
        }
    }
}
