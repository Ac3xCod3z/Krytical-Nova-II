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
using KryticalNova.Player.Entities;
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

        /// <summary>
        /// Feel test item 5 (owner, 2 Nov): the Pack, the Serums and the wallet are real on the server's body. A character saved with
        /// stims in its Pack and Creds in its wallet (the NGO components' own texts: the same format, no migration) loads into a fresh
        /// server body with exactly that, and saves back every byte as it was.
        /// </summary>
        [UnityTest]
        public IEnumerator B3_ThePackSerumsAndWallet_AreReal_ASaveLoadsUnchanged_AndSavesBackByteForByte()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            const string account = "00112233445566778899aabbccddeeff";
            const string characterId = "ffeeddccbbaa99887766554433221100";
            GameObject first = Make(NetRoleKind.ServerBody, false, new PlayerKey(3));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(first.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            stored.characters.Add(new CharacterSave { characterId = characterId, name = "Stims" });
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            ServerBodyContext Context(int key) => new ServerBodyContext
            {
                Key = new PlayerKey((ulong)key), Serial = (ulong)key, NetworkId = key, Account = account, CharacterId = characterId, Name = "Stims",
                RecipeText = RecipeText.Encode(recipe),
            };

            var rules = new PlayerAdmission();
            Assert.IsNull(rules.Begin(first, Context(3)), "admitted");
            for (int i = 0; i < 10; i++) yield return null;
            var inventory = first.GetComponent<PlayerInventory>();
            int stims = inventory.CountItem(9001);
            Assert.AreEqual(5, inventory.ServerGive(9001, 5), "the server's body takes the stims (HP Stim (S), id 9001)");
            first.GetComponent<KryticalNova.Economy.PlayerWallet>().ServerSet(250);
            first.GetComponent<PlayerSaveAgent>().Save();
            ServerSave.WaitForDisk();
            CharacterSave saved = ServerSave.ReadAccountFrom(ServerSave.Store, account).Find(characterId);
            string pack = saved.Get("inventory"), wallet = saved.Get("wallet");
            Assert.IsNotNull(pack, "the Pack is real: it is saved");
            Assert.IsNotNull(wallet, "the wallet is real: it is saved");
            first.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            Object.Destroy(first);
            yield return null;

            GameObject second = Make(NetRoleKind.ServerBody, false, new PlayerKey(4));
            yield return null;
            Assert.IsNull(rules.Begin(second, Context(4)), "admitted again");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(stims + 5, second.GetComponent<PlayerInventory>().CountItem(9001), "the saved Pack is loaded, not the starter bag");
            Assert.AreEqual(wallet, second.GetComponent<KryticalNova.Economy.PlayerWallet>().CaptureSave(), "the saved Creds are loaded");
            second.GetComponent<PlayerSaveAgent>().Save();
            ServerSave.WaitForDisk();
            CharacterSave back = ServerSave.ReadAccountFrom(ServerSave.Store, account).Find(characterId);
            Assert.AreEqual(pack, back.Get("inventory"), "the Pack saves back byte for byte");
            Assert.AreEqual(wallet, back.Get("wallet"), "the wallet saves back byte for byte");
            second.GetComponent<PlayerSaveAgent>().OnServerLeaving();
        }

        /// <summary>
        /// Feel test item 5 (owner, 2 Nov): a use asked by the owner runs on the server's body by the item's id, with NGO's rules: a full
        /// pool refuses, a hurt one heals and the stack loses one, a second use at once is refused by the shared cooldown (no double
        /// use), an item not held is refused, and no use writes a ledger line (NGO writes none).
        /// </summary>
        [UnityTest]
        public IEnumerator B4_AUse_RunsOnTheServerBody_ByTheItemsId_OnceUnderTheCooldown_AndWritesNoLedgerLine()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            const string account = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";
            const string characterId = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(5));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            stored.characters.Add(new CharacterSave { characterId = characterId, name = "User" });
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(5), Serial = 5, NetworkId = 5, Account = account, CharacterId = characterId, Name = "User",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), "admitted");
            for (int i = 0; i < 10; i++) yield return null;

            var inventory = body.GetComponent<PlayerInventory>();
            var resources = body.GetComponent<KryticalNova.Progression.PlayerResources>();
            inventory.ServerGive(9001, 3);
            int held = inventory.CountItem(9001);
            bool ledgerBefore = ServerSave.HasPendingLedger(account);

            Assert.IsFalse(PlayerRequestServerSystem.UseOn(body, 9001, out RequestReplyRpc reply), "full HP: a stim would be wasted");
            Assert.AreEqual(RequestReplyRpc.ItemRefused, reply.Kind);
            Assert.AreEqual("Already at full strength.", reply.Text.ToString());
            Assert.AreEqual(held, inventory.CountItem(9001));

            resources.ApplyDamage(resources.MaxHealth * 0.5f);
            float hurt = resources.Health;
            Assert.IsTrue(PlayerRequestServerSystem.UseOn(body, 9001, out reply), "hurt: the stim is used");
            Assert.AreEqual(RequestReplyRpc.ItemUsed, reply.Kind);
            Assert.AreEqual(1, reply.Health, "it restored HP (the owner's feel)");
            Assert.AreEqual(held - 1, inventory.CountItem(9001), "one used from the server's own Pack");
            Assert.Greater(resources.Health, hurt, "the server's body healed");

            Assert.IsFalse(PlayerRequestServerSystem.UseOn(body, 9001, out reply), "a second use at once: no double use");
            Assert.AreEqual("You can't use another item yet.", reply.Text.ToString());
            Assert.AreEqual(held - 1, inventory.CountItem(9001));

            if (inventory.CountItem(9006) == 0)
            {
                Assert.IsFalse(PlayerRequestServerSystem.UseOn(body, 9006, out reply), "an item not held is never used");
                Assert.AreEqual("You have none left.", reply.Text.ToString());
            }
            Assert.AreEqual(ledgerBefore, ServerSave.HasPendingLedger(account), "a use writes no ledger line (NGO writes none)");
            body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
        }

        [UnityTest]
        public IEnumerator B5_AGmLine_ActsOnTheGmsOwnServerBody_AndWritesOneGmLedgerLine()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            const string account = "1a2b3c4d5e6f708192a3b4c5d6e7f801";
            const string characterId = "b1c2d3e4f5a60718293a4b5c6d7e8f91";
            PlayerKeys.Reset();
            PlayerKey key = PlayerKeys.Mint(5);   // connection 5's key, as ConnectionKeySystem mints it
            GameObject body = PlayerBodyAssembler.Create(Prefab(), NetRoleKind.ServerBody, key, 5, false, new Vector3(0f, 0.08f, 0f), 0f);
            m_Made.Add(body);
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            stored.characters.Add(new CharacterSave { characterId = characterId, name = "Ace" });
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = key, Serial = 5, NetworkId = 5, Account = account, CharacterId = characterId, Name = "Ace",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), "admitted");
            for (int i = 0; i < 10; i++) yield return null;

            Assert.AreEqual(key, ((IPlayerBody)body.GetComponent<PointAndClickPlayer>()).Owner, "a server body's owner is its own key");
            Assert.AreSame(body.GetComponent<BodyRoleTag>(), BodyRoleTag.ServerBodyOf(key), "the portals and the GM find it in the server bodies");
            Assert.IsNotNull(KryticalNova.Networking.PlayerGm.CommandRunner, "the dev tools run GM lines");
            var inventory = body.GetComponent<PlayerInventory>();
            int held = inventory.CountItem(9001);
            int linesBefore = ServerSave.ReadLedger(account, 0, 100).FindAll(l => l.kind == LedgerKinds.Gm).Count;

            string answer = KryticalNova.Networking.PlayerGm.CommandRunner(5, "item.give 9001 2");
            for (int i = 0; i < 3 && answer.StartsWith("The commands are still being loaded", StringComparison.Ordinal); i++)
            {
                yield return null;
                answer = KryticalNova.Networking.PlayerGm.CommandRunner(5, "item.give 9001 2");
            }
            StringAssert.StartsWith("Gave 2x", answer, "the line ran on this server body");
            Assert.AreEqual(held + 2, inventory.CountItem(9001), "the GM's own server body got the items");

            List<LedgerEntry> gm = ServerSave.ReadLedger(account, 0, 100).FindAll(l => l.kind == LedgerKinds.Gm);
            Assert.AreEqual(linesBefore + 1, gm.Count, "one gm ledger line on the account");
            Assert.AreEqual(1, gm[0].items.Count);
            Assert.AreEqual(9001, gm[0].items[0].item);
            Assert.AreEqual(2, gm[0].items[0].count, "with what it added");
            StringAssert.Contains("item.give 9001 2", gm[0].note, "and the command itself");
            body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            PlayerKeys.Reset();
        }

        [UnityTest]
        public IEnumerator B6_TheOwnersWindows_ShowTheServersCredsAndProgression_ByNgosOwnPaths()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(6));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(6));
            yield return null;
            server.GetComponent<KryticalNova.Economy.PlayerWallet>().ServerSet(1234);
            var serverProgress = server.GetComponent<KryticalNova.Progression.PlayerCharacterProgression>();
            serverProgress.SetLevel(7);
            serverProgress.AddRenown(40);

            OwnerProgress? read = OwnerProgressBridge.Read(server);
            Assert.IsTrue(read.HasValue, "the server body has a wallet and a progression");
            Assert.AreEqual(1234, read.Value.Creds);
            Assert.AreEqual(7, read.Value.Level);
            Assert.AreEqual(serverProgress.Renown, read.Value.Renown);
            Assert.AreEqual(serverProgress.StatPoints, read.Value.StatPoints);

            var viewProgress = view.GetComponent<KryticalNova.Progression.PlayerCharacterProgression>();
            int levelUps = 0;
            viewProgress.LevelGained += _ => levelUps++;
            OwnerProgressBridge.Mirror(view, read.Value);
            Assert.AreEqual(1234, view.GetComponent<KryticalNova.Economy.PlayerWallet>().Creds, "the Pack window's Creds are the server's");
            Assert.AreEqual(7, viewProgress.CurrentLevel, "the Character window's level is the server's");
            Assert.AreEqual(serverProgress.Renown, viewProgress.Renown, "and its Renown");
            Assert.AreEqual(serverProgress.SkillPoints, viewProgress.SkillPoints);
            Assert.Greater(levelUps, 0, "a level gained on the server plays its level-up on the owner's game (NGO's path)");
            Assert.IsTrue(OwnerProgressBridge.Same(read.Value, OwnerProgressBridge.Read(view).Value), "the owner shows exactly what the server holds");
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
