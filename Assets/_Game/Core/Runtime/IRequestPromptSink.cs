using System;

namespace KryticalNova.Core
{
    /// <summary>
    /// The HUD's accept / decline panel for a request from another player (a trade, a contact request, a Crew
    /// invitation). Requests queue: one is shown at a time. Implemented by the UI assembly; null on a dedicated server.
    /// </summary>
    public interface IRequestPromptSink
    {
        /// <param name="id">Names the request so it can be withdrawn (<see cref="Hide"/>). A second request with the same id replaces the first.</param>
        /// <param name="eyebrow">Small heading, e.g. "TRADE".</param>
        /// <param name="text">What is being asked, e.g. "Vex wants to trade with you".</param>
        /// <param name="seconds">How long the request waits; it then goes away without an answer.</param>
        /// <param name="answer">Called once with true (accept) or false (decline). Not called when the request lapses or is hidden.</param>
        void Show(string id, string eyebrow, string text, float seconds, Action<bool> answer);

        /// <summary>Withdraws a request (it was answered elsewhere, cancelled or lapsed on the server).</summary>
        void Hide(string id);
    }
}
