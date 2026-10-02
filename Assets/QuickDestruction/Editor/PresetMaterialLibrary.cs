using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Shared generated materials for the one-click presets and the demo builder. Materials
    /// are created on first use below Assets/QuickDestruction_Generated/PresetMaterials and
    /// reused afterwards, so repeated preset bakes never duplicate assets. The shader follows
    /// the active render pipeline (HDRP Lit, URP Lit or built-in Standard).
    /// </summary>
    public static class PresetMaterialLibrary
    {
        public const string LibraryFolder = BakePipeline.GeneratedRootFolder + "/PresetMaterials";

        const int VariantCount = 3;

        struct VariantSpec
        {
            public Color Color;
            public float Smoothness;

            public VariantSpec(Color color, float smoothness)
            {
                Color = color;
                Smoothness = smoothness;
            }
        }

        // Deterministic (color, smoothness) table, three variants per preset type.
        static readonly VariantSpec[] m_concrete =
        {
            new VariantSpec(new Color(0.55f, 0.55f, 0.55f), 0.1f),
            new VariantSpec(new Color(0.48f, 0.48f, 0.49f), 0.12f),
            new VariantSpec(new Color(0.62f, 0.61f, 0.59f), 0.08f)
        };
        static readonly VariantSpec[] m_glass =
        {
            new VariantSpec(new Color(0.75f, 0.87f, 0.95f), 0.95f),
            new VariantSpec(new Color(0.68f, 0.82f, 0.92f), 0.95f),
            new VariantSpec(new Color(0.82f, 0.92f, 0.97f), 0.95f)
        };
        static readonly VariantSpec[] m_wood =
        {
            new VariantSpec(new Color(0.55f, 0.36f, 0.2f), 0.35f),
            new VariantSpec(new Color(0.45f, 0.3f, 0.18f), 0.38f),
            new VariantSpec(new Color(0.35f, 0.24f, 0.15f), 0.32f)
        };
        static readonly VariantSpec[] m_brick =
        {
            new VariantSpec(new Color(0.58f, 0.28f, 0.22f), 0.15f),
            new VariantSpec(new Color(0.52f, 0.24f, 0.19f), 0.18f),
            new VariantSpec(new Color(0.64f, 0.34f, 0.26f), 0.12f)
        };
        static readonly VariantSpec[] m_slab =
        {
            new VariantSpec(new Color(0.66f, 0.63f, 0.58f), 0.1f),
            new VariantSpec(new Color(0.59f, 0.57f, 0.53f), 0.12f),
            new VariantSpec(new Color(0.71f, 0.68f, 0.62f), 0.08f)
        };
        static readonly VariantSpec[] m_basalt =
        {
            new VariantSpec(new Color(0.28f, 0.29f, 0.32f), 0.14f),
            new VariantSpec(new Color(0.24f, 0.25f, 0.28f), 0.16f),
            new VariantSpec(new Color(0.33f, 0.34f, 0.36f), 0.12f)
        };
        static readonly VariantSpec[] m_stone =
        {
            new VariantSpec(new Color(0.5f, 0.47f, 0.43f), 0.1f),
            new VariantSpec(new Color(0.44f, 0.42f, 0.39f), 0.12f),
            new VariantSpec(new Color(0.57f, 0.53f, 0.48f), 0.08f)
        };

        /// <summary>Best matching Lit shader for the active render pipeline.</summary>
        public static Shader FindPipelineLitShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                string pipelineName = pipeline.GetType().FullName ??
                                      pipeline.GetType().Name;
                if (pipelineName.Contains("HighDefinition") ||
                    pipelineName.Contains("HDRenderPipeline"))
                {
                    Shader hdrp = Shader.Find("HDRP/Lit");
                    if (hdrp != null) return hdrp;
                }
                else if (pipelineName.Contains("Universal"))
                {
                    Shader urp = Shader.Find("Universal Render Pipeline/Lit");
                    if (urp != null) return urp;
                }
            }
            return Shader.Find("Standard");
        }

        /// <summary>Asset path of the shared material for one preset type/variant.</summary>
        public static string GetMaterialPath(FracturePattern pattern, int variant)
        {
            return $"{LibraryFolder}/{MaterialName(pattern, variant)}.mat";
        }

        /// <summary>Asset path of the shared cut-surface material for a preset.</summary>
        public static string GetInteriorMaterialPath(FracturePattern pattern)
        {
            return $"{LibraryFolder}/QD_{TypeName(pattern)}_Interior.mat";
        }

        /// <summary>
        /// Returns the shared generated material for a preset type/variant, creating the
        /// asset on first use. Loading by path keeps the reference valid across domain
        /// reloads and unused-asset unloads.
        /// </summary>
        public static Material GetMaterial(FracturePattern pattern, int variant)
        {
            string path = GetMaterialPath(pattern, variant);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            VariantSpec spec = GetSpec(pattern, variant);
            material = new Material(FindPipelineLitShader()) { name = MaterialName(pattern, variant) };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", spec.Color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", spec.Color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", spec.Smoothness);

            BakePipeline.EnsureGeneratedRoot();
            EnsureFolder(LibraryFolder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Returns a visible, pipeline-aware default for newly exposed cut faces. Profiles
        /// created by Quick Setup reference this asset directly, so the Interior Material
        /// field never starts as an unexplained None value.
        /// </summary>
        public static Material GetInteriorMaterial(FracturePattern pattern)
        {
            string path = GetInteriorMaterialPath(pattern);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            VariantSpec exterior = GetSpec(pattern, 0);
            Color cutColor = Color.Lerp(exterior.Color, new Color(0.12f, 0.1f, 0.08f),
                pattern == FracturePattern.RadialGlass ? 0.08f : 0.28f);
            float smoothness = pattern == FracturePattern.RadialGlass
                ? 0.8f
                : Mathf.Min(exterior.Smoothness, 0.18f);

            material = new Material(FindPipelineLitShader())
                { name = $"QD_{TypeName(pattern)}_Interior" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", cutColor);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", cutColor);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);

            BakePipeline.EnsureGeneratedRoot();
            EnsureFolder(LibraryFolder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static string MaterialName(FracturePattern pattern, int variant)
        {
            return $"QD_{TypeName(pattern)}_{ClampVariant(variant) + 1:00}";
        }

        static string TypeName(FracturePattern pattern)
        {
            switch (pattern)
            {
                case FracturePattern.RadialGlass: return "Glass";
                case FracturePattern.DirectionalWood: return "Wood";
                case FracturePattern.BrickCells: return "Brick";
                case FracturePattern.LayeredSlabs: return "Slab";
                case FracturePattern.ColumnarPillars: return "Basalt";
                case FracturePattern.ImpactShatter: return "Stone";
                default: return "Concrete";
            }
        }

        static VariantSpec GetSpec(FracturePattern pattern, int variant)
        {
            int index = ClampVariant(variant);
            switch (pattern)
            {
                case FracturePattern.RadialGlass: return m_glass[index];
                case FracturePattern.DirectionalWood: return m_wood[index];
                case FracturePattern.BrickCells: return m_brick[index];
                case FracturePattern.LayeredSlabs: return m_slab[index];
                case FracturePattern.ColumnarPillars: return m_basalt[index];
                case FracturePattern.ImpactShatter: return m_stone[index];
                default: return m_concrete[index];
            }
        }

        static int ClampVariant(int variant)
        {
            return Mathf.Clamp(variant, 0, VariantCount - 1);
        }

        // Same recursive folder pattern as BakePipeline.EnsureFolder.
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
