namespace KryticalNova.Core
{
    /// <summary>
    /// Static slots that the client-only UI assembly populates at runtime. Gameplay/networking code (Runtime assembly)
    /// talks to presentation only through these interfaces, so the UI assembly can be stripped from UNITY_SERVER builds.
    /// Both slots are null on a dedicated server; every caller must null-check.
    /// </summary>
    public static class ClientPresentationRegistry
    {
        public static ICombatFeedbackSink Feedback { get; private set; }
        public static IPointerBlockQuery PointerBlock { get; private set; }

        public static void RegisterFeedback(ICombatFeedbackSink sink) => Feedback = sink;

        public static void UnregisterFeedback(ICombatFeedbackSink sink)
        {
            if (ReferenceEquals(Feedback, sink)) Feedback = null;
        }

        public static void RegisterPointerBlock(IPointerBlockQuery query) => PointerBlock = query;

        public static void UnregisterPointerBlock(IPointerBlockQuery query)
        {
            if (ReferenceEquals(PointerBlock, query)) PointerBlock = null;
        }
    }
}
