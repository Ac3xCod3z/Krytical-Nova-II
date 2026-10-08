using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.UI.Hud;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Composes each faction's uGUI HUD pieces out of that faction's Synty HUD pack and wires them for the game
    /// (<see cref="HudChrome"/>). The packs' prefabs are instanced inside ours and overridden there: nothing under
    /// Assets/Synty is changed. Every faction gets the same pieces with the same parts, so the HUD works the same in
    /// all three and only the art differs.
    ///
    /// The Hotbar: the pack's ARPG bar widened to ten slots (keys 1 to 0) with its XP bar and without its HP and Flux
    /// containers, and the pack's small item bar cut to four slots for Q W E R.
    /// The player's plate: the pack's portrait frame for a live picture of the character, the level, and the HP and
    /// Flux bars with their numbers.
    /// </summary>
    public static class FactionHudBuilder
    {
        public const string Folder = "Assets/_Game/UI/HUD/Faction";
        public const string KitFolder = "Assets/_Game/UI/Skins";
        private const int NumberSlots = 10, LetterSlots = 4;
        private const float LetterGap = 50f;
        /// <summary>The Q W E R bar's slots are drawn larger than the main bar's in every pack: brought down to match.</summary>
        private const float LetterScale = 0.72f;
        /// <summary>
        /// How wide every faction's main bar (slots 1 to 0 with its ends) is drawn, in panel pixels of the 1280 x 720
        /// interface. It sits in the middle of the screen, so this is what must fit between the chat and the right side.
        /// </summary>
        private const float MainBarPanelWidth = 380f;
        /// <summary>How wide the glass of every faction's minimap dial is drawn, in panel pixels: the size of the map.</summary>
        private const float MinimapGlassPanelWidth = 176f;
        /// <summary>How wide every faction's player plate is drawn, in panel pixels.</summary>
        private const float VitalsPanelWidth = 400f;
        /// <summary>The smallest lettering on a piece, in the packs' units: about 11 panel pixels on the plate.</summary>
        private const float SmallLine = 40f;
        private const float LineWidth = 520f;
        private static readonly Color CooldownShade = new Color(0.03f, 0.03f, 0.08f, 0.72f);

        private sealed class Spec
        {
            public FactionId Faction;
            public string Pack;          // folder under Assets/Synty
            public string Main;          // the bar that holds slots 1 to 0
            public string Letters;       // the small bar for Q W E R
            public string Experience;    // an XP bar of its own, when the main bar has none
            public Vector2 ExperienceAt; // where a separate XP bar sits, from the main bar's top centre
            public float ExperienceHeight; // how tall a separate XP bar is drawn (units)
            public bool SlotsOnTop;      // the bar's own art is drawn over the ends of its row: the slots and keys go over it
            public Action<Spec, RectTransform, Vitals> LayVitals; // puts the pack's parts of the player's plate in place
            public string Minimap;       // the minimap dial
            public float MinimapGlass;   // how much of the dial's map circle shows inside its frame (1 = all of it)
            public string[] MinimapOff;  // parts of the dial that would lie on the map, which is drawn over the dial
            public string Lettering;     // a label of the pack: its font is the plate's
            public Color Flux;           // the Flux bar's fill
        }

        /// <summary>What the player's plate is made of, found while its parts are put in place.</summary>
        private sealed class Vitals
        {
            public Image ExamplePortrait;   // the pack's sample picture: the live one takes its place
            public float PortraitScale = 1f;
            public Slider Health, Flux;
            public TMP_Text Level;
            public string LevelFormat = "{0}";
            public Vector2 ModeAt, PointsAt; // the two small lines, by their left end (units from the root's middle)
        }

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Faction = FactionId.Hegemony, Pack = "InterfaceSciFiSoldierHUD",
                Main = "ActionBar/HUD_SciFiSoldier_ARPGBar_01", Letters = "ActionBar/HUD_SciFiSoldier_HotBar_03",
                Experience = "Player_Health_Equipment/_Parts/HUD_SciFiSoldier_XPBar_01",
                ExperienceAt = new Vector2(0f, -24f), ExperienceHeight = 40f, SlotsOnTop = true,
                Minimap = "Minimap/HUD_SciFiSoldier_Minimap_Dial_01", MinimapGlass = 0.92f, MinimapOff = new[] { "ParticleFX_Glow" },
                LayVitals = LayHegemonyVitals, Lettering = "_CommonComponents/Label_SciFiSoldier_SemiBold", Flux = new Color(0.12f, 0.72f, 1f),
            },
            new Spec
            {
                Faction = FactionId.Null, Pack = "InterfaceDarkFantasyHUD",
                Main = "ActionBar/HUD_DarkFantasy_ARPGBar_02", Letters = "ActionBar/HUD_DarkFantasy_HotBar_01",
                Minimap = "Minimap/HUD_DarkFantasy_Minimap_Dial_02", MinimapGlass = 0.65f,
                LayVitals = LayNullVitals, Lettering = "_CommonComponents/Label_DarkFantasy_Bold", Flux = new Color(0.17f, 0.45f, 0.95f),
            },
            new Spec
            {
                Faction = FactionId.Covenant, Pack = "InterfaceFantasyWarriorHUD",
                Main = "ActionBar/HUD_FantasyWarrior_ARPGBar_05", Letters = "ActionBar/HUD_FantasyWarrior_HotBar_01",
                Minimap = "Minimap/HUD_FantasyWarrior_Minimap_Dial_04", MinimapGlass = 0.82f, MinimapOff = new[] { "SPR_Curlicue_Bottom" },
                LayVitals = LayCovenantVitals, Lettering = "_CommonComponents/Label_FantasyWarrior_SemiBold", Flux = new Color(0.19f, 0.73f, 0.95f),
            },
        };

        public static string HotbarPath(FactionId faction) => $"{Folder}/{faction}/HUD_{faction}_Hotbar.prefab";

        public static string VitalsPath(FactionId faction) => $"{Folder}/{faction}/HUD_{faction}_Vitals.prefab";

        public static string MinimapPath(FactionId faction) => $"{Folder}/{faction}/HUD_{faction}_Minimap.prefab";

        public static string KitPath(FactionId faction) => $"{KitFolder}/FactionHudKit_{faction}.asset";

        [MenuItem("Krytical Nova/UI/Build Faction HUD")]
        public static void Build()
        {
            foreach (Spec spec in Specs)
            {
                HotbarChromeView hotbar = BuildHotbar(spec);
                VitalsChromeView vitals = BuildVitals(spec);
                MinimapChromeView minimap = BuildMinimap(spec);
                EnsureKit(spec.Faction, hotbar, vitals, minimap);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[HUD] Faction HUD pieces composed in {Folder}.");
        }

        // ------------------------------------------------------------------ one piece

        /// <summary>
        /// Makes one piece: <paramref name="lay"/> puts the packs' parts under an empty root and fills in the view;
        /// the root then takes the size of what is drawn in it, centred on it, the packs' own behaviour is switched
        /// off, and the piece is saved as a prefab. <paramref name="lay"/> returns the width (in the packs' units) of
        /// the part that will be drawn <paramref name="panelWidth"/> panel pixels wide, or 0 for the whole piece.
        /// </summary>
        private static T Compose<T>(string path, float panelWidth, Func<RectTransform, T, float> lay) where T : HudChromePiece
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                // A canvas to lay things out in: the packs are drawn for 3840 x 2160.
                var canvasGo = new GameObject("Compose", typeof(RectTransform), typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.sizeDelta = new Vector2(3840f, 2160f);

                var rootGo = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path), typeof(RectTransform));
                rootGo.layer = LayerMask.NameToLayer("UI");
                var root = (RectTransform)rootGo.transform;
                root.SetParent(canvasRect, false);
                var view = rootGo.AddComponent<T>();

                float measured = lay(root, view);
                Canvas.ForceUpdateCanvases();

                // The root takes the size of everything drawn in it, centred on it.
                Bounds all = Drawn(root, root);
                foreach (RectTransform child in root) child.anchoredPosition -= (Vector2)all.center;
                root.sizeDelta = all.size;
                Canvas.ForceUpdateCanvases();

                var so = new SerializedObject(view);
                // A part the piece stands on was measured before the root moved under it.
                SerializedProperty standOn = so.FindProperty("standOn");
                Rect stand = standOn.rectValue;
                if (stand.width > 0f) standOn.rectValue = new Rect(stand.position - (Vector2)all.center, stand.size);
                so.FindProperty("unitsToPanel").floatValue = panelWidth / Mathf.Max(1f, measured > 0f ? measured : all.size.x);
                so.ApplyModifiedPropertiesWithoutUndo();
                Finished(root, view);

                // Views only: nothing of the packs' own behaviour may fight what the game shows.
                foreach (Animator animator in rootGo.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (Selectable selectable in rootGo.GetComponentsInChildren<Selectable>(true))
                {
                    selectable.transition = Selectable.Transition.None;
                    selectable.interactable = true;
                    if (!(selectable is Slider)) selectable.enabled = false;
                }
                foreach (Graphic graphic in rootGo.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                root.SetParent(null, false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootGo, scene);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(rootGo, path);
                return saved != null ? saved.GetComponent<T>() : null;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>What can only be measured once the root has its final size.</summary>
        private static void Finished(RectTransform root, HudChromePiece view)
        {
            if (!(view is HotbarChromeView)) return;
            // The view puts the middle of the row of slots 1 to 0 on the screen's middle.
            var so = new SerializedObject(view);
            SerializedProperty slots = so.FindProperty("slots");
            var first = slots.GetArrayElementAtIndex(0).FindPropertyRelative("area").objectReferenceValue as RectTransform;
            var last = slots.GetArrayElementAtIndex(NumberSlots - 1).FindPropertyRelative("area").objectReferenceValue as RectTransform;
            if (first == null || last == null) return;
            Bounds firstSlot = RectTransformUtility.CalculateRelativeRectTransformBounds(root, first);
            Bounds lastSlot = RectTransformUtility.CalculateRelativeRectTransformBounds(root, last);
            so.FindProperty("mainCentre").floatValue = Mathf.Clamp01(((firstSlot.min.x + lastSlot.max.x) * 0.5f - root.rect.xMin) / Mathf.Max(1f, root.rect.width));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ the Hotbar

        private static HotbarChromeView BuildHotbar(Spec spec)
        {
            GameObject main = Load(spec, spec.Main), letters = Load(spec, spec.Letters);
            if (main == null || letters == null) return null;

            return Compose<HotbarChromeView>(HotbarPath(spec.Faction), MainBarPanelWidth, (root, view) =>
            {
                RectTransform mainBar = Place(main, root, "Main");
                RectTransform letterBar = Place(letters, root, "Letters");
                letterBar.localScale = new Vector3(LetterScale, LetterScale, 1f);
                List<RectTransform> numberSlots = SetSlots(mainBar, NumberSlots);
                List<RectTransform> letterSlots = SetSlots(letterBar, LetterSlots);
                // HP and Flux are on the player's plate: the bar's own containers are not shown.
                foreach (string holder in new[] { "Container_L", "Container_R", "Container_Left", "Container_Right" })
                {
                    Transform found = Find(mainBar, holder);
                    if (found != null) found.gameObject.SetActive(false);
                }
                Canvas.ForceUpdateCanvases();

                // The XP bar the main bar carries (some ship it switched off), or one of its own over the bar.
                Transform experience = Find(mainBar, "XPBar");
                if (experience != null) experience.gameObject.SetActive(true);
                else if (!string.IsNullOrEmpty(spec.Experience))
                {
                    GameObject bar = Load(spec, spec.Experience);
                    if (bar != null)
                    {
                        // As wide as the row of slots and centred on it, in the slots' own frame.
                        RectTransform placed = Place(bar, root, "XPBar");
                        Bounds first = RectTransformUtility.CalculateRelativeRectTransformBounds(root, numberSlots[0]);
                        Bounds last = RectTransformUtility.CalculateRelativeRectTransformBounds(root, numberSlots[numberSlots.Count - 1]);
                        placed.sizeDelta = new Vector2(last.max.x - first.min.x, spec.ExperienceHeight > 0f ? spec.ExperienceHeight : placed.sizeDelta.y);
                        Canvas.ForceUpdateCanvases();
                        Bounds onto = RectTransformUtility.CalculateRelativeRectTransformBounds(root, mainBar);
                        Bounds own = RectTransformUtility.CalculateRelativeRectTransformBounds(root, placed);
                        placed.anchoredPosition += new Vector2((first.min.x + last.max.x) * 0.5f - own.center.x + spec.ExperienceAt.x, onto.max.y - own.min.y + spec.ExperienceAt.y);
                        FrameLike(placed, numberSlots[0]);
                        experience = placed;
                    }
                }
                Canvas.ForceUpdateCanvases();

                if (spec.SlotsOnTop && numberSlots.Count > 0)
                {
                    var over = numberSlots[0].parent.gameObject.AddComponent<Canvas>();
                    over.overrideSorting = true;
                    over.sortingOrder = HudChrome.CanvasOrder + 1;
                }

                // The Q W E R bar to the right of the main bar's art, its slots level with the main slots.
                Bounds mainDrawn = Drawn(root, mainBar);
                Bounds firstLetter = RectTransformUtility.CalculateRelativeRectTransformBounds(root, letterSlots[0]);
                Bounds firstNumber = RectTransformUtility.CalculateRelativeRectTransformBounds(root, numberSlots[0]);
                letterBar.anchoredPosition += new Vector2(mainDrawn.max.x - firstLetter.min.x + LetterGap, firstNumber.center.y - firstLetter.center.y);
                Canvas.ForceUpdateCanvases();

                var so = new SerializedObject(view);
                SerializedProperty slots = so.FindProperty("slots");
                slots.arraySize = NumberSlots + LetterSlots;
                for (int i = 0; i < NumberSlots + LetterSlots; i++)
                    WireSlot(slots.GetArrayElementAtIndex(i), i < NumberSlots ? numberSlots[i] : letterSlots[i - NumberSlots]);
                so.FindProperty("experience").objectReferenceValue = experience != null ? experience.GetComponentInChildren<Slider>(true) : null;
                // One line of text on the XP bar: a bar that ships two shows the one on the fill.
                TMP_Text experienceLabel = experience != null ? Label(experience, "Label_XP", "Label_Number", "Label_PlayerXP") : null;
                if (experience != null)
                    foreach (TMP_Text other in experience.GetComponentsInChildren<TMP_Text>(true))
                        if (other != experienceLabel) other.gameObject.SetActive(false);
                so.FindProperty("experienceLabel").objectReferenceValue = experienceLabel;
                so.FindProperty("mainWidth").floatValue = mainDrawn.size.x;
                so.ApplyModifiedPropertiesWithoutUndo();
                return mainDrawn.size.x;
            });
        }

        // ------------------------------------------------------------------ the minimap's dial

        private static MinimapChromeView BuildMinimap(Spec spec)
        {
            GameObject dial = Load(spec, spec.Minimap);
            if (dial == null) return null;

            return Compose<MinimapChromeView>(MinimapPath(spec.Faction), MinimapGlassPanelWidth, (root, view) =>
            {
                RectTransform placed = Place(dial, root, "Dial");
                Canvas.ForceUpdateCanvases();
                // The pack's sample map and markers go: the game's map is drawn in their place, over the piece.
                Transform contents = Find(placed, "Minimap_Contents") ?? Find(placed, "HUD_Minimap_Contents");
                if (contents == null)
                {
                    Debug.LogError($"[HUD] {spec.Faction}: the minimap dial has no map circle to stand on.");
                    return 0f;
                }
                // Its own rectangle, not its children's: the sample map inside it is four times its size.
                var corners = new Vector3[4];
                ((RectTransform)contents).GetWorldCorners(corners);
                var circle = new Bounds(root.InverseTransformPoint(corners[0]), Vector3.zero);
                circle.Encapsulate(root.InverseTransformPoint(corners[2]));
                contents.gameObject.SetActive(false);
                if (spec.MinimapOff != null)
                    foreach (string part in spec.MinimapOff) Off(placed, part);
                float glass = Mathf.Min(circle.size.x, circle.size.y) * spec.MinimapGlass;
                var so = new SerializedObject(view);
                so.FindProperty("standOn").rectValue = new Rect(circle.center.x - glass * 0.5f, circle.center.y - glass * 0.5f, glass, glass);
                so.ApplyModifiedPropertiesWithoutUndo();
                return glass;
            });
        }

        // ------------------------------------------------------------------ the player's plate

        private static VitalsChromeView BuildVitals(Spec spec)
        {
            GameObject lettering = Load(spec, spec.Lettering);
            TMP_Text like = lettering != null ? lettering.GetComponentInChildren<TMP_Text>(true) : null;
            if (like == null) return null;

            return Compose<VitalsChromeView>(VitalsPath(spec.Faction), VitalsPanelWidth, (root, view) =>
            {
                var parts = new Vitals();
                spec.LayVitals(spec, root, parts);
                Canvas.ForceUpdateCanvases();
                if (parts.Health == null || parts.Flux == null || parts.Level == null || parts.ExamplePortrait == null)
                {
                    Debug.LogError($"[HUD] {spec.Faction}: the player's plate is missing a part (HP {parts.Health != null}, Flux {parts.Flux != null}, level {parts.Level != null}, portrait {parts.ExamplePortrait != null}).");
                    return 0f;
                }

                // Flux in the colour it has everywhere else.
                if (parts.Flux.fillRect != null && parts.Flux.fillRect.TryGetComponent(out Image fluxFill)) fluxFill.color = spec.Flux;

                // The live picture where the pack's sample picture was.
                parts.ExamplePortrait.enabled = false;
                var liveGo = new GameObject("Live", typeof(RectTransform));
                liveGo.layer = root.gameObject.layer;
                var liveRect = (RectTransform)liveGo.transform;
                var holder = (RectTransform)parts.ExamplePortrait.transform.parent;
                liveRect.SetParent(holder, false);
                liveRect.SetSiblingIndex(parts.ExamplePortrait.transform.GetSiblingIndex() + 1);
                liveRect.anchorMin = liveRect.anchorMax = liveRect.pivot = new Vector2(0.5f, 0.5f);
                float side = Mathf.Max(holder.rect.width, holder.rect.height) * parts.PortraitScale;
                liveRect.sizeDelta = new Vector2(side, side);
                var live = liveGo.AddComponent<RawImage>();
                live.enabled = false;

                TMP_Text healthLabel = OnBar(parts.Health, like);
                TMP_Text fluxLabel = OnBar(parts.Flux, like);
                TMP_Text mode = Line(root, "Label_Mode", like, parts.ModeAt, SmallLine);
                TMP_Text points = Line(root, "Label_Points", like, parts.PointsAt, SmallLine);
                parts.Level.textWrappingMode = TextWrappingModes.NoWrap;

                var so = new SerializedObject(view);
                so.FindProperty("portrait").objectReferenceValue = live;
                so.FindProperty("health").objectReferenceValue = parts.Health;
                so.FindProperty("flux").objectReferenceValue = parts.Flux;
                so.FindProperty("healthLabel").objectReferenceValue = healthLabel;
                so.FindProperty("fluxLabel").objectReferenceValue = fluxLabel;
                so.FindProperty("level").objectReferenceValue = parts.Level;
                so.FindProperty("levelFormat").stringValue = parts.LevelFormat;
                so.FindProperty("mode").objectReferenceValue = mode;
                so.FindProperty("points").objectReferenceValue = points;
                so.ApplyModifiedPropertiesWithoutUndo();
                return 0f;
            });
        }

        // Sci-Fi Soldier: a comms screen for the picture with the level on its name plate, and two of the pack's health bars.
        private static void LayHegemonyVitals(Spec spec, RectTransform root, Vitals parts)
        {
            RectTransform screen = Place(Load(spec, "Radio/HUD_SciFiSoldier_Comms_01"), root, "Portrait");
            GameObject bar = Load(spec, "Player_Health_Equipment/HUD_SciFiSoldier_HealthBar_02");
            RectTransform health = Place(bar, root, "Health");
            RectTransform flux = Place(bar, root, "Flux");
            // The screen is 330 x 400 round the root's middle: the bars beside its upper half.
            PutLeftAt(root, health, 195f, 80f);
            PutLeftAt(root, flux, 195f, -20f);
            Off(screen, "SPR_Static");

            parts.ExamplePortrait = ImageNamed(screen, "SPR_Portrait");
            parts.Health = health.GetComponentInChildren<Slider>(true);
            parts.Flux = flux.GetComponentInChildren<Slider>(true);
            parts.Level = Label(screen, "Label_Name");
            parts.LevelFormat = "LV {0}";
            parts.ModeAt = new Vector2(275f, -95f);
            parts.PointsAt = new Vector2(275f, -145f);
            Image fluxIcon = ImageNamed(flux, "ICON");
            Sprite bolt = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Synty/{spec.Pack}/Sprites/Icons_Map/ICON_SciFiSoldier_Map_Lightning_01.png");
            if (fluxIcon != null && bolt != null) fluxIcon.sprite = bolt;
        }

        // Dark Fantasy: the pack's own plate (picture in a diamond, a long bar and a short one), and its level badge under the picture.
        private static void LayNullVitals(Spec spec, RectTransform root, Vitals parts)
        {
            RectTransform plate = Place(Load(spec, "Player_Health_Equipment/HUD_DarkFantasy_HealthStats_05"), root, "Plate");
            RectTransform badge = Place(Load(spec, "ActionBar/_Parts/HUD_DarkFantasy_PlayerLevel_02"), root, "Level");
            badge.localScale = new Vector3(0.5f, 0.5f, 1f);
            badge.anchoredPosition = new Vector2(-500f, -190f);

            parts.ExamplePortrait = ImageNamed(plate, "SPR_Portrait");
            parts.PortraitScale = 1.15f;
            parts.Health = SliderUnder(plate, "Bar_HP");
            parts.Flux = SliderUnder(plate, "Bar_SP");
            parts.Level = Label(badge, "Label_PlayerLevel");
            parts.ModeAt = new Vector2(215f, -100f);
            parts.PointsAt = new Vector2(-250f, -165f);
            // The short bar's ring was the green of the pack's stamina: Flux blue, with the mark of the stat that feeds it.
            Transform ring = Find(Find(plate, "Bar_SP"), "IconBox");
            Image ringBack = ImageNamed(ring, "SPR_Background"), ringIcon = ImageNamed(ring, "ICON");
            if (ringBack != null) ringBack.color = new Color(0.03f, 0.12f, 0.3f);
            Sprite spirit = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Synty/{spec.Pack}/Sprites/Icons_Stats/ICON_DarkFantasy_Stat_Spirit_01_Underlay.png");
            if (ringIcon != null && spirit != null) ringIcon.sprite = spirit;
        }

        // Fantasy Warrior: the pack's hero portrait, its level gem on the portrait's corner, and two of its health bars.
        private static void LayCovenantVitals(Spec spec, RectTransform root, Vitals parts)
        {
            RectTransform picture = Place(Load(spec, "Portraits/HUD_FantasyWarrior_CharacterPortrait_05"), root, "Portrait");
            GameObject bar = Load(spec, "Player_Health_Equipment/HUD_FantasyWarrior_HealthBar_01");
            RectTransform health = Place(bar, root, "Health");
            RectTransform flux = Place(bar, root, "Flux");
            RectTransform gem = Place(Load(spec, "ActionBar/_Parts/HUD_FantasyWarrior_PlayerLevel_01"), root, "Level");
            gem.localScale = new Vector3(0.8f, 0.8f, 1f);
            // The portrait's pivot is its right middle: its corner goes under the gem, and the bars start at the gem.
            picture.anchoredPosition = new Vector2(-40f, 0f);
            gem.anchoredPosition = new Vector2(-20f, -120f);
            PutLeftAt(root, health, 40f, 70f);
            PutLeftAt(root, flux, 40f, -30f);
            Off(picture, "HitPoints");

            parts.ExamplePortrait = ImageNamed(picture, "SPR_Example_HeroPortrait");
            parts.PortraitScale = 1.05f;
            parts.Health = health.GetComponentInChildren<Slider>(true);
            parts.Flux = flux.GetComponentInChildren<Slider>(true);
            parts.Level = Label(gem, "Label_PlayerLevel");
            parts.ModeAt = new Vector2(150f, -105f);
            parts.PointsAt = new Vector2(150f, -155f);
            Image fluxIcon = ImageNamed(flux, "ICON");
            Sprite spirit = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Synty/{spec.Pack}/Sprites/Icons_Stats/ICON_FantasyWarrior_Stat_Spirit_01_Underlay.png");
            if (fluxIcon != null && spirit != null)
            {
                fluxIcon.sprite = spirit;
                fluxIcon.color = new Color(0.78f, 0.92f, 1f); // the heart's red tint is not Flux's
            }
        }

        /// <summary>Moves a part so that its rectangle's left edge and its middle height are at these places under the root.</summary>
        private static void PutLeftAt(RectTransform root, RectTransform part, float left, float middle)
        {
            Canvas.ForceUpdateCanvases();
            Bounds now = RectTransformUtility.CalculateRelativeRectTransformBounds(root, part);
            part.anchoredPosition += new Vector2(left - now.min.x, middle - now.center.y);
        }

        /// <summary>The numbers on a bar, in the pack's lettering: over the whole bar, in the middle.</summary>
        private static TMP_Text OnBar(Slider bar, TMP_Text like)
        {
            var rect = (RectTransform)bar.transform;
            TextMeshProUGUI text = NewLabel(rect.parent, "Label_Numbers", like, Mathf.Max(SmallLine, rect.rect.height * 0.6f), TextAlignmentOptions.Center);
            var own = text.rectTransform;
            own.anchorMin = rect.anchorMin;
            own.anchorMax = rect.anchorMax;
            own.pivot = rect.pivot;
            own.anchoredPosition = rect.anchoredPosition;
            own.sizeDelta = rect.sizeDelta;
            own.SetAsLastSibling();
            return text;
        }

        /// <summary>One small line of text, by its left end.</summary>
        private static TMP_Text Line(RectTransform root, string name, TMP_Text like, Vector2 at, float size)
        {
            TextMeshProUGUI text = NewLabel(root, name, like, size, TextAlignmentOptions.Left);
            var own = text.rectTransform;
            own.anchorMin = own.anchorMax = new Vector2(0.5f, 0.5f);
            own.pivot = new Vector2(0f, 0.5f);
            own.sizeDelta = new Vector2(LineWidth, size * 1.3f);
            own.anchoredPosition = at;
            return text;
        }

        private static TextMeshProUGUI NewLabel(Transform parent, string name, TMP_Text like, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = like.font;
            text.fontSharedMaterial = like.fontSharedMaterial;
            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = Color.white;
            text.outlineWidth = 0.22f;
            text.outlineColor = new Color32(0x10, 0x10, 0x1C, 0xFF);
            text.text = string.Empty;
            return text;
        }

        private static void Off(Transform root, string name)
        {
            Transform found = Find(root, name);
            if (found != null) found.gameObject.SetActive(false);
        }

        private static GameObject Load(Spec spec, string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            string path = $"Assets/Synty/{spec.Pack}/Prefabs/{relative}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogError($"[HUD] {spec.Faction}: no prefab at {path}. Nothing was put in its place.");
            return prefab;
        }

        private static RectTransform Place(GameObject prefab, RectTransform parent, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.SetActive(true);
            var rect = (RectTransform)instance.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>
        /// Gives a bar exactly <paramref name="wanted"/> slots: more copies of its own slot prefab, or some switched
        /// off, with the bar made that much wider or narrower.
        /// </summary>
        private static List<RectTransform> SetSlots(RectTransform bar, int wanted)
        {
            Transform row = Find(bar, "ActionBar") ?? Find(bar, "HotBar") ?? Find(bar, "Hotbar") ?? bar;
            var items = new List<RectTransform>();
            foreach (RectTransform child in row)
                if (child.gameObject.activeSelf && (child.name.StartsWith("Item") || child.name.StartsWith("Button") || child.name.StartsWith("Hotbar_Item") || child.name.StartsWith("HotBar_Item")))
                    items.Add(child);
            if (items.Count == 0)
            {
                Debug.LogError($"[HUD] No slots found under '{row.name}' of {bar.name}.");
                return items;
            }
            Canvas.ForceUpdateCanvases();
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            float pitch = items[0].rect.width + (layout != null ? layout.spacing : 0f);
            if (items.Count > 1)
                pitch = Mathf.Abs(row.InverseTransformPoint(items[1].position).x - row.InverseTransformPoint(items[0].position).x);
            float rowWidth = ((RectTransform)row).rect.width;

            int had = items.Count;
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(items[0].gameObject);
            for (int i = had; i < wanted; i++)
            {
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(source, row);
                copy.name = $"Item_{i:00}";
                copy.transform.SetSiblingIndex(items[items.Count - 1].GetSiblingIndex() + 1);
                var copyRect = (RectTransform)copy.transform;
                copyRect.sizeDelta = items[0].sizeDelta;
                copyRect.localScale = items[0].localScale;
                items.Add(copyRect);
            }
            for (int i = items.Count - 1; i >= wanted; i--)
            {
                items[i].gameObject.SetActive(false);
                items.RemoveAt(i);
            }
            float change = (wanted - had) * pitch;
            float barWidth = bar.rect.width;
            bar.sizeDelta += new Vector2(change, 0f);
            // Art that spans the bar at a fixed width (brackets, a drawn shadow) grows with it.
            foreach (RectTransform child in bar)
                if (child != row && Mathf.Approximately(child.anchorMin.x, child.anchorMax.x) && child.rect.width >= barWidth * 0.8f)
                    child.sizeDelta += new Vector2(change, 0f);
            Canvas.ForceUpdateCanvases();
            // A row that neither stretches with the bar nor fits its content is widened by hand.
            var rowRect = (RectTransform)row;
            if (row != bar && Mathf.Abs(rowRect.rect.width - (rowWidth + change)) > 1f && row.GetComponent<ContentSizeFitter>() == null)
                rowRect.sizeDelta += new Vector2(rowWidth + change - rowRect.rect.width, 0f);
            Canvas.ForceUpdateCanvases();
            return items;
        }

        private static void WireSlot(SerializedProperty slot, RectTransform item)
        {
            Transform keyHolder = Find(item, "Hotkey") ?? Find(item, "Input_Hotkey") ?? Find(item, "Input");
            TMP_Text key = keyHolder != null ? keyHolder.GetComponentInChildren<TMP_Text>(true) : null;
            // Some packs ship the keycap switched off (they show a gamepad button instead): the cap and its letter go on.
            if (key != null)
                for (Transform up = key.transform; up != null && up != item; up = up.parent) up.gameObject.SetActive(true);

            Image icon = null;
            foreach (Image image in item.GetComponentsInChildren<Image>(true))
            {
                if (keyHolder != null && image.transform.IsChildOf(keyHolder)) continue;
                if (image.name == "ICON" || image.name == "Icon") { icon = image; break; }
            }
            if (icon != null)
            {
                // The packs tint their one-colour glyphs; the game's icons are full pictures.
                icon.sprite = null;
                icon.enabled = false;
                icon.preserveAspect = true;
                icon.color = Color.white;
                icon.material = null;
                // A slot drawn for an item on a shelf shows only part of it, off to one side: a skill's picture sits in the middle, whole.
                var shelf = icon.transform.parent.GetComponent<RectMask2D>();
                if (shelf != null)
                {
                    shelf.enabled = false;
                    Image back = ImageNamed(item, "SPR_Background");
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    icon.rectTransform.anchoredPosition = Vector2.zero;
                    if (back != null) icon.rectTransform.sizeDelta = back.rectTransform.rect.size * 0.94f;
                }
                for (Transform up = icon.transform; up != null && up != item; up = up.parent) up.gameObject.SetActive(true);
            }

            Transform cooldown = Find(item, "Cooldown");
            Image fill = cooldown != null ? ImageNamed(cooldown, "SPR_Cooldown") : null;
            if (fill == null && icon != null)
            {
                // A slot made for items has no cooldown of its own: the same radial sweep, over its icon.
                var made = new GameObject("Cooldown", typeof(RectTransform));
                made.layer = item.gameObject.layer;
                var madeRect = (RectTransform)made.transform;
                madeRect.SetParent(icon.transform.parent, false);
                madeRect.SetSiblingIndex(icon.transform.GetSiblingIndex() + 1);
                Stretch(madeRect);
                fill = made.AddComponent<Image>();
                cooldown = made.transform;
            }
            if (fill != null)
            {
                // One sweep in every pack: the picture dimmed, not blacked out (some packs multiply theirs to black).
                fill.material = null;
                fill.color = CooldownShade;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = false;
                fill.gameObject.SetActive(true);
            }
            if (cooldown != null)
            {
                // The pack's own seconds label gives way to ours, which also counts items and shows a charge.
                foreach (TMP_Text label in cooldown.GetComponentsInChildren<TMP_Text>(true)) label.gameObject.SetActive(false);
                cooldown.gameObject.SetActive(false);
            }
            Transform flash = Find(item, "Flash");
            if (flash != null) flash.gameObject.SetActive(false);

            TMP_Text count = null;
            if (key != null)
            {
                var countGo = new GameObject("Count", typeof(RectTransform));
                countGo.layer = item.gameObject.layer;
                var countRect = (RectTransform)countGo.transform;
                countRect.SetParent(item, false);
                Stretch(countRect);
                var text = countGo.AddComponent<TextMeshProUGUI>();
                text.font = key.font;
                text.fontSharedMaterial = key.fontSharedMaterial;
                text.fontSize = Mathf.Max(30f, item.rect.height * 0.3f);
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.color = Color.white;
                text.outlineWidth = 0.25f;
                text.outlineColor = new Color32(0x14, 0x12, 0x2B, 0xFF);
                text.text = string.Empty;
                count = text;
            }

            slot.FindPropertyRelative("area").objectReferenceValue = item;
            slot.FindPropertyRelative("icon").objectReferenceValue = icon;
            slot.FindPropertyRelative("cooldown").objectReferenceValue = cooldown != null ? cooldown.gameObject : null;
            slot.FindPropertyRelative("cooldownFill").objectReferenceValue = fill;
            slot.FindPropertyRelative("count").objectReferenceValue = count;
            slot.FindPropertyRelative("key").objectReferenceValue = key;
            slot.FindPropertyRelative("selected").objectReferenceValue = Object(item, "Selected");
            slot.FindPropertyRelative("highlighted").objectReferenceValue = Object(item, "Highlighted");
            slot.FindPropertyRelative("disabled").objectReferenceValue = Object(item, "Disabled");
        }

        /// <summary>A bar that ships bare gets the frame the slots beside it wear.</summary>
        private static void FrameLike(RectTransform bar, RectTransform slot)
        {
            Image source = ImageNamed(slot, "SPR_Frame");
            if (source == null || source.sprite == null) return;
            var go = new GameObject("SPR_Frame", typeof(RectTransform));
            go.layer = bar.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(bar, false);
            Stretch(rect);
            var frame = go.AddComponent<Image>();
            frame.sprite = source.sprite;
            // Lighter than the fill it frames, which is the slots' own colour.
            frame.color = Color.Lerp(source.color, Color.white, 0.6f);
            frame.type = source.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            frame.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        }

        private static void EnsureKit(FactionId faction, HotbarChromeView hotbar, VitalsChromeView vitals, MinimapChromeView minimap)
        {
            string path = KitPath(faction);
            var kit = AssetDatabase.LoadAssetAtPath<FactionHudKit>(path);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<FactionHudKit>();
                kit.faction = faction;
                AssetDatabase.CreateAsset(kit, path);
            }
            kit.faction = faction;
            kit.hotbar = hotbar;
            kit.vitals = vitals;
            kit.minimap = minimap;
            EditorUtility.SetDirty(kit);
        }

        // ------------------------------------------------------------------ finding things in the packs' prefabs

        /// <summary>
        /// The rectangle round what is drawn under <paramref name="under"/>, in <paramref name="root"/>'s space:
        /// pictures that are switched on, without the soft shadows and glows that fade to nothing, and without what a
        /// mask cuts away.
        /// </summary>
        private static Bounds Drawn(RectTransform root, RectTransform under)
        {
            bool any = false;
            var all = new Bounds();
            var corners = new Vector3[4];
            foreach (Graphic graphic in under.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic.enabled || graphic.color.a < 0.02f || graphic is TMP_Text) continue;
                if (graphic is Image image && image.sprite == null && image.GetComponent<Mask>() != null) continue;
                string name = graphic.name;
                if (name.Contains("Shadow") || name.Contains("Glow") || name.Contains("Vignette") || name.Contains("Flash")) continue;
                // A mask cuts only what agrees to be cut: a frame drawn round a masked picture does not.
                bool cut = false;
                if (graphic is MaskableGraphic maskable && maskable.maskable)
                    for (Transform up = graphic.transform.parent; up != null && up != root.parent; up = up.parent)
                        if ((up.TryGetComponent(out Mask mask) && mask.enabled) || (up.TryGetComponent(out RectMask2D rectMask) && rectMask.enabled)) { cut = true; break; }
                if (cut) continue;
                graphic.rectTransform.GetWorldCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 point = root.InverseTransformPoint(corners[i]);
                    if (!any) { all = new Bounds(point, Vector3.zero); any = true; }
                    else all.Encapsulate(point);
                }
            }
            return all;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform each in root.GetComponentsInChildren<Transform>(true))
                if (each != root && each.name == name) return each;
            return null;
        }

        private static GameObject Object(Transform root, string name)
        {
            Transform found = Find(root, name);
            if (found == null) return null;
            found.gameObject.SetActive(false);
            return found.gameObject;
        }

        private static Image ImageNamed(Transform root, string name)
        {
            Transform found = Find(root, name);
            return found != null ? found.GetComponent<Image>() : null;
        }

        private static Slider SliderUnder(Transform root, params string[] names)
        {
            foreach (string name in names)
            {
                Transform holder = Find(root, name);
                Slider slider = holder != null ? holder.GetComponentInChildren<Slider>(true) : null;
                if (slider != null) return slider;
            }
            return null;
        }

        private static TMP_Text Label(Transform root, params string[] names)
        {
            foreach (string name in names)
            {
                Transform found = Find(root, name);
                if (found == null) continue;
                found.gameObject.SetActive(true);
                var label = found.GetComponent<TMP_Text>();
                if (label != null) return label;
            }
            return null;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
