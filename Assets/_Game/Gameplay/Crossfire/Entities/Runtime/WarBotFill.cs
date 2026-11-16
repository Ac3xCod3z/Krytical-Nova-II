using System.Collections.Generic;
using KryticalNova.Factions;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Crossfire.Entities
{
    /// <summary>What Crossfire asks of the War Bots when a short side voted yes (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF).</summary>
    public struct WarBotRequest
    {
        /// <summary>The short side.</summary>
        public FactionId Faction;
        /// <summary>The war's bracket: 1 = I ... 10 = X (<see cref="CrossfireRules.BracketOf"/>).</summary>
        public int Bracket;
        /// <summary>Bots wanted on that side now (to the war cap).</summary>
        public int Count;
        /// <summary>The side's Rally Point landing (<see cref="CrossfireArea.Landing"/>), world metres and degrees about up.</summary>
        public Vector3 RallyAt;
        public float RallyYaw;
    }

    /// <summary>
    /// Crossfire's War Bots, as Crossfire sees them (owner, 16 Nov 2026). Crossfire decides WHEN and HOW MANY (the cap, the vote, a real
    /// player taking a bot's place, the war's end); the War Bots system, a higher layer inside the server, implements this and puts itself
    /// in <see cref="WarBots.Fill"/> (Crossfire never references it). Bots are player-shaped server ghosts with no connection and take the
    /// same kill path as players; Crossfire gives them no EXP and no item drop, and no drop on a kill where either side is a bot.
    /// Server only, main thread.
    /// </summary>
    public interface IWarBotFill
    {
        /// <summary>
        /// Puts up to <see cref="WarBotRequest.Count"/> bots on the side at its Rally Point. <paramref name="realPlayers"/> are the server
        /// player ghosts of every side in this war, bots left out: the bots take their levels and gear from them. Returns how many came in.
        /// </summary>
        int Fill(in WarBotRequest request, IReadOnlyList<Entity> realPlayers);

        /// <summary>A real player took a bot's place: takes up to <paramref name="count"/> bots off the side (the War Bots pick which). Returns how many left.</summary>
        int Release(FactionId faction, int count);

        /// <summary>Bots on the side now (the war cap counts them).</summary>
        int Count(FactionId faction);

        /// <summary>This server ghost is a War Bot.</summary>
        bool IsWarBot(Entity ghost);

        /// <summary>The side a War Bot fights for; <see cref="FactionId.None"/> when the ghost is not a War Bot (the kill counter reads it).</summary>
        FactionId SideOf(Entity ghost);

        /// <summary>The war is over (the Gap): every war bot goes.</summary>
        void EndWar();
    }

    /// <summary>The War Bots the server runs, or null (then no side is offered the bot vote). Filled by the War Bots system.</summary>
    public static class WarBots
    {
        public static IWarBotFill Fill;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Fill = null;
    }
}
