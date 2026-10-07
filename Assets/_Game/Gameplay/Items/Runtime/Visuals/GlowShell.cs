using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// A glow laid over a model as shells: copies of each of its meshes, drawn again with a glow material on every
    /// submesh, one copy per layer of the glow. (A material added to the model's own renderer would only cover its
    /// last submesh.) Works on any model whatever shader it uses; see <see cref="GlowRim"/> for what the layers are.
    /// </summary>
    public static class GlowShell
    {
        private const string ShellName = "GlowShell";
        private static readonly List<Material> Scratch = new List<Material>(4);
        private static readonly List<MeshFilter> Filters = new List<MeshFilter>(8);

        /// <summary>Lays the glow's layers over every mesh under <paramref name="model"/>, replacing shells it already wears.</summary>
        public static void Attach(GameObject model, IReadOnlyList<Material> layers)
        {
            if (model == null || layers == null || layers.Count == 0) return;
            Detach(model);
            model.GetComponentsInChildren(true, Filters);
            foreach (var filter in Filters)
            {
                Mesh mesh = filter.sharedMesh;
                var source = filter.GetComponent<MeshRenderer>();
                // A shell being removed this frame is still in the hierarchy: never build a shell of a shell.
                if (mesh == null || source == null || filter.name == ShellName) continue;
                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    var shell = new GameObject(ShellName) { layer = filter.gameObject.layer };
                    shell.transform.SetParent(filter.transform, false);
                    shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = shell.AddComponent<MeshRenderer>();
                    Scratch.Clear();
                    for (int i = 0; i < mesh.subMeshCount; i++) Scratch.Add(layer);
                    renderer.SetSharedMaterials(Scratch);
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                }
            }
            Filters.Clear();
        }

        public static void Detach(GameObject model)
        {
            if (model == null) return;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == ShellName) Object.Destroy(filter.gameObject);
        }
    }
}
