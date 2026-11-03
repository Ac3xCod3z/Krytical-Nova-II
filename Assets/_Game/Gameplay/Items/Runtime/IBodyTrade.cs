using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>One line of a trade offer for the entity netcode: the Pack cell it comes from (-1 for the partner's) and the stack.</summary>
    public struct BodyTradeLine
    {
        public short cell;
        public ItemStack stack;

        public BodyTradeLine(short cell, ItemStack stack)
        {
            this.cell = cell;
            this.stack = stack;
        }
    }

    /// <summary>What the owner is told about its trade (NGO's <c>TradeHeader</c> and the two offers), for the entity netcode.</summary>
    public struct BodyTradeView
    {
        public bool active;
        public string partnerName;
        public long myCreds, theirCreds;
        public bool myLocked, theirLocked, myConfirmed, theirConfirmed;
        public long perTrade, remainingToday;
        public BodyTradeLine[] mine, theirs;
    }

    /// <summary>
    /// A player body's side of trading, for the entity netcode (M8 8.2.3): the network runs its owner's trade requests on the server
    /// body and shows the server's view, notices and request prompt on the owner's own view. Here, below Economy, so the network need
    /// not know Economy (as <see cref="IBodyStorage"/>).
    /// </summary>
    public interface IBodyTrade
    {
        void RunTradeOp(KryticalNova.Core.TradeOpKind op, string name, short a, ushort b, long creds, bool flag);
        void MirrorView(in BodyTradeView view);
        void MirrorNotice(string text);
        void MirrorPrompt(string fromName, float seconds);
        void MirrorPromptClosed();
    }

    /// <summary>Server: how a server body's trade reaches its own player (NGO's owner RPCs: the view, the notice, the prompt).</summary>
    public interface ITradeOwnerLink
    {
        void View(Component part, in BodyTradeView view);
        void Notice(Component part, string text);
        void Prompt(Component part, string fromName, float seconds);
        void PromptClosed(Component part);
    }

    /// <summary>The server's <see cref="ITradeOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class TradeOwnerLink
    {
        public static ITradeOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
