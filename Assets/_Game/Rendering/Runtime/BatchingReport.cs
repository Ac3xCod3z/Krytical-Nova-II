using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// What draws the world, and what falls out of the GPU Resident Drawer (which batches every MeshRenderer it can
    /// take into a few instanced draws) and why: skinned, a MaterialPropertyBlock set on it, a shader without DOTS
    /// instancing, a script with a per-object render callback, part of a static batch. Also the set-pass calls'
    /// ingredients (materials and shader variants in view) and the shadow casters by kind of thing. One pass over the
    /// loaded renderers, cached per shader and per script type: about 8,500 renderers in Zone01 take well under a
    /// second. Krytical Nova ▸ Rendering ▸ Batching Report (Open Scene) in the Editor, <c>gm.batching</c> in game.
    /// See Design/Rendering-Bible.md, "Batching".
    /// </summary>
    public static class BatchingReport
    {
        /// <summary>Why a renderer is not drawn by the GPU Resident Drawer; <see cref="Resident"/> when it is.</summary>
        public enum Reason { Resident, Skinned, Particles, OtherRenderer, PropertyBlock, ShaderWithoutDotsInstancing, RenderCallback, StaticBatch, NoMesh }

        /// <summary>The report stops by itself past this many seconds, whatever it has, rather than hold the frame.</summary>
        public const double TimeLimitSeconds = 60;
        private const int TopRows = 12;

        public sealed class Result
        {
            public string Text, Summary;
            public int Renderers, Resident, NotResident, Materials, Variants, ShadowCasters;
            public readonly Dictionary<Reason, int> ByReason = new Dictionary<Reason, int>();
            public double Seconds;
            public bool Cancelled;
        }

        private static readonly Dictionary<Shader, bool> DotsByShader = new Dictionary<Shader, bool>();
        private static readonly Dictionary<Type, bool> CallbackByType = new Dictionary<Type, bool>();
        private static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>(16);
        private static readonly List<Material> Shared = new List<Material>(8);

        /// <summary>The reason for one renderer, as the report sorts it.</summary>
        public static Reason Classify(Renderer renderer)
        {
            switch (renderer)
            {
                case SkinnedMeshRenderer _: return Reason.Skinned;
                case ParticleSystemRenderer _: return Reason.Particles;
                case MeshRenderer _: break;
                default: return Reason.OtherRenderer;
            }
            if (renderer.isPartOfStaticBatch) return Reason.StaticBatch;
            if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return Reason.NoMesh;
            if (renderer.HasPropertyBlock()) return Reason.PropertyBlock;
            renderer.GetSharedMaterials(Shared);
            foreach (Material material in Shared)
                if (material != null && material.shader != null && !HasDotsInstancing(material.shader)) return Reason.ShaderWithoutDotsInstancing;
            renderer.GetComponents(Behaviours);
            foreach (MonoBehaviour behaviour in Behaviours)
                if (behaviour != null && HasRenderCallback(behaviour.GetType())) return Reason.RenderCallback;
            return Reason.Resident;
        }

        /// <summary>
        /// Builds the report over every enabled renderer in the loaded scenes. <paramref name="progress"/> is told the
        /// share done every few hundred renderers and stops the pass by returning true.
        /// </summary>
        public static Result Build(Func<float, bool> progress = null)
        {
            var clock = Stopwatch.StartNew();
            var result = new Result();
            foreach (Reason reason in Enum.GetValues(typeof(Reason))) result.ByReason[reason] = 0;
            var offenders = new Dictionary<string, Group>();
            var shaders = new Dictionary<string, ShaderRow>();
            var materials = new HashSet<Material>();
            var variants = new HashSet<string>();
            var casters = new Dictionary<string, int[]>(); // kind -> all casters, small casters
            var keywords = new List<string>(16);

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                if ((i & 255) == 0 && i > 0)
                {
                    if (progress != null && progress((float)i / renderers.Length)) { result.Cancelled = true; break; }
                    if (clock.Elapsed.TotalSeconds > TimeLimitSeconds) { result.Cancelled = true; break; }
                }
                Renderer r = renderers[i];
                if (r == null || !r.enabled) continue;
                result.Renderers++;
                Reason reason = Classify(r);
                result.ByReason[reason]++;
                if (reason == Reason.Resident) result.Resident++;
                else
                {
                    result.NotResident++;
                    string what = reason == Reason.ShaderWithoutDotsInstancing ? FirstShaderWithoutDots(r) : Owner(r);
                    string key = Label(reason) + " | " + what;
                    if (!offenders.TryGetValue(key, out Group group)) offenders[key] = group = new Group { Reason = reason, What = what, Example = Path(r.transform) };
                    group.Count++;
                }

                r.GetSharedMaterials(Shared);
                foreach (Material m in Shared)
                {
                    if (m == null || m.shader == null) continue;
                    materials.Add(m);
                    keywords.Clear();
                    foreach (LocalKeyword k in m.enabledKeywords) keywords.Add(k.name);
                    keywords.Sort(StringComparer.Ordinal);
                    string variant = m.shader.name + "|" + string.Join(" ", keywords);
                    variants.Add(variant);
                    if (!shaders.TryGetValue(m.shader.name, out ShaderRow row)) shaders[m.shader.name] = row = new ShaderRow();
                    row.Renderers++;
                    row.Materials.Add(m);
                    row.Variants.Add(variant);
                }

                if (r.shadowCastingMode != ShadowCastingMode.Off && r is MeshRenderer)
                {
                    result.ShadowCasters++;
                    GeneratedLod lod = r.GetComponentInParent<GeneratedLod>();
                    string kind = lod != null ? lod.category.ToString() : "(no LOD group)";
                    if (!casters.TryGetValue(kind, out int[] counts)) casters[kind] = counts = new int[2];
                    counts[0]++;
                    if (r.bounds.size.magnitude < 1.5f) counts[1]++;
                }
            }
            result.Materials = materials.Count;
            result.Variants = variants.Count;
            result.Seconds = clock.Elapsed.TotalSeconds;
            result.Text = Write(result, offenders, shaders, casters);
            result.Summary = string.Format(CultureInfo.InvariantCulture,
                "{0} renderers: {1} drawn by the GPU Resident Drawer, {2} not ({3} skinned, {4} particles, {5} property block, {6} shader, {7} callback, {8} other); {9} materials in {10} shader variants; {11} shadow casters. {12:0.00} s{13}.",
                result.Renderers, result.Resident, result.NotResident, result.ByReason[Reason.Skinned], result.ByReason[Reason.Particles],
                result.ByReason[Reason.PropertyBlock], result.ByReason[Reason.ShaderWithoutDotsInstancing], result.ByReason[Reason.RenderCallback],
                result.ByReason[Reason.OtherRenderer] + result.ByReason[Reason.StaticBatch] + result.ByReason[Reason.NoMesh],
                result.Materials, result.Variants, result.ShadowCasters, result.Seconds, result.Cancelled ? " (stopped early: partial)" : string.Empty);
            return result;
        }

        private sealed class Group { public Reason Reason; public string What, Example; public int Count; }
        private sealed class ShaderRow { public int Renderers; public readonly HashSet<Material> Materials = new HashSet<Material>(); public readonly HashSet<string> Variants = new HashSet<string>(); }

        private static string Write(Result result, Dictionary<string, Group> offenders, Dictionary<string, ShaderRow> shaders, Dictionary<string, int[]> casters)
        {
            var inv = CultureInfo.InvariantCulture;
            var text = new StringBuilder(16 * 1024);
            text.Append("Krytical Nova II batching report  ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv))
                .Append(Application.isEditor ? "  EDITOR" : string.Empty).Append('\n');
            text.Append("Scenes: ");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                text.Append(i > 0 ? ", " : string.Empty).Append(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
            text.Append('\n');
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            text.Append("Graphics: ").Append(SystemInfo.graphicsDeviceType).Append(", threading ").Append(SystemInfo.renderingThreadingMode)
                .Append(", ").Append(SystemInfo.graphicsDeviceName).Append('\n');
            if (pipeline != null)
                text.Append("Pipeline ").Append(pipeline.name).Append(": SRP Batcher ").Append(pipeline.useSRPBatcher ? "on" : "off")
                    // Read by name: its enum lives in the GPU-driven assembly, which this one does not reference.
                    .Append(", GPU Resident Drawer ").Append(typeof(UniversalRenderPipelineAsset).GetProperty("gpuResidentDrawerMode")?.GetValue(pipeline))
                    .Append(", its occlusion culling ").Append(pipeline.gpuResidentDrawerEnableOcclusionCullingInCameras ? "on" : "off").Append('\n');
            text.Append('\n').Append(result.Summary).Append("\n\nNOT DRAWN BY THE RESIDENT DRAWER, BY REASON\n");
            foreach (KeyValuePair<Reason, int> pair in result.ByReason)
                if (pair.Key != Reason.Resident && pair.Value > 0) text.Append("  ").Append(Label(pair.Key).PadRight(34)).Append(pair.Value.ToString(inv).PadLeft(6)).Append('\n');

            var groups = new List<Group>(offenders.Values);
            groups.Sort((a, b) => a.Reason != b.Reason ? a.Reason.CompareTo(b.Reason) : b.Count.CompareTo(a.Count));
            Reason current = Reason.Resident;
            int shown = 0;
            foreach (Group g in groups)
            {
                if (g.Reason != current) { current = g.Reason; shown = 0; text.Append('\n').Append(Label(current).ToUpperInvariant()).Append(" (most first)\n"); }
                if (shown++ >= TopRows) continue;
                text.Append("  ").Append(g.Count.ToString(inv).PadLeft(5)).Append("  ").Append(g.What).Append("   e.g. ").Append(g.Example).Append('\n');
            }

            text.Append("\nSHADERS (renderers, materials, variants; variants are what set-pass calls are made of)\n");
            var rows = new List<KeyValuePair<string, ShaderRow>>(shaders);
            rows.Sort((a, b) => b.Value.Variants.Count != a.Value.Variants.Count ? b.Value.Variants.Count.CompareTo(a.Value.Variants.Count) : b.Value.Renderers.CompareTo(a.Value.Renderers));
            foreach (KeyValuePair<string, ShaderRow> row in rows)
                text.Append("  ").Append(row.Value.Renderers.ToString(inv).PadLeft(6)).Append(row.Value.Materials.Count.ToString(inv).PadLeft(6))
                    .Append(row.Value.Variants.Count.ToString(inv).PadLeft(6)).Append("  ").Append(row.Key).Append('\n');

            text.Append("\nSHADOW CASTERS (mesh renderers) BY KIND OF THING: all, smaller than 1.5 m\n");
            var kinds = new List<KeyValuePair<string, int[]>>(casters);
            kinds.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
            foreach (KeyValuePair<string, int[]> kind in kinds)
                text.Append("  ").Append(kind.Key.PadRight(16)).Append(kind.Value[0].ToString(inv).PadLeft(6)).Append(kind.Value[1].ToString(inv).PadLeft(6)).Append('\n');
            return text.ToString();
        }

        private static string Label(Reason reason)
        {
            switch (reason)
            {
                case Reason.Skinned: return "skinned (batched GPU skinning)";
                case Reason.Particles: return "particle system";
                case Reason.OtherRenderer: return "other renderer (trail, line, sprite)";
                case Reason.PropertyBlock: return "material property block set";
                case Reason.ShaderWithoutDotsInstancing: return "shader without DOTS instancing";
                case Reason.RenderCallback: return "script with a render callback";
                case Reason.StaticBatch: return "part of a static batch";
                case Reason.NoMesh: return "mesh renderer with no mesh";
                default: return "drawn by the resident drawer";
            }
        }

        /// <summary>The shader can be drawn through BatchRendererGroup: it declares DOTS_INSTANCING_ON (URP's DOTS.hlsl, Shader Graph).</summary>
        public static bool HasDotsInstancing(Shader shader)
        {
            if (DotsByShader.TryGetValue(shader, out bool has)) return has;
            has = shader.keywordSpace.FindKeyword("DOTS_INSTANCING_ON").isValid;
            DotsByShader[shader] = has;
            return has;
        }

        private static bool HasRenderCallback(Type type)
        {
            if (CallbackByType.TryGetValue(type, out bool has)) return has;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            has = type.GetMethod("OnRenderObject", flags) != null || type.GetMethod("OnWillRenderObject", flags) != null;
            CallbackByType[type] = has;
            return has;
        }

        private static string FirstShaderWithoutDots(Renderer r)
        {
            r.GetSharedMaterials(Shared);
            foreach (Material m in Shared)
                if (m != null && m.shader != null && !HasDotsInstancing(m.shader)) return m.shader.name;
            return "?";
        }

        // What a renderer draws: its mesh (Synty names say what they are), or its object, with Unity's " (12)" copy number off.
        private static string Owner(Renderer r)
        {
            string name = r is MeshRenderer && r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null ? filter.sharedMesh.name
                : r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null ? skinned.sharedMesh.name : r.name;
            int cut = name.IndexOf(" (", StringComparison.Ordinal);
            return (cut > 0 ? name.Substring(0, cut) : name) + " / " + r.GetType().Name;
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
