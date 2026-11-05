using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>What the server tells a Callout's player's own game (NGO PlayerDuel's owner RPCs; M4 step 8).</summary>
    public enum DuelShowKind : byte
    {
        Prompt = 1,
        PromptClosed = 2,
        Result = 3,
        Countdown = 4,
        CountdownCancelled = 5,
    }

    /// <summary>A player's Callout as the entity netcode reaches it (implemented by PlayerDuel, which this assembly does not name).</summary>
    public interface IBodyDuel
    {
        /// <summary>The server body: its owner's request (NGO's RequestRpc / RespondRpc / ForfeitRpc bodies, after the Social limit).</summary>
        void RunDuelOp(DuelOpKind op, Component target, bool accept);
        /// <summary>The own view: what the server told it (NGO's PromptRpc, PromptClosedRpc, ResultRpc, CountdownRpc, CountdownCancelledRpc).</summary>
        void MirrorShow(DuelShowKind kind, string name, float seconds, bool won);
    }

    /// <summary>The server's way to a player's own game for its Callout (the entity netcode sets it).</summary>
    public interface IDuelOwnerLink
    {
        void Show(Component part, DuelShowKind kind, string name, float seconds, bool won);
    }

    public static class DuelOwnerLink
    {
        public static IDuelOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
