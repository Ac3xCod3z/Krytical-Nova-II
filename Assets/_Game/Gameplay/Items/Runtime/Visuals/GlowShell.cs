using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// A glow laid over a model as a shell: a copy of each of its meshes, drawn once more with the glow material on
    /// every submesh. (A material added to the model's own renderer would only cover its last submesh.) Works on any
    /// model whatever shader it uses; weapons wear it for their Temper, pets for their growth stage.
    /// </summary>
    public static class GlowShell
    {
        private const string ShellName = "GlowShell";
        private static readonly List<Material> Scratch = new List<Material>(4);

        /// <summary>Lays <paramref name="glow"/> over every mesh under <paramref name="model"/>, replacing a shell it already wears.</summary>
        public static void Attach(GameObject model, Material glow)
        {
            if (model == null || glow == null) return;
            Detach(model);
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                var source = filter.GetComponent<MeshRenderer>();
                // A shell being removed this frame is still in the hierarchy: never build a shell of a shell.
                if (mesh == null || source == null || filter.name == ShellName) continue;
                var shell = new GameObject(ShellName) { layer = filter.gameObject.layer };
                shell.transform.SetParent(filter.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = shell.AddComponent<MeshRenderer>();
                Scratch.Clear();
                for (int i = 0; i < mesh.subMeshCount; i++) Scratch.Add(glow);
                renderer.SetSharedMaterials(Scratch);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
        }

        public static void Detach(GameObject model)
        {
            if (model == null) return;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == ShellName) Object.Destroy(filter.gameObject);
        }
    }
}
