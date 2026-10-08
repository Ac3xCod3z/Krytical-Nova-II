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
    /// The Hotbar: the pack's ARPG bar widened to ten slots (keys 1 to 0) between its HP and Flux containers, its XP
    /// bar and level badge, and the pack's small item bar cut to four slots for Q W E R.
    /// </summary>
    public static class FactionHudBuilder
    {
        public const string Folder = "Assets/_Game/UI/HUD/Faction";
        public const string KitFolder = "Assets/_Game/UI/Skins";
        private const int NumberSlots = 10, LetterSlots = 4;
        private const float LetterGap = 50f;
        /// <summary>The Q W E R bar's slots are drawn larger than the main bar's in every pack: brought down to match.</summary>
        private const float LetterScale = 0.72f;
        /// <summary>How wide every faction's Hotbar is drawn, in panel pixels of the 1280 x 720 interface.</summary>
        private const float PanelWidth = 640f;
        private static readonly Color CooldownShade = new Color(0.03f, 0.03f, 0.08f, 0.72f);

        private sealed class Spec
        {
            public FactionId Faction;
            public string Pack;          // folder under Assets/Synty
            public string Main;          // the ARPG bar
            public string Letters;       // the small bar for Q W E R
            public string Experience;    // an XP bar of its own, when the ARPG bar has none
            public string Level;         // the level badge
            public Vector2 LevelAt;      // the badge's nudge from sitting centred on top of the HP container (units)
            public Vector2 ExperienceAt; // where a separate XP bar sits, from the main bar's top centre
            public float ExperienceHeight; // how tall a separate XP bar is drawn (units)
            public bool SlotsOnTop;      // the bar's own art is drawn over the ends of its row: the slots and keys go over it
        }

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Faction = FactionId.Hegemony, Pack = "InterfaceSciFiSoldierHUD",
                Main = "ActionBar/HUD_SciFiSoldier_ARPGBar_01", Letters = "ActionBar/HUD_SciFiSoldier_HotBar_03",
                Experience = "Player_Health_Equipment/_Parts/HUD_SciFiSoldier_XPBar_01", Level = "ActionBar/_Parts/HUD_SciFiSoldier_PlayerLevel_01",
                LevelAt = new Vector2(70f, -50f), ExperienceAt = new Vector2(0f, -24f), ExperienceHeight = 40f, SlotsOnTop = true,
            },
            new Spec
            {
                Faction = FactionId.Null, Pack = "InterfaceDarkFantasyHUD",
                Main = "ActionBar/HUD_DarkFantasy_ARPGBar_02", Letters = "ActionBar/HUD_DarkFantasy_HotBar_01",
                Level = "ActionBar/_Parts/HUD_DarkFantasy_PlayerLevel_02", LevelAt = new Vector2(0f, -170f),
            },
            new Spec
            {
                Faction = FactionId.Covenant, Pack = "InterfaceFantasyWarriorHUD",
                Main = "ActionBar/HUD_FantasyWarrior_ARPGBar_05", Letters = "ActionBar/HUD_FantasyWarrior_HotBar_01",
                Level = "ActionBar/_Parts/HUD_FantasyWarrior_PlayerLevel_01", LevelAt = new Vector2(-110f, -110f),
            },
        };

        public static string HotbarPath(FactionId faction) => $"{Folder}/{faction}/HUD_{faction}_Hotbar.prefab";

        public static string KitPath(FactionId faction) => $"{KitFolder}/FactionHudKit_{faction}.asset";

        [MenuItem("Krytical Nova/UI/Build Faction HUD")]
        public static void Build()
        {
            foreach (Spec spec in Specs) BuildHotbar(spec);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HUD] Faction HUD pieces composed in {Folder}.");
        }

        private static void BuildHotbar(Spec spec)
        {
            GameObject main = Load(spec, spec.Main), letters = Load(spec, spec.Letters), badge = Load(spec, spec.Level);
            if (main == null || letters == null || badge == null) return;

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                // A canvas to lay things out in: the packs are drawn for 3840 x 2160.
                var canvasGo = new GameObject("Compose", typeof(RectTransform), typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.sizeDelta = new Vector2(3840f, 2160f);

                var rootGo = new GameObject($"HUD_{spec.Faction}_Hotbar", typeof(RectTransform));
                rootGo.layer = LayerMask.NameToLayer("UI");
                var root = (RectTransform)rootGo.transform;
                root.SetParent(canvasRect, false);

                RectTransform mainBar = Place(main, root, "Main");
                RectTransform letterBar = Place(letters, root, "Letters");
                letterBar.localScale = new Vector3(LetterScale, LetterScale, 1f);
                List<RectTransform> numberSlots = SetSlots(mainBar, NumberSlots);
                List<RectTransform> letterSlots = SetSlots(letterBar, LetterSlots);
                Canvas.ForceUpdateCanvases();

                // The XP bar the ARPG bar carries (some ship it switched off), or one of its own over the bar.
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

                // The Q W E R bar to the right of the Flux container, its slots level with the main slots. Measured
                // from the slots and the container, not from the bars' soft shadows.
                Transform healthHolder = Find(mainBar, "Container_L") ?? Find(mainBar, "Container_Left");
                Transform fluxHolder = Find(mainBar, "Container_R") ?? Find(mainBar, "Container_Right");
                Bounds fluxBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, fluxHolder);
                Bounds firstLetter = RectTransformUtility.CalculateRelativeRectTransformBounds(root, letterSlots[0]);
                Bounds firstNumber = RectTransformUtility.CalculateRelativeRectTransformBounds(root, numberSlots[0]);
                letterBar.anchoredPosition += new Vector2(fluxBounds.max.x - firstLetter.min.x + LetterGap, firstNumber.center.y - firstLetter.center.y);

                // The level badge on top of the HP container.
                RectTransform level = Place(badge, root, "Level");
                Bounds healthBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, healthHolder);
                Bounds badgeBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, level);
                level.anchoredPosition += new Vector2(healthBounds.center.x - badgeBounds.center.x + spec.LevelAt.x, healthBounds.max.y - badgeBounds.min.y + spec.LevelAt.y);
                Canvas.ForceUpdateCanvases();

                // The root takes the size of everything in it, centred on it.
                Bounds all = Around(root);
                foreach (RectTransform child in root) child.anchoredPosition -= (Vector2)all.center;
                root.sizeDelta = all.size;
                Canvas.ForceUpdateCanvases();

                var view = rootGo.AddComponent<HotbarChromeView>();
                var so = new SerializedObject(view);
                SerializedProperty slots = so.FindProperty("slots");
                slots.arraySize = NumberSlots + LetterSlots;
                for (int i = 0; i < NumberSlots + LetterSlots; i++)
                    WireSlot(slots.GetArrayElementAtIndex(i), i < NumberSlots ? numberSlots[i] : letterSlots[i - NumberSlots]);
                so.FindProperty("health").objectReferenceValue = SliderUnder(mainBar, "Container_L", "Container_Left");
                so.FindProperty("flux").objectReferenceValue = SliderUnder(mainBar, "Container_R", "Container_Right");
                so.FindProperty("experience").objectReferenceValue = experience != null ? experience.GetComponentInChildren<Slider>(true) : null;
                // One line of text on the XP bar: a bar that ships two shows the one on the fill.
                TMP_Text experienceLabel = experience != null ? Label(experience, "Label_XP", "Label_Number", "Label_PlayerXP") : null;
                if (experience != null)
                    foreach (TMP_Text other in experience.GetComponentsInChildren<TMP_Text>(true))
                        if (other != experienceLabel) other.gameObject.SetActive(false);
                so.FindProperty("experienceLabel").objectReferenceValue = experienceLabel;
                so.FindProperty("level").objectReferenceValue = Label(level, "Label_PlayerLevel");
                // The containers frame the screen's middle: the view puts the middle of the row of slots there.
                Bounds firstSlot = RectTransformUtility.CalculateRelativeRectTransformBounds(root, numberSlots[0]);
                Bounds lastSlot = RectTransformUtility.CalculateRelativeRectTransformBounds(root, numberSlots[numberSlots.Count - 1]);
                so.FindProperty("mainCentre").floatValue = Mathf.Clamp01(((firstSlot.min.x + lastSlot.max.x) * 0.5f - root.rect.xMin) / Mathf.Max(1f, root.rect.width));
                so.ApplyModifiedPropertiesWithoutUndo();

                // Views only: nothing of the packs' own behaviour may fight what the game shows.
                foreach (Animator animator in rootGo.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (Selectable selectable in rootGo.GetComponentsInChildren<Selectable>(true))
                {
                    selectable.transition = Selectable.Transition.None;
                    selectable.interactable = true;
                    if (!(selectable is Slider)) selectable.enabled = false;
                }
                foreach (Graphic graphic in rootGo.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

                string path = HotbarPath(spec.Faction);
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                root.SetParent(null, false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootGo, scene);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(rootGo, path);
                EnsureKit(spec.Faction, saved != null ? saved.GetComponent<HotbarChromeView>() : null, PanelWidth / Mathf.Max(1f, all.size.x));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
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

        private static void EnsureKit(FactionId faction, HotbarChromeView hotbar, float unitsToPanel)
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
            kit.unitsToPanel = unitsToPanel;
            EditorUtility.SetDirty(kit);
        }

        // ------------------------------------------------------------------ finding things in the packs' prefabs

        /// <summary>The rectangle round everything under <paramref name="root"/>, in its own space.</summary>
        private static Bounds Around(RectTransform root)
        {
            bool any = false;
            var all = new Bounds();
            foreach (RectTransform child in root)
            {
                if (!child.gameObject.activeSelf) continue;
                Bounds each = RectTransformUtility.CalculateRelativeRectTransformBounds(root, child);
                if (!any) { all = each; any = true; }
                else all.Encapsulate(each);
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
