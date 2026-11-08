using System.Linq;
using KryticalNova.EditorTools;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The publish gate's object-by-object comparison: paths from a bundle dump, and what differs from the scene (a swap included).</summary>
    public class ObjectCompareTests
    {
        // Root "Map" (id 10, transform 110) holds "Sky" (id 11, transform 111, inactive); a script sits on each.
        private const string Dump =
            "ID: 10 (ClassID: 1) GameObject\n\tm_Name \"Map\" (string)\n\tm_IsActive 1 (bool)\n\n" +
            "ID: 11 (ClassID: 1) GameObject\n\tm_Name \"Sky\" (string)\n\tm_IsActive 0 (bool)\n\n" +
            "ID: 110 (ClassID: 4) Transform\n\tm_GameObject  (PPtr<GameObject>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 10 (SInt64)\n\tm_Father  (PPtr<Transform>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 0 (SInt64)\n\n" +
            "ID: 111 (ClassID: 4) Transform\n\tm_GameObject  (PPtr<GameObject>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 11 (SInt64)\n\tm_Father  (PPtr<Transform>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 110 (SInt64)\n\n" +
            "ID: 20 (ClassID: 114) MonoBehaviour\n\tm_GameObject  (PPtr<GameObject>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 11 (SInt64)\n\tm_Enabled 0 (UInt8)\n\tm_Script  (PPtr<MonoScript>)\n\t\tm_PathID 7 (SInt64)\n\tm_Name \"\" (string)\n\tkeyLight  (PPtr<Light>)\n\n" +
            "ID: 21 (ClassID: 114) MonoBehaviour\n\tm_GameObject  (PPtr<GameObject>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 10 (SInt64)\n\tm_Enabled 1 (UInt8)\n\tm_Script  (PPtr<MonoScript>)\n\t\tm_PathID 8 (SInt64)\n\tm_Name \"\" (string)\n\tradius 3 (float)\n\n" +
            "ID: 22 (ClassID: 108) Light\n\tm_GameObject  (PPtr<GameObject>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 11 (SInt64)\n\tm_Enabled 1 (UInt8)\n\n";

        private static ObjectStates Bundle() => ObjectCompare.FromDump(BundleDump.Parse(Dump.Split('\n')).ToList());

        private static ObjectStates SceneAsTheBundle()
        {
            var scene = new ObjectStates();
            scene.AddObject("Map", true);
            scene.AddObject("Map/Sky", false);
            scene.AddComponent("Map/Sky", ObjectStates.BehaviourPrefix + "keyLight", false);
            scene.AddComponent("Map", ObjectStates.BehaviourPrefix + "radius", true);
            scene.AddComponent("Map/Sky", "Light", true);
            scene.ScriptNames["keyLight"] = "EnvironmentPresenter";
            return scene;
        }

        [Test]
        public void ReadsHierarchyPathsAndStatesFromADump()
        {
            ObjectStates bundle = Bundle();
            Assert.AreEqual(new[] { true }, bundle.Objects["Map"].ToArray());
            Assert.AreEqual(new[] { false }, bundle.Objects["Map/Sky"].ToArray());
            Assert.AreEqual(new[] { false }, bundle.Components["Map/Sky|MB:keyLight"].ToArray());
            Assert.AreEqual(new[] { true }, bundle.Components["Map|MB:radius"].ToArray());
            Assert.AreEqual(new[] { true }, bundle.Components["Map/Sky|Light"].ToArray());
        }

        [Test]
        public void ABundleAsItsSceneHasNoDifference()
        {
            Assert.IsEmpty(ObjectCompare.Compare("World_Test", SceneAsTheBundle(), Bundle()));
        }

        [Test]
        public void ASwitchedOffComponentIsNamedWithItsPath()
        {
            ObjectStates scene = SceneAsTheBundle();
            scene.Components["Map/Sky|MB:keyLight"][0] = true; // on in the scene, off in the bundle
            var lines = ObjectCompare.Compare("World_Test", scene, Bundle());
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("EnvironmentPresenter on 'Map/Sky' is SWITCHED OFF in the built bundle but on in the scene", lines[0]);
        }

        [Test]
        public void ASwapIsCaughtThoughTheCountsAreTheSame()
        {
            ObjectStates scene = SceneAsTheBundle();
            // the scene has the presenter ON and the light OFF; the bundle has the presenter OFF and the light ON: one off each way
            scene.Components["Map/Sky|MB:keyLight"][0] = true;
            scene.Components["Map/Sky|Light"][0] = false;
            var lines = ObjectCompare.Compare("World_Test", scene, Bundle());
            Assert.AreEqual(2, lines.Count);
            Assert.IsTrue(lines.Any(l => l.Contains("SWITCHED OFF")));
            Assert.IsTrue(lines.Any(l => l.Contains("SWITCHED ON")));
        }

        [Test]
        public void AnInactiveObjectIsNamed()
        {
            ObjectStates scene = SceneAsTheBundle();
            scene.Objects["Map/Sky"][0] = true;
            var lines = ObjectCompare.Compare("World_Test", scene, Bundle());
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("'Map/Sky' is INACTIVE in the built bundle but active in the scene", lines[0]);
        }

        [Test]
        public void ThingsOnlyTheSceneHasAreNotAProblem()
        {
            ObjectStates scene = SceneAsTheBundle();
            scene.AddObject("Map/EditorOnlyGizmo", false);
            scene.AddComponent("Map/EditorOnlyGizmo", "MeshRenderer", false);
            Assert.IsEmpty(ObjectCompare.Compare("World_Test", scene, Bundle()));
        }

        [Test]
        public void TheListIsCutAtTheLimitWithACountOfTheRest()
        {
            var scene = new ObjectStates();
            var bundle = new ObjectStates();
            for (int i = 0; i < 5; i++) bundle.AddObject("Map/Object" + i, false);
            var lines = ObjectCompare.Compare("World_Test", scene, bundle, 3);
            Assert.AreEqual(4, lines.Count);
            StringAssert.Contains("and 2 more", lines[3]);
        }
    }
}
