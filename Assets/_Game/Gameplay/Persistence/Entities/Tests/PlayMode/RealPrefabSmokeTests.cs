using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities.Bodies;
using KryticalNova.Player.Entities.Presentation;
using Object = UnityEngine.Object;

namespace KryticalNova.Persistence.Entities.Tests.Play
{
    /// <summary>
    /// M3a-2b step 9, layer B (design B1, B2): the REAL Player.prefab woken up as ServerBody, local ClientView and remote ClientView.
    /// EditMode cannot run Awake/OnEnable/Start, so these need Play: the Editor's PlayMode runner (owner's or Master's OK, nothing else
    /// using the Editor) or a development test player. Explicit: never part of the batch EditMode run. Saves go to a MemorySaveStore,
    /// never a real store. The first run records the log lines the offline-style stubs print (the allow-list); later runs assert the
    /// set does not grow.
    /// </summary>
    [Explicit("Layer B: needs Play with the real Player.prefab (M3a-2b B1/B2)")]
    [Category("B")]
    public class RealPrefabSmokeTests
    {
        const string PrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        const int Frames = 120;

        // Lines the stubs are expected to print without a NetworkManager (fill from the first run's "[M3A2B-B1] log:" lines).
        static readonly string[] Allowed =
        {
            "NetworkVariable", "NetworkList", "not spawned", "NetworkManager", "NetworkObject",
        };

        INetRoleAdapter m_OldAdapter;
        readonly List<string> m_Problems = new List<string>();
        readonly List<GameObject> m_Made = new List<GameObject>();
        ISaveStore m_OldStore;

        [SetUp]
        public void SetUp()
        {
            m_OldAdapter = NetRole.Adapter;
            m_Problems.Clear();
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
            m_OldStore = ServerSave.Store;
            ServerSave.Store = new MemorySaveStore();
            SavePassthrough.ResetToDefault();
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (GameObject go in m_Made) if (go != null) Object.Destroy(go);
            m_Made.Clear();
            NetRole.Install(m_OldAdapter);
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
            ServerSave.Store = m_OldStore;
            LogAssert.ignoreFailingMessages = false;
        }

        void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning) return;
            Debug.Log("[M3A2B-B1] log: " + type + " " + condition);
            foreach (string ok in Allowed) if (condition.Contains(ok)) return;
            if (type != LogType.Warning) m_Problems.Add(type + ": " + condition);
        }

        static GameObject Prefab()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null) return prefab;
#endif
            Assert.Ignore("Player.prefab is only reachable in the Editor's PlayMode runner.");
            return null;
        }

        GameObject Make(NetRoleKind role, bool local, PlayerKey key)
        {
            GameObject body = PlayerBodyAssembler.Create(Prefab(), role, key, key.Value, local, new Vector3(0f, 0.08f, 0f), 0f);
            m_Made.Add(body);
            return body;
        }

        static void AssertAgentsOff(GameObject body)
        {
            foreach (NavMeshAgent agent in body.GetComponentsInChildren<NavMeshAgent>(true))
                Assert.IsFalse(agent.enabled, body.name + ": the NavMeshAgent stays off (the ghost moves bodies)");
        }

        /// <summary>Every public static "Local" of a Krytical Nova component type that points at a body of this role.</summary>
        static List<string> LocalStaticsOn(GameObject body)
        {
            var found = new List<string>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith("KryticalNova", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (Type type in types)
                {
                    if (type == null || !typeof(Component).IsAssignableFrom(type) || type.ContainsGenericParameters) continue;
                    PropertyInfo local = type.GetProperty("Local", BindingFlags.Public | BindingFlags.Static);
                    if (local == null || !typeof(Component).IsAssignableFrom(local.PropertyType)) continue;
                    object value;
                    try { value = local.GetValue(null); } catch (Exception) { continue; }
                    if (value is Component c && c != null && c.transform.root == body.transform) found.Add(type.Name + ".Local");
                }
            }
            return found;
        }

        [UnityTest]
        public IEnumerator B1_TheServerBody_WakesUpCleanly_StaysPut_AndHoldsNoLocalStatics()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(1));
            Vector3 at = body.transform.position;
            for (int i = 0; i < Frames; i++) yield return null;

            CollectionAssert.IsEmpty(m_Problems, "errors from the Awake/OnEnable/Start chain of the stripped server body");
            Assert.IsNull(LocalPlayer.Object, "no local player on a server");
            Assert.IsNull(PlayerInventory.Local);
            CollectionAssert.IsEmpty(LocalStaticsOn(body), "no X.Local of any system points at a server body");
            AssertAgentsOff(body);
            Assert.AreEqual(at, body.transform.position, "physics never moves a server body (kinematic)");
            var click = body.GetComponent<PointAndClickPlayer>();
            Assert.IsNotNull(click, "kept (others call its server API)");
            Assert.IsFalse(click.enabled);
            Assert.IsFalse(click.ViewMode);
        }

        [UnityTest]
        public IEnumerator B1_TheClientViews_LocalIsTheLocalStub_RemoteIsBare()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ClientView));
            int released = 0;
            var factory = new ClientBodyFactory(Prefab()) { BeforeLocalBody = () => released++ };
            GameObject mine = factory.Create(new PlayerBodyRequest { NetworkId = 1, IsLocal = true, Position = Vector3.zero });
            GameObject theirs = factory.Create(new PlayerBodyRequest { NetworkId = 2, IsLocal = false, Position = new Vector3(3f, 0f, 0f) });
            m_Made.Add(mine);
            m_Made.Add(theirs);
            for (int i = 0; i < Frames; i++) yield return null;

            CollectionAssert.IsEmpty(m_Problems, "errors from the Awake/OnEnable/Start chain of the client views");
            Assert.AreEqual(1, released);
            Assert.AreSame(mine.GetComponent<PlayerInventory>(), PlayerInventory.Local, "the HUD's inventory is the local stub's");
            Assert.AreSame(mine, LocalPlayer.Object);
            Assert.IsTrue(mine.GetComponent<PointAndClickPlayer>().ViewMode);
            Assert.IsFalse(ReferenceEquals(PlayerBodies.All, PointAndClickPlayer.Active), "the view mode never replaces the body list");
            Assert.IsNull(theirs.GetComponent<PointAndClickPlayer>());
            Assert.IsNull(theirs.GetComponent<PlayerInventory>(), "no rule stub on another player's body");
            Assert.IsNotNull(theirs.GetComponent<PlayerIdentity>(), "the look and the name card stay");
            AssertAgentsOff(mine);
            CollectionAssert.IsEmpty(LocalStaticsOn(theirs), "no X.Local points at another player's body");
        }

        [UnityTest]
        public IEnumerator B2_PassthroughParts_StayByteIdentical_ThroughLoadAndSave()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            const string account = "0123456789abcdef0123456789abcdef";
            const string characterId = "fedcba9876543210fedcba9876543210";
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(2));
            yield return null;

            var identity = body.GetComponent<PlayerIdentity>();
            CharacterRecipe recipe = AnyLegalRecipe(identity.Database);
            var stored = new AccountSave { accountId = account };
            var character = new CharacterSave { characterId = characterId, name = "Smoke" };
            stored.characters.Add(character);
            var odd = new Dictionary<string, string>();
            foreach (string key in SavePassthrough.PassthroughKeys)
            {
                string text = "{ \"kept\" :  1.50 , \"key\":\"" + key + "\" , \"unknown\" : [ ] }";
                odd[key] = text;
                stored.Set(key, text);
                character.Set(key, text);
            }
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();

            var rules = new PlayerAdmission();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(2), Serial = 2, NetworkId = 2, Account = account, CharacterId = characterId, Name = "Smoke",
                RecipeText = RecipeText.Encode(recipe),
            };
            string refusal = rules.Begin(body, in context);
            Assert.IsNull(refusal, "admitted");
            var agent = body.GetComponent<PlayerSaveAgent>();
            Assert.IsTrue(agent.IsLoaded);
            for (int i = 0; i < 30; i++) yield return null;
            agent.Save();
            ServerSave.WaitForDisk();

            AccountSave back = ServerSave.ReadAccountFrom(ServerSave.Store, account);
            CharacterSave backCharacter = back.Find(characterId);
            foreach (KeyValuePair<string, string> kv in odd)
            {
                Assert.AreEqual(kv.Value, back.Get(kv.Key), "account part '" + kv.Key + "' kept byte for byte");
                Assert.AreEqual(kv.Value, backCharacter.Get(kv.Key), "character part '" + kv.Key + "' kept byte for byte");
            }
            Assert.IsNotNull(backCharacter.Get(PlayerIdentity.LookSaveKey), "the look is real: it is saved");
            agent.OnServerLeaving();
        }

        static CharacterRecipe AnyLegalRecipe(CharacterCreationDatabase database)
        {
            Assert.IsNotNull(database, "PlayerIdentity has its character database");
            foreach (LineageDefinition lineage in database.Lineages)
                foreach (BodyType bodyType in Enum.GetValues(typeof(BodyType)))
                {
                    CharacterRecipe recipe;
                    try { recipe = RecipeEdit.Default(lineage, bodyType); } catch (Exception) { continue; }
                    if (CharacterRecipeValidator.Validate(recipe, database, out _)) return recipe;
                }
            Assert.Fail("no legal recipe in the database");
            return default;
        }
    }
}
