using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Where a faction's own Synty pack lacks a piece another pack has, this makes the faction's version of it: a
    /// prefab variant of the pack that has it (the vendor prefab is not touched), restyled. Frames and boxes are
    /// swapped for the faction pack's own where it has an equivalent; art with no equivalent is recoloured into a
    /// copy (<see cref="SpriteRecolourer"/>); a motif that reads as the other race is replaced; the lettering is
    /// the faction's.
    ///
    /// First of its kind, to be judged before the rest are made: Dark Fantasy's inventory window for the Hegemony.
    /// </summary>
    public static class FactionVariantBuilder
    {
        private const string Folder = "Assets/_Game/UI/HUD/Faction";

        private sealed class Restyle
        {
            public FactionId Faction;
            public string Source;                         // the prefab of the pack that has the piece
            public string Name;                           // what the variant is called
            public string Pack;                           // the faction's own pack, under Assets/Synty
            public Dictionary<string, string> Swaps;      // source sprite name -> the faction pack's own sprite (path under the pack)
            public string[] Remove;                       // motifs of the other race
            public string MotifAt, Motif;                 // where one of them was, the faction's own goes (sprite path under the pack)
            public Vector2 MotifSize;
            public string BodyLettering, TitleLettering;  // labels of the faction's pack, for their fonts
            public string[] Keep;                         // sprites left as they are (sample pictures, item icons)
        }

        private static readonly Restyle HegemonyInventory = new Restyle
        {
            Faction = FactionId.Hegemony, Pack = "InterfaceSciFiSoldierHUD", Name = "HUD_Hegemony_Inventory_Character",
            Source = "Assets/Synty/InterfaceDarkFantasyHUD/Prefabs/Input_Interactions/HUD_DarkFantasy_Inventory_Character_01.prefab",
            Swaps = new Dictionary<string, string>
            {
                { "SPR_DarkFantasy_Frame_Box_Medium_17", "Sprites/HUD/SPR_HUD_SciFiSoldier_Frame_02.png" },
                { "SPR_HUD_DarkFantasy_Frame_Box_01", "Sprites/HUD/SPR_HUD_SciFiSoldier_Frame_04.png" },
                { "SPR_HUD_DarkFantasy_Box_Small_Parchment_01", "Sprites/HUD/SPR_HUD_SciFiSoldier_Box_02.png" },
            },
            Remove = new[] { "SPR_DarkFantasy_Greeble_Skull_01", "SPR_DarkFantasy_Greeble_Horns_05" },
            MotifAt = "SPR_DarkFantasy_Greeble_Skull_01", Motif = "Sprites/SciFi/SPR_SciFiSoldier_Greeble_Hexagon_02.png", MotifSize = new Vector2(220f, 260f),
            BodyLettering = "Prefabs/_CommonComponents/Label_SciFiSoldier_SemiBold.prefab", TitleLettering = "Prefabs/_CommonComponents/Label_SciFiSoldier_Header.prefab",
            Keep = new[] { "ICON_", "SPR_HUD_DarkFantasy_Example_" },
        };

        [MenuItem("Krytical Nova/UI/Build Faction Window Variants")]
        public static void Build()
        {
            Make(HegemonyInventory);
            AssetDatabase.SaveAssets();
        }

        public static string PathOf(FactionId faction, string name) => $"{Folder}/{faction}/Windows/{name}.prefab";

        private static GameObject Make(Restyle restyle)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(restyle.Source);
            var skin = AssetDatabase.LoadAssetAtPath<UiSkin>($"Assets/_Game/UI/Skins/UiSkin_{restyle.Faction}.asset");
            if (source == null || skin == null)
            {
                Debug.LogError($"[HUD] {restyle.Faction}: no source prefab at {restyle.Source}, or no skin. Nothing was put in its place.");
                return null;
            }
            string pack = $"Assets/Synty/{restyle.Pack}/";
            string artFolder = $"{Folder}/{restyle.Faction}/Art";
            TMP_Text body = Lettering(pack + restyle.BodyLettering), title = Lettering(pack + restyle.TitleLettering);

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                instance.name = restyle.Name;
                var copies = new Dictionary<Sprite, Sprite>();
                int swapped = 0, recoloured = 0;

                foreach (Image image in instance.GetComponentsInChildren<Image>(true))
                {
                    Sprite sprite = image.sprite;
                    string name = sprite != null ? sprite.name : string.Empty;
                    if (System.Array.Exists(restyle.Keep, kept => name.StartsWith(kept))) continue;
                    if (System.Array.IndexOf(restyle.Remove, name) >= 0)
                    {
                        if (name == restyle.MotifAt) PutMotif(image, pack + restyle.Motif, restyle.MotifSize, skin.accent);
                        image.enabled = false;
                        continue;
                    }
                    // The tint first: it is part of the look whatever the sprite.
                    image.color = SpriteRecolourer.Remap(image.color, skin.panelTone, skin.accent);
                    if (sprite == null) continue;
                    if (restyle.Swaps.TryGetValue(name, out string own))
                    {
                        var ownSprite = AssetDatabase.LoadAssetAtPath<Sprite>(pack + own);
                        if (ownSprite == null) { Debug.LogError($"[HUD] {restyle.Faction}: no sprite at {pack + own}."); continue; }
                        image.sprite = ownSprite;
                        if (ownSprite.border != Vector4.zero) image.type = Image.Type.Sliced;
                        swapped++;
                        continue;
                    }
                    // White and grey masks, gradients and vignettes carry no colour of their own: the tint was enough.
                    if (IsColourless(sprite)) continue;
                    if (!copies.TryGetValue(sprite, out Sprite copy))
                    {
                        copy = SpriteRecolourer.Copy(sprite, restyle.Faction, artFolder, skin.panelTone, skin.accent);
                        copies[sprite] = copy;
                        recoloured++;
                    }
                    if (copy != null) image.sprite = copy;
                }

                foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true))
                {
                    TMP_Text like = text.fontSize >= 64f && title != null ? title : body;
                    if (like == null) continue;
                    text.font = like.font;
                    text.fontSharedMaterial = like.fontSharedMaterial;
                    // The faction's letters are wider than the source's: a count stays on one line, a size down.
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    if (like == body) text.fontSize *= 0.8f;
                    text.color = SpriteRecolourer.Remap(text.color, skin.panelTone, skin.accent);
                }

                string path = PathOf(restyle.Faction, restyle.Name);
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
                Debug.Log($"[HUD] {restyle.Faction}: {restyle.Name} made as a variant of {source.name} ({swapped} images on the faction's own sprites, {recoloured} sprites recoloured into {artFolder}).");
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static TMP_Text Lettering(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null ? prefab.GetComponentInChildren<TMP_Text>(true) : null;
        }

        /// <summary>True for art that is white or grey all over (it is coloured by its image's tint).</summary>
        private static bool IsColourless(Sprite sprite)
        {
            string name = sprite.name;
            return name.Contains("Gradient") || name.Contains("Vignette") || name.Contains("White") || name.Contains("Glow") || name.Contains("Mask") || name.EndsWith("_Background");
        }

        private static void PutMotif(Image where, string spritePath, Vector2 size, Color tint)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite == null) { Debug.LogError($"[HUD] No motif sprite at {spritePath}."); return; }
            var go = new GameObject("SPR_Motif", typeof(RectTransform));
            go.layer = where.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(where.transform.parent, false);
            rect.SetSiblingIndex(where.transform.GetSiblingIndex() + 1);
            var like = where.rectTransform;
            rect.anchorMin = like.anchorMin;
            rect.anchorMax = like.anchorMax;
            rect.pivot = like.pivot;
            rect.anchoredPosition = like.anchoredPosition;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.Lerp(Color.white, tint, 0.25f);
        }
    }
}
