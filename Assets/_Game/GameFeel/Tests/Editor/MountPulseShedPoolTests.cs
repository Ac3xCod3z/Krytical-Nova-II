using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using KryticalNova.GameFeel;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Perf night chunk 5: <see cref="MountPulse"/> reuses its shed of particles instead of making and destroying one per landing.
    /// The copy is placed, turned, parented and scaled exactly as the old <c>Instantiate</c> placed it, the prefab is never changed,
    /// and 20 landings make at most as many copies as are falling at once (one when the landings are apart). Prints objects made and
    /// managed bytes per 20 landings, old vs new.
    /// </summary>
    public sealed class MountPulseShedPoolTests
    {
        GameObject m_Prefab, m_Mount;
        MountPulse m_Pulse;

        [SetUp]
        public void SetUp()
        {
            m_Prefab = new GameObject("leaves (test prefab)") { hideFlags = HideFlags.HideAndDontSave };
            ParticleSystem ps = m_Prefab.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.stopAction = ParticleSystemStopAction.Destroy;   // as Brambleback_Vanish
            main.loop = false;
            m_Mount = new GameObject("mount") { hideFlags = HideFlags.HideAndDontSave };
            m_Mount.transform.SetPositionAndRotation(new Vector3(3f, 1f, -7f), Quaternion.Euler(0f, 37f, 0f));
            m_Pulse = m_Mount.AddComponent<MountPulse>();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Mount != null) Object.DestroyImmediate(m_Mount);
            if (m_Prefab != null) Object.DestroyImmediate(m_Prefab);
        }

        int Copies() => m_Mount.GetComponentsInChildren<MountPulseShed>(true).Length;

        [Test]
        public void TheShed_IsPlacedAsTheOldCopyWas_AndThePrefabIsUntouched()
        {
            const float scale = 0.9f;
            // the old way, verbatim
            Transform mount = m_Mount.transform;
            GameObject old = Object.Instantiate(m_Prefab, mount.position + Vector3.up * 0.6f, mount.rotation, mount);
            old.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);

            MountPulseShed shed = m_Pulse.Shed(m_Prefab, scale, 10f);
            for (int round = 0; round < 2; round++)   // the fresh copy, then the reused one after the mount moved
            {
                if (round == 1)
                {
                    mount.SetPositionAndRotation(new Vector3(-20f, 4f, 9f), Quaternion.Euler(0f, 200f, 0f));
                    old.transform.SetPositionAndRotation(mount.position + Vector3.up * 0.6f, mount.rotation);
                    Assert.AreSame(shed, m_Pulse.Shed(m_Prefab, scale, 10f + MountPulse.ShedSeconds), "an ended shed is reused");
                }
                Assert.AreSame(mount, shed.transform.parent);
                Assert.That(Vector3.Distance(old.transform.position, shed.transform.position), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(old.transform.rotation, shed.transform.rotation), Is.LessThan(1e-3f));
                Assert.AreEqual(old.transform.localScale, shed.transform.localScale);
                Assert.IsTrue(shed.gameObject.activeSelf);
                Assert.AreEqual(10f + MountPulse.ShedSeconds * (round + 1), shed.Until, 1e-4f);
            }
            Assert.AreEqual(ParticleSystemStopAction.Destroy, m_Prefab.GetComponent<ParticleSystem>().main.stopAction, "the prefab keeps its own stop action");
            Assert.AreEqual(ParticleSystemStopAction.Disable, shed.GetComponent<ParticleSystem>().main.stopAction, "the copy switches off instead");
            Object.DestroyImmediate(old);
        }

        [Test]
        public void TwentyLandings_ApartMakeOneCopy_CloseMakeOnlyWhatFallsAtOnce()
        {
            for (int i = 0; i < 20; i++) m_Pulse.Shed(m_Prefab, 1f, 100f + i * 2f);
            Assert.AreEqual(1, Copies(), "landings 2 s apart");

            TearDown(); SetUp();
            for (int i = 0; i < 20; i++) m_Pulse.Shed(m_Prefab, 1f, 100f + i * 0.1f);
            int atOnce = Mathf.CeilToInt(MountPulse.ShedSeconds / 0.1f) + 1;
            Assert.LessOrEqual(Copies(), atOnce, "landings 0.1 s apart: only the copies still falling");
            Assert.Less(Copies(), 20);

            // a copy hidden by its own stop action (its particles done) is free at once
            TearDown(); SetUp();
            MountPulseShed first = m_Pulse.Shed(m_Prefab, 1f, 0f);
            first.gameObject.SetActive(false);
            Assert.AreSame(first, m_Pulse.Shed(m_Prefab, 1f, 0.2f));
            // a different prefab gets its own copy
            var other = new GameObject("sparks") { hideFlags = HideFlags.HideAndDontSave }; other.AddComponent<ParticleSystem>();
            Assert.AreNotSame(first, m_Pulse.Shed(other, 1f, 10f));
            Object.DestroyImmediate(other);
        }

        [Test]
        public void Measure_ObjectsAndGarbage_Per20Landings_OldVsNew()
        {
            // GC.GetAllocatedBytesForCurrentThread reads 0 under Unity's Mono: the Test Framework's GC.Alloc recorder decides; the
            // heap growth printed is only an estimate.
            Transform mount = m_Mount.transform;
            for (int i = 0; i < 3; i++) Object.DestroyImmediate(Object.Instantiate(m_Prefab, mount.position, mount.rotation, mount));   // warm up
            m_Pulse.Shed(m_Prefab, 1f, 0f);
            m_Pulse.Shed(m_Prefab, 1f, 2f);   // warm up the reuse path too
            var made = new GameObject[20];
            System.Action oldWay = () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    made[i] = Object.Instantiate(m_Prefab, mount.position + Vector3.up * 0.6f, mount.rotation, mount);
                    made[i].transform.localScale = Vector3.one;
                }
            };
            float t = 2f;
            System.Action newWay = () => { for (int i = 0; i < 20; i++) m_Pulse.Shed(m_Prefab, 1f, t += 2f); };

            long before = GC.GetTotalMemory(false); oldWay(); long oldBytes = GC.GetTotalMemory(false) - before;
            foreach (var g in made) Object.DestroyImmediate(g);
            before = GC.GetTotalMemory(false); newWay(); long newBytes = GC.GetTotalMemory(false) - before;
            string line = $"[perf chunk 5] MountPulse, 20 landings 2 s apart: old 20 objects / ~{oldBytes} bytes of heap, new {Copies()} object(s) / ~{newBytes} bytes";
            TestContext.WriteLine(line);
            Debug.Log(line);
            Assert.AreEqual(1, Copies(), line);
            Assert.That(oldWay, Is.AllocatingGCMemory(), "a new copy per landing made garbage");
            foreach (var g in made) Object.DestroyImmediate(g);
            Assert.That(newWay, Is.Not.AllocatingGCMemory(), "reusing the copy makes none");
            Assert.AreEqual(1, Copies(), line);
        }
    }
}
