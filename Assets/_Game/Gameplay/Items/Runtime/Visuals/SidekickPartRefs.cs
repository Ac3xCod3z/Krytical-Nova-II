using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Builds a <see cref="SidekickPartRef"/> from a Sidekick part model (the FBX's SkinnedMeshRenderer). Shared by the
    /// editor gear generator and the runtime part library, so both read a part exactly the same way.
    /// </summary>
    public static class SidekickPartRefs
    {
        public static SidekickPartRef FromRenderer(SkinnedMeshRenderer smr, BodyRegion region)
        {
            if (smr == null || smr.sharedMesh == null) return default;

            // Record every dangle-bone chain so a pruned character skeleton can grow the bones on demand.
            var bones = smr.bones;
            var names = new string[bones.Length];
            var extras = new List<ExtraBone>();
            var seen = new HashSet<string>();
            var chain = new List<Transform>();
            for (int b = 0; b < bones.Length; b++)
            {
                var bone = bones[b];
                names[b] = bone != null ? bone.name : "root";
                chain.Clear();
                for (var t = bone; t != null && t.name.Contains("_dyn_"); t = t.parent) chain.Add(t);
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    var t = chain[i];
                    if (!seen.Add(t.name) || t.parent == null) continue;
                    extras.Add(new ExtraBone
                    {
                        name = t.name, parent = t.parent.name,
                        localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale
                    });
                }
            }

            return new SidekickPartRef
            {
                region = region,
                mesh = smr.sharedMesh,
                boneNames = names,
                extraBones = extras.ToArray()
            };
        }
    }
}
