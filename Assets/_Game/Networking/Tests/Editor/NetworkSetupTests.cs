using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace KryticalNova.Networking.Tests
{
    /// <summary>
    /// Netcode lets a game in only when its network setup matches the server's (a hash of the tick rate, approval, scene
    /// management, the prefabs and variable length safety). The worlds and the menus' roster link (<see cref="RosterLink"/>,
    /// online phase 4, milestone 5) all use the NetworkManager prefab, so no scene may change those settings on its copy:
    /// on 19 Oct 2026 every world turned variable length safety on while the prefab had it off, and the roster link was
    /// dropped before it could ask anything.
    /// </summary>
    public sealed class NetworkSetupTests
    {
        private const string PrefabPath = "Assets/_Game/Networking/Prefabs/NetworkManager.prefab";
        private const string PrefabGuid = "e84dbd4512b7cf24097ea29798272bba";

        private static readonly string[] Hashed =
        {
            "TickRate", "ConnectionApproval", "ForceSamePrefabs", "EnableSceneManagement", "EnsureNetworkVariableLengthSafety", "RpcHashSize",
        };

        // Owner, 20 Oct 2026: the maps are Addressables, which Netcode's scene syncing cannot load (it knows only the build
        // list). Each map server runs one map, and the game loads it before it joins (MapScenes), so the syncing is off.
        [Test]
        public void SceneSyncingIsOffBecauseTheMapsAreAddressables()
        {
            StringAssert.IsMatch(@"\n\s+EnableSceneManagement: 0\s*\n", File.ReadAllText(PrefabPath));
        }

        [Test]
        public void NoSceneChangesASettingTheServerCompares()
        {
            string prefab = File.ReadAllText(PrefabPath);
            var expected = new Dictionary<string, string>();
            foreach (string field in Hashed)
            {
                Match own = Regex.Match(prefab, @"\n\s+" + field + @": (\S+)");
                if (own.Success) expected["NetworkConfig." + field] = own.Groups[1].Value;
            }
            Match lists = Regex.Match(prefab, @"NetworkPrefabsLists:\s*\n((?:\s+- \{[^\n]*\n)*)");
            Assert.IsTrue(lists.Success, "the prefab's prefab lists");
            var listGuids = new List<string>();
            foreach (Match guid in Regex.Matches(lists.Groups[1].Value, @"guid: ([0-9a-f]{32})")) listGuids.Add(guid.Groups[1].Value);
            expected["NetworkConfig.Prefabs.NetworkPrefabsLists.Array.size"] = listGuids.Count.ToString();

            var wrong = new List<string>();
            var block = new Regex(@"- target: \{fileID: \d+, guid: " + PrefabGuid + @", type: 3\}\s*\n\s*propertyPath: (\S+)\s*\n\s*value: ([^\n]*)\n\s*objectReference: ([^\n]*)");
            foreach (string scene in Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(scene);
                if (!text.Contains(PrefabGuid)) continue;
                foreach (Match m in block.Matches(text))
                {
                    string path = m.Groups[1].Value, value = m.Groups[2].Value.Trim();
                    if (expected.TryGetValue(path, out string own) && own != value)
                        wrong.Add($"{scene}: {path} is {value}, the prefab has {own}");
                    Match element = Regex.Match(path, @"^NetworkConfig\.Prefabs\.NetworkPrefabsLists\.Array\.data\[(\d+)\]$");
                    if (element.Success)
                    {
                        int index = int.Parse(element.Groups[1].Value);
                        Match guid = Regex.Match(m.Groups[3].Value, @"guid: ([0-9a-f]{32})");
                        if (!guid.Success || index >= listGuids.Count || listGuids[index] != guid.Groups[1].Value)
                            wrong.Add($"{scene}: prefab list {index} is not the prefab's");
                    }
                }
            }
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }
    }
}
