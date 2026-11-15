using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KryticalNova.Networking.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 15 part 1: <see cref="NetworkCombatVFXPool"/> keeps each pooled instance's particle systems after the
    /// first look instead of searching the hierarchy on every release and return. Filtered by <see cref="NetworkCombatVFXPool.Active"/>
    /// they are exactly the systems the old GetComponentsInChildren&lt;ParticleSystem&gt;() found, in the same order, as children are
    /// switched on and off; reading them again allocates nothing.
    /// </summary>
    public sealed class VfxPoolSystemsCacheTests
    {
        readonly List<GameObject> m_Made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in m_Made) if (go != null) Object.DestroyImmediate(go);
            m_Made.Clear();
        }

        GameObject Effect(out GameObject[] children)
        {
            var root = new GameObject("chunk 15 effect") { hideFlags = HideFlags.HideAndDontSave };
            m_Made.Add(root);
            root.AddComponent<ParticleSystem>();
            children = new GameObject[4];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = new GameObject("part " + i) { hideFlags = HideFlags.HideAndDontSave };
                children[i].transform.SetParent(i == 3 ? children[1].transform : root.transform, false);
                children[i].AddComponent<ParticleSystem>();
            }
            children[2].SetActive(false);   // a part the prefab ships switched off
            return root;
        }

        static List<ParticleSystem> NewWay(NetworkCombatVFXPool pool, GameObject instance)
        {
            var found = new List<ParticleSystem>();
            foreach (ParticleSystem ps in pool.SystemsOf(instance)) if (NetworkCombatVFXPool.Active(ps, instance)) found.Add(ps);
            return found;
        }

        [Test]
        public void TheActiveSystems_AreTheOldSearchesAnswer_AsPartsSwitchOnAndOff()
        {
            var host = new GameObject("chunk 15 pool") { hideFlags = HideFlags.HideAndDontSave };
            m_Made.Add(host);
            var pool = host.AddComponent<NetworkCombatVFXPool>();
            GameObject effect = Effect(out GameObject[] parts);
            CollectionAssert.AreEqual(effect.GetComponentsInChildren<ParticleSystem>(), NewWay(pool, effect), "as shipped");
            parts[2].SetActive(true);
            CollectionAssert.AreEqual(effect.GetComponentsInChildren<ParticleSystem>(), NewWay(pool, effect), "a part switched on");
            parts[1].SetActive(false);
            CollectionAssert.AreEqual(effect.GetComponentsInChildren<ParticleSystem>(), NewWay(pool, effect), "a part and its child switched off");
            effect.SetActive(false);
            CollectionAssert.AreEqual(effect.GetComponentsInChildren<ParticleSystem>(), NewWay(pool, effect), "the whole copy off: its switched-on parts, as the old search gave");
        }

        [Test]
        public void ReadingAKeptInstance_AllocatesNothing()
        {
            var host = new GameObject("chunk 15 pool") { hideFlags = HideFlags.HideAndDontSave };
            m_Made.Add(host);
            var pool = host.AddComponent<NetworkCombatVFXPool>();
            GameObject effect = Effect(out _);
            pool.SystemsOf(effect);   // the first look
            Assert.That(() =>
            {
                for (int r = 0; r < 100; r++)
                    foreach (ParticleSystem ps in pool.SystemsOf(effect)) if (NetworkCombatVFXPool.Active(ps, effect)) { }
            }, Is.Not.AllocatingGCMemory());
            Debug.Log("[perf chunk 15] VFX pool: 100 returns read the kept systems with 0 bytes (was a GetComponentsInChildren array each)");
        }
    }
}
