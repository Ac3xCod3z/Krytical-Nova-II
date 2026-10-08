using KryticalNova.Player;
using System.Collections.Generic;
using KryticalNova.Items;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Items on the ground, as the HUD shows them: a name over each drop near the camera, in the colour of its
    /// rarity, and the pickup key (~) that takes the nearest one. Picking up is a request to
    /// <see cref="PlayerLoot"/>; the server decides. A drop still held for whoever dropped it shows the seconds
    /// left, and the local player's own drops get a notice that counts down to the moment anyone may take them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LootHudController : MonoBehaviour
    {
        private const string LayerClass = "loot-labels";
        private const string LabelClass = "loot-label";
        private const string HeldClass = "loot-label--held";
        private const string MineClass = "loot-label--mine";
        private const float EndingSeconds = 5f; // the notice turns urgent for the last seconds
        private static readonly string[] RarityTextClasses = { "rarity-text-0", "rarity-text-1", "rarity-text-2", "rarity-text-3", "rarity-text-4" };
        private const float LabelHeight = 0.75f;

        [Tooltip("Drops further than this from the camera show no name.")]
        [SerializeField, Min(5f)] private float labelDistance = 32f;

        private readonly Dictionary<WorldDrop, Label> labels = new Dictionary<WorldDrop, Label>();
        private readonly List<WorldDrop> stale = new List<WorldDrop>();

        private VisualElement root, layer, notice, noticeFill;
        private Label noticeText, noticeHelp;
        private Camera targetCamera;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            layer = root.Q(className: LayerClass);
            if (layer == null)
            {
                layer = new VisualElement { name = "loot-labels", pickingMode = PickingMode.Ignore };
                layer.AddToClassList(LayerClass);
                root.Insert(0, layer); // under every window
            }
            notice = root.Q("loot-notice");
            noticeText = root.Q<Label>("loot-notice-text");
            noticeHelp = root.Q<Label>("loot-notice-help");
            noticeFill = root.Q("loot-notice-fill");
            if (notice != null) notice.style.display = DisplayStyle.None;
        }

        private void OnDisable()
        {
            foreach (Label label in labels.Values) label.RemoveFromHierarchy();
            labels.Clear();
        }

        private void Update()
        {
            if (layer == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && KeyBindings.Pressed(KeyBindings.PickUp) && !HudTyping.IsTyping(root))
            {
                PlayerLoot loot = PlayerLoot.Local;
                if (loot != null) loot.RequestPickupNearest();
            }
        }

        private void LateUpdate()
        {
            if (layer == null || root.panel == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            Vector3 cameraPosition = targetCamera.transform.position;
            float maxSqr = labelDistance * labelDistance;
            IReadOnlyList<WorldDrop> drops = WorldDrop.Active;
            PlayerInventory local = PlayerInventory.Local;
            Unity.Netcode.NetworkObject localObject = local != null ? local.NetworkObject : null;
            WorldDrop soonest = null; // the local player's own drop that opens to others first
            int mineCount = 0;
            for (int i = 0; i < drops.Count; i++)
            {
                WorldDrop drop = drops[i];
                if (drop == null || drop.Stack.IsEmpty) continue;
                bool held = drop.IsHeld;
                bool mine = held && drop.IsHeldFor(localObject);
                if (mine)
                {
                    mineCount++;
                    if (soonest == null || drop.HeldSecondsLeft < soonest.HeldSecondsLeft) soonest = drop;
                }
                if (!labels.TryGetValue(drop, out Label label))
                {
                    label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                    label.AddToClassList(LabelClass);
                    layer.Add(label);
                    labels.Add(drop, label);
                }

                Vector3 world = drop.Position + Vector3.up * LabelHeight;
                Vector3 screen = targetCamera.WorldToScreenPoint(world);
                bool visible = screen.z > 0f && (world - cameraPosition).sqrMagnitude <= maxSqr;
                label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;

                string text = held ? $"{drop.DisplayName}  ·  {Mathf.CeilToInt(drop.HeldSecondsLeft)}s" : drop.DisplayName;
                if (label.text != text)
                {
                    label.text = text;
                    foreach (string c in RarityTextClasses) label.RemoveFromClassList(c);
                    label.AddToClassList(RarityTextClasses[Mathf.Clamp((int)drop.Rarity, 0, RarityTextClasses.Length - 1)]);
                }
                label.EnableInClassList(HeldClass, held && !mine);
                label.EnableInClassList(MineClass, mine);
                Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                label.style.left = point.x - label.resolvedStyle.width * 0.5f;
                label.style.top = point.y - label.resolvedStyle.height;
                Hud.HudChrome.HideOver(label, point);
            }

            ShowNotice(soonest, mineCount, local);

            stale.Clear();
            foreach (var pair in labels)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled) stale.Add(pair.Key);
            foreach (WorldDrop gone in stale)
            {
                if (labels.TryGetValue(gone, out Label label)) label.RemoveFromHierarchy();
                labels.Remove(gone);
            }
        }

        /// <summary>The countdown for what the local player dropped: the drop that opens to others first, and how many more wait.</summary>
        private void ShowNotice(WorldDrop drop, int count, PlayerInventory local)
        {
            if (notice == null) return;
            if (drop == null)
            {
                notice.style.display = DisplayStyle.None;
                return;
            }
            float left = drop.HeldSecondsLeft;
            PlayerLoot loot = local != null ? local.GetComponent<PlayerLoot>() : null;
            bool freeForAll = loot == null || loot.DropsOpenToEveryone;
            notice.style.display = DisplayStyle.Flex;
            notice.EnableInClassList("ending", left <= EndingSeconds);
            noticeText.text = $"{drop.DisplayName}  ·  yours alone for {Mathf.CeilToInt(left)}s";
            string then = freeForAll ? "Then free for all" : "Then open to your faction";
            noticeHelp.text = count > 1 ? $"{then}  ·  [{KeyBindings.Label(KeyBindings.PickUp)}] takes it back  ·  {count - 1} more dropped" : $"{then}  ·  [{KeyBindings.Label(KeyBindings.PickUp)}] takes it back";
            float total = Mathf.Max(0.01f, drop.HoldSeconds);
            noticeFill.style.width = Length.Percent(Mathf.Clamp01(left / total) * 100f);
        }
    }
}
