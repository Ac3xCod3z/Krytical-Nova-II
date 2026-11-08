using System.Linq;
using KryticalNova.EditorTools;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The publish gate's text work: reading binary2text's dump of a scene bundle and comparing it with its scene.</summary>
    public class BundleDumpTests
    {
        // Two GameObjects (one inactive) and two MonoBehaviours: the sky presenter's fields on the first (switched off), another script's on the second.
        private const string Dump =
            "ID: 10 (ClassID: 1) GameObject\n" +
            "\tm_Component  (vector)\n\t\tsize 1 (int)\n\t\tdata  (ComponentPair)\n\t\t\tcomponent  (PPtr<Component>)\n\t\t\t\tm_FileID 0 (int)\n\t\t\t\tm_PathID 20 (SInt64)\n" +
            "\n\tm_Layer 0 (unsigned int)\n\tm_Name \"Sky\" (string)\n\tm_Tag 0 (UInt16)\n\tm_IsActive 1 (bool)\n\n\n" +
            "ID: 11 (ClassID: 1) GameObject\n\tm_Layer 0 (unsigned int)\n\tm_Name \"Lamp\" (string)\n\tm_IsActive 0 (bool)\n\n\n" +
            "ID: 20 (ClassID: 114) MonoBehaviour\n" +
            "\tm_GameObject  (PPtr<GameObject>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 10 (SInt64)\n" +
            "\tm_Enabled 0 (UInt8)\n\tm_Script  (PPtr<MonoScript>)\n\t\tm_FileID 1 (int)\n\t\tm_PathID 7 (SInt64)\n\tm_Name \"\" (string)\n" +
            "\tkeyLight  (PPtr<Light>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 5 (SInt64)\n\tpreviewFogInEditMode 0 (UInt8)\n\n\n" +
            "ID: 21 (ClassID: 114) MonoBehaviour\n" +
            "\tm_GameObject  (PPtr<GameObject>)\n\t\tm_FileID 0 (int)\n\t\tm_PathID 11 (SInt64)\n" +
            "\tm_Enabled 1 (UInt8)\n\tm_Script  (PPtr<MonoScript>)\n\t\tm_FileID 1 (int)\n\t\tm_PathID 8 (SInt64)\n\tm_Name \"\" (string)\n" +
            "\tradius 3 (float)\n\n\n";

        private static MapCounts SceneWithPresenterOn()
        {
            var scene = new MapCounts { InactiveObjects = 1 };
            return scene;
        }

        [Test]
        public void ReadsEnabledActiveNamesOwnersAndSignatures()
        {
            var objects = BundleDump.Parse(Dump.Split('\n'));
            Assert.AreEqual(4, objects.Count);
            Assert.AreEqual(0, objects.First(o => o.Id == 11).Active);
            Assert.AreEqual(1, objects.First(o => o.Id == 10).Active);
            Assert.AreEqual("Sky", objects.First(o => o.Id == 10).Name);
            DumpObject presenter = objects.First(o => o.Id == 20);
            Assert.AreEqual(0, presenter.Enabled);
            Assert.AreEqual(10, presenter.OwnerId);
            Assert.AreEqual("keyLight,previewFogInEditMode", presenter.Signature, "standard fields (m_Script, m_Name, ...) are left out");
        }

        [Test]
        public void CountsWhatIsSwitchedOffAndNamesTheOwner()
        {
            MapCounts bundle = BundleDump.Count(BundleDump.Parse(Dump.Split('\n')));
            Assert.AreEqual(1, bundle.DisabledBehaviours);
            Assert.AreEqual(1, bundle.InactiveObjects);
            Assert.AreEqual("on 'Sky'", bundle.OffBySignature["keyLight,previewFogInEditMode"].Key);
        }

        [Test]
        public void ABundleWithSomethingExtraSwitchedOffFailsAndSaysWhat()
        {
            MapCounts bundle = BundleDump.Count(BundleDump.Parse(Dump.Split('\n')));
            MapCounts scene = SceneWithPresenterOn(); // the scene has the presenter on and one inactive object
            var problems = BundleDump.Compare("World_Test", scene, bundle);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("1 more component(s) are switched off in the built bundle", problems[0]);
            StringAssert.Contains("on 'Sky'", problems[0]);
        }

        [Test]
        public void ABundleAsItsSceneIsClean()
        {
            MapCounts bundle = BundleDump.Count(BundleDump.Parse(Dump.Split('\n')));
            var scene = SceneWithPresenterOn();
            scene.DisabledBehaviours = 1;
            scene.AddOff("keyLight,previewFogInEditMode", "EnvironmentPresenter");
            Assert.IsEmpty(BundleDump.Compare("World_Test", scene, bundle));
        }

        [Test]
        public void FewerSwitchedOffInTheBundleThanInTheSceneIsNotAProblem()
        {
            var scene = new MapCounts { DisabledBehaviours = 5, InactiveObjects = 4 };
            Assert.IsEmpty(BundleDump.Compare("World_Test", scene, new MapCounts()));
        }

        [Test]
        public void ExtraInactiveObjectsFail()
        {
            var problems = BundleDump.Compare("World_Test", new MapCounts(), new MapCounts { InactiveObjects = 2 });
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("2 more object(s) are inactive", problems[0]);
        }
    }
}
