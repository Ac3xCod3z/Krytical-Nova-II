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
    /// Composes each faction's namecard for a player (<see cref="PlayerCardView"/>) out of the same pack prefab the
    /// Hostile plate is made of (the pack's world-space enemy plate: level badge and health bar), with lines of ours
    /// in the pack's lettering: the name, the Standing over it, the Crew under it, and a place for the faction's
    /// crest before the name. Behind them is the pack's own small panel (the box and frame of its interaction
    /// prompt), which the view sizes to what the card says. Nothing under Assets/Synty is changed. The badge and bar are the size of a Hostile
    /// plate's, so a player and a Hostile side by side read as one family. It writes the card into the faction's
    /// HUD kit and touches nothing else there.
    /// </summary>
    public static class PlayerCardBuilder
    {
        private const float StandingSize = 0.72f, CrewSize = 0.76f, CrestSize = 1.25f; // of the name's own size
        private const float CrewSymbolSize = 1.3f; // of the Crew line's size
        private static readonly Color StandingColor = new Color(1f, 0.84f, 0.42f);
        private static readonly Color CrewColor = new Color(0.78f, 0.86f, 0.9f);
        /// <summary>How see-through the panel behind a card is: the world shows a little through it.</summary>
        private const float BackAlpha = 0.82f;
        /// <summary>How thick the panel's sliced edge is drawn, as a share of the badge's height.</summary>
        private const float BackEdge = 0.42f;

        public static string CardPath(FactionId faction) => $"{FactionHudBuilder.Folder}/{faction}/HUD_{faction}_PlayerCard.prefab";

        [MenuItem("Krytical Nova/UI/Build Player Namecards")]
        public static void Build()
        {
            foreach (var pack in HostilePlateBuilder.Packs)
            {
                var kit = AssetDatabase.LoadAssetAtPath<FactionHudKit>(FactionHudBuilder.KitPath(pack.faction));
                GameObject prefab = HostilePlateBuilder.LoadEnemyPlate(pack);
                if (kit == null || prefab == null)
                {
                    Debug.LogError($"[HUD] {pack.faction}: no HUD kit or no enemy plate in its pack. Its namecard was not made.");
                    continue;
                }
                kit.playerCard = Compose(pack, prefab);
                EditorUtility.SetDirty(kit);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[HUD] Player namecards composed in {FactionHudBuilder.Folder}.");
        }

        /// <summary>
        /// A panel in the look of the pack's interaction prompt: its box, and its frame when it has one, copied onto
        /// pictures of our own so the view can size them. Sliced, with the edge drawn <paramref name="edge"/> units thick.
        /// </summary>
        private static RectTransform BuildBack((FactionId faction, string pack, string prefix) pack, RectTransform root, float edge)
        {
            string source = $"Assets/Synty/{pack.pack}/Prefabs/Input_Interactions/HUD_{pack.prefix}_Interact_ContextSensitive_01.prefab";
            var prompt = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (prompt == null)
            {
                Debug.LogWarning($"[HUD] {pack.faction}: no prefab at {source}. Its namecard has no panel behind it.");
                return null;
            }
            Image box = null, frame = null;
            foreach (Image image in prompt.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.type != Image.Type.Sliced) continue;
                if (box == null && image.name == "SPR_Background") box = image;
                else if (frame == null && image.name == "SPR_Frame") frame = image;
            }
            if (box == null)
            {
                Debug.LogWarning($"[HUD] {pack.faction}: the pack's interaction prompt has no box. Its namecard has no panel behind it.");
                return null;
            }

            var backGo = new GameObject("Back", typeof(RectTransform), typeof(Image)) { layer = root.gameObject.layer };
            var back = (RectTransform)backGo.transform;
            back.SetParent(root, false);
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(0.5f, 0.5f);
            Like(backGo.GetComponent<Image>(), box, edge, BackAlpha);
            if (frame != null)
            {
                var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image)) { layer = backGo.layer };
                var frameRect = (RectTransform)frameGo.transform;
                frameRect.SetParent(back, false);
                frameRect.anchorMin = Vector2.zero;
                frameRect.anchorMax = Vector2.one;
                frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
                Like(frameGo.GetComponent<Image>(), frame, edge, 1f);
            }
            return back;
        }

        private static void Like(Image picture, Image source, float edge, float alpha)
        {
            picture.sprite = source.sprite;
            picture.type = Image.Type.Sliced;
            picture.fillCenter = source.fillCenter;
            picture.material = source.material;
            Color colour = source.color;
            picture.color = new Color(colour.r, colour.g, colour.b, colour.a * alpha);
            // The sprite's edge is drawn for a large panel: here it is as thick as the card can carry.
            Vector4 border = source.sprite.border;
            float widest = Mathf.Max(Mathf.Max(border.x, border.z), Mathf.Max(border.y, border.w)) / Mathf.Max(1f, source.sprite.pixelsPerUnit) * 100f;
            picture.pixelsPerUnitMultiplier = Mathf.Max(0.01f, widest / Mathf.Max(1f, edge));
        }

        private static PlayerCardView Compose((FactionId faction, string pack, string prefix) packOf, GameObject prefab)
        {
            FactionId faction = packOf.faction;
            string path = CardPath(faction);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var canvasGo = new GameObject("Compose", typeof(RectTransform), typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.sizeDelta = new Vector2(3840f, 2160f);

                var rootGo = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path), typeof(RectTransform));
                rootGo.layer = LayerMask.NameToLayer("UI");
                var root = (RectTransform)rootGo.transform;
                root.SetParent(canvasRect, false);
                var view = rootGo.AddComponent<PlayerCardView>();

                var info = (RectTransform)((GameObject)PrefabUtility.InstantiatePrefab(prefab, root)).transform;
                info.name = "Info";
                info.gameObject.SetActive(true);
                info.anchorMin = info.anchorMax = new Vector2(0.5f, 0.5f);
                info.anchoredPosition = Vector2.zero;

                TMP_Text packName = HostilePlateBuilder.TextNamed(info, "Label_Name");
                TMP_Text level = HostilePlateBuilder.TextNamed(info, "Label_EnemyLevel");
                Slider slider = info.GetComponentInChildren<Slider>(true);
                // The bar and the badge are side by side in every pack: the bar can go and leave the badge.
                var barHolder = slider != null ? slider.transform.parent as RectTransform : null;
                var badge = level != null ? level.transform.parent as RectTransform : null;
                if (packName == null || level == null || slider == null || barHolder == null || badge == null || badge.IsChildOf(barHolder))
                {
                    Debug.LogError($"[HUD] {faction}: the pack's enemy plate is not a name, a badge and a bar beside it. Its namecard was not made.");
                    return null;
                }
                packName.gameObject.SetActive(false);
                HostilePlateBuilder.SetUp(level, slider);
                Canvas.ForceUpdateCanvases();

                // The badge and bar stand in the middle, as on a Hostile's plate.
                Bounds drawn = FactionHudBuilder.Drawn(root, info);
                info.anchoredPosition -= new Vector2(drawn.center.x, 0f);
                Canvas.ForceUpdateCanvases();
                drawn = FactionHudBuilder.Drawn(root, info);
                Bounds badgeDrawn = FactionHudBuilder.Drawn(root, badge);

                float units = HostilePlateBuilder.PanelWidth / Mathf.Max(1f, drawn.size.x);
                float size = HostilePlateBuilder.NamePanelSize / units;
                float gap = size * 0.3f;
                float nameHeight = size * 1.3f, standingHeight = size * StandingSize * 1.3f, crewHeight = size * CrewSize * 1.3f;

                // From the foot up: the Crew, the badge and bar, the name, the Standing.
                // The pack's panel, behind everything: made first so the rest draws over it.
                RectTransform back = BuildBack(pack: packOf, root: root, edge: badgeDrawn.size.y * BackEdge);
                if (back != null) back.SetAsFirstSibling();

                float foot = drawn.min.y - size * 0.1f - crewHeight;
                float barTop = drawn.max.y - foot;
                float top = barTop + gap * 0.3f + nameHeight + standingHeight;
                float half = top * 0.5f;
                foreach (RectTransform child in root)
                    if (child != back) child.anchoredPosition -= new Vector2(0f, foot + half);
                root.anchorMin = root.anchorMax = Vector2.zero; // placed in screen pixels from the bottom-left corner
                root.pivot = new Vector2(0.5f, 0f);
                root.sizeDelta = new Vector2(drawn.size.x, top);

                TextMeshProUGUI name = Line(root, "Label_PlayerName", packName, size, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                    nameHeight, new Vector2(0f, barTop + nameHeight * 0.5f + gap * 0.3f - half), drawn.size.x * 4f);
                name.text = "Player";

                TextMeshProUGUI standing = Line(root, "Label_Standing", packName, size * StandingSize, TextAlignmentOptions.Bottom, new Vector2(0.5f, 0f),
                    standingHeight, new Vector2(0f, barTop + gap * 0.3f + nameHeight - half), drawn.size.x * 4f);
                standing.color = StandingColor;
                standing.characterSpacing = 4f;
                standing.text = "Standing";

                TextMeshProUGUI crew = Line(root, "Label_Crew", packName, size * CrewSize, TextAlignmentOptions.Top, new Vector2(0.5f, 1f),
                    crewHeight, new Vector2(0f, crewHeight - half), drawn.size.x * 4f);
                crew.color = CrewColor;
                crew.richText = false; // a Crew is written <Name>: never read as markup
                crew.text = "<Crew>";

                var crestGo = new GameObject("Crest", typeof(RectTransform), typeof(Image)) { layer = rootGo.layer };
                var crestRect = (RectTransform)crestGo.transform;
                crestRect.SetParent(root, false);
                crestRect.anchorMin = crestRect.anchorMax = crestRect.pivot = new Vector2(0.5f, 0.5f);
                crestRect.sizeDelta = Vector2.one * size * CrestSize;
                crestRect.anchoredPosition = name.rectTransform.anchoredPosition;
                var crest = crestGo.GetComponent<Image>();
                crest.preserveAspect = true;

                // The Crew's symbol, before the Crew's name: the view puts it there.
                var symbolGo = new GameObject("CrewSymbol", typeof(RectTransform), typeof(Image)) { layer = rootGo.layer };
                var symbolRect = (RectTransform)symbolGo.transform;
                symbolRect.SetParent(root, false);
                symbolRect.anchorMin = symbolRect.anchorMax = symbolRect.pivot = new Vector2(0.5f, 0.5f);
                symbolRect.sizeDelta = Vector2.one * size * CrewSize * CrewSymbolSize;
                symbolRect.anchoredPosition = crew.rectTransform.anchoredPosition;
                var crewSymbol = symbolGo.GetComponent<Image>();
                crewSymbol.preserveAspect = true;
                crewSymbol.raycastTarget = false;

                standing.gameObject.SetActive(false);
                crew.gameObject.SetActive(false);
                crestGo.SetActive(false);
                symbolGo.SetActive(false);

                var so = new SerializedObject(view);
                so.FindProperty("nameLabel").objectReferenceValue = name;
                so.FindProperty("levelLabel").objectReferenceValue = level;
                so.FindProperty("standingLabel").objectReferenceValue = standing;
                so.FindProperty("crewLabel").objectReferenceValue = crew;
                so.FindProperty("crest").objectReferenceValue = crest;
                so.FindProperty("crewSymbol").objectReferenceValue = crewSymbol;
                so.FindProperty("health").objectReferenceValue = slider;
                so.FindProperty("bar").objectReferenceValue = barHolder.gameObject;
                so.FindProperty("badge").objectReferenceValue = badge;
                so.FindProperty("back").objectReferenceValue = back;
                so.FindProperty("backPadding").vector2Value = new Vector2(gap * 2.2f, gap * 1.1f);
                so.FindProperty("unitsToPanel").floatValue = units;
                so.FindProperty("barUnits").floatValue = drawn.size.x;
                so.FindProperty("badgeRest").vector2Value = badge.anchoredPosition;
                so.FindProperty("badgeCentreX").floatValue = badgeDrawn.center.x;
                so.FindProperty("badgeWidth").floatValue = badgeDrawn.size.x;
                so.FindProperty("badgeHeight").floatValue = badgeDrawn.size.y;
                so.FindProperty("rowY").floatValue = badgeDrawn.center.y - foot;
                so.FindProperty("barTop").floatValue = barTop;
                so.FindProperty("gap").floatValue = gap;
                so.ApplyModifiedPropertiesWithoutUndo();

                HostilePlateBuilder.LookedAtOnly(rootGo);
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                root.SetParent(null, false);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rootGo, scene);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(rootGo, path);
                return saved != null ? saved.GetComponent<PlayerCardView>() : null;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static TextMeshProUGUI Line(RectTransform root, string name, TMP_Text like, float size, TextAlignmentOptions alignment,
            Vector2 pivot, float height, Vector2 at, float width)
        {
            TextMeshProUGUI text = FactionHudBuilder.NewLabel(root, name, like, size, alignment);
            RectTransform own = text.rectTransform;
            own.anchorMin = own.anchorMax = new Vector2(0.5f, 0.5f);
            own.pivot = pivot;
            own.sizeDelta = new Vector2(width, height);
            own.anchoredPosition = at;
            return text;
        }
    }
}
