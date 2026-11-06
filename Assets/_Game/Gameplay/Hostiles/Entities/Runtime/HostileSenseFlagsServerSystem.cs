using KryticalNova.Brains;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.World;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// The game's managed rules on the sensed players (M5.3), after the feed and before the grids: a player on cross-faction neutral ground
    /// (<see cref="TradeZone.CrossFactionAt"/>) is Neutral; one a GM made Hostiles ignore (<see cref="HostileIgnore"/>, by the server body)
    /// is Ignored. One of the three bridge systems that may touch managed code.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(HostilePlayerFeedSystem))]
    [UpdateBefore(typeof(SenseBuildSystem))]
    public partial class HostileSenseFlagsServerSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<HostileBrainConfig>();
            RequireForUpdate<SenseWorld>();
        }

        protected override void OnUpdate()
        {
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            // job later (perf phase): the neutral zones as ECS volumes, the ignore list as a component on the ghost
            for (int i = 0; i < sense.Players.Length; i++)
            {
                SensedPlayer p = sense.Players[i];
                if (TradeZone.CrossFactionAt(p.Feet)) p.Flags |= SensedFlags.Neutral;
                if (GhostBodyRegistry.TryGetByGhost(World, p.Ghost, out GhostBodyEntry entry) && entry.Body != null
                    && HostileIgnore.IsIgnored(entry.Body.transform))
                    p.Flags |= SensedFlags.Ignored;
                sense.Players[i] = p;
            }
        }
    }
}
