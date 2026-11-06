using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Server: each player's fight as its server body's PlayerCombatState decides it (Cadence step 1; NGO's netInCombat, server-written),
    /// onto the ghost's <see cref="CombatFlag"/>; written only when it changes.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CombatFlagServerSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<CombatFlag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out PlayerCombatState state)) continue;
                byte now = (byte)(state.InCombat ? 1 : 0);
                if (em.GetComponentData<CombatFlag>(entry.Ghost).InCombat != now) em.SetComponentData(entry.Ghost, new CombatFlag { InCombat = now });
            }
        }
    }

    /// <summary>
    /// Client: every view takes its player's fight from the ghost (NGO netInCombat.OnValueChanged), so the own view's fight music starts
    /// with the server's fight (a struck Strike Dummy included) and Cadence has its beat.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatFlagClientSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<CombatFlag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out PlayerCombatState state)) continue;
                state.MirrorInCombat(em.GetComponentData<CombatFlag>(entry.Ghost).InCombat != 0);
            }
        }
    }
}
