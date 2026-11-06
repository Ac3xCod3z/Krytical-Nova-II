using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Client: every player's weapon as its ghost says (owner's hand test, 5 Nov 2026: the weapon was never drawn on a view): a change
    /// plays the draw or sheathe on that view (NGO PlayWeaponDrawClientRpc to everyone), the first sight snaps to it (NGO's late joiner).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class WeaponStancePresentSystem : SystemBase
    {
        readonly Dictionary<Entity, bool> m_Shown = new Dictionary<Entity, bool>();

        /// <summary>Draws and sheathes played here (tests).</summary>
        public int Played { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<CombatState>(entry.Ghost)) continue;
                bool drawn = em.GetComponentData<CombatState>(entry.Ghost).WeaponDrawn != 0;
                bool seen = m_Shown.TryGetValue(entry.Ghost, out bool was);
                if (seen && was == drawn) continue;
                m_Shown[entry.Ghost] = drawn;
                if (!entry.Body.TryGetComponent(out PlayerMeleeCombat melee)) continue;
                melee.ShowWeaponStance(drawn, animate: seen);
                if (seen) Played++;
            }
        }
    }
}
