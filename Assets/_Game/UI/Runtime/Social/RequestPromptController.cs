using System;
using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The HUD's accept / decline panel for requests from other players (a trade, a contact request, a Crew
    /// invitation). Requests queue and are shown one at a time, each with its own timer; one that runs out simply
    /// goes away. Y / N answer it unless a Callout or Squad request is on screen (those own the keys). The server
    /// decided the request exists and decides what an answer does; this only shows it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class RequestPromptController : MonoBehaviour, IRequestPromptSink
    {
        private sealed class Request
        {
            public string Id, Eyebrow, Text;
            public float Started, Ends;
            public Action<bool> Answer;
        }

        private readonly List<Request> queue = new List<Request>(4);

        private VisualElement root, panel, fill, duelRequest, partyInvite;
        private Label eyebrow, text, more;
        private Request shown;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            panel = root?.Q("request-prompt");
            if (panel == null)
            {
                Debug.LogError("[RequestPrompt] SocialPanels.uxml is not instanced in this UI document.", this);
                return;
            }
            eyebrow = panel.Q<Label>("request-eyebrow");
            text = panel.Q<Label>("request-text");
            more = panel.Q<Label>("request-more");
            fill = panel.Q("request-fill");
            duelRequest = root.Q("duel-request");
            partyInvite = root.Q("party-invite");
            panel.Q<Button>("request-accept").clicked += () => Answer(true);
            panel.Q<Button>("request-decline").clicked += () => Answer(false);
            panel.style.display = DisplayStyle.None;
            ClientPresentationRegistry.RegisterPrompts(this);
        }

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterPrompts(this);
            queue.Clear();
            shown = null;
        }

        // ------------------------------------------------------------------ IRequestPromptSink

        public void Show(string id, string heading, string message, float seconds, Action<bool> answer)
        {
            if (panel == null || string.IsNullOrEmpty(id)) return;
            Hide(id);
            // A kind of request the player refuses (Settings, Gameplay) is answered no for them, as the Decline button would.
            if (Refused(heading))
            {
                answer?.Invoke(false);
                return;
            }
            float now = Time.unscaledTime;
            queue.Add(new Request { Id = id, Eyebrow = heading, Text = message, Started = now, Ends = now + Mathf.Max(1f, seconds), Answer = answer });
            Present();
        }

        /// <summary>The kinds of request, by the heading they arrive under, that the player has asked not to be shown.</summary>
        public static bool Refused(string heading)
        {
            switch (heading)
            {
                case "TRADE": return HudPreferences.RefuseTrades;
                case "CONTACT REQUEST": return HudPreferences.RefuseContacts;
                case "CREW INVITATION": return HudPreferences.RefuseCrewInvites;
                case "SPONSOR REQUEST": return HudPreferences.RefuseSponsorRequests;
                default: return false;
            }
        }

        public void Hide(string id)
        {
            queue.RemoveAll(request => request.Id == id);
            if (shown != null && shown.Id == id) shown = null;
            Present();
        }

        // ------------------------------------------------------------------

        private void Update()
        {
            if (panel == null || queue.Count == 0) return;
            float now = Time.unscaledTime;
            // A loop, not RemoveAll with a test that reads "now": that makes an object every frame, queue or no queue.
            bool ended = false;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (now < queue[i].Ends) continue;
                queue.RemoveAt(i);
                ended = true;
            }
            if (ended)
            {
                if (shown != null && now >= shown.Ends) shown = null;
                Present();
            }
            if (shown == null) return;

            fill.style.width = Length.Percent(100f * Mathf.InverseLerp(shown.Ends, shown.Started, now));
            var keyboard = Keyboard.current;
            if (keyboard == null || HudTyping.IsTyping(root) || IsVisible(duelRequest) || IsVisible(partyInvite)) return;
            if (keyboard.yKey.wasPressedThisFrame) Answer(true);
            else if (keyboard.nKey.wasPressedThisFrame) Answer(false);
        }

        private void Present()
        {
            if (panel == null) return;
            if (shown == null && queue.Count > 0) shown = queue[0];
            if (shown == null)
            {
                panel.style.display = DisplayStyle.None;
                return;
            }
            eyebrow.text = (shown.Eyebrow ?? "REQUEST").ToUpperInvariant();
            text.text = shown.Text;
            more.text = queue.Count > 1 ? $"{queue.Count - 1} more waiting" : string.Empty;
            panel.style.display = DisplayStyle.Flex;
            panel.BringToFront();
        }

        private void Answer(bool accept)
        {
            Request answered = shown;
            if (answered == null) return;
            queue.Remove(answered);
            shown = null;
            Present();
            if (GearUIController.Instance != null) GearUIController.Instance.SuppressWorldInput();
            answered.Answer?.Invoke(accept);
        }

        private static bool IsVisible(VisualElement element) => element != null && element.resolvedStyle.display != DisplayStyle.None;
    }
}
