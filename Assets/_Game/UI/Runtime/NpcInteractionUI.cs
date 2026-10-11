using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.NPC;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Client-only town UI: NPC nameplates projected from world space, the NPC dialogue panel, and a short
    /// message toast (e.g. "the portal is sealed"). One UIDocument; no per-NPC canvases.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class NpcInteractionUI : MonoBehaviour, INpcDialogueSink, IGameMessageSink, IPointerBlockQuery
    {
        private const string BlockingClass = "blocks-world";
        private const float CloseDistancePadding = 3f;
        private const float MessageSeconds = 4f;
        private const float MessageFadeSeconds = 0.6f;

        [Tooltip("Nameplates further than this from the camera are hidden.")]
        [SerializeField, Min(5f)] private float nameplateDistance = 38f;

        private readonly Dictionary<NpcInteractable, VisualElement> plates = new Dictionary<NpcInteractable, VisualElement>();
        private readonly List<NpcInteractable> staleNpcs = new List<NpcInteractable>();

        private VisualElement root;
        private VisualElement plateLayer;
        private VisualElement dialogue;
        private Label dialogueName;
        private Label dialogueTitle;
        private Label dialogueGreeting;
        private Label dialogueServices;
        private Button closeButton;
        private VisualElement actions;
        private Button comingSoon;
        private Label toast;
        private Camera targetCamera;
        private INpcDialogueSource currentNpc;
        private Transform currentPlayer;
        private float toastHideTime = -1f;
        private bool toastPending;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            SyntySkin.Decorate(root);
            plateLayer = root.Q("nameplates");
            dialogue = root.Q("npc-dialogue");
            dialogueName = root.Q<Label>("npc-name");
            dialogueTitle = root.Q<Label>("npc-title");
            dialogueGreeting = root.Q<Label>("npc-greeting");
            dialogueServices = root.Q<Label>("npc-services");
            closeButton = root.Q<Button>("npc-close");
            actions = root.Q("npc-actions");
            comingSoon = root.Q<Button>("npc-shop");
            toast = root.Q<Label>("zone-toast");
            if (closeButton != null) closeButton.clicked += HideDialogue;
            SetVisible(dialogue, false);
            SetVisible(toast, false);

            ClientPresentationRegistry.RegisterDialogue(this);
            ClientPresentationRegistry.RegisterMessages(this);
            ClientPresentationRegistry.RegisterPointerBlock(this);
        }

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterDialogue(this);
            ClientPresentationRegistry.UnregisterMessages(this);
            ClientPresentationRegistry.UnregisterPointerBlock(this);
            if (closeButton != null) closeButton.clicked -= HideDialogue;
            foreach (var plate in plates.Values) plate.RemoveFromHierarchy();
            plates.Clear();
        }

        private void LateUpdate()
        {
            if (root == null || root.panel == null) return;
            if (targetCamera == null) targetCamera = Camera.main;

            UpdateDialogue();
            UpdateToast();
            if (plateLayer != null)
            {
                bool showPlates = HudPreferences.ShowNameplates;
                plateLayer.style.display = showPlates ? DisplayStyle.Flex : DisplayStyle.None;
                if (showPlates && targetCamera != null) UpdateNameplates();
            }
        }

        // ------------------------------------------------------------------ dialogue

        public void ShowDialogue(INpcDialogueSource npc, Transform player)
        {
            if (dialogue == null || npc == null) return;
            if (currentNpc != null && !ReferenceEquals(currentNpc, npc)) EndCurrentNpc();
            currentNpc = npc;
            currentPlayer = player;
            dialogueName.text = npc.DisplayName;
            dialogueTitle.text = npc.Title;
            dialogueGreeting.text = string.IsNullOrEmpty(npc.Greeting) ? "…" : $"“{npc.Greeting}”";
            dialogueServices.text = npc.Services;
            BuildServiceButtons(npc);
            SetVisible(dialogue, true);
        }

        /// <summary>One button per working service this NPC offers; the "coming soon" button stands in when there is none.</summary>
        private void BuildServiceButtons(INpcDialogueSource npc)
        {
            if (actions == null) return;
            for (int i = actions.childCount - 1; i >= 0; i--)
                if (actions[i] != comingSoon) actions.RemoveAt(i);

            NpcOffer offers = npc is NpcInteractable interactable ? interactable.Offers : NpcOffer.None;
            int shown = 0;
            foreach (NpcServices.Entry entry in NpcServices.All)
            {
                if ((offers & entry.Offer) == 0) continue;
                string service = entry.Id;
                var from = npc as NpcInteractable;
                var button = new Button(() => { HideDialogue(); NpcServices.Open(service, from); }) { text = entry.Label };
                button.AddToClassList("npc-action");
                button.AddToClassList("kn-button-primary");
                actions.Add(button);
                shown++;
            }
            SetVisible(comingSoon, shown == 0);
        }

        public void HideDialogue()
        {
            EndCurrentNpc();
            currentNpc = null;
            currentPlayer = null;
            SetVisible(dialogue, false);
        }

        private void EndCurrentNpc()
        {
            if (currentNpc is NpcInteractable npc && npc != null) npc.EndInteraction();
        }

        private void UpdateDialogue()
        {
            if (currentNpc == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { HideDialogue(); return; }
            if (currentPlayer == null || !currentPlayer.gameObject.activeInHierarchy) { HideDialogue(); return; }

            Vector3 offset = currentPlayer.position - currentNpc.Position;
            offset.y = 0f;
            float limit = currentNpc.InteractRange + CloseDistancePadding;
            if (offset.sqrMagnitude > limit * limit) HideDialogue();
        }

        // ------------------------------------------------------------------ toast

        public void ShowMessage(string message)
        {
            if (toast == null || string.IsNullOrEmpty(message)) return;
            toast.text = message;
            toast.style.opacity = 1f;
            SetVisible(toast, true);
            // The countdown starts on the next UI frame, so a hitch (or a server message during a load) can't eat it.
            toastPending = true;
        }

        private void UpdateToast()
        {
            if (toastPending)
            {
                toastPending = false;
                toastHideTime = Time.unscaledTime + MessageSeconds;
            }
            if (toastHideTime < 0f) return;
            float remaining = toastHideTime - Time.unscaledTime;
            if (remaining <= 0f)
            {
                SetVisible(toast, false);
                toastHideTime = -1f;
                return;
            }
            toast.style.opacity = Mathf.Clamp01(remaining / MessageFadeSeconds);
        }

        // ------------------------------------------------------------------ nameplates

        private void UpdateNameplates()
        {
            var npcs = NpcInteractable.Active;
            for (int i = 0; i < npcs.Count; i++)
                if (!plates.ContainsKey(npcs[i])) plates.Add(npcs[i], CreatePlate(npcs[i]));

            staleNpcs.Clear();
            Vector3 cameraPosition = targetCamera.transform.position;
            float maxDistanceSq = nameplateDistance * nameplateDistance;
            foreach (var pair in plates)
            {
                NpcInteractable npc = pair.Key;
                VisualElement plate = pair.Value;
                if (npc == null || !npc.isActiveAndEnabled) { staleNpcs.Add(npc); continue; }

                Vector3 world = npc.NameplatePosition;
                Vector3 screen = targetCamera.WorldToScreenPoint(world);
                bool visible = screen.z > 0f && (world - cameraPosition).sqrMagnitude <= maxDistanceSq;
                plate.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;

                Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                plate.style.left = panelPoint.x - plate.resolvedStyle.width * 0.5f;
                plate.style.top = panelPoint.y - plate.resolvedStyle.height;
                Hud.HudChrome.HideOver(plate, panelPoint);
            }

            for (int i = 0; i < staleNpcs.Count; i++)
            {
                if (plates.TryGetValue(staleNpcs[i], out var plate)) plate.RemoveFromHierarchy();
                plates.Remove(staleNpcs[i]);
            }
        }

        private VisualElement CreatePlate(NpcInteractable npc)
        {
            var plate = new VisualElement { pickingMode = PickingMode.Ignore };
            plate.AddToClassList("nameplate");
            var nameLabel = new Label(npc.DisplayName) { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("nameplate-name");
            plate.Add(nameLabel);
            if (!string.IsNullOrEmpty(npc.Title))
            {
                var titleLabel = new Label(npc.Title) { pickingMode = PickingMode.Ignore };
                titleLabel.AddToClassList("nameplate-title");
                plate.Add(titleLabel);
            }
            plateLayer.Add(plate);
            return plate;
        }

        // ------------------------------------------------------------------ pointer

        public bool BlocksWorldPointer(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled || root == null || root.panel == null) return false;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            for (VisualElement element = root.panel.Pick(panelPoint); element != null; element = element.parent)
                if (element.ClassListContains(BlockingClass)) return true;
            return false;
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
