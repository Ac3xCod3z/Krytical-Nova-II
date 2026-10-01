using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>What the dialogue panel needs to show for one NPC. Implemented by the NPC component.</summary>
    public interface INpcDialogueSource
    {
        string DisplayName { get; }
        string Title { get; }
        string Greeting { get; }
        string Services { get; }
        float InteractRange { get; }
        Vector3 Position { get; }
    }

    /// <summary>Client-side NPC dialogue window. Implemented by the UI assembly.</summary>
    public interface INpcDialogueSink
    {
        void ShowDialogue(INpcDialogueSource npc, Transform player);
        void HideDialogue();
    }

    /// <summary>Short on-screen notices (portal sealed, zone messages). Implemented by the UI assembly.</summary>
    public interface IGameMessageSink
    {
        void ShowMessage(string message);
    }
}
