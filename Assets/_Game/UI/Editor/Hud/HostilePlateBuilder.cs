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
    /// Composes each faction's plate for a Hostile (<see cref="HostilePlateView"/>) out of that faction's Synty HUD
    /// pack: the pack's own world-space enemy plate (level badge, health bar, name), with a line over it for a
    /// Bruiser and one under it for a Hostile another player holds the kill of. Nothing under Assets/Synty is
    /// changed. Every faction's plate has the same parts and is drawn the same width. It writes the plate into the
    /// faction's HUD kit and touches nothing else there.
    /// </summary>
    public static class HostilePlateBuilder
    {
        /// <summary>How wide every faction's badge and bar are drawn together, in panel pixels of the 1280 x 720 interface.</summary>
        private const float PanelWidth = 118f;
        /// <summary>How tall every faction's name is lettered, in panel pixels: the packs' own sizes are far apart.</summary>
        private const float NamePanelSize = 12f;
        private const float TierSize = 0.72f, TakenSize = 0.66f; // of the name's own size
        private static readonly Color TakenColor = new Color(0.57f, 0.64f, 0.7f);
        private static readonly Color TierColor = new Color(1f, 0.79f, 0.24f);

        private static readonly (FactionId faction, string pack, string prefix)[] Packs =
        {
            (FactionId.Hegemony, "InterfaceSciFiSoldierHUD", "SciFiSoldier"),
            (FactionId.Null, "InterfaceDarkFantasyHUD", "DarkFantasy"),
            (FactionId.Covenant, "InterfaceFantasyWarriorHUD", "FantasyWarrior"),
        };

        public static string PlatePath(FactionId faction) => $"{FactionHudBuilder.Folder}/{faction}/HUD_{faction}_HostilePlate.prefab";

        [MenuItem("Krytical Nova/UI/Build Hostile Plates")]
        public static void Build()
        {
            foreach (var pack in Packs)
            {
                var kit = AssetDatabase.LoadAssetAtPath<FactionHudKit>(FactionHudBuilder.KitPath(pack.faction));
                if (kit == null)
                {
                    Debug.LogError($"[HUD] {pack.faction} has no HUD kit. Run Krytical Nova ▸ UI ▸ Build Faction HUD first.");
                    continue;
                }
                string source = $"Assets/Synty/{pack.pack}/Prefabs/NPC_HealthBars_EnemyData/HUD_{pack.prefix}_WorldSpace_EnemyInfo_01.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                if (prefab == null)
                {
                    Debug.LogError($"[HUD] {pack.faction}: no prefab at {source}. Its Hostile plate was not made.");
                    continue;
                }
                kit.hostilePlate = Compose(pack.faction, prefab);
                EditorUtility.SetDirty(kit);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[HUD] Hostile plates composed in {FactionHudBuilder.Folder}.");
        }

        private static HostilePlateView Compose(FactionId faction, GameObject prefab)
        {
            string path = PlatePath(faction);
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
                var view = rootGo.AddComponent<HostilePlateView>();

                var info = (RectTransform)((GameObject)PrefabUtility.InstantiatePrefab(prefab, root)).transform;
                info.name = "Info";
                info.gameObject.SetActive(true);
                info.anchorMin = info.anchorMax = new Vector2(0.5f, 0.5f);
                info.anchoredPosition = Vector2.zero;

                TMP_Text packName = TextNamed(info, "Label_Name");
                TMP_Text level = TextNamed(info, "Label_EnemyLevel");
                Slider bar = info.GetComponentInChildren<Slider>(true);
                if (packName == null || level == null || bar == null)
                {
                    Debug.LogError($"[HUD] {faction}: the pack's enemy plate has no name, level or bar. Its Hostile plate was not made.");
                    return null;
                }
                // The pack's own name line is one size and one colour, set for its sample. Ours is as long as the
                // name, the same height in every faction, and takes the colour of the level gap: a line of our own
                // in the pack's lettering stands in its place.
                packName.gameObject.SetActive(false);
                level.text = "1";
                bar.minValue = 0f;
                bar.maxValue = 1f;
                bar.wholeNumbers = false;
                bar.value = 1f;
                Canvas.ForceUpdateCanvases();

                // The badge and bar stand in the middle, over the Hostile, with the name centred over them.
                Bounds drawn = FactionHudBuilder.Drawn(root, info);
                info.anchoredPosition -= new Vector2(drawn.center.x, 0f);
                Canvas.ForceUpdateCanvases();
                drawn = FactionHudBuilder.Drawn(root, info);

                float units = PanelWidth / Mathf.Max(1f, drawn.size.x);
                float size = NamePanelSize / units;
                TextMeshProUGUI name = FactionHudBuilder.NewLabel(root, "Label_HostileName", packName, size, TextAlignmentOptions.Bottom);
                name.text = "Hostile";
                RectTransform nameRect = name.rectTransform;
                nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0.5f);
                nameRect.pivot = new Vector2(0.5f, 0f);
                nameRect.sizeDelta = new Vector2(drawn.size.x * 3f, size * 1.3f);
                nameRect.anchoredPosition = new Vector2(0f, drawn.max.y + size * 0.15f);

                TextMeshProUGUI tier = FactionHudBuilder.NewLabel(root, "Label_Tier", packName, size * TierSize, TextAlignmentOptions.Bottom);
                tier.text = "BRUISER";
                tier.color = TierColor;
                tier.characterSpacing = 6f;
                RectTransform tierRect = tier.rectTransform;
                tierRect.anchorMin = tierRect.anchorMax = new Vector2(0.5f, 0.5f);
                tierRect.pivot = new Vector2(0.5f, 0f);
                tierRect.sizeDelta = new Vector2(drawn.size.x * 3f, size * TierSize * 1.3f);
                tierRect.anchoredPosition = new Vector2(0f, nameRect.anchoredPosition.y + size * 1.2f);

                TextMeshProUGUI taken = FactionHudBuilder.NewLabel(root, "Label_Taken", packName, size * TakenSize, TextAlignmentOptions.Top);
                taken.text = "TAKEN";
                taken.color = TakenColor;
                taken.characterSpacing = 6f;
                RectTransform takenRect = taken.rectTransform;
                takenRect.anchorMin = takenRect.anchorMax = new Vector2(0.5f, 0.5f);
                takenRect.pivot = new Vector2(0.5f, 1f);
                takenRect.sizeDelta = new Vector2(drawn.size.x * 2f, size * TakenSize * 1.3f);
                takenRect.anchoredPosition = new Vector2(0f, drawn.min.y - size * 0.1f);
                Canvas.ForceUpdateCanvases();

                // The root stands on its foot: everything is lifted so the lowest line ends at the root's bottom edge.
                float foot = takenRect.anchoredPosition.y - takenRect.sizeDelta.y;
                float top = tierRect.anchoredPosition.y + tierRect.sizeDelta.y;
                foreach (RectTransform child in root) child.anchoredPosition -= new Vector2(0f, foot);
                root.anchorMin = root.anchorMax = Vector2.zero; // placed in screen pixels from the bottom-left corner
                root.pivot = new Vector2(0.5f, 0f);
                root.sizeDelta = new Vector2(drawn.size.x, top - foot);
                // The children were laid out from the root's middle: its foot is half its height lower.
                foreach (RectTransform child in root) child.anchoredPosition -= new Vector2(0f, (top - foot) * 0.5f);
                tier.gameObject.SetActive(false);
                taken.gameObject.SetActive(false);

                var so = new SerializedObject(view);
                so.FindProperty("nameLabel").objectReferenceValue = name;
                so.FindProperty("levelLabel").objectReferenceValue = level;
                so.FindProperty("tierLabel").objectReferenceValue = tier;
                so.FindProperty("takenLabel").objectReferenceValue = taken;
                so.FindProperty("health").objectReferenceValue = bar;
                so.FindProperty("barUnits").floatValue = drawn.size.x;
                so.FindProperty("unitsToPanel").floatValue = units;
                so.ApplyModifiedPropertiesWithoutUndo();

                // A view only: nothing of the pack's own behaviour may fight what the game shows, and nothing takes a click.
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
                return saved != null ? saved.GetComponent<HostilePlateView>() : null;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static TMP_Text TextNamed(Transform root, string name)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == name) return text;
            return null;
        }
    }
}
