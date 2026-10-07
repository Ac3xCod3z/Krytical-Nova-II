using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Herald's window: the two Renown ladders. The Ascension tab shows the level, the Nova's tier, what an
    /// attempt costs and its odds; the Nova Standings tab shows the four tracks, what each gives and costs, and which
    /// one is worn. It sends requests to <see cref="PlayerNova"/>; the server decides. Odds, costs and reasons come
    /// from the same rules the server uses (<see cref="NovaRules"/>), so the window never offers what the server
    /// refuses. Opened from the Herald's dialogue and closed when the player walks away.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HeraldWindowController : MonoBehaviour
    {
        private enum Tab { Ascension, Standings }

        private const float RefreshSeconds = 0.25f;
        private const string FilledClass = "filled";
        private static readonly string[] TabNames = { "ascension", "standings" };
        private static readonly string[] Tones = { "success", "fail", "slip" };
        private static readonly string[] Intros =
        {
            "Ascension spends Renown and Creds on a chance to rise a level. Every level raises all four attributes, and every few levels your Nova changes. A slip can cost a level, but never a Nova you have earned.",
            "Nova Standings are ranks on four tracks, bought in order with Renown and never lost. Every track you hold raises its stat, whichever one you wear. The Standing you wear shows on your namecard.",
        };

        private sealed class TrackCard
        {
            public StandingTrack Track;
            public VisualElement Root;
            public VisualElement[] Pips;
            public Label Level, Rank, Bonus, Next;
            public Button Buy, Wear;
        }

        private VisualElement root, window, ascensionBody, standingsBody, tierPips, levelPips, tracks;
        private Label purse, intro, level, tier, nextTier, gain, odds, cost, reason, result, worn, standingResult;
        private Button ascend;
        private readonly Button[] tabButtons = new Button[TabNames.Length];
        private TrackCard[] cards = new TrackCard[0];
        private Tab tab = Tab.Ascension;
        private PlayerNova nova;
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private bool initialized;
        private float nextRefresh;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("herald-window");
            if (window == null)
            {
                Debug.LogError("[Herald] HeraldWindow.uxml is not instanced in this UI document.", this);
                return;
            }
            ascensionBody = window.Q("herald-ascension");
            standingsBody = window.Q("herald-standings");
            tierPips = window.Q("herald-tier-pips");
            tracks = window.Q("herald-tracks");
            purse = window.Q<Label>("herald-purse");
            intro = window.Q<Label>("herald-intro");
            level = window.Q<Label>("herald-level");
            tier = window.Q<Label>("herald-tier");
            nextTier = window.Q<Label>("herald-next-tier");
            gain = window.Q<Label>("herald-gain");
            odds = window.Q<Label>("herald-odds");
            cost = window.Q<Label>("herald-cost");
            reason = window.Q<Label>("herald-reason");
            result = window.Q<Label>("herald-result");
            worn = window.Q<Label>("herald-worn");
            standingResult = window.Q<Label>("herald-standing-result");
            ascend = window.Q<Button>("herald-ascend");
            ascend.clicked += () => { if (nova != null) nova.RequestAscend(); };
            window.Q<Button>("close-herald").clicked += Close;
            for (int i = 0; i < TabNames.Length; i++)
            {
                Tab target = (Tab)i;
                tabButtons[i] = window.Q<Button>("herald-tab-" + TabNames[i]);
                tabButtons[i].clicked += () => ShowTab(target);
            }
            // The pips inside a tier sit on their own row under the tier pips.
            levelPips = new VisualElement();
            levelPips.AddToClassList("herald-pips");
            tierPips.parent.Insert(tierPips.parent.IndexOf(tierPips) + 1, levelPips);
            SetOpen(false);

            NpcServices.Register(ServiceIds.Herald, NpcOffer.Herald, "SPEND RENOWN", Open);
            PlayerInventory.LocalChanged += Close;
            initialized = true;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            PlayerInventory.LocalChanged -= Close;
            NpcServices.Unregister(ServiceIds.Herald);
            Bind(null);
        }

        private void Bind(PlayerNova next)
        {
            if (nova != null)
            {
                nova.Finished -= OnFinished;
                nova.Changed -= Refresh;
            }
            nova = next;
            if (nova == null) return;
            nova.Finished += OnFinished;
            nova.Changed += Refresh;
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && window.style.display == DisplayStyle.Flex;

        public void Open()
        {
            if (!initialized) return;
            PlayerNova next = PlayerNova.Local;
            if (next == null || next.Settings == null)
            {
                ClientPresentationRegistry.Messages?.ShowMessage("The Herald has nothing for this character.");
                return;
            }
            Bind(next);
            BuildPips();
            BuildTracks();
            SetResult(result, string.Empty, null);
            SetResult(standingResult, string.Empty, null);
            SetOpen(true);
            window.parent?.BringToFront();
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            placement.Open(window, null);
            ShowTab(tab);
            GearUIController.Instance?.SuppressWorldInput();
        }

        public void Close()
        {
            if (!initialized || !IsOpen) return;
            SetOpen(false);
            placement.Close();
            Bind(null);
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void SetOpen(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", open);
        }

        private void Update()
        {
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            if (nova == null) { Close(); return; }
            placement.Tick();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            if (!nova.InReach)
            {
                Close();
                ClientPresentationRegistry.Messages?.ShowMessage("You walked away from the Herald.");
                return;
            }
            Refresh(); // Renown and Creds move with everything else the player does
        }

        private void ShowTab(Tab next)
        {
            tab = next;
            for (int i = 0; i < tabButtons.Length; i++) tabButtons[i].EnableInClassList("active", i == (int)tab);
            intro.text = Intros[(int)tab];
            ascensionBody.style.display = tab == Tab.Ascension ? DisplayStyle.Flex : DisplayStyle.None;
            standingsBody.style.display = tab == Tab.Standings ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh();
        }

        // ------------------------------------------------------------------ building

        private static VisualElement Pip(VisualElement parent, bool small)
        {
            var pip = new VisualElement { pickingMode = PickingMode.Ignore };
            pip.AddToClassList("herald-pip");
            if (small) pip.AddToClassList("small");
            parent.Add(pip);
            return pip;
        }

        private void BuildPips()
        {
            AscensionSettings rules = nova.Settings.ascension;
            int tiers = NovaRules.TierCount(rules);
            if (tierPips.childCount != tiers)
            {
                tierPips.Clear();
                for (int i = 0; i < tiers; i++) Pip(tierPips, false);
            }
            if (levelPips.childCount != rules.levelsPerTier)
            {
                levelPips.Clear();
                for (int i = 0; i < rules.levelsPerTier; i++) Pip(levelPips, true);
            }
        }

        private void BuildTracks()
        {
            StandingSettings rules = nova.Settings.standings;
            int count = rules.tracks != null ? rules.tracks.Length : 0;
            if (cards.Length == count && tracks.childCount == count) return;
            tracks.Clear();
            cards = new TrackCard[count];
            for (int i = 0; i < count; i++)
            {
                StandingTrackInfo info = rules.tracks[i];
                var card = new TrackCard { Track = info.track, Root = new VisualElement() };
                card.Root.AddToClassList("kn-card");
                card.Root.AddToClassList("herald-track");

                var head = new VisualElement();
                head.AddToClassList("herald-track-head");
                var name = new Label(info.name.ToUpperInvariant());
                name.AddToClassList("herald-track-name");
                card.Level = new Label();
                card.Level.AddToClassList("herald-track-level");
                head.Add(name);
                head.Add(card.Level);
                card.Root.Add(head);

                card.Rank = new Label();
                card.Rank.AddToClassList("herald-track-rank");
                card.Root.Add(card.Rank);

                var pips = new VisualElement();
                pips.AddToClassList("herald-pips");
                pips.style.justifyContent = Justify.FlexStart;
                card.Pips = new VisualElement[rules.MaxLevel];
                for (int p = 0; p < card.Pips.Length; p++) card.Pips[p] = Pip(pips, true);
                card.Root.Add(pips);

                card.Bonus = new Label();
                card.Bonus.AddToClassList("herald-track-bonus");
                card.Root.Add(card.Bonus);
                card.Next = new Label();
                card.Next.AddToClassList("herald-track-next");
                card.Root.Add(card.Next);

                var buttons = new VisualElement();
                buttons.AddToClassList("herald-track-buttons");
                StandingTrack track = info.track;
                card.Buy = new Button(() => { if (nova != null) nova.RequestBuyStanding(track); }) { text = "BUY RANK" };
                card.Buy.AddToClassList("kn-button-primary");
                card.Wear = new Button(() => { if (nova != null) nova.RequestWearStanding(track); }) { text = "WEAR" };
                buttons.Add(card.Buy);
                buttons.Add(card.Wear);
                card.Root.Add(buttons);

                tracks.Add(card.Root);
                cards[i] = card;
            }
        }

        // ------------------------------------------------------------------ refresh

        private void Refresh()
        {
            if (!IsOpen || nova == null || nova.Settings == null) return;
            purse.text = $"RENOWN {nova.Renown:N0}   ·   CREDS {nova.Creds:N0}";
            if (tab == Tab.Ascension) RefreshAscension();
            else RefreshStandings();
        }

        private void RefreshAscension()
        {
            AscensionSettings rules = nova.Settings.ascension;
            int now = nova.Ascension;
            int nowTier = NovaRules.Tier(rules, now);
            int tiers = NovaRules.TierCount(rules);

            level.text = "+" + now;
            tier.text = nowTier > 0 ? "NOVA " + NovaRules.Numeral(nowTier) : "NO NOVA YET";
            for (int i = 0; i < tierPips.childCount; i++) tierPips[i].EnableInClassList(FilledClass, i < nowTier);
            int inTier = nowTier > 0 ? now - NovaRules.TierFloor(rules, now) + 1 : 0;
            for (int i = 0; i < levelPips.childCount; i++) levelPips[i].EnableInClassList(FilledClass, i < inTier);

            int toNext = NovaRules.LevelsToNextTier(rules, now);
            nextTier.text = now >= rules.maxLevel ? "Your Ascension is complete."
                : nowTier >= tiers ? $"{rules.maxLevel - now} more level{(rules.maxLevel - now == 1 ? "" : "s")} to complete your Ascension."
                : $"{toNext} more level{(toNext == 1 ? "" : "s")} to Nova {NovaRules.Numeral(nowTier + 1)}.";

            int each = rules.attributesPerLevel;
            gain.text = $"Each level: +{each} Strength, Agility, Vitality and Spirit.\nYou have +{now * each} to each now.";

            AscensionOdds chance = NovaRules.OddsAt(rules, now);
            string slipNote = chance.slip <= 0f || !NovaRules.SlipIsHarmless(rules, now) ? string.Empty
                : now <= 0 ? "\nAt +0 there is no level to lose."
                : "\nYour Nova holds here: a slip costs no level.";
            odds.text = $"Rise {chance.rise:0.#}%   ·   Hold {chance.hold:0.#}%   ·   Slip {chance.slip:0.#}%{slipNote}";
            cost.text = $"Cost: {rules.renownCost:N0} Renown   ·   {rules.credsCost:N0} Creds";

            string blocked = NovaRules.AscendBlocked(rules, now, nova.Renown, nova.Creds);
            reason.text = blocked ?? string.Empty;
            ascend.SetEnabled(blocked == null);
        }

        private void RefreshStandings()
        {
            StandingSettings rules = nova.Settings.standings;
            NovaState state = nova.State;
            string wornName = nova.WornStanding;
            worn.text = wornName != null ? "You wear: " + wornName : "You wear no Standing yet. Your first rank is worn at once.";
            bool wearsAny = state.TryGetWorn(out StandingTrack wornTrack);

            foreach (TrackCard card in cards)
            {
                if (!NovaRules.TryGetTrack(rules, card.Track, out StandingTrackInfo info)) continue;
                int held = state.Standing(card.Track);
                bool isWorn = wearsAny && wornTrack == card.Track;
                card.Root.EnableInClassList("worn", isWorn);
                card.Level.text = $"{held} / {rules.MaxLevel}";
                card.Rank.text = NovaRules.StandingName(rules, nova.Faction, card.Track, held) ?? "Not held";
                for (int p = 0; p < card.Pips.Length; p++) card.Pips[p].EnableInClassList(FilledClass, p < held);

                string stat = StatDisplay.Name(info.stat);
                float percent = NovaRules.StandingPercent(rules, held);
                int price = NovaRules.StandingCost(rules, held);
                card.Bonus.text = price < 0 ? $"{stat} +{percent:0.#}%"
                    : $"{stat} +{percent:0.#}%   ·   next rank +{NovaRules.StandingPercent(rules, held + 1):0.#}%";
                card.Next.text = price < 0 ? "Complete."
                    : $"Next: {NovaRules.StandingName(rules, nova.Faction, card.Track, held + 1)}\n{price:N0} Renown";
                card.Buy.SetEnabled(NovaRules.StandingBlocked(rules, held, nova.Renown) == null);
                card.Buy.style.display = price < 0 ? DisplayStyle.None : DisplayStyle.Flex;
                card.Wear.text = isWorn ? "WORN" : "WEAR";
                card.Wear.SetEnabled(held > 0 && !isWorn);
            }
        }

        // ------------------------------------------------------------------ results

        private void OnFinished(NovaWorkResult finished)
        {
            if (nova == null) return;
            if (finished.kind == NovaWorkKind.Standing)
            {
                string name = NovaRules.StandingName(nova.Settings.standings, nova.Faction, finished.track, finished.after);
                SetResult(standingResult, $"You are now {name}.", "success");
                Refresh();
                return;
            }

            string text;
            string tone;
            switch (finished.outcome)
            {
                case AscensionOutcome.Rise:
                    int reached = NovaRules.Tier(nova.Settings.ascension, finished.after);
                    text = finished.newTier ? $"Ascended to +{finished.after}. Your Nova is now Nova {NovaRules.Numeral(reached)}."
                        : $"Ascended to +{finished.after}.";
                    tone = "success";
                    break;
                case AscensionOutcome.Slip:
                    text = $"You slipped to +{finished.after}.";
                    tone = "slip";
                    break;
                case AscensionOutcome.SlipHeld:
                    text = $"A slip, but your Nova held. You stay at +{finished.after}.";
                    tone = "fail";
                    break;
                default:
                    text = $"No change. You hold at +{finished.after}.";
                    tone = "fail";
                    break;
            }
            SetResult(result, text, tone);
            Refresh();
        }

        private static void SetResult(Label label, string text, string tone)
        {
            label.text = text;
            foreach (string name in Tones) label.EnableInClassList(name, name == tone);
        }
    }
}
