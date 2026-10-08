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
        public void EveryHotbar_HasItsFourteenSlots_AndTheXpBar()
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
                foreach (string part in new[] { "experience", "experienceLabel" })
                    Assert.IsNotNull(so.FindProperty(part).objectReferenceValue, $"{faction}: the Hotbar has no {part}");
            }
        }

        [Test]
        public void EveryPlayerPlate_HasThePortrait_Level_HpAndFlux_WithTheirNumbers()
        {
            foreach (FactionId faction in Factions)
            {
                var so = new SerializedObject(Kit(faction).vitals);
                foreach (string part in new[] { "portrait", "health", "flux", "healthLabel", "fluxLabel", "level", "mode", "points" })
                    Assert.IsNotNull(so.FindProperty(part).objectReferenceValue, $"{faction}: the player's plate has no {part}");
                StringAssert.Contains("{0}", so.FindProperty("levelFormat").stringValue, $"{faction}: the level's wording has no place for the number");
            }
        }

        [Test]
        public void EveryPiece_IsAsWideOnScreenAsTheOtherFactions()
        {
            float plate = -1f, bar = -1f, glass = -1f;
            foreach (FactionId faction in Factions)
            {
                FactionHudKit kit = Kit(faction);
                if (plate < 0f) plate = kit.vitals.PanelWidth;
                if (bar < 0f) bar = kit.hotbar.MainBarPanelWidth;
                if (glass < 0f) glass = kit.minimap.StandPanelSize.x;
                // The dials' frames differ; the map inside them is one size, and round.
                Assert.AreEqual(glass, kit.minimap.StandPanelSize.x, 1f, $"{faction}: width of the minimap's glass in panel pixels");
                Assert.AreEqual(kit.minimap.StandPanelSize.x, kit.minimap.StandPanelSize.y, 0.5f, $"{faction}: the minimap's glass is round");
                Assert.Less(kit.minimap.StandPanelSize.x, kit.minimap.PanelWidth, $"{faction}: the dial's frame reaches out round its glass");
                Assert.AreEqual(plate, kit.vitals.PanelWidth, 1f, $"{faction}: width of the player's plate in panel pixels");
                // The Q W E R bars differ from pack to pack: it is the main bar, in the middle of the screen, that is the same.
                Assert.AreEqual(bar, kit.hotbar.MainBarPanelWidth, 1f, $"{faction}: width of the main bar in panel pixels");
            }
        }

        [Test]
        public void EveryHotbar_LeavesTheCornersFree_WithItsMainBarLeftOfItsMiddle()
        {
            foreach (FactionId faction in Factions)
            {
                FactionHudKit kit = Kit(faction);
                // The main bar is centred: with the chat's 16 + 390 pixels on its left it must end short of both.
                Assert.Less(kit.hotbar.MainBarPanelWidth, 1280f - 2f * (16f + 390f), "the main bar leaves the chat's corner free");
                Assert.Less(kit.hotbar.PanelWidth, 1280f * 0.6f, "the Hotbar leaves the corners of the screen to the other panels");
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
                foreach (HudChromePiece piece in Kit(faction).Pieces())
                {
                    foreach (Graphic graphic in piece.GetComponentsInChildren<Graphic>(true))
                        Assert.IsFalse(graphic.raycastTarget, $"{faction}: {graphic.name} would take clicks meant for the HUD's own elements");
                    foreach (Animator animator in piece.GetComponentsInChildren<Animator>(true))
                        Assert.IsFalse(animator.enabled, $"{faction}: the animator on {animator.name} would fight what the game shows");
                }
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
                    VisualElement root = HudLike(out VisualElement[] slots, out _, out _, out ProgressBar experience, out _);

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
                    experience.value = 80f;
                    experience.title = "800 / 1000 XP";

                    Assert.IsTrue(view.Attach(root), $"{faction}: the Hotbar found no place in the HUD");
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

                    Assert.AreEqual(0.8f, ((Slider)so.FindProperty("experience").objectReferenceValue).normalizedValue, 0.001f, $"{faction}: XP");
                    Assert.AreEqual("800 / 1000 XP", ((TMP_Text)so.FindProperty("experienceLabel").objectReferenceValue).text);

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

        [Test]
        public void ThePlayerPlate_ShowsWhatTheStatusPanelShows()
        {
            foreach (FactionId faction in Factions)
            {
                var canvas = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
                try
                {
                    VitalsChromeView view = Object.Instantiate(Kit(faction).vitals, canvas.transform, false);
                    VisualElement root = HudLike(out _, out ProgressBar health, out ProgressBar flux, out _, out Label level);
                    health.value = 50f;
                    health.title = "HP 2100 / 4200";
                    flux.value = 25f;
                    flux.title = "FLUX 480 / 1920";
                    level.text = "LEVEL 37";
                    root.Q<Label>("mode-label").text = "RUN · 9.5 m/s";
                    root.Q<Label>("points-summary").text = "3 stat points  |  1 skill point";

                    Assert.IsTrue(view.Attach(root), $"{faction}: the player's plate found no place in the HUD");
                    view.Tick();

                    var so = new SerializedObject(view);
                    Assert.AreEqual(0.5f, ((Slider)so.FindProperty("health").objectReferenceValue).normalizedValue, 0.001f, $"{faction}: HP");
                    Assert.AreEqual(0.25f, ((Slider)so.FindProperty("flux").objectReferenceValue).normalizedValue, 0.001f, $"{faction}: Flux");
                    Assert.AreEqual("2100 / 4200", ((TMP_Text)so.FindProperty("healthLabel").objectReferenceValue).text, $"{faction}: the bar is red; it says the numbers");
                    Assert.AreEqual("480 / 1920", ((TMP_Text)so.FindProperty("fluxLabel").objectReferenceValue).text);
                    StringAssert.EndsWith("37", ((TMP_Text)so.FindProperty("level").objectReferenceValue).text, $"{faction}: the level");
                    Assert.AreEqual("RUN · 9.5 m/s", ((TMP_Text)so.FindProperty("mode").objectReferenceValue).text);
                    Assert.AreEqual("3 stat points  |  1 skill point", ((TMP_Text)so.FindProperty("points").objectReferenceValue).text);
                    Assert.IsFalse(((RawImage)so.FindProperty("portrait").objectReferenceValue).enabled, $"{faction}: no picture is shown until there is a player to draw");

                    view.Detach();
                    Assert.AreEqual(StyleKeyword.Null, root.Q("summary").style.width.keyword);
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
            var summary = new VisualElement { name = "summary" };
            root.Add(summary);
            summary.Add(level);
            summary.Add(new Label { name = "mode-label" });
            summary.Add(health);
            summary.Add(flux);
            summary.Add(experience);
            summary.Add(new Label { name = "points-summary" });
            return root;
        }
    }
}
