using KryticalNova.EditorTools;
using KryticalNova.Factions;
using KryticalNova.UI.Hud;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;
using Slider = UnityEngine.UI.Slider;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The factions' HUD pieces (composed from Synty's HUD packs by FactionHudBuilder): that every faction has every
    /// piece, whole and the same size on screen, and that a piece shows what the HUD's own elements show.
    /// </summary>
    public sealed class FactionHudTests
    {
        private static readonly FactionId[] Factions = { FactionId.Hegemony, FactionId.Null, FactionId.Covenant };
        private const int Slots = 14;

        private static FactionHudKit Kit(FactionId faction)
        {
            var kit = AssetDatabase.LoadAssetAtPath<FactionHudKit>(FactionHudBuilder.KitPath(faction));
            Assert.IsNotNull(kit, $"{faction} has no HUD kit at {FactionHudBuilder.KitPath(faction)}. Run Krytical Nova ▸ UI ▸ Build Faction HUD.");
            return kit;
        }

        // ---------------------------------------------------------------- one parts list, three factions

        [Test]
        public void EveryFaction_HasEveryPiece()
        {
            foreach (FactionId faction in Factions)
            {
                FactionHudKit kit = Kit(faction);
                Assert.AreEqual(faction, kit.faction);
                int index = 0;
                foreach (HudChromePiece piece in kit.Pieces())
                {
                    Assert.IsNotNull(piece, $"{faction}: piece {index} of the kit is missing. No faction may lack a piece another has.");
                    index++;
                }
                Assert.Greater(index, 0);
            }
        }

        [Test]
        public void EveryHotbar_HasItsFourteenSlots_Containers_XpAndLevel()
        {
            foreach (FactionId faction in Factions)
            {
                HotbarChromeView hotbar = Kit(faction).hotbar;
                Assert.AreEqual(Slots, hotbar.SlotCount, $"{faction}: slots 1 to 0 and Q W E R");
                var so = new SerializedObject(hotbar);
                SerializedProperty slots = so.FindProperty("slots");
                for (int i = 0; i < Slots; i++)
                {
                    SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                    foreach (string part in new[] { "area", "icon", "cooldown", "cooldownFill", "count", "key" })
                        Assert.IsNotNull(slot.FindPropertyRelative(part).objectReferenceValue, $"{faction}: slot {i} has no {part}");
                }
                foreach (string part in new[] { "health", "flux", "experience", "experienceLabel", "level" })
                    Assert.IsNotNull(so.FindProperty(part).objectReferenceValue, $"{faction}: the Hotbar has no {part}");
            }
        }

        [Test]
        public void EveryHotbar_IsAsWideOnScreenAsTheOthers_WithItsMainBarLeftOfItsMiddle()
        {
            float first = -1f;
            foreach (FactionId faction in Factions)
            {
                FactionHudKit kit = Kit(faction);
                float width = ((RectTransform)kit.hotbar.transform).rect.width * kit.unitsToPanel;
                if (first < 0f) first = width;
                Assert.AreEqual(first, width, 1f, $"{faction}: Hotbar width in panel pixels");
                Assert.Less(width, 1280f * 0.6f, "the Hotbar leaves the corners of the screen to the other panels");
                // Q W E R hang off the right end, so the middle of slots 1 to 0 is left of the piece's middle.
                float centre = new SerializedObject(kit.hotbar).FindProperty("mainCentre").floatValue;
                Assert.That(centre, Is.InRange(0.3f, 0.5f), $"{faction}: where the main bar's middle is across the piece");
            }
        }

        [Test]
        public void ThePieces_AreOnlyLookedAt()
        {
            foreach (FactionId faction in Factions)
            {
                HotbarChromeView hotbar = Kit(faction).hotbar;
                foreach (Graphic graphic in hotbar.GetComponentsInChildren<Graphic>(true))
                    Assert.IsFalse(graphic.raycastTarget, $"{faction}: {graphic.name} would take clicks meant for the HUD's own elements");
                foreach (Animator animator in hotbar.GetComponentsInChildren<Animator>(true))
                    Assert.IsFalse(animator.enabled, $"{faction}: the animator on {animator.name} would fight what the game shows");
            }
        }

        // ---------------------------------------------------------------- a piece shows what the elements show

        [Test]
        public void TheHotbar_ShowsWhatItsElementsShow()
        {
            foreach (FactionId faction in Factions)
            {
                var canvas = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
                try
                {
                    HotbarChromeView view = Object.Instantiate(Kit(faction).hotbar, canvas.transform, false);
                    VisualElement root = HudLike(out VisualElement[] slots, out ProgressBar health, out ProgressBar flux, out ProgressBar experience, out Label level);

                    var art = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
                    slots[0].Q(className: "slot-icon").style.backgroundImage = new StyleBackground(art);
                    slots[0].AddToClassList("on-cooldown");
                    slots[0].Q(className: "slot-cooldown").style.height = Length.Percent(40f);
                    slots[0].Q<Label>(className: "slot-rank").text = "3.2s";
                    slots[1].AddToClassList("selected");
                    slots[1].Q<Label>(className: "slot-rank").text = "RANK 4";
                    slots[2].AddToClassList("insufficient-chi");
                    slots[3].Q<Label>(className: "slot-rank").text = "x7";
                    slots[4].AddToClassList("drop-target");
                    slots[5].AddToClassList("on-cooldown"); // an item that has run out: no sweep, greyed
                    slots[5].Q<Label>(className: "slot-rank").text = "x0";
                    slots[10].Q<Label>(className: "slot-key").text = "Q";
                    health.value = 50f;
                    flux.value = 25f;
                    experience.value = 80f;
                    experience.title = "800 / 1000 XP";
                    level.text = "LEVEL 37";

                    Assert.IsTrue(view.Attach(root, 0.25f), $"{faction}: the Hotbar found no place in the HUD");
                    view.Tick();

                    var so = new SerializedObject(view);
                    SerializedProperty parts = so.FindProperty("slots");
                    T Part<T>(int slot, string name) where T : Object => parts.GetArrayElementAtIndex(slot).FindPropertyRelative(name).objectReferenceValue as T;

                    Assert.AreSame(art, Part<Image>(0, "icon").sprite, $"{faction}: the slot's picture");
                    Assert.IsTrue(Part<Image>(0, "icon").enabled);
                    Assert.IsFalse(Part<Image>(1, "icon").enabled, $"{faction}: an empty slot shows no picture");
                    Assert.IsTrue(Part<GameObject>(0, "cooldown").activeSelf, $"{faction}: the cooldown sweep");
                    Assert.AreEqual(0.4f, Part<Image>(0, "cooldownFill").fillAmount, 0.001f);
                    Assert.AreEqual("3.2s", Part<TMP_Text>(0, "count").text);
                    Assert.IsFalse(Part<GameObject>(1, "cooldown").activeSelf);
                    Assert.AreEqual(string.Empty, Part<TMP_Text>(1, "count").text, $"{faction}: a rank is not worth saying on the slot");
                    Assert.AreEqual("x7", Part<TMP_Text>(3, "count").text, $"{faction}: how many of an item are left");
                    Assert.AreEqual("Q", Part<TMP_Text>(10, "key").text);
                    Shown(Part<GameObject>(1, "selected"), true, $"{faction}: selected");
                    Shown(Part<GameObject>(0, "selected"), false, $"{faction}: not selected");
                    Shown(Part<GameObject>(2, "disabled"), true, $"{faction}: not enough Flux");
                    Shown(Part<GameObject>(5, "disabled"), true, $"{faction}: an item that has run out");
                    Shown(Part<GameObject>(4, "highlighted"), true, $"{faction}: a drop would land here");
                    Assert.IsFalse(Part<GameObject>(5, "cooldown").activeSelf, $"{faction}: nothing to sweep on an item that has run out");

                    Assert.AreEqual(0.5f, ((Slider)so.FindProperty("health").objectReferenceValue).normalizedValue, 0.001f, $"{faction}: HP");
                    Assert.AreEqual(0.25f, ((Slider)so.FindProperty("flux").objectReferenceValue).normalizedValue, 0.001f, $"{faction}: Flux");
                    Assert.AreEqual(0.8f, ((Slider)so.FindProperty("experience").objectReferenceValue).normalizedValue, 0.001f, $"{faction}: XP");
                    Assert.AreEqual("800 / 1000 XP", ((TMP_Text)so.FindProperty("experienceLabel").objectReferenceValue).text);
                    Assert.AreEqual("37", ((TMP_Text)so.FindProperty("level").objectReferenceValue).text, $"{faction}: the badge says the number only");

                    // Leaving gives the elements back as they were.
                    view.Detach();
                    Assert.AreEqual(StyleKeyword.Null, slots[0].style.position.keyword);
                    Assert.AreEqual(StyleKeyword.Null, root.Q("hotbar").style.width.keyword);
                    Assert.AreEqual(StyleKeyword.Null, root.Q("hotbar-dock").style.translate.keyword);
                    Object.DestroyImmediate(art);
                }
                finally
                {
                    Object.DestroyImmediate(canvas);
                }
            }
        }

        // Packs differ in which states a slot can show: a part that exists must be right.
        private static void Shown(GameObject part, bool expected, string what)
        {
            if (part != null) Assert.AreEqual(expected, part.activeSelf, what);
        }

        /// <summary>The part of the HUD's tree the Hotbar reads, as ProgressionHUD builds it.</summary>
        private static VisualElement HudLike(out VisualElement[] slots, out ProgressBar health, out ProgressBar flux, out ProgressBar experience, out Label level)
        {
            var root = new VisualElement();
            var dock = new VisualElement { name = "hotbar-dock" };
            var hotbar = new VisualElement { name = "hotbar" };
            root.Add(dock);
            dock.Add(hotbar);
            slots = new VisualElement[Slots];
            for (int i = 0; i < Slots; i++)
            {
                var slot = new VisualElement { name = "slot-" + i };
                slot.AddToClassList("hotbar-slot");
                var icon = new VisualElement();
                icon.AddToClassList("slot-icon");
                var sweep = new VisualElement();
                sweep.AddToClassList("slot-cooldown");
                var key = new Label((i + 1).ToString());
                key.AddToClassList("slot-key");
                var rank = new Label("EMPTY");
                rank.AddToClassList("slot-rank");
                slot.Add(icon);
                slot.Add(sweep);
                slot.Add(key);
                slot.Add(rank);
                hotbar.Add(slot);
                slots[i] = slot;
            }
            health = new ProgressBar { name = "hp-bar", lowValue = 0f, highValue = 100f };
            flux = new ProgressBar { name = "chi-bar", lowValue = 0f, highValue = 100f };
            experience = new ProgressBar { name = "xp-bar", lowValue = 0f, highValue = 100f };
            level = new Label { name = "level-label" };
            root.Add(health);
            root.Add(flux);
            root.Add(experience);
            root.Add(level);
            return root;
        }
    }
}
