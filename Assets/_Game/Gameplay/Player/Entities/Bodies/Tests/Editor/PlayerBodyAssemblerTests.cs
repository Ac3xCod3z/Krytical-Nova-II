using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    // Fakes carry the exact names the role table knows, so the table's rules can be proven on plain GameObjects.
    internal class PlayerInput : MonoBehaviour { }

    [RequireComponent(typeof(PlayerInput))]
    internal class PointAndClickPlayer : MonoBehaviour { }

    internal class PlayerEquipment : MonoBehaviour { }

    internal class Mystery : MonoBehaviour { }

    /// <summary>M3a-2b step 2: the role table and the assembler. Nothing here wakes a component up (Awake needs a built player: B1).</summary>
    public class PlayerBodyAssemblerTests
    {
        private const string PrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private readonly List<GameObject> m_Made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in m_Made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            m_Made.Clear();
        }

        private GameObject Plain()
        {
            var go = new GameObject("Plain");
            m_Made.Add(go);
            return go;
        }

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Player.prefab not found at " + PrefabPath);
            return prefab;
        }

        private BodyBuild BuildReal(NetRoleKind role, bool local)
        {
            BodyBuild build = PlayerBodyAssembler.Build(LoadPrefab(), role, new PlayerKey(3), 3, local, new Vector3(1, 2, 3), 90f);
            m_Made.Add(build.Holder);
            return build;
        }

        private static string[] Names(GameObject body) =>
            body.GetComponentsInChildren<Component>(true).Where(c => c != null).Select(c => c.GetType().Name).ToArray();

        // ------------------------------------------------------------------ the table

        [Test]
        public void ShapeOf_RejectsOffline_AndSplitsTheClientViewByLocal()
        {
            Assert.Throws<ArgumentException>(() => PlayerRoleTable.ShapeOf(NetRoleKind.Offline, false));
            Assert.AreEqual(BodyShape.Server, PlayerRoleTable.ShapeOf(NetRoleKind.ServerBody, true));
            Assert.AreEqual(BodyShape.ViewLocal, PlayerRoleTable.ShapeOf(NetRoleKind.ClientView, true));
            Assert.AreEqual(BodyShape.ViewRemote, PlayerRoleTable.ShapeOf(NetRoleKind.ClientView, false));
        }

        [Test]
        public void Table_NoTypeIsInTwoGroups()
        {
            var all = PlayerRoleTable.Presentation.Concat(PlayerRoleTable.Movement).Concat(PlayerRoleTable.Automation)
                .Concat(PlayerRoleTable.RoleAware).Concat(PlayerRoleTable.Rules).Concat(PlayerRoleTable.Infrastructure).ToList();
            var dups = all.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dups, "classified twice: " + string.Join(", ", dups));
        }

        [Test]
        public void Table_SpotChecksPerShape()
        {
            Assert.AreEqual(PartAction.Strip, PlayerRoleTable.ActionFor(BodyShape.Server, "AnimancerComponent"));
            Assert.AreEqual(PartAction.Disable, PlayerRoleTable.ActionFor(BodyShape.Server, "PointAndClickPlayer"));
            Assert.AreEqual(PartAction.Disable, PlayerRoleTable.ActionFor(BodyShape.Server, "NavMeshAgent"));
            Assert.AreEqual(PartAction.Strip, PlayerRoleTable.ActionFor(BodyShape.Server, "NetworkTransform"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.Server, "PlayerWallet"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.Server, "PlayerSaveAgent"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.Server, "CapsuleCollider"));

            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.ViewLocal, "PointAndClickPlayer"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.ViewLocal, "PlayerInput"));
            Assert.AreEqual(PartAction.Disable, PlayerRoleTable.ActionFor(BodyShape.ViewLocal, "NavMeshAgent"));
            Assert.AreEqual(PartAction.Disable, PlayerRoleTable.ActionFor(BodyShape.ViewLocal, "PlayerAutopilot"));
            Assert.AreEqual(PartAction.Strip, PlayerRoleTable.ActionFor(BodyShape.ViewLocal, "PlayerMoveAnticipation"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.ViewLocal, "PlayerWallet"));

            Assert.AreEqual(PartAction.Strip, PlayerRoleTable.ActionFor(BodyShape.ViewRemote, "PlayerWallet"));
            Assert.AreEqual(PartAction.Strip, PlayerRoleTable.ActionFor(BodyShape.ViewRemote, "PlayerSaveAgent"));
            Assert.AreEqual(PartAction.Strip, PlayerRoleTable.ActionFor(BodyShape.ViewRemote, "PointAndClickPlayer"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.ViewRemote, "CombatEntity"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.ViewRemote, "PlayerIdentity"));
            Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(BodyShape.ViewRemote, "AnimancerComponent"));
        }

        [Test]
        public void AnUnknownType_IsKept_AndNotClassified()
        {
            Assert.IsFalse(PlayerRoleTable.IsClassified("Mystery"));
            foreach (BodyShape shape in Enum.GetValues(typeof(BodyShape)))
                Assert.AreEqual(PartAction.Keep, PlayerRoleTable.ActionFor(shape, "Mystery"));
        }

        // ------------------------------------------------------------------ the dependency order (plain GameObjects)

        [Test]
        public void Apply_ServerKeepsPlayerInputDisabledWhileTheClickScriptNeedsIt()
        {
            GameObject go = Plain();
            go.AddComponent<PlayerInput>();
            go.AddComponent<PointAndClickPlayer>();
            go.AddComponent<PlayerEquipment>();
            BodyBuildReport report = PlayerBodyAssembler.Apply(go, BodyShape.Server);
            Assert.IsNotNull(go.GetComponent<PlayerInput>(), "required by PointAndClickPlayer: cannot be destroyed");
            Assert.IsFalse(go.GetComponent<PlayerInput>().enabled);
            Assert.IsFalse(go.GetComponent<PointAndClickPlayer>().enabled);
            CollectionAssert.Contains(report.Downgraded, "PlayerInput");
            CollectionAssert.Contains(report.Disabled, "PointAndClickPlayer");
            Assert.IsNotNull(go.GetComponent<PlayerEquipment>());
        }

        [Test]
        public void Apply_RemoteRemovesDependantsFirst()
        {
            GameObject go = Plain();
            go.AddComponent<PlayerInput>();         // the real prefab carries it; Unity does not auto-add a RequireComponent for fakes declared in another file
            go.AddComponent<PointAndClickPlayer>();
            go.AddComponent<PlayerEquipment>();
            go.AddComponent<Mystery>();
            BodyBuildReport report = PlayerBodyAssembler.Apply(go, BodyShape.ViewRemote);
            Assert.IsNull(go.GetComponent<PointAndClickPlayer>());
            Assert.IsNull(go.GetComponent<PlayerInput>());
            Assert.IsNull(go.GetComponent<PlayerEquipment>());
            Assert.IsNotNull(go.GetComponent<Mystery>());
            Assert.IsEmpty(report.Downgraded);
            Assert.Less(report.Stripped.IndexOf("PointAndClickPlayer"), report.Stripped.IndexOf("PlayerInput"));
        }

        [Test]
        public void IsRequiredByAnother_ReadsRequireComponent()
        {
            GameObject go = Plain();
            go.AddComponent<PlayerInput>();
            go.AddComponent<PointAndClickPlayer>();
            Assert.IsTrue(PlayerBodyAssembler.IsRequiredByAnother(go.GetComponent<PlayerInput>()));
            Assert.IsFalse(PlayerBodyAssembler.IsRequiredByAnother(go.GetComponent<PointAndClickPlayer>()));
        }

        // ------------------------------------------------------------------ the real prefab (no Awake)

        [Test]
        public void Lint_EveryComponentOfPlayerPrefabIsClassified()
        {
            GameObject prefab = LoadPrefab();
            var unknown = new SortedSet<string>();
            foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
            {
                if (c == null) { Assert.Fail("a component of Player.prefab has a missing script"); continue; }
                string name = c.GetType().Name;
                if (!PlayerRoleTable.IsClassified(name)) unknown.Add(name);
            }
            Assert.IsEmpty(unknown, "Add these to PlayerRoleTable (a new player system forces a decision): " + string.Join(", ", unknown));
        }

        [Test]
        public void Lint_EveryTypeNamedInTheTableIsUsedOrKnown_NoTypos()
        {
            // The table is allowed to name types the prefab no longer holds, but never twice; typos show as unknown on the prefab.
            var onPrefab = new HashSet<string>(LoadPrefab().GetComponentsInChildren<Component>(true).Where(c => c != null).Select(c => c.GetType().Name));
            int hits = PlayerRoleTable.Rules.Count(onPrefab.Contains);
            Assert.GreaterOrEqual(hits, PlayerRoleTable.Rules.Length * 2 / 3, "most rule names should match a component on the prefab");
        }

        [Test]
        public void Build_Server_IsAsleepTaggedAndStripped()
        {
            BodyBuild build = BuildReal(NetRoleKind.ServerBody, false);
            GameObject body = build.Body;
            Assert.IsFalse(body.activeInHierarchy, "nothing may have woken up");
            Assert.AreSame(body.GetComponent<BodyRoleTag>(), build.Tag);
            Assert.AreEqual(NetRoleKind.ServerBody, build.Tag.Kind);
            Assert.AreEqual(new PlayerKey(3), build.Tag.Key);
            Assert.AreEqual(3UL, build.Tag.ConnectionSerial);
            string[] names = Names(body);
            foreach (string gone in new[] { "AnimancerComponent", "Animator", "PlayerAnimancerMotor", "EquipmentVisualBinder", "NetworkTransform", "PlayerMoveAnticipation", "PlayerAutopilot", "CharacterLook" })
                CollectionAssert.DoesNotContain(names, gone, gone + " must be stripped on a server body");
            CollectionAssert.Contains(names, "PointAndClickPlayer");
            CollectionAssert.Contains(names, "NavMeshAgent");
            CollectionAssert.Contains(names, "PlayerSaveAgent");
            CollectionAssert.Contains(names, "PlayerInventory");
            foreach (Behaviour b in body.GetComponentsInChildren<Behaviour>(true))
                if (b != null && (b.GetType().Name == "PointAndClickPlayer" || b.GetType().Name == "NavMeshAgent"))
                    Assert.IsFalse(b.enabled, b.GetType().Name + " must be disabled on a server body");
        }

        [Test]
        public void Build_PlacesTheBodyAndMakesItsRigidbodiesKinematic()
        {
            BodyBuild build = BuildReal(NetRoleKind.ServerBody, false);
            Assert.AreEqual(new Vector3(1, 2, 3), build.Body.transform.position);
            Assert.AreEqual(90f, build.Body.transform.eulerAngles.y, 0.01f);
            var bodies = build.Body.GetComponentsInChildren<Rigidbody>(true);
            Assert.IsNotEmpty(bodies);
            foreach (Rigidbody rb in bodies)
            {
                Assert.IsTrue(rb.isKinematic);
                Assert.IsFalse(rb.useGravity);
            }
        }

        [Test]
        public void Build_ViewLocal_KeepsPresentationAndInputButNotTheNetworkTransform()
        {
            BodyBuild build = BuildReal(NetRoleKind.ClientView, true);
            string[] names = Names(build.Body);
            CollectionAssert.Contains(names, "AnimancerComponent");
            CollectionAssert.Contains(names, "PointAndClickPlayer");
            CollectionAssert.Contains(names, "PlayerInput");
            CollectionAssert.Contains(names, "PlayerWallet");
            CollectionAssert.DoesNotContain(names, "NetworkTransform");
            CollectionAssert.DoesNotContain(names, "PlayerMoveAnticipation");
            Assert.IsTrue(build.Tag.IsLocal);
            foreach (Behaviour b in build.Body.GetComponentsInChildren<Behaviour>(true))
                if (b != null && (b.GetType().Name == "NavMeshAgent" || b.GetType().Name == "PlayerAutopilot"))
                    Assert.IsFalse(b.enabled, b.GetType().Name + " must be off on the local view");
        }

        [Test]
        public void Build_ViewRemote_HasNoRuleStubLeftEnabled()
        {
            BodyBuild build = BuildReal(NetRoleKind.ClientView, false);
            string[] names = Names(build.Body);
            CollectionAssert.Contains(names, "AnimancerComponent");
            CollectionAssert.Contains(names, "PlayerIdentity");
            CollectionAssert.Contains(names, "CombatEntity");
            CollectionAssert.DoesNotContain(names, "PointAndClickPlayer");
            CollectionAssert.DoesNotContain(names, "PlayerSaveAgent");
            var kept = new HashSet<string>(PlayerRoleTable.RemoteKeepsRules);
            foreach (Component c in build.Body.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string name = c.GetType().Name;
                if (!Array.Exists(PlayerRoleTable.Rules, r => r == name) || kept.Contains(name)) continue;
                // a rule stub that stays must be one another kept part requires, and it is then switched off
                CollectionAssert.Contains(build.Report.Downgraded, name, name + " is still on a remote body but was not downgraded");
                Assert.IsFalse(c is Behaviour b && b.enabled, name + " must be disabled");
            }
            UnityEngine.Debug.Log("[Bodies] remote view downgraded: " + string.Join(", ", build.Report.Downgraded));
        }

        [Test]
        public void Build_NeverRunsAnythingAndLeavesNoStrayObject()
        {
            int before = UnityEngine.Object.FindObjectsByType<BodyRoleTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            BodyBuild build = BuildReal(NetRoleKind.ServerBody, false);
            Assert.IsNotNull(build.Holder);
            Assert.AreSame(build.Holder.transform, build.Body.transform.parent);
            UnityEngine.Object.DestroyImmediate(build.Holder);
            int after = UnityEngine.Object.FindObjectsByType<BodyRoleTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.AreEqual(before, after);
        }
    }
}
