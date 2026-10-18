using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using KryticalNova.EditorTools.AssetRestore;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The restore tool against a small package made here: index, dependency walk, restore, log, catalog.</summary>
    public class AssetRestoreTests
    {
        private const string Folder = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Prefab = "11111111111111111111111111111111";
        private const string Material = "22222222222222222222222222222222";
        private const string Texture = "33333333333333333333333333333333";
        private const string Other = "44444444444444444444444444444444";

        private string root;
        private string package;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "KN_AssetRestoreTests_" + Path.GetRandomFileName()).Replace('\\', '/');
            Directory.CreateDirectory(root);
            package = Path.Combine(root, "POLYGON_Test.unitypackage");
            WritePackage(package, new[]
            {
                Entry(Folder, "Assets/Synty/PolygonTest/Props", null, Meta(Folder, "folderAsset: yes")),
                Entry(Prefab, "Assets/Synty/PolygonTest/Props/SM_Prop_Lamp_01.prefab",
                    "%YAML 1.1\n--- !u!23 &1\nMeshRenderer:\n  m_Materials:\n  - {fileID: 2100000, guid: " + Material + ", type: 2}\n  m_Script: {fileID: 11500000, guid: 0000000000000000e000000000000000, type: 0}\n",
                    Meta(Prefab, "")),
                Entry(Material, "Assets/Synty/PolygonTest/Props/Lamp.mat",
                    "%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_TexEnvs:\n  - _BaseMap:\n      m_Texture: {fileID: 2800000, guid: " + Texture + ", type: 3}\n",
                    Meta(Material, "")),
                Entry(Texture, "Assets/Synty/PolygonTest/Props/Lamp.png", "\x89PNG binary bytes", Meta(Texture, "")),
                Entry(Other, "Assets/Synty/PolygonTest/Props/SM_Prop_Barrel_01.prefab", "%YAML 1.1\n--- !u!1 &1\nGameObject: {}\n", Meta(Other, "")),
            });
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }

        [Test]
        public void IndexReadsPathsSizesAndDependencies()
        {
            var index = Index();
            Assert.IsTrue(index.TryGet(Prefab, out var prefab));
            Assert.AreEqual("Assets/Synty/PolygonTest/Props/SM_Prop_Lamp_01.prefab", prefab.AssetPath);
            CollectionAssert.AreEquivalent(new[] { Material }, prefab.Deps, "built-in GUIDs and its own are left out");
            Assert.IsTrue(index.TryGet(Material, out var material));
            CollectionAssert.AreEquivalent(new[] { Texture }, material.Deps);
            Assert.IsTrue(index.TryGet(Folder, out var folder));
            Assert.IsTrue(folder.IsFolder);
        }

        [Test]
        public void IndexSurvivesTheCache()
        {
            var index = Index();
            string cache = Path.Combine(root, "Library/index.bin");
            index.Save(cache);
            var loaded = PackageIndex.Load(cache);
            Assert.AreEqual(index.Count, loaded.Count);
            Assert.IsTrue(loaded.TryGet(Prefab, out var prefab));
            CollectionAssert.AreEquivalent(new[] { Material }, prefab.Deps);
        }

        [Test]
        public void PlanTakesTheAssetAndWhatItNeedsButNotTheRestOfThePack()
        {
            var plan = AssetRestorer.PlanFor(Index(), new[] { Prefab }, _ => false, pathExists: _ => false);
            var guids = plan.Items.Select(i => i.Guid).ToList();
            CollectionAssert.AreEquivalent(new[] { Prefab, Material, Texture, Folder }, guids);
            Assert.AreEqual(3, plan.Files);
        }

        [Test]
        public void PlanSkipsWhatTheProjectAlreadyHas()
        {
            var plan = AssetRestorer.PlanFor(Index(), new[] { Prefab }, g => g == Material, pathExists: _ => false);
            CollectionAssert.DoesNotContain(plan.Items.Select(i => i.Guid).ToList(), Material);
            CollectionAssert.DoesNotContain(plan.Items.Select(i => i.Guid).ToList(), Texture, "what is behind an existing asset is not followed");
        }

        [Test]
        public void RestoreWritesFilesWithTheirOriginalMetaAndNeverOverwrites()
        {
            var index = Index();
            string project = Path.Combine(root, "Project").Replace('\\', '/');
            string existing = Path.Combine(project, "Assets/Synty/PolygonTest/Props/Lamp.png");
            Directory.CreateDirectory(Path.GetDirectoryName(existing));
            File.WriteAllText(existing, "the project's own copy");

            var plan = AssetRestorer.PlanFor(index, new[] { Prefab }, _ => false, pathExists: p => Directory.Exists(Path.Combine(project, p)));
            var result = AssetRestorer.Execute(index, plan, project, null, CancellationToken.None);

            string prefabPath = Path.Combine(project, "Assets/Synty/PolygonTest/Props/SM_Prop_Lamp_01.prefab");
            Assert.IsTrue(File.Exists(prefabPath));
            StringAssert.Contains("guid: " + Prefab, File.ReadAllText(prefabPath + ".meta"));
            Assert.IsTrue(File.Exists(Path.Combine(project, "Assets/Synty/PolygonTest/Props/Lamp.mat.meta")));
            Assert.AreEqual("the project's own copy", File.ReadAllText(existing), "an existing file is left alone");
            Assert.IsTrue(result.Skipped.Any(s => s.Contains("Lamp.png")));
            Assert.IsFalse(File.Exists(Path.Combine(project, "Assets/Synty/PolygonTest/Props/SM_Prop_Barrel_01.prefab")), "only what was asked for and its needs");
        }

        [Test]
        public void RestoreGoesBackToTheOldPathFromTheLog()
        {
            var index = Index();
            var old = new Dictionary<string, string> { { Texture, "Assets/Synty/Moved/Lamp.png" } };
            var plan = AssetRestorer.PlanFor(index, new[] { Material }, _ => false, old, _ => false);
            Assert.AreEqual("Assets/Synty/Moved/Lamp.png", plan.Targets[Texture]);
        }

        [Test]
        public void TargetsStayInsideAssets()
        {
            Assert.IsTrue(AssetRestorer.IsSafeTarget("Assets/Synty/X/a.prefab"));
            Assert.IsFalse(AssetRestorer.IsSafeTarget("Assets/../ProjectSettings/x.asset"));
            Assert.IsFalse(AssetRestorer.IsSafeTarget("C:/Windows/x"));
            Assert.IsFalse(AssetRestorer.IsSafeTarget("Packages/x"));
        }

        [Test]
        public void RemovalLogRoundTrip()
        {
            var items = new List<RemovalLog.Removal>
            {
                new RemovalLog.Removal { Guid = Prefab, Path = "Assets/Synty/PolygonTest/Props/SM_Prop_Lamp_01.prefab", Bytes = 2048 },
                new RemovalLog.Removal { Guid = "55555555555555555555555555555555", Path = "Assets/_Recovery/0 (1).unity", Bytes = 10 },
            };
            string list = RemovalLog.RecordBatch(root, "PolygonTest", "Test batch.", items, Index(), "abc123def456");
            var table = RemovalLog.LoadTable(root);
            Assert.AreEqual(2, table.Count);
            StringAssert.Contains("POLYGON_Test.unitypackage", table.First(e => e.Guid == Prefab).Source);
            Assert.AreEqual("", table.First(e => e.Path.StartsWith("Assets/_Recovery")).Source);
            string log = File.ReadAllText(Path.Combine(root, AssetRestorePaths.RemovalLog));
            StringAssert.Contains("PolygonTest", log);
            StringAssert.Contains("abc123def456", log);
            string lines = File.ReadAllText(Path.Combine(root, list));
            StringAssert.Contains("SM_Prop_Lamp_01.prefab", lines);
            StringAssert.Contains("git only", lines);
        }

        [Test]
        public void CatalogKeepsRemovedRows()
        {
            string asset = Path.Combine(root, "Assets/Synty/PolygonTest/Props/SM_Prop_Lamp_01.prefab");
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            File.WriteAllText(asset, "x");
            File.WriteAllText(asset + ".meta", Meta(Prefab, ""));
            var first = AssetCatalog.Snapshot(root, Index(), null, null);
            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(AssetCatalog.InProject, first[0].Status);
            StringAssert.Contains("POLYGON_Test.unitypackage", first[0].Source);

            string table = Path.Combine(root, "catalog.tsv");
            AssetCatalog.Save(table, first, "today");
            File.Delete(asset);
            File.Delete(asset + ".meta");
            var second = AssetCatalog.Snapshot(root, Index(), AssetCatalog.Load(table), new Dictionary<string, string> { { Prefab, "2026-10-18" } });
            Assert.AreEqual(1, second.Count);
            Assert.AreEqual(AssetCatalog.Removed, second[0].Status);
            Assert.AreEqual("2026-10-18", second[0].RemovedOn);

            string page = Path.Combine(root, "catalog.html");
            AssetCatalog.SavePage(page, second, "today");
            StringAssert.Contains("SM_Prop_Lamp_01.prefab", File.ReadAllText(page));
        }

        // ---------------------------------------------------------------- a package made by hand

        private PackageIndex Index()
        {
            return PackageIndex.Build(new[] { root }, null, null, CancellationToken.None);
        }

        private static string Meta(string guid, string extra) => "fileFormatVersion: 2\nguid: " + guid + "\n" + extra + "\n";

        private static (string guid, string path, string asset, string meta) Entry(string guid, string path, string asset, string meta) => (guid, path, asset, meta);

        private static void WritePackage(string path, IEnumerable<(string guid, string path, string asset, string meta)> entries)
        {
            using (var file = File.Create(path))
            using (var gz = new GZipStream(file, CompressionMode.Compress))
            {
                foreach (var e in entries)
                {
                    WriteTar(gz, "./" + e.guid + "/", null, '5');
                    if (e.asset != null) WriteTar(gz, "./" + e.guid + "/asset", Encoding.UTF8.GetBytes(e.asset), '0');
                    WriteTar(gz, "./" + e.guid + "/asset.meta", Encoding.UTF8.GetBytes(e.meta), '0');
                    WriteTar(gz, "./" + e.guid + "/pathname", Encoding.UTF8.GetBytes(e.path + "\n00"), '0');
                }
                gz.Write(new byte[1024], 0, 1024);
            }
        }

        private static void WriteTar(Stream s, string name, byte[] data, char type)
        {
            data = data ?? new byte[0];
            var h = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(h, 0);
            Encoding.ASCII.GetBytes("0000644\0").CopyTo(h, 100);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 108);
            Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 116);
            Encoding.ASCII.GetBytes(System.Convert.ToString(data.Length, 8).PadLeft(11, '0') + "\0").CopyTo(h, 124);
            Encoding.ASCII.GetBytes("00000000000\0").CopyTo(h, 136);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            h[156] = (byte)type;
            Encoding.ASCII.GetBytes("ustar\0" + "00").CopyTo(h, 257);
            int sum = h.Sum(b => (int)b);
            Encoding.ASCII.GetBytes(System.Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
            s.Write(h, 0, 512);
            s.Write(data, 0, data.Length);
            int pad = (512 - data.Length % 512) % 512;
            s.Write(new byte[pad], 0, pad);
        }
    }
}
