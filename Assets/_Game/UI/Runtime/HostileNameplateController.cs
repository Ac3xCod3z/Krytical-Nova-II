using System.Collections.Generic;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.UI.Hud;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The plate over a Hostile: its name and level in the colour of the level gap to the player looking at it,
    /// and its health. Two players of different levels see the same Hostile in different colours; the colours
    /// are <see cref="LevelGapSettings"/>, never written here. A plate shows while the Hostile is the player's
    /// target, was hurt in the last few seconds, or is close by. A Bruiser says so; a Hostile another player
    /// holds the kill of says that too. Looks only: nothing here is replicated or decided.
    /// While the HUD wears a faction's art the plate is that faction's own (<see cref="HostilePlateView"/>, on the
    /// HUD pieces' canvas); with no faction art it is the plain plate drawn here.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HostileNameplateController : MonoBehaviour
    {
        private const string LayerName = "player-namecards";
        private const float GapAboveHead = 0.22f;
        private static readonly Color HealthColor = new Color(0.86f, 0.2f, 0.22f);
        private static readonly Color HealthBack = new Color(0.03f, 0.05f, 0.07f, 0.85f);
        private static readonly Color TakenColor = new Color(0.57f, 0.64f, 0.7f);

        [Tooltip("A plate shows for a Hostile within this many metres of the player.")]
        [SerializeField, Min(1f)] private float nearDistance = 15f;
        [Tooltip("A plate stays this many seconds after the Hostile was last hurt.")]
        [SerializeField, Min(0f)] private float hurtSeconds = 5f;
        [Tooltip("Plates further than this from the camera are hidden, whatever else is true.")]
        [SerializeField, Min(5f)] private float maxDistance = 45f;

        private sealed class Plate
        {
            public HostilePlateView View; // the faction's plate; the elements below are the plain one's
            public VisualElement Root, Fill;
            public Label Tier, Name, Taken;
            public HostileNet Hostile;
            public float LastHealth = -1f, HurtAt = float.NegativeInfinity;
            public int Gap = int.MinValue, Level = -1;
            public bool TakenShown, TierShown;
            public float Share = -1f;
        }

        private readonly Dictionary<HostileNet, Plate> plates = new Dictionary<HostileNet, Plate>();
        private readonly List<HostileNet> stale = new List<HostileNet>();
        private VisualElement root, layer;
        private Camera targetCamera;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            layer = root?.Q(LayerName);
            if (layer == null) Debug.LogError("[Hostile nameplates] ProgressionHUD.uxml has no '" + LayerName + "' element.", this);
            HudChrome.KitChanged += DropPlates;
        }

        private void OnDisable()
        {
            HudChrome.KitChanged -= DropPlates;
            DropPlates();
        }

        /// <summary>Every plate goes: the next frame makes them again, in whatever art the HUD wears now.</summary>
        private void DropPlates()
        {
            foreach (Plate plate in plates.Values) Remove(plate);
            plates.Clear();
        }

        private static void Remove(Plate plate)
        {
            if (plate.View != null) Destroy(plate.View.gameObject);
            plate.Root?.RemoveFromHierarchy();
        }

        private static void SetShown(Plate plate, bool shown)
        {
            if (plate.View != null) plate.View.Show(shown);
            else plate.Root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void LateUpdate()
        {
            if (layer == null || root.panel == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            PlayerInventory localInventory = PlayerInventory.Local;
            if (localInventory == null) { HideAll(); return; }
            Transform viewer = localInventory.transform;
            var progression = localInventory.GetComponent<PlayerCharacterProgression>();
            int viewerLevel = progression != null ? progression.CurrentLevel : 1;
            NetworkObject viewerObject = localInventory.NetworkObject;
            Transform target = localInventory.TryGetComponent(out PointAndClickPlayer pointer) ? pointer.SelectedTarget : null;
            bool show = HudPreferences.ShowNameplates;
            Vector3 cameraPosition = targetCamera.transform.position;
            float now = Time.time;
            // Screen pixels in a panel pixel: a faction's plate is sized like the rest of the HUD.
            Vector2 panelOrigin = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
            float panelAcross = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.width, 0f)).x - panelOrigin.x;
            float panelScale = panelAcross > 1f ? Screen.width / panelAcross : 1f;

            IReadOnlyList<HostileNet> hostiles = HostileNet.Active;
            for (int i = 0; i < hostiles.Count; i++)
            {
                HostileNet hostile = hostiles[i];
                CombatEntity entity = hostile.Entity;
                plates.TryGetValue(hostile, out Plate plate);

                // Being hurt is remembered even while the plate is hidden, so it shows as soon as it may.
                float health = entity.CurrentHealth;
                if (plate != null)
                {
                    if (plate.LastHealth >= 0f && health < plate.LastHealth) plate.HurtAt = now;
                    plate.LastHealth = health;
                }

                Vector3 body = hostile.transform.position;
                bool wanted = show && health > 0f
                    && (body - cameraPosition).sqrMagnitude <= maxDistance * maxDistance
                    && ((target != null && target == hostile.transform)
                        || (plate != null && now - plate.HurtAt <= hurtSeconds)
                        || (body - viewer.position).sqrMagnitude <= nearDistance * nearDistance);
                if (plate == null)
                {
                    if (!wanted && health >= entity.MaxHealth) continue; // nothing to show and nothing to remember yet
                    plate = Create(hostile);
                    plate.LastHealth = health;
                    if (health < entity.MaxHealth) plate.HurtAt = now;
                    plates.Add(hostile, plate);
                }

                Vector3 screen = default;
                if (wanted)
                {
                    HostileDefinition definition = hostile.Definition;
                    float height = definition != null ? definition.bodySize.y * definition.scale : 1.8f;
                    screen = targetCamera.WorldToScreenPoint(body + Vector3.up * (height + GapAboveHead));
                    wanted = screen.z > 0f;
                }
                SetShown(plate, wanted);
                if (!wanted) continue;

                Fill(plate, viewerLevel, viewerObject, health, entity.MaxHealth);
                if (plate.View != null)
                {
                    plate.View.Place(new Vector2(screen.x, screen.y), panelScale);
                    continue;
                }
                Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                plate.Root.style.left = point.x - plate.Root.resolvedStyle.width * 0.5f;
                plate.Root.style.top = point.y - plate.Root.resolvedStyle.height;
            }

            // A Hostile that went back to the pool keeps no plate: the body may come back as another level.
            stale.Clear();
            foreach (var pair in plates)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled) stale.Add(pair.Key);
            foreach (HostileNet gone in stale)
            {
                if (plates.TryGetValue(gone, out Plate plate)) Remove(plate);
                plates.Remove(gone);
            }
        }

        private void HideAll()
        {
            foreach (Plate plate in plates.Values) SetShown(plate, false);
        }

        private Plate Create(HostileNet hostile)
        {
            // The faction's own plate while the HUD wears a faction's art.
            HostilePlateView art = HudChrome.ShownKit != null ? HudChrome.ShownKit.hostilePlate : null;
            if (art != null && HudChrome.WorldLayer != null)
            {
                HostilePlateView view = Instantiate(art, HudChrome.WorldLayer, false);
                view.name = art.name;
                view.Show(false);
                return new Plate { Hostile = hostile, View = view };
            }

            var plate = new Plate { Hostile = hostile, Root = new VisualElement { pickingMode = PickingMode.Ignore } };
            plate.Root.AddToClassList("namecard");

            plate.Tier = Line("namecard-standing", plate.Root);
            plate.Tier.text = "BRUISER";
            plate.Tier.style.display = DisplayStyle.None;
            plate.Name = Line("namecard-name", plate.Root);

            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.width = 96f;
            bar.style.height = 6f;
            bar.style.marginTop = 2f;
            bar.style.backgroundColor = HealthBack;
            bar.style.borderTopWidth = bar.style.borderBottomWidth = bar.style.borderLeftWidth = bar.style.borderRightWidth = 1f;
            bar.style.borderTopColor = bar.style.borderBottomColor = bar.style.borderLeftColor = bar.style.borderRightColor = Color.black;
            plate.Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            plate.Fill.style.height = Length.Percent(100f);
            plate.Fill.style.backgroundColor = HealthColor; // one colour at every gap: only the name tells the gap
            bar.Add(plate.Fill);
            plate.Root.Add(bar);

            plate.Taken = Line("namecard-crew", plate.Root);
            plate.Taken.text = "TAKEN";
            plate.Taken.style.color = TakenColor;
            plate.Taken.style.display = DisplayStyle.None;

            plate.Root.style.display = DisplayStyle.None;
            layer.Add(plate.Root);
            return plate;
        }

        private static Label Line(string className, VisualElement parent)
        {
            var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        /// <summary>Writes what changed since the plate was last shown. Called only for plates on screen.</summary>
        private static void Fill(Plate plate, int viewerLevel, NetworkObject viewer, float health, float maxHealth)
        {
            HostileNet hostile = plate.Hostile;
            int level = hostile.Level;
            int gap = LevelGapRules.Gap(level, viewerLevel);
            HostilePlateView view = plate.View;
            if (level != plate.Level)
            {
                plate.Level = level;
                string name = hostile.Definition != null ? hostile.Definition.displayName : hostile.Entity.EntityName;
                // The faction's plate has a badge for the level; the plain one writes it after the name.
                if (view != null) view.Say(name, level);
                else plate.Name.text = name + "   Lv " + level;
            }
            if (gap != plate.Gap)
            {
                plate.Gap = gap;
                LevelGapSettings gaps = hostile.LevelGap;
                Color colour = gaps != null ? LevelGapRules.NameColor(gaps, gap) : Color.white;
                if (view != null) view.Colour(colour);
                else plate.Name.style.color = colour;
            }

            bool bruiser = hostile.Definition != null && hostile.Definition.tier == HostileTier.Bruiser;
            if (bruiser != plate.TierShown)
            {
                plate.TierShown = bruiser;
                if (view != null) view.Tier(bruiser);
                else plate.Tier.style.display = bruiser ? DisplayStyle.Flex : DisplayStyle.None;
            }

            float share = maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
            if (!Mathf.Approximately(share, plate.Share))
            {
                plate.Share = share;
                if (view != null) view.Health(share);
                else plate.Fill.style.width = Length.Percent(share * 100f);
            }

            bool taken = hostile.IsTaggedByAnother(viewer);
            if (taken != plate.TakenShown)
            {
                plate.TakenShown = taken;
                if (view != null) view.Taken(taken);
                else plate.Taken.style.display = taken ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
