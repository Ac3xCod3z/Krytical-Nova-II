using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The party panels of the HUD. While the local player is in a party, a frame under the status frame lists the
    /// other members (leader in gold) with their level and, while they are in sight, their health. The frame is a
    /// movable HUD panel: in the layout editor it shows all four rows so it can be placed at its full size. An invitation
    /// from another player shows with JOIN / REFUSE (keys Y / N) until it is answered or lapses. The panels use the
    /// shared panel skin, so each player sees them in the look of their own faction. The server decides everything
    /// (<see cref="PlayerParty"/>); this only shows it and passes the player's choice on.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PartyHudController : MonoBehaviour, IPartySink
    {
        private const float RefreshInterval = 0.25f;
        private const string LeaderClass = "party-member--leader";
        private const string AwayClass = "party-member--away";
        private const string DownClass = "party-member--down";
        // Set by HudLayoutEditor on a panel being placed.
        private const string EditingClass = "hud-editing";

        private sealed class Row
        {
            public VisualElement Root;
            public Label Name;
            public Label Level;
            public VisualElement Fill;
        }

        private readonly List<Row> rows = new List<Row>(PartyRules.MaxSize);

        private VisualElement root;
        private VisualElement boundRoot;
        private VisualElement frames;
        private VisualElement members;
        private VisualElement invite;
        private VisualElement inviteFill;
        private VisualElement duelRequest;
        private Label inviteText;

        private float nextRefresh;
        private float inviteStarted;
        private float inviteEnds;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            frames = root?.Q("party-frames");
            invite = root?.Q("party-invite");
            members = frames?.Q("party-members");
            if (frames == null || invite == null || members == null) return;

            inviteText = invite.Q<Label>("party-invite-text");
            inviteFill = invite.Q("party-invite-fill");
            duelRequest = root.Q("duel-request");

            if (boundRoot != root)
            {
                boundRoot = root;
                rows.Clear();
                members.Clear();
                Bind(frames, "party-leave", Leave);
                Bind(invite, "party-accept", () => Answer(true));
                Bind(invite, "party-decline", () => Answer(false));
            }

            SetVisible(frames, false);
            SetVisible(invite, false);
            ClientPresentationRegistry.RegisterParty(this);
        }

        private void OnDisable() => ClientPresentationRegistry.UnregisterParty(this);

        private void Update()
        {
            if (invite == null) return;
            float now = Time.unscaledTime;

            if (IsVisible(invite))
            {
                if (now >= inviteEnds) SetVisible(invite, false);
                else
                {
                    inviteFill.style.width = Length.Percent(100f * Mathf.InverseLerp(inviteEnds, inviteStarted, now));
                    var keyboard = Keyboard.current;
                    // A duel request on screen owns Y / N; the invitation is then answered with its buttons.
                    if (keyboard != null && !IsTyping() && (duelRequest == null || !IsVisible(duelRequest)))
                    {
                        if (keyboard.yKey.wasPressedThisFrame) Answer(true);
                        else if (keyboard.nKey.wasPressedThisFrame) Answer(false);
                    }
                }
            }

            if (now >= nextRefresh)
            {
                nextRefresh = now + RefreshInterval;
                RefreshFrames();
            }
        }

        // ------------------------------------------------------------------ IPartySink

        public void ShowInvite(string inviterName, float seconds)
        {
            if (invite == null) return;
            inviteText.text = $"{inviterName} invites you to a party";
            inviteStarted = Time.unscaledTime;
            inviteEnds = inviteStarted + Mathf.Max(1f, seconds);
            inviteFill.style.width = Length.Percent(100f);
            SetVisible(invite, true);
            invite.BringToFront();
        }

        public void HideInvite()
        {
            if (invite != null) SetVisible(invite, false);
        }

        // ------------------------------------------------------------------ member frames

        /// <summary>Polled: the local player object changes when a session starts, and health moves all the time.</summary>
        private void RefreshFrames()
        {
            PlayerParty mine = PlayerParty.Local;
            IReadOnlyList<PartyMember> roster = mine != null && mine.IsSpawned ? mine.Roster : null;
            // While the layout is edited the editor shows the frame and puts its display back afterwards.
            bool editing = frames.ClassListContains(EditingClass);
            bool grouped = roster != null && roster.Count >= 2;
            if (!editing && !grouped)
            {
                SetVisible(frames, false);
                return;
            }

            int shown = 0;
            for (int i = 0; grouped && i < roster.Count; i++)
            {
                PartyMember member = roster[i];
                if (member.ObjectId == mine.NetworkObjectId) continue;
                Row row = shown < rows.Count ? rows[shown] : AddRow();
                shown++;

                bool inSight = mine.TryGetVitals(member, out float health, out int level);
                row.Name.text = member.Name.ToUpperInvariant();
                row.Level.text = $"LV {level}";
                row.Fill.style.width = Length.Percent(inSight ? 100f * health : 100f);
                row.Root.EnableInClassList(LeaderClass, i == 0);
                row.Root.EnableInClassList(AwayClass, !inSight);
                row.Root.EnableInClassList(DownClass, inSight && health <= 0f);
                SetVisible(row.Root, true);
            }
            // Placed at its full size: the rows a full party would fill stand in for the missing members.
            for (; editing && shown < PartyRules.MaxSize - 1; shown++)
            {
                Row row = shown < rows.Count ? rows[shown] : AddRow();
                row.Name.text = $"PARTY MEMBER {shown + 1}";
                row.Level.text = "LV --";
                row.Fill.style.width = Length.Percent(100f);
                row.Root.RemoveFromClassList(LeaderClass);
                row.Root.RemoveFromClassList(DownClass);
                row.Root.AddToClassList(AwayClass);
                SetVisible(row.Root, true);
            }
            for (int i = shown; i < rows.Count; i++) SetVisible(rows[i].Root, false);
            if (!editing) SetVisible(frames, true);
        }

        private Row AddRow()
        {
            var row = new Row
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore },
                Name = new Label { enableRichText = false, pickingMode = PickingMode.Ignore },
                Level = new Label { pickingMode = PickingMode.Ignore },
                Fill = new VisualElement { pickingMode = PickingMode.Ignore },
            };
            row.Root.AddToClassList("party-member");
            row.Name.AddToClassList("party-member-name");
            row.Level.AddToClassList("party-member-level");
            row.Fill.AddToClassList("party-hp-fill");

            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.AddToClassList("party-member-top");
            top.Add(row.Name);
            top.Add(row.Level);
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("party-hp");
            bar.Add(row.Fill);
            row.Root.Add(top);
            row.Root.Add(bar);

            members.Add(row.Root);
            rows.Add(row);
            return row;
        }

        private static void Leave()
        {
            PlayerParty mine = PlayerParty.Local;
            if (mine != null) mine.Leave();
        }

        private void Answer(bool accept)
        {
            SetVisible(invite, false);
            PlayerParty mine = PlayerParty.Local;
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
