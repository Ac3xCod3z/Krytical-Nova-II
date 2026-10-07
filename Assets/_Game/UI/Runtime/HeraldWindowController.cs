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
    /// It also gives the moment its weight on the HUD: a short charge on the button before an attempt goes, the answer
    /// landing on the level, and a banner when the player's own Ascension rises, their Nova changes or they take a
    /// Standing rank (the banner shows with the window shut too: the GM tools raise both without it).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HeraldWindowController : MonoBehaviour
    {
        private enum Tab { Ascension, Standings }

        private const float RefreshSeconds = 0.25f;
        private const float RebindSeconds = 0.5f;
        private const float ChargeSeconds = 0.45f;   // the bar under the button fills in this long (Herald.uss)
        private const float BannerSeconds = 3.6f, BannerShortSeconds = 2.2f;
        private const int PopMs = 60, FlashMs = 450, PipFirstMs = 220, PipStepMs = 55;
        private const string FilledClass = "filled";
        private const string ChargingClass = "charging", PopClass = "pop", NewClass = "new", SmallClass = "small";
        private const string BannerShownClass = "nova-banner--shown";
        private static readonly string[] CardTones = { "rise", "hold", "slip" };
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
        private VisualElement novaCard, chargeFill, banner, bannerPips;
        private Label purse, intro, level, tier, nextTier, gain, odds, cost, reason, result, worn, standingResult;
        private Label bannerEyebrow, bannerTitle, bannerDetail;
        private Button ascend;
        private readonly Button[] tabButtons = new Button[TabNames.Length];
        private TrackCard[] cards = new TrackCard[0];
        private Tab tab = Tab.Ascension;
        private PlayerNova nova;
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private bool initialized;
        private float nextRefresh, nextRebind;
        private float chargeEnds = -1f, bannerEnds = -1f;
        private int bannerVersion;

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
            ascend.clicked += BeginAscend;
            novaCard = window.Q(className: "herald-nova");
            chargeFill = window.Q("herald-charge-fill");
            banner = root.Q("nova-banner");
            if (banner != null)
            {
                bannerEyebrow = banner.Q<Label>("nova-banner-eyebrow");
                bannerTitle = banner.Q<Label>("nova-banner-title");
                bannerDetail = banner.Q<Label>("nova-banner-detail");
                bannerPips = banner.Q("nova-banner-pips");
                banner.RemoveFromClassList(BannerShownClass);
                banner.style.display = DisplayStyle.None;
            }
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
            CancelCharge();
            SetOpen(false);
            placement.Close();
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void SetOpen(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", open);
        }

        private void Update()
        {
            if (!initialized) return;
            float now = Time.unscaledTime;
            TickBanner(now);
            // Polled: the local player object changes when a session starts, and the banner needs it with the window shut.
            if (now >= nextRebind)
            {
                nextRebind = now + RebindSeconds;
                PlayerNova local = PlayerNova.Local;
                if (local != nova)
                {
                    Close();
                    Bind(local);
                }
            }
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            if (nova == null) { Close(); return; }
            placement.Tick();
            if (chargeEnds >= 0f && now >= chargeEnds)
            {
                CancelCharge();
                nova.RequestAscend();
            }
            if (now < nextRefresh) return;
            nextRefresh = now + RefreshSeconds;
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
            ascend.SetEnabled(blocked == null && chargeEnds < 0f);
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

        // ------------------------------------------------------------------ the attempt

        /// <summary>The press: a short charge under the button before the request goes, so the answer lands as a beat.</summary>
        private void BeginAscend()
        {
            if (nova == null || chargeEnds >= 0f) return;
            chargeEnds = Time.unscaledTime + ChargeSeconds;
            ascend.SetEnabled(false);
            ascend.text = "ASCENDING";
            chargeFill?.AddToClassList(ChargingClass);
            SetResult(result, string.Empty, null);
        }

        private void CancelCharge()
        {
            if (chargeEnds < 0f) return;
            chargeEnds = -1f;
            ascend.text = "ASCEND";
            chargeFill?.RemoveFromClassList(ChargingClass);
        }

        /// <summary>The answer lands on the card: it takes the colour of the outcome for a moment, and a level that moved jumps and settles.</summary>
        private void Reveal(AscensionOutcome outcome)
        {
            if (!IsOpen || novaCard == null) return;
            string tone = outcome == AscensionOutcome.Rise ? "rise" : outcome == AscensionOutcome.Slip ? "slip" : "hold";
            foreach (string name in CardTones) novaCard.EnableInClassList(name, name == tone);
            novaCard.schedule.Execute(() => novaCard.RemoveFromClassList(tone)).StartingIn(FlashMs);
            if (outcome != AscensionOutcome.Rise && outcome != AscensionOutcome.Slip) return; // the level did not move
            level.AddToClassList(PopClass);
            level.schedule.Execute(() => level.RemoveFromClassList(PopClass)).StartingIn(PopMs);
        }

        // ------------------------------------------------------------------ the banner

        private void AnnounceAscension(NovaWorkResult finished)
        {
            AscensionSettings rules = nova.Settings.ascension;
            int now = finished.after;
            int tiers = NovaRules.TierCount(rules);
            int reached = NovaRules.Tier(rules, now);
            string attributes = $"all four attributes +{now * rules.attributesPerLevel}";
            Color colour = nova.Settings.look.ColourOf(reached);
            if (finished.newTier)
            {
                ShowBanner("YOUR NOVA HAS RISEN", "NOVA " + NovaRules.Numeral(reached), $"Ascension +{now}  ·  {attributes}",
                    reached, tiers, false, colour, BannerSeconds);
                return;
            }
            // A plain level with the window open is told by the window itself: the level jumps on its card.
            if (IsOpen) return;
            int toNext = NovaRules.LevelsToNextTier(rules, now);
            string ahead = now >= rules.maxLevel ? "Ascension complete"
                : reached >= tiers ? $"{rules.maxLevel - now} to complete"
                : $"{toNext} to Nova {NovaRules.Numeral(reached + 1)}";
            int inTier = reached > 0 ? now - NovaRules.TierFloor(rules, now) + 1 : 0;
            ShowBanner("ASCENSION", "+" + now, $"{ahead}  ·  {attributes}", inTier, rules.levelsPerTier, true, colour, BannerShortSeconds);
        }

        private void AnnounceStanding(NovaWorkResult finished, string name)
        {
            StandingSettings rules = nova.Settings.standings;
            string detail = NovaRules.TryGetTrack(rules, finished.track, out StandingTrackInfo info)
                ? $"{StatDisplay.Name(info.stat)} +{NovaRules.StandingPercent(rules, finished.after):0.#}%" : string.Empty;
            ShowBanner("NOVA STANDING", (name ?? "Standing").ToUpperInvariant(), detail, finished.after, rules.MaxLevel, true,
                nova.Settings.look.standingColour, BannerSeconds);
        }

        /// <summary>
        /// The banner comes up with its pips empty; they fill one after another, and the last (the one just earned)
        /// lands bigger and settles.
        /// </summary>
        private void ShowBanner(string eyebrow, string title, string detail, int filled, int total, bool small, Color colour, float seconds)
        {
            if (banner == null) return;
            int version = ++bannerVersion; // a banner shown over this one cancels what this one still had to do
            bannerEyebrow.text = eyebrow;
            bannerTitle.text = title;
            bannerTitle.style.color = Legible(colour);
            bannerDetail.text = detail;

            total = Mathf.Max(0, total);
            if (bannerPips.childCount != total)
            {
                bannerPips.Clear();
                for (int i = 0; i < total; i++) Pip(bannerPips, small);
            }
            filled = Mathf.Clamp(filled, 0, total);
            for (int i = 0; i < total; i++)
            {
                VisualElement pip = bannerPips[i];
                pip.EnableInClassList(SmallClass, small);
                pip.RemoveFromClassList(FilledClass);
                pip.RemoveFromClassList(NewClass);
                if (i >= filled) continue;
                bool last = i == filled - 1;
                // Those already held are there at once on a plain level; on anything bigger they count up.
                int delay = PipFirstMs + (seconds > BannerShortSeconds || last ? i * PipStepMs : 0);
                pip.schedule.Execute(() =>
                {
                    if (version != bannerVersion) return;
                    pip.AddToClassList(FilledClass);
                    if (!last) return;
                    pip.AddToClassList(NewClass);
                    pip.schedule.Execute(() => pip.RemoveFromClassList(NewClass)).StartingIn(PopMs);
                }).StartingIn(delay);
            }

            PlaceBanner();
            banner.style.display = DisplayStyle.Flex;
            banner.BringToFront();
            // The class goes on a frame later so the transition has a state to start from.
            banner.schedule.Execute(() => { if (version == bannerVersion) banner.AddToClassList(BannerShownClass); });
            bannerEnds = Time.unscaledTime + seconds;
        }

        /// <summary>Over the middle of the screen, or of the wider side the open window leaves free: never over the window.</summary>
        private void PlaceBanner()
        {
            float screen = root.resolvedStyle.width;
            if (float.IsNaN(screen) || screen <= 0f) return;
            float centre = screen * 0.5f;
            if (IsOpen)
            {
                Rect taken = window.worldBound;
                float left = Mathf.Max(0f, taken.xMin), right = Mathf.Max(0f, screen - taken.xMax);
                centre = left >= right ? left * 0.5f : taken.xMax + right * 0.5f;
            }
            banner.style.left = centre;
        }

        private void TickBanner(float now)
        {
            if (banner == null || bannerEnds < 0f || now < bannerEnds) return;
            bannerEnds = -1f;
            banner.RemoveFromClassList(BannerShownClass);
            banner.schedule.Execute(() => { if (bannerEnds < 0f) banner.style.display = DisplayStyle.None; }).StartingIn(350);
        }

        /// <summary>A tier's colour as text: its hue at full strength, lifted toward white so the dark tiers still read.</summary>
        private static Color Legible(Color colour)
        {
            float max = colour.maxColorComponent;
            if (max < 0.01f) return Color.white;
            var vivid = new Color(colour.r / max, colour.g / max, colour.b / max, 1f);
            return Color.Lerp(vivid, Color.white, 0.3f);
        }

        // ------------------------------------------------------------------ results

        private void OnFinished(NovaWorkResult finished)
        {
            if (nova == null || nova.Settings == null) return;
            if (finished.kind == NovaWorkKind.Standing)
            {
                string name = NovaRules.StandingName(nova.Settings.standings, nova.Faction, finished.track, finished.after);
                SetResult(standingResult, $"You are now {name}.", "success");
                Refresh();
                AnnounceStanding(finished, name);
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
            Reveal(finished.outcome);
            if (finished.outcome == AscensionOutcome.Rise) AnnounceAscension(finished);
        }

        private static void SetResult(Label label, string text, string tone)
        {
            label.text = text;
            foreach (string name in Tones) label.EnableInClassList(name, name == tone);
        }
    }
}
