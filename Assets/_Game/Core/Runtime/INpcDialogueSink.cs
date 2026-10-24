using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>What the dialogue panel needs to show for one NPC. Implemented by the NPC component.</summary>
    public interface INpcDialogueSource
    {
        string DisplayName { get; }
        string Title { get; }
        /// <summary>The line the NPC says this time (picked when the player walked up).</summary>
        string Greeting { get; }
        /// <summary>That line in the NPC's voice, or null when it is only shown.</summary>
        AudioClip GreetingClip { get; }
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
