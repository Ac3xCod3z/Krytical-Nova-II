using KryticalNova.Crossfire;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Marshal's "JOIN CROSSFIRE" button (owner's war flow, 16 Nov 2026): a player who said no to the join prompt, or missed it, can
    /// still join at the Marshal while the war takes players. The server decides everything (room, bracket, a Merc offer); this only asks.
    /// </summary>
    public static class CrossfireMarshalService
    {
        public const string Id = "crossfire";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register() => NpcServices.Register(Id, NpcOffer.Crossfire, "JOIN CROSSFIRE", Join);

        private static void Join()
        {
            if (CrossfireJoinLink.Requester == null) { Debug.LogWarning("[Crossfire] Not in a game."); return; }
            CrossfireJoinLink.Requester();
        }
    }
}
