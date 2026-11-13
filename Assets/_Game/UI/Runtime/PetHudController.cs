using KryticalNova.Mounts;
using KryticalNova.Pets;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The banner that announces an evolution of the local player's pet. A pet's stage, growth, hunger and bonus are
    /// not on the HUD: they are in the pet's own tooltip (<see cref="PetTooltip"/>), so the Equipment window stays
    /// uncrowded. Everything shown comes from <see cref="PlayerPet"/>; this decides nothing. The banner uses the
    /// shared panel skin, so each player sees it in the look of their own faction.
    /// The same banner announces the evolutions of the player's selected mount (build 17 item 15.1), from
    /// <see cref="PlayerMounts.Evolved"/>, with the mount's three pips and its two stats at the new stage.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PetHudController : MonoBehaviour
    {
        private const float RebindInterval = 0.5f;
        private const float BannerSeconds = 4.5f;
        private const string ShownClass = "pet-evolved--shown";

        private VisualElement banner, bannerPips;
        private Label bannerTitle, bannerDetail;

        private PlayerPet bound;
        private PlayerMounts boundMounts;
        private float nextRebind;
        private float bannerEnds = -1f;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            banner = root?.Q("pet-evolved");
            if (banner == null)
            {
                Debug.LogError("[PetHud] PetPanels.uxml is not instanced in this UI document.", this);
                return;
            }
            bannerTitle = banner.Q<Label>("pet-evolved-title");
            bannerDetail = banner.Q<Label>("pet-evolved-detail");
            bannerPips = banner.Q("pet-evolved-pips");
            PetTooltip.BuildPips(bannerPips);
            banner.RemoveFromClassList(ShownClass);
            banner.style.display = DisplayStyle.None;
            // With the HUD's other moments, top centre or where the player put them.
            VisualElement column = root.Q(HeraldWindowController.MilestoneColumn);
            if (column != null && banner.parent != column) column.Add(banner);
        }

        private void OnDisable()
        {
            Bind(null);
            BindMounts(null);
        }

        private void Update()
        {
            if (banner == null) return;
            float now = Time.unscaledTime;
            if (bannerEnds >= 0f && now >= bannerEnds)
            {
                bannerEnds = -1f;
                banner.RemoveFromClassList(ShownClass);
                banner.schedule.Execute(() => { if (bannerEnds < 0f) banner.style.display = DisplayStyle.None; }).StartingIn(350);
            }
            // Polled: the local player object changes when a session starts.
            if (now < nextRebind) return;
            nextRebind = now + RebindInterval;
            Bind(PlayerPet.Local);
            BindMounts(PlayerMounts.Local);
        }

        private void BindMounts(PlayerMounts next)
        {
            if (boundMounts == next) return;
            if (boundMounts != null) boundMounts.Evolved -= OnMountEvolved;
            boundMounts = next;
            if (boundMounts != null) boundMounts.Evolved += OnMountEvolved;
        }

        private void Bind(PlayerPet next)
        {
            if (bound == next) return;
            if (bound != null) bound.Evolved -= OnEvolved;
            bound = next;
            if (bound != null) bound.Evolved += OnEvolved;
        }

        private void OnEvolved(PetDefinition pet, int stage)
        {
            if (banner == null || pet == null) return;
            int growth = PetRules.StageStart(stage);
            string bonus = StatDisplay.FormatModifier(pet.stat, ModifierType.Flat, PetRules.StatBonus(pet.maxValue, growth, true));
            ShowBanner(pet.displayName, $"{pet.StageLabel(stage)}  ·  {bonus}", PetRules.StageCount, stage);
        }

        private void OnMountEvolved(MountDefinition mount, int stage)
        {
            if (banner == null || mount == null) return;
            ShowBanner(mount.displayName, MountBannerDetail(mount, stage), MountRules.StageCount, stage);
        }

        /// <summary>The mount banner's line: "Stage name  ·  +25 Vitality  +25 Strength" (the stats at the stage's start, fed).</summary>
        public static string MountBannerDetail(MountDefinition mount, int stage)
        {
            int growth = MountRules.StageStart(stage);
            return $"{mount.StageName(stage)}  ·  {PlayerMounts.StatLine(mount.firstStat, mount.firstMax, growth, true)}  " +
                   PlayerMounts.StatLine(mount.secondStat, mount.secondMax, growth, true);
        }

        private void ShowBanner(string name, string detail, int pips, int stage)
        {
            bannerTitle.text = (name ?? string.Empty).ToUpperInvariant();
            bannerDetail.text = detail;
            if (bannerPips.childCount != pips) PetTooltip.BuildPips(bannerPips, pips);
            PetTooltip.SetPips(bannerPips, stage);
            banner.style.display = DisplayStyle.Flex;
            banner.parent?.BringToFront();
            // The class goes on a frame later so the transition has a state to start from.
            banner.schedule.Execute(() => banner.AddToClassList(ShownClass));
            bannerEnds = Time.unscaledTime + BannerSeconds;
        }
    }
}
