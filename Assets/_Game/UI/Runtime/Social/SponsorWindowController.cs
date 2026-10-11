using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Sponsor / Recruit window: the player's Sponsor (if it has one), its Recruits (if it sponsors), whether
    /// each is online and near, the Sponsor points earned, and a button to end a link. A name typed here sends a
    /// request; so does "Sponsor Request" in a player's menu and "/sponsor name". No key of its own: it opens from
    /// "/sponsor" and from the button in the Contacts window. It only shows what <see cref="PlayerSponsor"/> was
    /// sent; the server decides.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class SponsorWindowController : MonoBehaviour
    {
        private const string WindowName = "sponsor-window";

        private VisualElement root, window;
        private ScrollView list;
        private Label pointsLabel, helpLabel;
        private TextField nameField;
        private PlayerSponsor sponsor;
        private bool initialized, open;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q(WindowName);
            if (window == null)
            {
                Debug.LogError("[Sponsor] SocialPanels.uxml has no sponsor window in this UI document.", this);
                return;
            }
            list = window.Q<ScrollView>("sponsor-list");
            pointsLabel = window.Q<Label>("sponsor-points");
            helpLabel = window.Q<Label>("sponsor-help");
            nameField = window.Q<TextField>("sponsor-name");
            HudTyping.Guard(nameField);
            nameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                evt.StopPropagation();
                SubmitName();
            }, TrickleDown.TrickleDown);
            window.Q<Button>("sponsor-request").clicked += SubmitName;
            window.Q<Button>("close-sponsor").clicked += () => SetOpen(false);
            Button fromContacts = root.Q<Button>("contacts-sponsor");
            if (fromContacts != null) fromContacts.clicked += () => SetOpen(true);
            SetDisplay(false);

            // No NPC opens it: the registry is how "/sponsor" and the GM tools ask for the window.
            NpcServices.Register(ServiceIds.Sponsor, NpcOffer.None, "SPONSOR", () => SetOpen(true));
            PlayerMenuController.Register("Sponsor Request", 66, target => sponsor != null && sponsor.IsSpawned && !Linked(target.Name), target =>
            {
                if (sponsor == null) return;
                if (target.InSight && target.Player.TryGetComponent(out PlayerSponsor theirs)) sponsor.Request(theirs);
                else sponsor.Request(target.Name);
            });
            PlayerInventory.LocalChanged += Rebind;
            initialized = true;
            Rebind();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            NpcServices.Unregister(ServiceIds.Sponsor);
            PlayerInventory.LocalChanged -= Rebind;
            Bind(null);
        }

        private void Rebind() => Bind(PlayerSponsor.Local);

        private void Bind(PlayerSponsor next)
        {
            if (sponsor != null) sponsor.Changed -= Refresh;
            sponsor = next;
            if (sponsor != null) sponsor.Changed += Refresh;
            if (initialized) Refresh();
        }

        private bool Linked(string characterName)
        {
            if (sponsor == null) return false;
            foreach (SponsorLinkView line in sponsor.Lines)
                if (string.Equals(line.name.ToString(), characterName, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && open;

        private void SetDisplay(bool shown)
        {
            open = shown;
            window.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", shown);
        }

        public void SetOpen(bool shown)
        {
            if (!initialized || shown == open) return;
            SetDisplay(shown);
            if (!shown) return;
            window.BringToFront();
            Refresh();
        }

        private void Update()
        {
            if (!open) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) SetOpen(false);
        }

        // ------------------------------------------------------------------ what is shown

        private void SubmitName()
        {
            string wanted = nameField.value != null ? nameField.value.Trim() : string.Empty;
            if (wanted.Length == 0 || sponsor == null) return;
            sponsor.Request(wanted);
            nameField.SetValueWithoutNotify(string.Empty);
        }

        private void Refresh()
        {
            if (!initialized || !open) return;
            list.Clear();
            if (sponsor == null || !sponsor.IsSpawned)
            {
                pointsLabel.text = string.Empty;
                helpLabel.text = "Sponsoring needs an online session.";
                return;
            }

            SponsorSettings settings = sponsor.Settings;
            bool sponsors = false, recruited = false;
            foreach (SponsorLinkView line in sponsor.Lines)
            {
                if (line.isSponsor) recruited = true; else sponsors = true;
                list.Add(Row(line));
            }
            pointsLabel.text = sponsors || sponsor.Points > 0 ? $"{sponsor.Points:N0} Sponsor points" : string.Empty;

            if (settings == null) { helpLabel.text = string.Empty; return; }
            int bonus = Mathf.RoundToInt(settings.FirstBonus * 100f), top = Mathf.RoundToInt(settings.TopBonus * 100f);
            string more = top > bonus ? $", up to +{top}% with their other Recruits in the Squad" : string.Empty;
            string squad = settings.addsToSquadBoost ? " In their Squad it adds to the Squad's boost." : string.Empty;
            if (recruited)
                helpLabel.text = sponsor.BoostOn
                    ? $"Your Sponsor is near: kills pay +{bonus}% XP{more}.{squad}"
                    : $"Hunt within {settings.rangeMetres:0} m of your Sponsor for +{bonus}% kill XP{more}.{squad} The link ends when you are within {settings.levelGap} levels of them.";
            else if (sponsors)
                helpLabel.text = $"You earn {settings.pointsPerMinute} Sponsor points for each minute spent within {settings.rangeMetres:0} m of a Recruit. You may Squad with your Recruits whatever their level; what you kill there pays XP to no one. A Recruit who closes to within {settings.levelGap} levels of you has caught up, and the link ends.";
            else
                helpLabel.text = $"A level {settings.sponsorMinLevel} character can sponsor up to {settings.maxRecruits} characters of its faction who are more than {settings.levelGap} levels below it. The Recruit earns +{bonus}% kill XP while the Sponsor is near{more}.";
        }

        private VisualElement Row(SponsorLinkView line)
        {
            var row = new VisualElement();
            row.AddToClassList("contact-row");
            row.AddToClassList(line.online ? "online" : "offline");
            var top = new VisualElement();
            top.AddToClassList("contact-top");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("contact-dot");
            top.Add(dot);
            top.Add(Text(line.name.ToString(), "contact-name"));
            top.Add(Text($"{(line.isSponsor ? "Your Sponsor" : "Recruit")} · level {line.level}", "contact-detail"));
            top.Add(Text(!line.online ? "offline" : line.near ? "near" : "online, out of reach", "contact-status"));
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.AddToClassList("social-spacer");
            top.Add(spacer);
            string id = line.characterId.ToString();
            var end = new Button(() => sponsor?.End(id)) { text = "END" };
            end.AddToClassList("contact-button");
            end.AddToClassList("kn-button-danger");
            top.Add(end);
            row.Add(top);
            return row;
        }

        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
    }
}
