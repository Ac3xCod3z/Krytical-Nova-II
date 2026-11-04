using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>What a Recall show is (NGO's PlayerRecall RPCs to everyone): the channel begun, the channel ended, the arrival in town.</summary>
    public enum RecallShowKind : byte
    {
        Begun = 1,
        Ended = 2,
        Arrived = 3,
    }

    /// <summary>Server: how a server body's Recall reaches every machine (NGO's ChannelRpc and ArrivedRpc, SendTo.Everyone; M8 8.4.10).</summary>
    public interface IRecallShowLink
    {
        void Show(Component part, RecallShowKind kind, float seconds);
    }

    /// <summary>The server's <see cref="IRecallShowLink"/> (null where there is no entity netcode server), and what a machine plays for a show.</summary>
    public static class RecallShowLink
    {
        public static IRecallShowLink Current;

        /// <summary>The id the charge aura is shown under: no skill has it, so it takes the game feel's default charge.</summary>
        public const string FeelId = "Recall";

        /// <summary>Any machine: NGO's ShowChannel and ArrivedRpc bodies for <paramref name="actor"/>.</summary>
        public static void Play(Transform actor, RecallShowKind kind, float seconds, bool isLocalPlayer)
        {
            var feel = ClientPresentationRegistry.Feel;
            if (feel == null || actor == null) return;
            if (kind == RecallShowKind.Begun) feel.ChargeStarted(actor, FeelId, seconds, isLocalPlayer);
            else if (kind == RecallShowKind.Ended) feel.ChargeEnded(actor, FeelId, false);
            else if (kind == RecallShowKind.Arrived) feel.Respawned(actor, isLocalPlayer);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
