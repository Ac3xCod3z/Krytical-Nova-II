using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The namecard over every player's head: the Standing they wear, their faction's crest, name and level in the
    /// faction's colour, and their Crew. Who sees whose is <see cref="NamecardRules"/>: your own (a setting), your
    /// faction always, another faction only while that player stands on neutral ground. It sits above the head and
    /// rises to clear a Nova. Everything on it is already replicated to every machine; nothing is sent for it.
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
            public VisualElement Root, Crest;
            public Label Standing, Name, Crew;
            public PlayerIdentity Identity;
            public PlayerCharacterProgression Progression;
            public PlayerCrew PlayerCrew;
            public PlayerNova Nova;
            public float Height = float.NaN;
            public string NameText, StandingText, CrewText;
            public FactionId Faction = (FactionId)255;
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
        }

        private void OnDisable()
        {
            foreach (Card card in cards.Values) card.Root.RemoveFromHierarchy();
            cards.Clear();
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

                // The card rides on the body's own place; only its height follows the head, eased, so a head that
                // sways or bobs does not shake the name about.
                Transform body = player.transform;
                float scale = body.lossyScale.y;
                float crown = card.Nova != null ? card.Nova.Crown.y - body.position.y : FallbackCrown * scale;
                float target = crown + (card.Nova != null ? card.Nova.NovaHeight : 0f) + GapAboveHead * scale;
                card.Height = float.IsNaN(card.Height) ? target : Mathf.Lerp(card.Height, target, follow);
                Vector3 world = body.position + Vector3.up * card.Height;

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
                card.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;

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
                if (cards.TryGetValue(gone, out Card card)) card.Root.RemoveFromHierarchy();
                cards.Remove(gone);
            }
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
                Root = new VisualElement { pickingMode = PickingMode.Ignore },
            };
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
            // A Crew is shown as <Name>: the text is never read as markup.
            var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        /// <summary>Writes what changed since the card was last shown. Called only for cards on screen.</summary>
        private static void Fill(Card card, FactionId faction, PlayerIdentity viewer)
        {
            string name = card.Identity != null ? card.Identity.CharacterName : card.Root.name;
            int level = card.Progression != null ? card.Progression.CurrentLevel : 1;
            string nameText = name + "   Lv " + level;
            if (nameText != card.NameText) card.Name.text = card.NameText = nameText;

            string standing = card.Nova != null ? card.Nova.WornStanding ?? string.Empty : string.Empty;
            if (standing != card.StandingText)
            {
                card.Standing.text = card.StandingText = standing;
                card.Standing.style.display = standing.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            CrewTag tag = card.PlayerCrew != null ? card.PlayerCrew.Tag : default;
            string crew = tag.InCrew ? "<" + tag.crew + ">" : string.Empty;
            if (crew != card.CrewText)
            {
                card.Crew.text = card.CrewText = crew;
                card.Crew.style.display = crew.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // A Callout opponent reads as an enemy for as long as the duel lasts.
            bool duelling = viewer != null && card.Identity != null && card.Identity != viewer && card.Identity.IsDuelling(viewer);
            if (faction == card.Faction && duelling == card.Duelling) return;
            card.Faction = faction;
            card.Duelling = duelling;
            FactionDefinition definition = card.Identity != null && card.Identity.Database != null && card.Identity.Database.Factions != null
                ? card.Identity.Database.Factions.Get(faction) : null;
            card.Name.style.color = duelling ? DuelColor : definition != null ? Opaque(definition.accentColor) : NoFactionColor;
            bool hasCrest = definition != null && definition.crest != null;
            card.Crest.style.display = hasCrest ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasCrest) card.Crest.style.backgroundImage = new StyleBackground(definition.crest);
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);
    }
}
