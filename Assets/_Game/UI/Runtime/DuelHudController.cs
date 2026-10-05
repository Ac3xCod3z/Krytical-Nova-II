using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The duel panels of the HUD. While a friendly player is selected a small strip offers DUEL, INVITE (to the party) and WHISPER. A request
    /// from another player shows with ACCEPT / DECLINE (keys Y / N) until it is answered or lapses. When the local
    /// player's duel ends the winner sees YOU WIN and the loser YOU LOSE. The panels use the shared panel skin, so
    /// each player sees them in the look of their own faction. The server decides everything
    /// (<see cref="PlayerDuel"/>); this only shows it and passes the player's choice on.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class DuelHudController : MonoBehaviour, IDuelSink
    {
        private const float TargetRefreshInterval = 0.2f;
        private const float ResultSeconds = 4.5f;
        private const string WinClass = "duel-result--win";
        private const string LoseClass = "duel-result--lose";

        private VisualElement root;
        private VisualElement boundRoot;
        private VisualElement targetStrip;
        private VisualElement request;
        private VisualElement requestFill;
        private VisualElement result;
        private Label targetName;
        private Label requestText;
        private Label resultTitle;
        private Label resultDetail;

        private PlayerDuel shownTarget;
        private float nextTargetRefresh;
        private float requestStarted;
        private float requestEnds;
        private float resultEnds;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            targetStrip = root?.Q("duel-target");
            request = root?.Q("duel-request");
            result = root?.Q("duel-result");
            if (targetStrip == null || request == null || result == null) return;

            targetName = targetStrip.Q<Label>("duel-target-name");
            requestText = request.Q<Label>("duel-request-text");
            requestFill = request.Q("duel-request-fill");
            resultTitle = result.Q<Label>("duel-result-title");
            resultDetail = result.Q<Label>("duel-result-detail");

            if (boundRoot != root)
            {
                boundRoot = root;
                Bind(targetStrip, "duel-target-duel", AskSelected);
                Bind(targetStrip, "duel-target-invite", InviteSelected);
                Bind(targetStrip, "duel-target-whisper", WhisperSelected);
                Bind(request, "duel-accept", () => Answer(true));
                Bind(request, "duel-decline", () => Answer(false));
            }

            SetVisible(targetStrip, false);
            SetVisible(request, false);
            SetVisible(result, false);
            ClientPresentationRegistry.RegisterDuel(this);
        }

        private void OnDisable() => ClientPresentationRegistry.UnregisterDuel(this);

        private void Update()
        {
            if (request == null) return;
            float now = Time.unscaledTime;

            if (IsVisible(request))
            {
                if (now >= requestEnds) SetVisible(request, false);
                else
                {
                    float left = Mathf.InverseLerp(requestEnds, requestStarted, now);
                    requestFill.style.width = Length.Percent(100f * left);
                    var keyboard = Keyboard.current;
                    if (keyboard != null && !IsTyping())
                    {
                        if (keyboard.yKey.wasPressedThisFrame) Answer(true);
                        else if (keyboard.nKey.wasPressedThisFrame) Answer(false);
                    }
                }
            }

            if (IsVisible(result) && now >= resultEnds) SetVisible(result, false);

            if (now >= nextTargetRefresh)
            {
                nextTargetRefresh = now + TargetRefreshInterval;
                RefreshTarget();
            }
        }

        // ------------------------------------------------------------------ IDuelSink

        public void ShowRequest(string challengerName, float seconds)
        {
            if (request == null) return;
            requestText.text = $"{challengerName} calls you out";
            requestStarted = Time.unscaledTime;
            requestEnds = requestStarted + Mathf.Max(1f, seconds);
            requestFill.style.width = Length.Percent(100f);
            SetVisible(request, true);
            request.BringToFront();
        }

        public void HideRequest()
        {
            if (request != null) SetVisible(request, false);
        }

        public void ShowResult(bool won, string opponentName)
        {
            if (result == null) return;
            resultTitle.text = won ? "YOU WIN" : "YOU LOSE";
            resultDetail.text = won ? $"You defeated {opponentName}" : $"{opponentName} defeated you";
            result.EnableInClassList(WinClass, won);
            result.EnableInClassList(LoseClass, !won);
            resultEnds = Time.unscaledTime + ResultSeconds;
            SetVisible(request, false);
            SetVisible(result, true);
            result.BringToFront();
        }

        // ------------------------------------------------------------------ the selected player

        /// <summary>The strip shows for another player the local player has selected and is not at war with.</summary>
        private void RefreshTarget()
        {
            PlayerDuel mine = PlayerDuel.Local;
            PlayerDuel other = null;
            if (mine != null && mine.IsSpawned && !mine.IsDuelling && mine.TryGetComponent(out PointAndClickPlayer controls))
            {
                Transform selected = controls.SelectedTarget;
                other = selected != null ? selected.GetComponentInParent<PlayerDuel>() : null;
                if (other == mine || (other != null && !other.IsSpawned)) other = null;
                if (other != null)
                {
                    var entity = other.GetComponent<IDamageable>();
                    if (entity == null || !entity.IsAlive || CombatHostility.IsHostile(mine.transform, entity)) other = null;
                }
            }

            shownTarget = other;
            SetVisible(targetStrip, other != null);
            if (other != null) targetName.text = other.DuelistName.ToUpperInvariant();
        }

        private void AskSelected()
        {
            PlayerDuel mine = PlayerDuel.Local;
            if (mine != null && shownTarget != null) mine.Request(shownTarget);
        }

        private void InviteSelected()
        {
            PlayerParty mine = PlayerParty.Local;
            if (mine != null && shownTarget != null) mine.Invite(shownTarget.GetComponent<PlayerParty>());
        }

        private void WhisperSelected()
        {
            if (shownTarget == null) return;
            var chat = GetComponent<ChatWindowController>();
            if (chat != null) chat.BeginWhisper(shownTarget.DuelistName);
        }

        private void Answer(bool accept)
        {
            SetVisible(request, false);
            PlayerDuel mine = PlayerDuel.Local;
            if (mine != null) mine.Respond(accept);
        }

        // ------------------------------------------------------------------ helpers

        private bool IsTyping()
        {
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            for (VisualElement element = focused; element != null; element = element.parent)
                if (element is TextField) return true;
            return false;
        }

        private static void Bind(VisualElement parent, string elementName, System.Action callback)
        {
            Button button = parent.Q<Button>(elementName);
            if (button != null) button.clicked += callback;
        }

        private static bool IsVisible(VisualElement element) => element.style.display != DisplayStyle.None;

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
