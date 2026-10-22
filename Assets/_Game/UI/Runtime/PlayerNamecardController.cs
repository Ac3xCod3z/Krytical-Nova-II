using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using KryticalNova.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;
using KryticalNova.World;

namespace KryticalNova.UI
{
    /// <summary>
    /// The namecard over every player's head: the Standing they wear, their faction's crest, name and level in the
    /// faction's colour, and their Crew. Who sees whose is <see cref="NamecardRules"/>: your own (a setting), your
    /// faction always, another faction only while that player stands on neutral ground. It sits above the head and
    /// rises to clear a Nova. Everything on it is already replicated to every machine; nothing is sent for it.
    /// While the HUD wears a faction's art a card is the art of the faction of the player it names, not of who
    /// reads it (<see cref="PlayerCardView"/>, on the HUD pieces' canvas): a player of another faction met on neutral
    /// ground is named in their own faction's card. The level is in the pack's badge, and the pack's health bar is on the players whose health is
    /// the viewer's business: an enemy, a Callout opponent, a Squad member, the target. With no faction art it is
    /// the plain card drawn here.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerNamecardController : MonoBehaviour
    {
        private const string LayerName = "player-namecards";
        private const float GapAboveHead = 0.14f;      // from the crown (or the top of the Nova) to the card's foot
        private const float FallbackCrown = 0.72f;     // above the player's root, for a body with no head joint found
        private const float HeightFollow = 7f;         // how fast the card follows the head up and down (per second)
        private const string WindowClass = "blocks-world";
        private static readonly Color DuelColor = new Color(1f, 0.36f, 0.32f);
        private static readonly Color NoFactionColor = new Color(0.9f, 0.93f, 0.96f);

        [Tooltip("Namecards further than this from the camera are hidden.")]
        [SerializeField, Min(5f)] private float maxDistance = 38f;

        private sealed class Card
        {
            public PlayerCardView View; // the faction's card; the elements below are the plain one's
            public VisualElement Root, Crest;
            public Label Standing, Name, Crew;
            public PlayerResources Vitals;
            public PlayerParty Party;
            public IDamageable Body;
            public int Level = -1;
            public PlayerIdentity Identity;
            public PlayerCharacterProgression Progression;
            public PlayerCrew PlayerCrew;
            public PlayerNova Nova;
            public KryticalNova.Autopilot.PlayerAutopilot Autopilot;
            public KryticalNova.Networking.PlayerGm Gm;
            public float Height = float.NaN;
            public string NameText, StandingText, CrewText;
            // What the name and the Crew line were last made from: the lines are put together again only when these change.
            public string NameSource;
            public bool Auto;
            public Unity.Collections.FixedString32Bytes CrewName;
            public int CrewSymbol = -1; // the Crew symbol shown in the emblem's place (symbol and colour in one number), or -1 for none
            public FactionId Faction = (FactionId)255;
            public FactionId ArtFaction; // whose art the card was made in
            public bool Duelling;
        }

        private readonly Dictionary<PointAndClickPlayer, Card> cards = new Dictionary<PointAndClickPlayer, Card>();
        private readonly List<PointAndClickPlayer> stale = new List<PointAndClickPlayer>();
        private VisualElement root, layer;
        private Camera targetCamera;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            layer = root?.Q(LayerName);
            if (layer == null) Debug.LogError("[Namecards] ProgressionHUD.uxml has no '" + LayerName + "' element.", this);
            HudChrome.KitChanged += DropCards;
        }

        private void OnDisable()
        {
            HudChrome.KitChanged -= DropCards;
            DropCards();
        }

        /// <summary>Every card goes: the next frame makes them again, in whatever art the HUD wears now.</summary>
        private void DropCards()
        {
            foreach (Card card in cards.Values) Remove(card);
            cards.Clear();
        }

        private static void Remove(Card card)
        {
            if (card.View != null) Destroy(card.View.gameObject);
            card.Root?.RemoveFromHierarchy();
        }

        private void LateUpdate()
        {
            if (layer == null || root.panel == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            PlayerInventory localInventory = PlayerInventory.Local;
            GameObject local = localInventory != null ? localInventory.gameObject : null;
            PlayerIdentity viewer = local != null ? local.GetComponent<PlayerIdentity>() : null;
            FactionId viewerFaction = viewer != null ? viewer.Faction : FactionId.None;
            bool show = HudPreferences.ShowNameplates;
            bool showOwn = HudPreferences.ShowOwnNamecard;
            Vector3 cameraPosition = targetCamera.transform.position;
            float follow = 1f - Mathf.Exp(-HeightFollow * Time.unscaledDeltaTime);
            // Screen pixels in a panel pixel: a faction's card is sized like the rest of the HUD.
            Vector2 panelOrigin = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
            float panelAcross = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.width, 0f)).x - panelOrigin.x;
            float panelScale = panelAcross > 1f ? Screen.width / panelAcross : 1f;
            PlayerParty viewerParty = local != null ? local.GetComponent<PlayerParty>() : null;
            Transform aimedAt = local != null && local.TryGetComponent(out PointAndClickPlayer pointer) ? pointer.SelectedTarget : null;

            IReadOnlyList<PointAndClickPlayer> players = PointAndClickPlayer.Active;
            for (int i = 0; i < players.Count; i++)
            {
                PointAndClickPlayer player = players[i];
                if (player == null) continue;
                if (!cards.TryGetValue(player, out Card card))
                {
                    card = Create(player);
                    cards.Add(player, card);
                }
                // A player's faction is known a moment after the player appears: the card is made again in its art.
                else if (card.Identity != null && card.Identity.Faction != card.ArtFaction)
                {
                    Remove(card);
                    card = Create(player);
                    cards[player] = card;
                }

                // The card rides on the body's own place; only its height follows the head, eased, so a head that
                // sways or bobs does not shake the name about. Height is along the body's own up: on a wall (walls as
                // a second ground) the name sits off the head, out from the face, as it does above it on the ground.
                Transform body = player.transform;
                Vector3 up = body.up;
                float scale = body.lossyScale.y;
                float crown = card.Nova != null ? Vector3.Dot(card.Nova.Crown - body.position, up) : FallbackCrown * scale;
                float target = crown + (card.Nova != null ? card.Nova.NovaHeight : 0f) + GapAboveHead * scale;
                card.Height = float.IsNaN(card.Height) ? target : Mathf.Lerp(card.Height, target, follow);
                Vector3 world = body.position + up * card.Height;

                FactionId faction = card.Identity != null ? card.Identity.Faction : FactionId.None;
                bool isSelf = player.gameObject == local;
                Vector3 screen = targetCamera.WorldToScreenPoint(world);
                bool visible = show && screen.z > 0f
                    && (card.Identity == null || card.Identity.HasIdentity)
                    && (world - cameraPosition).sqrMagnitude <= maxDistance * maxDistance
                    && NamecardRules.Visible(isSelf, showOwn, viewerFaction, faction, !isSelf && faction != viewerFaction && TradeZone.CrossFactionAt(body.position));
                Vector2 point = default;
                if (visible)
                {
                    // Windows are a little see-through: a name behind one would read through it.
                    point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                    visible = !UnderWindow(point);
                }
                if (card.View != null) card.View.Show(visible);
                else card.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;

                if (card.View != null)
                {
                    FillView(card, faction, viewer, !isSelf && WatchesHealth(card, local, viewerParty, aimedAt));
                    card.View.Place(new Vector2(screen.x, screen.y), panelScale);
                    continue;
                }
                Fill(card, faction, viewer);
                card.Root.style.left = point.x - card.Root.resolvedStyle.width * 0.5f;
                card.Root.style.top = point.y - card.Root.resolvedStyle.height;
                Hud.HudChrome.HideOver(card.Root, point);
            }

            stale.Clear();
            foreach (var pair in cards)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled) stale.Add(pair.Key);
            foreach (PointAndClickPlayer gone in stale)
            {
                if (cards.TryGetValue(gone, out Card card)) Remove(card);
                cards.Remove(gone);
            }
        }

        /// <summary>
        /// Whose health the viewer is shown: the player they have targeted, a member of their Squad, and anyone
        /// they could strike (another faction off neutral ground, or a Callout opponent).
        /// </summary>
        private static bool WatchesHealth(Card card, GameObject local, PlayerParty viewerParty, Transform target)
        {
            if (local == null) return false;
            Transform body = card.Identity != null ? card.Identity.transform : null;
            if (target != null && body != null && target == body) return true;
            if (viewerParty != null && viewerParty.IsInPartyWith(card.Party)) return true;
            return card.Body != null && KryticalNova.Combat.CombatHostility.IsHostile(local.transform, card.Body);
        }

        /// <summary>True when a point of the screen is covered by a window (anything that blocks clicks on the world).</summary>
        private bool UnderWindow(Vector2 panelPoint)
        {
            for (VisualElement at = root.panel.Pick(panelPoint); at != null; at = at.parent)
                if (at.ClassListContains(WindowClass)) return true;
            return false;
        }

        private Card Create(PointAndClickPlayer player)
        {
            var card = new Card
            {
                Identity = player.GetComponent<PlayerIdentity>(),
                Progression = player.GetComponent<PlayerCharacterProgression>(),
                PlayerCrew = player.GetComponent<PlayerCrew>(),
                Nova = player.GetComponent<PlayerNova>(),
                Autopilot = player.GetComponent<KryticalNova.Autopilot.PlayerAutopilot>(),
                Gm = player.GetComponent<KryticalNova.Networking.PlayerGm>(),
                Vitals = player.GetComponent<PlayerResources>(),
                Party = player.GetComponent<PlayerParty>(),
                Body = player.GetComponent<IDamageable>(),
            };
            // The card of the player's own faction while the HUD wears a faction's art.
            card.ArtFaction = card.Identity != null ? card.Identity.Faction : FactionId.None;
            FactionHudKit kit = HudChrome.KitFor(card.ArtFaction);
            PlayerCardView art = kit != null ? kit.playerCard : null;
            if (art == null && HudChrome.ShownKit != null) art = HudChrome.ShownKit.playerCard;
            if (art != null && HudChrome.WorldLayer != null)
            {
                card.View = Instantiate(art, HudChrome.WorldLayer, false);
                card.View.name = art.name;
                card.View.Show(false);
                return card;
            }
            card.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Root.AddToClassList("namecard");
            card.Standing = Line("namecard-standing", card.Root);

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("namecard-row");
            card.Crest = new VisualElement { pickingMode = PickingMode.Ignore };
            card.Crest.AddToClassList("namecard-crest");
            row.Add(card.Crest);
            card.Name = Line("namecard-name", row);
            card.Root.Add(row);

            card.Crew = Line("namecard-crew", card.Root);
            card.Root.style.display = DisplayStyle.None;
            layer.Add(card.Root);
            return card;
        }

        private static Label Line(string className, VisualElement parent)
        {
            // A line is never read as markup (a Crew name is shown as a chip; see CrewChipText).
            var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        /// <summary>The same for a faction's card: the level goes in the badge, and the bar shows where it may.</summary>
        private static void FillView(Card card, FactionId faction, PlayerIdentity viewer, bool showHealth)
        {
            PlayerCardView view = card.View;
            string name = card.Identity != null ? card.Identity.CharacterName : view.name;
            if (card.Gm != null && card.Gm.ShowsTag) name = KryticalNova.Networking.PlayerGm.Tag + name; // a Game Master says so
            // A character hunting on Autopilot says so, to everyone who can read its name.
            bool auto = card.Autopilot != null && card.Autopilot.IsOn;
            int level = card.Progression != null ? card.Progression.CurrentLevel : 1;
            if (name != card.NameSource || auto != card.Auto || level != card.Level || card.NameText == null)
            {
                card.NameSource = name;
                card.Auto = auto;
                card.NameText = auto ? name + "   AUTO" : name;
                card.Level = level;
                view.Say(card.NameText, level);
            }

            string standing = card.Nova != null ? card.Nova.WornStanding ?? string.Empty : string.Empty;
            if (standing != card.StandingText) view.Standing(card.StandingText = standing);

            CrewTag tag = card.PlayerCrew != null ? card.PlayerCrew.Tag : default;
            if (card.CrewText == null || !tag.crew.Equals(card.CrewName) || CrewEmblem(tag) != card.CrewSymbol)
            {
                card.CrewName = tag.crew;
                view.Crew(card.CrewText = CrewChipText(tag), CrewPlate(card, tag), CrewInk(card, tag));
            }
            // A player in a Crew shows the Crew's symbol where the faction's emblem stands; without a Crew, the emblem.
            int symbol = CrewEmblem(tag);
            bool emblemChanged = symbol != card.CrewSymbol;
            card.CrewSymbol = symbol;

            float share = card.Vitals != null && card.Vitals.MaxHealth > 0f ? Mathf.Clamp01(card.Vitals.Health / card.Vitals.MaxHealth) : 1f;
            view.Health(showHealth && card.Vitals != null, share);

            bool duelling = viewer != null && card.Identity != null && card.Identity != viewer && card.Identity.IsDuelling(viewer);
            if (faction == card.Faction && duelling == card.Duelling && !emblemChanged) return;
            card.Faction = faction;
            card.Duelling = duelling;
            FactionDefinition definition = card.Identity != null && card.Identity.Database != null && card.Identity.Database.Factions != null
                ? card.Identity.Database.Factions.Get(faction) : null;
            view.Colour(duelling ? DuelColor : definition != null ? Opaque(definition.accentColor) : NoFactionColor);
            Sprite emblem = Emblem(card, tag, faction, definition, out Color tint);
            view.Crest(emblem, tint);
            view.CrewSymbol(null);
        }

        /// <summary>
        /// The Crew line (owner, 22 Oct 2026: no more &lt;Crew&gt;): the Crew's name in capitals, shown as a chip, a
        /// plate in the Crew's own colour beside the Crew's symbol, in place of the brackets.
        /// </summary>
        private static string CrewChipText(in CrewTag tag) => tag.InCrew ? tag.crew.ToString().ToUpperInvariant() : string.Empty;

        // The plate the founder chose, else the Crew's colour (the one its symbol is drawn in), or a plain slate.
        private static Color CrewPlate(Card card, in CrewTag tag)
        {
            CrewSettings settings = tag.InCrew && card.PlayerCrew != null ? card.PlayerCrew.Settings : null;
            return settings != null ? settings.TagPlate(tag.tagBackground, tag.colour) : new Color(0.32f, 0.38f, 0.46f);
        }

        // The letters the founder chose (owner, 22 Oct 2026), else ink that reads on the plate.
        private static Color CrewInk(Card card, in CrewTag tag)
        {
            CrewSettings settings = tag.InCrew && card.PlayerCrew != null ? card.PlayerCrew.Settings : null;
            return settings != null && settings.HasTagInk(tag.tagColour) ? settings.TagInk(tag.tagColour) : ChipInk(CrewPlate(card, tag));
        }

        /// <summary>Dark ink on a light plate, light ink on a dark one.</summary>
        public static Color ChipInk(Color plate) =>
            plate.r * 0.299f + plate.g * 0.587f + plate.b * 0.114f > 0.55f ? new Color(0.08f, 0.07f, 0.12f) : new Color(0.97f, 0.96f, 0.92f);

        // The Crew's symbol and its colours as one number, so a change of any is seen; -1 without a Crew.
        private static int CrewEmblem(in CrewTag tag) =>
            tag.InCrew ? tag.symbol | (tag.colour << 8) | (tag.tagColour << 16) | ((tag.tagBackground & 0x7f) << 24) : -1;

        /// <summary>What stands before the name: the Crew's symbol in the Crew's colour, or else the faction's emblem.</summary>
        private static Sprite Emblem(Card card, in CrewTag tag, FactionId faction, FactionDefinition definition, out Color tint)
        {
            tint = Color.white;
            CrewSettings settings = tag.InCrew && card.PlayerCrew != null ? card.PlayerCrew.Settings : null;
            Sprite symbol = settings != null && settings.symbols != null ? settings.symbols.Get(faction, tag.symbol) : null;
            if (symbol == null) return definition != null ? definition.crest : null;
            tint = settings.SymbolColour(tag.colour);
            return symbol;
        }

        /// <summary>Writes what changed since the card was last shown. Called only for cards on screen.</summary>
        private static void Fill(Card card, FactionId faction, PlayerIdentity viewer)
        {
            string name = card.Identity != null ? card.Identity.CharacterName : card.Root.name;
            if (card.Gm != null && card.Gm.ShowsTag) name = KryticalNova.Networking.PlayerGm.Tag + name; // a Game Master says so
            int level = card.Progression != null ? card.Progression.CurrentLevel : 1;
            string nameText = name + "   Lv " + level;
            // A character hunting on Autopilot says so, to everyone who can read its name.
            if (card.Autopilot != null && card.Autopilot.IsOn) nameText += "   AUTO";
            if (nameText != card.NameText) card.Name.text = card.NameText = nameText;

            string standing = card.Nova != null ? card.Nova.WornStanding ?? string.Empty : string.Empty;
            if (standing != card.StandingText)
            {
                card.Standing.text = card.StandingText = standing;
                card.Standing.style.display = standing.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            CrewTag tag = card.PlayerCrew != null ? card.PlayerCrew.Tag : default;
            string crew = CrewChipText(tag);
            if (crew != card.CrewText || CrewEmblem(tag) != card.CrewSymbol)
            {
                card.Crew.text = card.CrewText = crew;
                card.Crew.style.display = crew.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                // The Crew chip: the name on a plate of the Crew's own colour, in ink that reads on it.
                Color plate = CrewPlate(card, tag);
                card.Crew.EnableInClassList("crew-chip", crew.Length > 0);
                card.Crew.style.backgroundColor = new Color(plate.r, plate.g, plate.b, 0.88f);
                card.Crew.style.color = CrewInk(card, tag);
            }
            int symbol = CrewEmblem(tag);
            bool emblemChanged = symbol != card.CrewSymbol;
            card.CrewSymbol = symbol;

            // A Callout opponent reads as an enemy for as long as the duel lasts.
            bool duelling = viewer != null && card.Identity != null && card.Identity != viewer && card.Identity.IsDuelling(viewer);
            if (faction == card.Faction && duelling == card.Duelling && !emblemChanged) return;
            card.Faction = faction;
            card.Duelling = duelling;
            FactionDefinition definition = card.Identity != null && card.Identity.Database != null && card.Identity.Database.Factions != null
                ? card.Identity.Database.Factions.Get(faction) : null;
            card.Name.style.color = duelling ? DuelColor : definition != null ? Opaque(definition.accentColor) : NoFactionColor;
            Sprite emblem = Emblem(card, tag, faction, definition, out Color tint);
            card.Crest.style.display = emblem != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (emblem != null)
            {
                card.Crest.style.backgroundImage = new StyleBackground(emblem);
                card.Crest.style.unityBackgroundImageTintColor = tint;
            }
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);
    }
}
