using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// Static slots that the client-only UI assembly populates at runtime. Gameplay/networking code (Runtime assembly)
    /// talks to presentation only through these interfaces, so the UI assembly can be stripped from UNITY_SERVER builds.
    /// Every slot is null (or empty) on a dedicated server; every caller must null-check.
    /// </summary>
    public static class ClientPresentationRegistry
    {
        private static readonly List<IPointerBlockQuery> PointerBlocks = new List<IPointerBlockQuery>(4);

        public static ICombatFeedbackSink Feedback { get; private set; }
        public static INpcDialogueSink Dialogue { get; private set; }
        public static IGameMessageSink Messages { get; private set; }
        public static IWorldMarkerSink Markers { get; private set; }
        public static IGameFeelSink Feel { get; private set; }
        public static IChatSink Chat { get; private set; }
        public static IDuelSink Duel { get; private set; }

        public static void RegisterDuel(IDuelSink sink) => Duel = sink;

        public static void UnregisterDuel(IDuelSink sink)
        {
            if (ReferenceEquals(Duel, sink)) Duel = null;
        }

        public static void RegisterChat(IChatSink sink) => Chat = sink;

        public static void UnregisterChat(IChatSink sink)
        {
            if (ReferenceEquals(Chat, sink)) Chat = null;
        }

        public static void RegisterFeel(IGameFeelSink sink) => Feel = sink;

        public static void UnregisterFeel(IGameFeelSink sink)
        {
            if (ReferenceEquals(Feel, sink)) Feel = null;
        }

        public static void RegisterMarkers(IWorldMarkerSink sink) => Markers = sink;

        public static void UnregisterMarkers(IWorldMarkerSink sink)
        {
            if (ReferenceEquals(Markers, sink)) Markers = null;
        }

        public static void RegisterFeedback(ICombatFeedbackSink sink) => Feedback = sink;

        public static void UnregisterFeedback(ICombatFeedbackSink sink)
        {
            if (ReferenceEquals(Feedback, sink)) Feedback = null;
        }

        public static void RegisterDialogue(INpcDialogueSink sink) => Dialogue = sink;

        public static void UnregisterDialogue(INpcDialogueSink sink)
        {
            if (ReferenceEquals(Dialogue, sink)) Dialogue = null;
        }

        public static void RegisterMessages(IGameMessageSink sink) => Messages = sink;

        public static void UnregisterMessages(IGameMessageSink sink)
        {
            if (ReferenceEquals(Messages, sink)) Messages = null;
        }

        /// <summary>Several UI documents may cover the screen; each registers its own pointer block.</summary>
        public static void RegisterPointerBlock(IPointerBlockQuery query)
        {
            if (query != null && !PointerBlocks.Contains(query)) PointerBlocks.Add(query);
        }

        public static void UnregisterPointerBlock(IPointerBlockQuery query) => PointerBlocks.Remove(query);

        /// <summary>True when any registered UI covers this screen position.</summary>
        public static bool BlocksWorldPointer(Vector2 screenPosition)
        {
            for (int i = 0; i < PointerBlocks.Count; i++)
                if (PointerBlocks[i].BlocksWorldPointer(screenPosition)) return true;
            return false;
        }
    }
}
