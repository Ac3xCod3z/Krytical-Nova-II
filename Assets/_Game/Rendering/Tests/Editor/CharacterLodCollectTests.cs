using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KryticalNova.Rendering.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 14: <see cref="CharacterLod"/> refills reused lists every few seconds instead of making four new
    /// arrays. The same skins and animators are found, every tier sets them as before and level 0 gives back their own settings, and a
    /// recollect allocates nothing (200 characters: 0 bytes).
    /// </summary>
    public sealed class CharacterLodCollectTests
    {
        readonly List<GameObject> m_Made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
        }

        CharacterLod Character(out SkinnedMeshRenderer[] skins, out Animator animator)
        {
            var root = new GameObject("chunk 14 character") { hideFlags = HideFlags.HideAndDontSave };
            m_Made.Add(root);
            animator = root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            skins = new SkinnedMeshRenderer[3];
            for (int i = 0; i < skins.Length; i++)
            {
                var part = new GameObject("part " + i) { hideFlags = HideFlags.HideAndDontSave };
                part.transform.SetParent(root.transform, false);
                if (i == 2) part.SetActive(false);   // an inactive part is found too (includeInactive)
                skins[i] = part.AddComponent<SkinnedMeshRenderer>();
                skins[i].quality = i == 0 ? SkinQuality.Bone4 : SkinQuality.Auto;
                skins[i].shadowCastingMode = i == 1 ? ShadowCastingMode.TwoSided : ShadowCastingMode.On;
            }
            var lod = root.AddComponent<CharacterLod>();
            lod.enabled = false;   // no Update of its own here: the test drives it
            return lod;
        }

        [Test]
        public void EveryTier_SetsTheSameParts_AndLevel0_GivesTheirOwnSettingsBack()
        {
            CharacterLod lod = Character(out SkinnedMeshRenderer[] skins, out _);
            lod.Recollect();
            lod.Drive(1, false);
            foreach (var s in skins) Assert.AreEqual(SkinQuality.Bone2, s.quality, "tier 1: lighter skinning on every part, the inactive one too");
            lod.Drive(2, false);
            foreach (var s in skins) { Assert.AreEqual(SkinQuality.Bone1, s.quality); Assert.AreEqual(ShadowCastingMode.Off, s.shadowCastingMode); }
            lod.Drive(3, false);
            foreach (var s in skins) Assert.IsTrue(s.forceRenderingOff, "tier 3: not drawn");
            lod.Drive(0, false);
            Assert.AreEqual(SkinQuality.Bone4, skins[0].quality, "its own quality back");
            Assert.AreEqual(SkinQuality.Auto, skins[1].quality);
            Assert.AreEqual(ShadowCastingMode.TwoSided, skins[1].shadowCastingMode, "its own shadows back");
            Assert.AreEqual(ShadowCastingMode.On, skins[2].shadowCastingMode);
            foreach (var s in skins) Assert.IsFalse(s.forceRenderingOff);
        }

        [Test]
        public void APartAddedLater_IsFoundByTheNextRecollect()
        {
            CharacterLod lod = Character(out _, out _);
            lod.Recollect();
            var gear = new GameObject("new gear") { hideFlags = HideFlags.HideAndDontSave };
            gear.transform.SetParent(lod.transform, false);
            var added = gear.AddComponent<SkinnedMeshRenderer>();
            lod.Recollect();
            lod.Drive(2, false);
            Assert.AreEqual(SkinQuality.Bone1, added.quality, "the new part takes the tier");
        }

        [Test]
        public void Recollect_200Characters_AllocatesNothing()
        {
            var lods = new List<CharacterLod>(200);
            for (int i = 0; i < 200; i++) { lods.Add(Character(out _, out _)); lods[i].Recollect(); }   // the lists grow once
            Assert.That(() => { for (int i = 0; i < lods.Count; i++) lods[i].Recollect(); }, Is.Not.AllocatingGCMemory());
            Debug.Log("[perf chunk 14] CharacterLod recollect over 200 characters: 0 bytes (was 4 new arrays + 2 component arrays each, every 5 s)");
        }
    }
}
