using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Crucible
{
    /// <summary>
    /// Import rules for the Crucible's textures (owner's texture pick, 17 Nov 2026, binding): everything 2K; the six unique floor
    /// normals are 4K files in the mipmap limit group <see cref="FloorGroup"/>, which High and Ultra show in full and every lower
    /// preset one mip down (2K), so no 2K copies of them are kept. The 8K masters stay in Drive and never come here.
    /// </summary>
    public sealed class CrucibleTextureImport : AssetPostprocessor
    {
        public const string FloorGroup = "Crucible Floors 4K";
        const string Folder = CrucibleData.TextureFolder + "/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool normal = file.Contains("_Normal_");
            bool floor4K = file.EndsWith("_Normal_4K");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.mipmapEnabled = true;
            ti.streamingMipmaps = true;
            ti.maxTextureSize = floor4K ? 4096 : 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC5 normals, BC7 colour
            ti.wrapMode = file.StartsWith("T_Banner") || file == "Crucible_Sky" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.anisoLevel = file.StartsWith("T_Floor") ? 4 : 1;
            ti.ignoreMipmapLimit = false;
            ti.mipmapLimitGroupName = floor4K ? FloorGroup : "";
        }

        /// <summary>High and Ultra keep the 4K floors whole; every other preset drops one mip (to 2K).</summary>
        [MenuItem("Krytical Nova/World/Crucible/Set Up Floor Mipmap Group")]
        public static string EnsureFloorGroup()
        {
            if (!TextureMipmapLimitGroups.HasGroup(FloorGroup)) TextureMipmapLimitGroups.CreateGroup(FloorGroup);
            int current = QualitySettings.GetQualityLevel();
            var sb = new System.Text.StringBuilder("Crucible floor mip group: ");
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                string n = QualitySettings.names[i];
                bool full = n.Contains("High") || n.Contains("Ultra");
                QualitySettings.SetTextureMipmapLimitSettings(FloorGroup,
                    new TextureMipmapLimitSettings { limitBias = full ? 0 : 1, limitBiasMode = TextureMipmapLimitBiasMode.OverrideGlobalLimit });
                sb.Append($"{n}={(full ? "4K" : "2K")} ");
            }
            QualitySettings.SetQualityLevel(current, false);
            return sb.ToString();
        }
    }
}
