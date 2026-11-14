using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// Client-only comic speech bubbles over someone in the world who is not a pet (a vendor's call-out, later a townsperson's bark):
    /// the HUD's bubble layer shows the line beside <c>speaker</c> for a while, keeping clear of <c>listener</c>. Purely local: nothing
    /// is sent. Null on a dedicated server.
    /// </summary>
    public interface ISpeechBubbleSink
    {
        void Speak(Transform speaker, Transform listener, string text, float seconds);
    }
}
