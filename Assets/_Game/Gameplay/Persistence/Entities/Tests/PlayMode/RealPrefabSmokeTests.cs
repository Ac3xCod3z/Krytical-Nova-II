using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Unity.Netcode;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Progression;
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
        IChatOwnerLink m_OldChat;

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
            m_OldChat = ChatOwnerLink.Current;
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
            ChatOwnerLink.Current = m_OldChat;
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

            // 8.1.1: the owner's view wallet only shows; it never earns, spends or sets (NGO: only the server's does)
            var viewWallet = view.GetComponent<KryticalNova.Economy.PlayerWallet>();
            Assert.AreEqual(0, viewWallet.ServerEarn(500, KryticalNova.Wallet.CredsSource.Gm), "a view earns nothing");
            Assert.IsFalse(viewWallet.ServerSpend(100, KryticalNova.Wallet.CredsSink.Gm), "a view spends nothing");
            viewWallet.ServerSet(1);
            Assert.AreEqual(1234, viewWallet.Creds, "the view still shows the server's balance");
        }

        sealed class Asked : IBodyRequests, IRestyleRequests
        {
            public void Restyle(byte service, in CharacterRecipe proposed) => Ops.Add($"Restyle {service} {proposed.height}");
            public readonly List<string> Ops = new List<string>();
            public void UseItem(ushort itemId) { }
            public void CastSkill(string skillId) { }
            public void PackOp(PackOpKind kind, byte fromContainer, byte fromIndex, byte toContainer, byte toIndex) =>
                Ops.Add($"{kind} {fromContainer}:{fromIndex} {toContainer}:{toIndex}");
            public void LoadoutOp(LoadoutOpKind kind, byte index, string name) => Ops.Add($"{kind} {index} {name}");
            public void RefillFlask() => Ops.Add("RefillFlask");
            public void Allocate(byte attribute, short count) => Ops.Add($"Allocate {attribute} {count}");
            public void UpgradeSkill(string skillId, byte count) => Ops.Add($"Upgrade {skillId} {count}");
            public void UpgradeMovement(byte skill, byte count) => Ops.Add($"Move {skill} {count}");
            public void Pickup(uint dropId) => Ops.Add($"Pickup {dropId}");
            public void DropItem(byte container, byte index) => Ops.Add($"Drop {container}:{index}");
            public void StorageOp(byte kind, StorageOpKind op, short a, short b, long amount) => Ops.Add($"Storage {kind} {op} {a} {b} {amount}");
            public void ShopBuy(string vendor, ushort itemId, ushort count) => Ops.Add($"Buy {vendor} {itemId} {count}");
            public void ShopSell(ushort cell, ushort count, ushort itemId, ushort stackCount, ulong data, bool confirmed) =>
                Ops.Add($"Sell {cell} {count} {itemId}x{stackCount}:{data} {confirmed}");
            public void TradeOp(TradeOpKind op, string name, short a, ushort b, long creds, bool flag) => Ops.Add($"Trade {op} {name} {a} {b} {creds} {flag}");
            public void KioskOpen(string shopName, BodyKioskOffer[] offers, byte tier) => Ops.Add($"KioskOpen {shopName} {offers.Length} {tier}");
            public void KioskClose() => Ops.Add("KioskClose");
            public void KioskCollect() => Ops.Add("KioskCollect");
            public void KioskBuy(uint standId, byte index, ushort itemId, ushort stackCount, ulong data, long unitPrice, ushort count) =>
                Ops.Add($"KioskBuy {standId} {index} {itemId}x{stackCount}:{data} {unitPrice} {count}");
            public void GearWork(GearWorkOpKind op, short cell, short cell2, ushort alloy, ushort protection) => Ops.Add($"GearWork {op} {cell} {cell2} {alloy} {protection}");
            public void Craft(ushort recipeId, ushort times) => Ops.Add($"Craft {recipeId} {times}");
            public void Nova(NovaOpKind op, byte arg) => Ops.Add($"Nova {op} {arg}");
            public void WeaveOp(WeaveOpKind op, byte slot, ushort id, byte stage) => Ops.Add($"Weave {op} {slot} {id} {stage}");
            public void AutobuffSlots(string[] skillIds) => Ops.Add("Autobuff " + string.Join("|", skillIds));
            public void CrateOp(bool withdraw, ushort a, ushort count) => Ops.Add($"Crate {(withdraw ? "Withdraw" : "Store")} {a} {count}");
            public void Chat(ChatChannel channel, string text, string whisperTarget, byte[] linkContainers, byte[] linkCells) =>
                Ops.Add($"Chat {channel} {ChatLinks.Strip(text)}|{whisperTarget}|{linkContainers?.Length ?? 0}");
        }

        /// <summary>What the server bodies told their players (the message line) and sent their trade windows (view, notice, prompt).</summary>
        sealed class Told : IOwnerMessages, ITradeOwnerLink, IKioskOwnerLink, IWorkshopOwnerLink, KryticalNova.Progression.INovaLink, IRestyleOwnerLink, IChatOwnerLink
        {
            // chat (M8 8.4.1): "system <text>" for a System notice, else "<kind> <channel> [to ]<sender>: <text>"
            public bool Line(Component part, in BodyChatLine line)
            {
                string what = line.kind == BodyChatKind.Line && line.channel == ChatChannel.System ? "system " + line.text
                    : $"{line.kind.ToString().ToLowerInvariant()} {line.channel} {(line.outgoing ? "to " : "")}{line.sender}: {ChatLinks.Strip(line.text)}";
                Lines.Add((Body(part), what));
                return true;
            }
            public void Restyled(Component part) => Lines.Add((Body(part), "restyled"));
            public void Finished(Component part, in KryticalNova.Progression.NovaWorkResult r) =>
                Lines.Add((Body(part), $"nova {(int)r.kind} {(int)r.outcome} {r.before}->{r.after}"));
            public void GearWorked(Component part, byte kind, byte outcome, ItemStack before, ItemStack after) =>
                Lines.Add((Body(part), $"forge {kind} {outcome} {before.itemId}"));
            public void Crafted(Component part, ushort recipeId, int times) => Lines.Add((Body(part), $"crafted {recipeId} x{times}"));
            public void View(Component part, in BodyKioskView view) =>
                Lines.Add((Body(part), $"kiosk {view.exists} {view.open} {view.shopName} till {view.till} shelf {view.shelf?.Length ?? 0}"));
            public readonly List<(GameObject body, string what)> Lines = new List<(GameObject, string)>();
            public bool Tell(Component part, string text) { Lines.Add((Body(part), "tell " + text)); return true; }
            public void View(Component part, in BodyTradeView view) =>
                Lines.Add((Body(part), $"view {view.active} {view.partnerName} mine {view.mine?.Length ?? 0} theirs {view.theirs?.Length ?? 0}"));
            public void Notice(Component part, string text) => Lines.Add((Body(part), "notice " + text));
            public void Prompt(Component part, string fromName, float seconds) => Lines.Add((Body(part), "prompt " + fromName));
            public void PromptClosed(Component part) => Lines.Add((Body(part), "prompt closed"));
            static GameObject Body(Component part) => BodyRoleTag.Of(part) != null ? BodyRoleTag.Of(part).gameObject : part.gameObject;
            public List<string> To(GameObject body) => Lines.FindAll(l => l.body == body).ConvertAll(l => l.what);
            public bool Any(GameObject body, string start) => Lines.Exists(l => l.body == body && l.what.StartsWith(start));
        }

        [UnityTest]
        public IEnumerator B7_PackRequests_RunOnTheServerBody_AndTheOwnersBodyWearsWhatTheServersWears()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(7));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(7));
            yield return null;
            var inv = server.GetComponent<PlayerInventory>();
            var viewInv = view.GetComponent<PlayerInventory>();
            Assert.IsNotNull(inv.Catalog, "the inventory has its catalog");

            // a piece to wear: the starter one the server body wears, else the catalog's first starter piece put on it
            EquipSlot slot = EquipSlot.None;
            foreach (EquipSlot each in EquipSlots.All) if (inv.GetEquipped(each).itemId != 0) { slot = each; break; }
            if (slot == EquipSlot.None)
            {
                ItemDefinition first = inv.Catalog.StarterEquipped.Count > 0 ? inv.Catalog.StarterEquipped[0] : null;
                Assert.IsNotNull(first, "the catalog has a starter piece");
                inv.ServerWear(new[] { new ItemStack(first.itemId, 1) }, out _, out _);
                slot = first.equipSlot;
            }
            ushort piece = inv.GetEquipped(slot).itemId;
            Assert.AreNotEqual(0, piece, "the server body wears a piece in " + slot);

            // the owner's view asks: nothing changes here, the request goes to the server
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            viewInv.RequestQuickUnequip(slot);
            viewInv.RequestSort();
            CollectionAssert.AreEqual(new[] { $"QuickUnequip 1:{(byte)slot} 0:0", "Sort 0:0 0:0" }, asked.Ops, "the view sends its requests by place");

            // the server body runs them with the game's rules
            inv.RunPackOp(PackOpKind.QuickUnequip, ItemLocation.Equip(slot), default);
            Assert.AreEqual(0, inv.GetEquipped(slot).itemId, "taken off on the server");
            Assert.AreEqual(1, inv.CountItem(piece), "into the Pack");
            int cell = -1;
            for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == piece) { cell = i; break; }
            inv.RunPackOp(PackOpKind.QuickEquip, ItemLocation.Bag(cell), default);
            Assert.AreEqual(piece, inv.GetEquipped(slot).itemId, "and on again");
            Assert.AreEqual(0, inv.CountItem(piece));

            // the mirror: the owner's body wears what the server's wears (and a view never runs a request itself)
            viewInv.MirrorWorn(slot, ItemStack.Empty);
            Assert.AreEqual(0, viewInv.GetEquipped(slot).itemId, "an empty slot mirrors empty");
            viewInv.MirrorWorn(slot, inv.GetEquipped(slot));
            Assert.AreEqual(piece, viewInv.GetEquipped(slot).itemId, "the owner's body wears the server's piece");
            Assert.AreEqual(inv.GetEquipped(slot).data, viewInv.GetEquipped(slot).data, "with its own state");
            viewInv.RunPackOp(PackOpKind.QuickUnequip, ItemLocation.Equip(slot), default);
            Assert.AreEqual(piece, viewInv.GetEquipped(slot).itemId, "a view body never runs a Pack request itself");

            // Destroy: NGO's rule on the server body (the item is gone, no ledger line: NGO writes none); the view only asks
            inv.ServerGive(9001, 2);
            int stimCell = -1;
            for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == 9001) { stimCell = i; break; }
            Assert.GreaterOrEqual(stimCell, 0, "the server body holds the stims");
            ItemStack stims = inv.Get(stimCell);
            viewInv.MirrorCell(stimCell, stims);
            asked.Ops.Clear();
            viewInv.RequestDestroy(ItemLocation.Bag(stimCell));
            Assert.AreEqual(new[] { $"Destroy 0:{stimCell} 0:0" }, asked.Ops.ToArray(), "the view asks the server");
            Assert.AreEqual(stims, viewInv.Get(stimCell), "and destroys nothing itself");
            viewInv.RunPackOp(PackOpKind.Destroy, ItemLocation.Bag(stimCell), default);
            Assert.AreEqual(stims, viewInv.Get(stimCell), "a view never runs a Destroy");
            int held = inv.CountItem(9001);
            inv.RunPackOp(PackOpKind.Destroy, ItemLocation.Bag(stimCell), default);
            Assert.AreEqual(held - stims.count, inv.CountItem(9001), "the server body destroyed that cell's stack");
            Assert.IsTrue(inv.Get(stimCell).IsEmpty, "the cell is empty");
        }

        [UnityTest]
        public IEnumerator B8_AnotherPlayer_SeesWhatTheServersBodyWears_ThroughTheLookText()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(8));
            GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(8));
            yield return null;
            var equipment = server.GetComponent<PlayerEquipment>();
            var inv = server.GetComponent<PlayerInventory>();
            bool wears = false;
            foreach (EquipSlot each in EquipSlots.All) if (equipment.Loadout[each] != 0) wears = true;
            Assert.IsTrue(wears, "the server body wears its starter pieces (as in B7)");
            EquipmentLooks looks = equipment.Looks;
            looks.helmetHidden = !looks.helmetHidden;
            equipment.ServerSetLooks(looks);

            string gear = GearText.Encode(equipment.Loadout, equipment.Looks, equipment.Temper);
            Assert.IsTrue(GearText.TryDecode(gear, out EquipmentLoadout loadout, out EquipmentLooks seen, out EquipmentTemper temper), gear);
            var remoteEquipment = remote.GetComponent<PlayerEquipment>();
            Assert.IsNotNull(remoteEquipment, "a remote view keeps its equipment (the look)");
            remoteEquipment.ApplyObserved(loadout, seen, temper);
            Assert.AreEqual(equipment.Loadout, remoteEquipment.Loadout, "the other player sees the same pieces");
            Assert.AreEqual(equipment.Looks.helmetHidden, remoteEquipment.Looks.helmetHidden, "and the helmet hidden as the server has it");
            Assert.AreEqual(equipment.Looks, remoteEquipment.Looks, "the looks exactly (dyes included)");
            Assert.AreEqual(equipment.Temper, remoteEquipment.Temper, "and the Temper everyone sees");
        }

        [UnityTest]
        public IEnumerator B9_Loadouts_RunOnTheServerBody_AreRealInTheSave_AndAnOldSaveLoadsAndSavesBackByteForByte()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("loadouts"), "the loadouts part is real now");

            // a body that saved a loadout by NGO's own rules gives the save text in NGO's format
            GameObject maker = Make(NetRoleKind.ServerBody, false, new PlayerKey(9));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(9));
            yield return null;
            var makerLoadouts = maker.GetComponent<PlayerLoadouts>();
            makerLoadouts.RunLoadoutOp(LoadoutOpKind.Save, 0, "Duel");
            Assert.IsTrue(makerLoadouts.IsUsed(0), "the server body saved what it wears");
            Assert.AreEqual("Duel", makerLoadouts.NameOf(0));
            makerLoadouts.RunLoadoutOp(LoadoutOpKind.Rename, 0, "Arena");
            Assert.AreEqual("Arena", makerLoadouts.NameOf(0), "renamed by the server's rules");
            string ngoText = makerLoadouts.CaptureSave();

            // the owner's view only asks, and shows the server's slots
            var viewLoadouts = view.GetComponent<PlayerLoadouts>();
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            viewLoadouts.RequestSave(1, "Mine");
            viewLoadouts.RequestDelete(0);
            viewLoadouts.RequestWear(0);
            CollectionAssert.AreEqual(new[] { "Save 1 Mine", "Delete 0 ", "Wear 0 " }, asked.Ops, "the view sends its requests");
            Assert.IsFalse(viewLoadouts.IsUsed(1), "and changes nothing itself");
            viewLoadouts.RunLoadoutOp(LoadoutOpKind.Save, 1, "Mine");
            Assert.IsFalse(viewLoadouts.IsUsed(1), "a view never runs a loadout request");
            viewLoadouts.MirrorEntry(0, makerLoadouts.Get(0));
            Assert.AreEqual(makerLoadouts.Get(0), viewLoadouts.Get(0), "the owner's window shows the server's slot");
            Assert.AreEqual("Arena", viewLoadouts.NameOf(0));

            // an old save (NGO's text) loads into a fresh server body unchanged and saves back every byte
            const string account = "9a8b7c6d5e4f30211203f4e5d6c7b8a9";
            const string characterId = "c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4";
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(10));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            var character = new CharacterSave { characterId = characterId, name = "Kit" };
            character.Set("loadouts", ngoText);
            stored.characters.Add(character);
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(10), Serial = 10, NetworkId = 10, Account = account, CharacterId = characterId, Name = "Kit",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), "admitted");
            var agent = body.GetComponent<PlayerSaveAgent>();
            for (int i = 0; i < 30; i++) yield return null;
            var loaded = body.GetComponent<PlayerLoadouts>();
            Assert.AreEqual(makerLoadouts.Get(0), loaded.Get(0), "the saved loadout is on the server body");
            agent.Save();
            ServerSave.WaitForDisk();
            Assert.AreEqual(ngoText, ServerSave.ReadAccountFrom(ServerSave.Store, account).Find(characterId).Get("loadouts"),
                "the loadouts part saves back byte for byte (same format, no migration)");

            loaded.RunLoadoutOp(LoadoutOpKind.Delete, 0, null);
            Assert.IsFalse(loaded.IsUsed(0), "deleted by the server's rules");
            agent.OnServerLeaving();
        }

        [UnityTest]
        public IEnumerator B10_AFlaskRefill_RunsOnTheServerBody_AtAnApothecary_ForCredsThroughTheWallet()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(11));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(11));
            yield return null;
            var inv = server.GetComponent<PlayerInventory>();
            var wallet = server.GetComponent<KryticalNova.Economy.PlayerWallet>();
            var flask = server.GetComponent<PlayerFlask>();
            Assert.IsNotNull(flask, "the server body has its Flask part");
            ItemDefinition flaskItem = null;
            foreach (ItemDefinition def in inv.Catalog.Items) if (def != null && def.useEffect == ItemUseEffect.FluxFlask) { flaskItem = def; break; }
            Assert.IsNotNull(flaskItem, "the catalog has a Flux Flask");

            // a Flask with two drinks used, and Creds for it
            int cell = -1;
            for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == flaskItem.itemId) { cell = i; break; }
            if (cell < 0)
            {
                Assert.AreEqual(1, inv.ServerGive(flaskItem.itemId, 1));
                for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == flaskItem.itemId) { cell = i; break; }
            }
            ItemStack held = inv.Get(cell);
            inv.ServerSetCellData(cell, held, (held.data & ~0xFFUL) | 2UL);
            wallet.ServerSet(1000);

            // the view only asks
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            view.GetComponent<PlayerFlask>().RequestRefill();
            CollectionAssert.AreEqual(new[] { "RefillFlask" }, asked.Ops, "the owner's view sends the refill to the server");

            // no Apothecary in reach: refused, nothing spent
            flask.RunRefill();
            Assert.AreEqual(1000, wallet.Creds, "no Apothecary: nothing spent");
            Assert.AreEqual(2, FlaskRules.Used(inv.Get(cell).data, 3), "and nothing refilled");

            // an Apothecary beside the body: two drinks refilled at the settings' price, through the wallet (CredsSink.Vendor)
            var definition = ScriptableObject.CreateInstance<KryticalNova.NPC.NpcDefinition>();
            definition.offers = KryticalNova.NPC.NpcOffer.FlaskRefill;
            var npcObject = new GameObject("Apothecary");
            npcObject.SetActive(false);
            var npc = npcObject.AddComponent<KryticalNova.NPC.NpcInteractable>();
            typeof(KryticalNova.NPC.NpcInteractable).GetField("definition", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(npc, definition);
            npcObject.transform.position = server.transform.position + new Vector3(1f, 0f, 0f);
            npcObject.SetActive(true);
            m_Made.Add(npcObject);

            // too few Creds: refused, nothing spent, nothing refilled
            var flaskSettings = (FlaskSettings)typeof(PlayerFlask).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flask);
            long price = flaskSettings != null ? flaskSettings.refillPricePerCharge : 60;
            Assert.Greater(price, 0, "a drink costs Creds");
            wallet.ServerSet(price * 2 - 1);
            flask.RunRefill();
            Assert.AreEqual(price * 2 - 1, wallet.Creds, "too few Creds: nothing spent");
            Assert.AreEqual(2, FlaskRules.Used(inv.Get(cell).data, 3), "and nothing refilled");

            // enough: both drinks at the settings' price, through the wallet (CredsSink.Vendor), and a second click charges nothing
            wallet.ServerSet(1000);
            flask.RunRefill();
            Assert.AreEqual(0, FlaskRules.Used(inv.Get(cell).data, 3), "both drinks refilled: the Flask is full");
            Assert.AreEqual(1000 - price * 2, wallet.Creds, "Creds down by exactly two drinks at the settings' price");
            long paid = 1000 - wallet.Creds;
            flask.RunRefill();
            Assert.AreEqual(1000 - paid, wallet.Creds, "a double click: nothing more spent");
            view.GetComponent<PlayerFlask>().RunRefill();
            Assert.AreEqual(1000 - paid, wallet.Creds, "a view never refills");
            Object.Destroy(definition);
        }

        [UnityTest]
        public IEnumerator B11_StatPoints_ArePutInByTheServerBody_AndOtherPlayersSeeTheLevel()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(12));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(12));
            GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(12));
            yield return null;
            var progress = server.GetComponent<PlayerCharacterProgression>();
            progress.SetLevel(5);
            progress.AddStatPoints(4);
            int points = progress.StatPoints, strength = progress.State.strength;
            Assert.GreaterOrEqual(points, 4, "the server body holds stat points");

            // the owner's view predicts and asks; it changes nothing itself
            var viewProgress = view.GetComponent<PlayerCharacterProgression>();
            OwnerProgressBridge.Mirror(view, OwnerProgressBridge.Read(server).Value);
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            int predicted = viewProgress.TryAllocateMultiple(CharacterAttribute.Strength, 3);
            Assert.AreEqual(3, predicted, "the owner's prediction (NGO's expected)");
            CollectionAssert.AreEqual(new[] { $"Allocate {(byte)CharacterAttribute.Strength} 3" }, asked.Ops, "sent to the server");
            Assert.AreEqual(points, viewProgress.StatPoints, "the view spends nothing itself");
            Assert.AreEqual(0, viewProgress.RunAllocate(CharacterAttribute.Strength, 3), "a view never allocates");

            // the server body allocates by its own rules
            Assert.AreEqual(3, progress.RunAllocate(CharacterAttribute.Strength, 3));
            Assert.AreEqual(strength + 3, progress.State.strength, "three points in Strength");
            Assert.AreEqual(points - 3 * Mathf.Max(1, progress.AllocationCost), progress.StatPoints, "paid in points");

            // more than the points pay for: NGO's rule puts in only what they pay for, never below zero; with none left, nothing at all
            int cost = Mathf.Max(1, progress.AllocationCost);
            int left = progress.StatPoints, vitality = progress.State.vitality;
            int put = progress.RunAllocate(CharacterAttribute.Vitality, 1000);
            Assert.LessOrEqual(put, left / cost, "never more than the points pay for");
            Assert.AreEqual(vitality + put, progress.State.vitality);
            Assert.AreEqual(left - put * cost, progress.StatPoints, "paid exactly");
            Assert.GreaterOrEqual(progress.StatPoints, 0, "never below zero");
            int agility = progress.State.agility, before = progress.StatPoints;
            if (before < cost)
            {
                Assert.AreEqual(0, progress.RunAllocate(CharacterAttribute.Agility, 5), "no points: nothing put in");
                Assert.AreEqual(agility, progress.State.agility);
                Assert.AreEqual(before, progress.StatPoints, "and nothing spent");
            }

            // another player's body: the level for the nameplate, without a level-up on first sight, with one after
            Assert.IsNotNull(remote.GetComponent<PlayerCharacterProgression>(), "a remote view keeps its progression (the nameplate's level)");
            var remoteProgress = remote.GetComponent<PlayerCharacterProgression>();
            int levelUps = 0;
            remoteProgress.LevelGained += _ => levelUps++;
            remoteProgress.MirrorObservedLevel(5);
            Assert.AreEqual(5, remoteProgress.CurrentLevel, "the nameplate shows level 5");
            Assert.AreEqual(0, levelUps, "first sight is no level-up");
            remoteProgress.MirrorObservedLevel(6);
            Assert.AreEqual(1, levelUps, "a level gained later shows as one");
        }

        [UnityTest]
        public IEnumerator B12_TheOwnersStatSheet_ShowsTheSerumsTheServerBodyTook()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(13));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(13));
            GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(13));
            yield return null;
            var serums = server.GetComponent<PlayerSerums>();
            Assert.IsNotNull(serums, "the server body has its Serums part");
            Assert.IsTrue(serums.ServerSetTaken(0, 3), "the server body took 3 of the first kind");
            Assert.IsTrue(serums.ServerSetTaken(2, 7));

            OwnerSerums? read = OwnerProgressBridge.ReadSerums(server);
            Assert.IsTrue(read.HasValue);
            Assert.AreEqual(3, read.Value.K0);
            Assert.AreEqual(7, read.Value.K2);

            var viewSerums = view.GetComponent<PlayerSerums>();
            int changed = 0;
            viewSerums.Changed += () => changed++;
            viewSerums.MirrorState(OwnerProgressBridge.StateOf(read.Value));
            Assert.AreEqual(serums.State, viewSerums.State, "the owner's sheet has the server's Serums");
            Assert.AreEqual(1, changed, "its windows are told once");
            viewSerums.MirrorState(OwnerProgressBridge.StateOf(read.Value));
            Assert.AreEqual(1, changed, "the same state again: nothing to redraw");
            Assert.IsNull(remote.GetComponent<PlayerSerums>(), "another player's body has no Serums (stats only, as NGO's observers)");
        }

        [UnityTest]
        public IEnumerator B13_AnXpBooster_IsUsedAndKept_TheSaveIsReal_AndTheOwnerSeesTheClock()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("xpboosts"), "the xpboosts part is real now");
            Assert.IsTrue(PlayerRequestServerSystem.EffectPorted(ItemUseEffect.ExperienceBoost), "and boosters are used");

            // an idle clock text in NGO's format (no booster running: it never changes from save to save)
            GameObject maker = Make(NetRoleKind.ServerBody, false, new PlayerKey(14));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(14));
            yield return null;
            string idleText = maker.GetComponent<PlayerExperienceBoosts>().CaptureSave();

            const string account = "7e6d5c4b3a2918070605f4e3d2c1b0a9";
            const string characterId = "e1f2a3b4c5d6e7f8091a2b3c4d5e6f70";
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(15));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            var character = new CharacterSave { characterId = characterId, name = "Boost" };
            character.Set("xpboosts", idleText);
            stored.characters.Add(character);
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(15), Serial = 15, NetworkId = 15, Account = account, CharacterId = characterId, Name = "Boost",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), "admitted");
            var agent = body.GetComponent<PlayerSaveAgent>();
            for (int i = 0; i < 30; i++) yield return null;
            agent.Save();
            ServerSave.WaitForDisk();
            Assert.AreEqual(idleText, ServerSave.ReadAccountFrom(ServerSave.Store, account).Find(characterId).Get("xpboosts"),
                "an old idle clock text loads and saves back byte for byte (same format, no migration)");

            // a booster used through the item path (ItemUseRpc's server side): the item goes, the clock runs
            var inv = body.GetComponent<PlayerInventory>();
            var boosts = body.GetComponent<PlayerExperienceBoosts>();
            ItemDefinition booster = null;
            foreach (ItemDefinition def in inv.Catalog.Items) if (def != null && def.useEffect == ItemUseEffect.ExperienceBoost) { booster = def; break; }
            Assert.IsNotNull(booster, "the catalog has an XP booster");
            int index = -1;
            for (int i = 0; i < boosts.Count; i++) if (boosts.BoosterAt(i).itemId == booster.itemId) { index = i; break; }
            Assert.GreaterOrEqual(index, 0, "the booster is in the settings' list");
            inv.ServerGive(booster.itemId, 1);
            int held = inv.CountItem(booster.itemId);
            Assert.IsTrue(PlayerRequestServerSystem.UseOn(body, booster.itemId, out RequestReplyRpc reply), "used: " + reply.Text);
            Assert.AreEqual(held - 1, inv.CountItem(booster.itemId), "the item is used from the Pack");
            float left = boosts.SecondsLeft(index);
            Assert.Greater(left, 0f, "its clock runs");

            // the running clock comes back from its save text (the time of saving is written while a clock runs, as NGO)
            string runningText = boosts.CaptureSave();
            GameObject fresh = Make(NetRoleKind.ServerBody, false, new PlayerKey(16));
            yield return null;
            var freshBoosts = fresh.GetComponent<PlayerExperienceBoosts>();
            freshBoosts.RestoreSave(runningText);
            Assert.AreEqual(Mathf.Round(left), freshBoosts.SecondsLeft(index), 2f, "the clock comes back from the save");

            // the owner's view shows the server's clock and never runs one itself
            var viewBoosts = view.GetComponent<PlayerExperienceBoosts>();
            viewBoosts.MirrorSeconds(index, left);
            Assert.AreEqual(left, viewBoosts.SecondsLeft(index), 0.01f, "the owner sees the server's clock");
            Assert.AreEqual(0f, viewBoosts.ServerCharacterBonus, "a view never gives the bonus");
            agent.OnServerLeaving();
        }

        [UnityTest]
        public IEnumerator B14_SkillRanks_AreBoughtOnTheServerBody_TheSaveIsReal_AndTheOwnerSeesThem()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("skills"), "the skills part is real now");
            Assert.IsTrue(PlayerRequestServerSystem.EffectPorted(ItemUseEffect.LearnSkill), "and Skill Chips / Dossiers are used");

            // a body that bought a rank by NGO's rules gives the save text in NGO's format
            GameObject maker = Make(NetRoleKind.ServerBody, false, new PlayerKey(17));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(17));
            yield return null;
            var makerSkills = maker.GetComponent<PlayerCombatSkills>();
            var makerProgress = maker.GetComponent<PlayerCharacterProgression>();
            makerProgress.SetLevel(makerProgress.MaxLevel);
            makerProgress.AddSkillPoints(500);
            SkillDefinitionSO skill = null;
            int index = -1;
            for (int i = 0; i < makerSkills.AvailableSkills.Count; i++)
            {
                SkillDefinitionSO each = makerSkills.AvailableSkills[i];
                if (each == null || makerSkills.IsLocked(each) || makerSkills.GetRank(each) >= each.maxRank) continue;
                skill = each;
                index = i;
                break;
            }
            Assert.IsNotNull(skill, "the body has a skill it can buy a rank of");
            int rank = makerSkills.GetRank(skill);
            Assert.AreEqual(1, makerSkills.RunUpgrade(skill.skillId, 1), "the server body buys one rank by NGO's rules");
            Assert.AreEqual(rank + 1, makerSkills.GetRank(skill));
            string ngoText = makerSkills.CaptureSave();

            // the owner's view predicts and asks, changes nothing itself, and shows the server's rank
            var viewSkills = view.GetComponent<PlayerCombatSkills>();
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            OwnerProgressBridge.Mirror(view, OwnerProgressBridge.Read(maker).Value);   // its points and level, as the owner has them
            int viewRank = viewSkills.GetRank(skill);
            int predicted = viewSkills.TryUpgradeMultiple(skill, 1);
            Assert.AreEqual(1, predicted, "the owner's prediction");
            CollectionAssert.AreEqual(new[] { $"Upgrade {skill.skillId} 1" }, asked.Ops, "sent to the server");
            Assert.AreEqual(viewRank, viewSkills.GetRank(skill), "the view buys nothing itself");
            Assert.AreEqual(0, viewSkills.RunUpgrade(skill.skillId, 1), "a view never upgrades");
            var ranks = new List<byte>();
            ulong books = makerSkills.ReadRanks(ranks);
            for (int i = 0; i < ranks.Count; i++) viewSkills.MirrorRank(i, ranks[i]);
            viewSkills.MirrorBooks(books);
            Assert.AreEqual(makerSkills.GetRank(skill), viewSkills.GetRank(skill), "the owner sees the server's rank");

            // an old save (NGO's text) loads into a fresh admitted server body and saves back every byte
            const string account = "5f4e3d2c1b0a99887766554433221100";
            const string characterId = "a0b1c2d3e4f5061728394a5b6c7d8e9f";
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(18));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            var character = new CharacterSave { characterId = characterId, name = "Ranks" };
            character.Set("skills", ngoText);
            stored.characters.Add(character);
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(18), Serial = 18, NetworkId = 18, Account = account, CharacterId = characterId, Name = "Ranks",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), "admitted");
            var agent = body.GetComponent<PlayerSaveAgent>();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(rank + 1, body.GetComponent<PlayerCombatSkills>().GetRank(skill), "the saved rank is on the server body");
            agent.Save();
            ServerSave.WaitForDisk();
            Assert.AreEqual(ngoText, ServerSave.ReadAccountFrom(ServerSave.Store, account).Find(characterId).Get("skills"),
                "the skills part saves back byte for byte (same format, no migration)");
            agent.OnServerLeaving();
        }

        [UnityTest]
        public IEnumerator B15_RunAndJumpRanks_AreBoughtOnTheServerBody_AndTheMovementSaveIsReal()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("movement"), "the movement part is real now");
            GameObject maker = Make(NetRoleKind.ServerBody, false, new PlayerKey(19));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(19));
            yield return null;
            var moves = maker.GetComponent<PlayerMovementSkills>();
            var progress = maker.GetComponent<PlayerCharacterProgression>();
            progress.SetLevel(progress.MaxLevel);
            progress.AddSkillPoints(500);
            int run = moves.RunRank;
            Assert.AreEqual(1, moves.RunUpgrade(MovementSkillId.Run, 1), "the server body buys a Run rank by NGO's rules");
            Assert.AreEqual(run + 1, moves.RunRank);
            string ngoText = moves.CaptureSave();

            var viewMoves = view.GetComponent<PlayerMovementSkills>();
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            OwnerProgressBridge.Mirror(view, OwnerProgressBridge.Read(maker).Value);
            int viewJump = viewMoves.JumpRank;
            int predicted = viewMoves.TryUpgradeMultiple(MovementSkillId.Jump, 1);
            Assert.AreEqual(1, predicted, "the owner's prediction");
            CollectionAssert.AreEqual(new[] { $"Move {(byte)MovementSkillId.Jump} 1" }, asked.Ops, "sent to the server");
            Assert.AreEqual(viewJump, viewMoves.JumpRank, "the view buys nothing itself");
            Assert.AreEqual(0, viewMoves.RunUpgrade(MovementSkillId.Jump, 1), "a view never upgrades");

            const string account = "0a1b2c3d4e5f60718293a4b5c6d7e8f9";
            const string characterId = "f9e8d7c6b5a4938271605f4e3d2c1b0a";
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(20));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            var character = new CharacterSave { characterId = characterId, name = "Runner" };
            character.Set("movement", ngoText);
            stored.characters.Add(character);
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(20), Serial = 20, NetworkId = 20, Account = account, CharacterId = characterId, Name = "Runner",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), "admitted");
            var agent = body.GetComponent<PlayerSaveAgent>();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(run + 1, body.GetComponent<PlayerMovementSkills>().RunRank, "the saved Run rank is on the server body");
            agent.Save();
            ServerSave.WaitForDisk();
            Assert.AreEqual(ngoText, ServerSave.ReadAccountFrom(ServerSave.Store, account).Find(characterId).Get("movement"),
                "the movement part saves back byte for byte (same format, no migration)");
            agent.OnServerLeaving();
        }

        GameObject Admitted(ulong key, string account, string characterId, string name, int networkId = 0)
        {
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(key));
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            stored.characters.Add(new CharacterSave { characterId = characterId, name = name });
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(key), Serial = key, NetworkId = networkId != 0 ? networkId : (int)key, Account = account, CharacterId = characterId, Name = name,
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), name + " admitted");
            return body;
        }

        [UnityTest]
        public IEnumerator B16_ADrop_IsHeldForItsDropperByKey_ThenTakenOnce_WithTheDropAndLootLedgerLines()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            PlayerKeys.Reset();
            const string accountA = "11112222333344445555666677778888", accountB = "88887777666655554444333322221111";
            GameObject a = Admitted(PlayerKeys.Mint(21).Value, accountA, "a0000000000000000000000000000001", "Dropper");
            GameObject b = Admitted(PlayerKeys.Mint(22).Value, accountB, "b0000000000000000000000000000002", "Taker");
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(21));
            for (int i = 0; i < 30; i++) yield return null;

            var lootA = a.GetComponent<PlayerLoot>();
            var lootB = b.GetComponent<PlayerLoot>();
            var invA = a.GetComponent<PlayerInventory>();
            var invB = b.GetComponent<PlayerInventory>();
            typeof(PlayerLoot).GetField("dropReserveSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(lootA, 0.5f);
            invA.ServerGive(9001, 3);
            int cell = -1;
            for (int i = 0; i < PlayerInventory.Capacity; i++) if (invA.Get(i).itemId == 9001) { cell = i; break; }
            int heldA = invA.CountItem(9001), heldB = invB.CountItem(9001);
            int stack = invA.Get(cell).count;

            // the owner's view only asks
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            view.GetComponent<PlayerLoot>().RequestDrop(ItemLocation.Bag(cell));
            CollectionAssert.AreEqual(new[] { $"Drop 0:{cell}" }, asked.Ops, "the view sends the drop to the server");

            // the server body drops it: held for the dropper by key (NGO's NetworkObjectId hold is Nobody on a DOTS body)
            int before = WorldDrop.Active.Count;
            lootA.RunDrop(ItemLocation.Bag(cell));
            Assert.AreEqual(heldA - stack, invA.CountItem(9001), "the stack left the dropper's Pack");
            Assert.AreEqual(before + 1, WorldDrop.Active.Count, "and lies on the ground");
            WorldDrop drop = WorldDrop.Active[WorldDrop.Active.Count - 1];
            Assert.AreNotEqual(0u, drop.ServerId, "it has a server id");
            Assert.AreSame(drop, WorldDrop.FindById(drop.ServerId));
            Assert.IsTrue(drop.IsHeld, "held");
            Assert.IsTrue(drop.IsHeldFor(a.GetComponent<NetworkObject>()), "for the dropper");
            Assert.IsTrue(drop.IsReservedForAnother(b.GetComponent<NetworkObject>()), "not for anyone else");

            lootB.RunPickup(drop.ServerId);
            Assert.AreEqual(heldB, invB.CountItem(9001), "another player cannot take it while it is held");

            yield return new WaitForSeconds(1f);   // the hold (0.5 s here; 20 s in the game) runs out
            lootB.RunPickup(drop.ServerId);
            Assert.AreEqual(heldB + stack, invB.CountItem(9001), "then they take the whole stack");
            lootB.RunPickup(drop.ServerId);
            Assert.AreEqual(heldB + stack, invB.CountItem(9001), "a second pickup gives nothing (no double)");
            Assert.IsNull(WorldDrop.FindById(drop.ServerId), "the drop is gone");

            List<LedgerEntry> droppedLines = ServerSave.ReadLedger(accountA, 0, 100).FindAll(l => l.kind == LedgerKinds.Drop);
            List<LedgerEntry> lootLines = ServerSave.ReadLedger(accountB, 0, 100).FindAll(l => l.kind == LedgerKinds.Loot);
            a.GetComponent<PlayerSaveAgent>().Save();
            b.GetComponent<PlayerSaveAgent>().Save();
            ServerSave.WaitForDisk();
            droppedLines = ServerSave.ReadLedger(accountA, 0, 100).FindAll(l => l.kind == LedgerKinds.Drop);
            lootLines = ServerSave.ReadLedger(accountB, 0, 100).FindAll(l => l.kind == LedgerKinds.Loot);
            Assert.AreEqual(1, droppedLines.Count, "one drop line on the dropper");
            Assert.AreEqual(-stack, droppedLines[0].items.Find(x => x.item == 9001).count, "with what was dropped");
            Assert.AreEqual(1, lootLines.Count, "one loot line on the taker");
            Assert.AreEqual(stack, lootLines[0].items.Find(x => x.item == 9001).count, "with what was taken");
            // a client of the entity netcode shows a server's drop as a view: in the drop list, with its id, and its pickup goes to the server
            var viewLoot = view.GetComponent<PlayerLoot>();
            WorldDrop shown = WorldDrop.ShowAsView(viewLoot.DropPrefab, 4242, new ItemStack(9001, 2), view.transform.position, 5f, true);
            Assert.IsNotNull(shown, "a view of the server's drop");
            Assert.Contains(shown, (System.Collections.ICollection)WorldDrop.Active, "in the drop list the HUD and the pickup key read");
            Assert.IsTrue(shown.ViewHeldForMe, "held for this player, as the server said");
            asked.Ops.Clear();
            viewLoot.RequestPickup(shown);
            CollectionAssert.AreEqual(new[] { "Pickup 4242" }, asked.Ops, "the pickup goes to the server by its id");
            shown.RemoveView();
            Assert.IsNull(WorldDrop.FindById(4242), "the view went when the server said");

            a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            b.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            PlayerKeys.Reset();
        }

        GameObject Npc(KryticalNova.NPC.NpcOffer offers, Vector3 at)
        {
            var definition = ScriptableObject.CreateInstance<KryticalNova.NPC.NpcDefinition>();
            definition.offers = offers;
            var npcObject = new GameObject("Npc " + offers);
            npcObject.SetActive(false);
            var npc = npcObject.AddComponent<KryticalNova.NPC.NpcInteractable>();
            typeof(KryticalNova.NPC.NpcInteractable).GetField("definition", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(npc, definition);
            npcObject.transform.position = at;
            npcObject.SetActive(true);
            m_Made.Add(npcObject);
            return npcObject;
        }

        [UnityTest]
        public IEnumerator B17_TheVaults_RunOnTheServerBody_CredsThroughTheWallet_BothSavesAreReal()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("vault"), "the vault part is real now");
            Assert.IsFalse(SavePassthrough.Is("deepvault"), "and the deepvault part");
            GameObject maker = Make(NetRoleKind.ServerBody, false, new PlayerKey(23));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(23));
            yield return null;
            var inv = maker.GetComponent<PlayerInventory>();
            var wallet = maker.GetComponent<KryticalNova.Economy.PlayerWallet>();
            var vault = maker.GetComponent<KryticalNova.Economy.PlayerVault>();
            var deep = maker.GetComponent<KryticalNova.Economy.PlayerDeepVault>();
            Assert.IsNotNull(vault, "the body has a Vault");
            Assert.IsNotNull(deep, "and a Deep Vault");

            inv.ServerGive(9001, 5);
            int Cell() { for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == 9001) return i; return -1; }
            int held = inv.CountItem(9001);

            // no Vaultkeeper in reach: refused, nothing moves
            vault.RunStorageOp(StorageOpKind.Deposit, Cell(), -1, 0);
            Assert.AreEqual(held, inv.CountItem(9001), "no Vaultkeeper: nothing deposited");

            GameObject keeper = Npc(KryticalNova.NPC.NpcOffer.Vault | KryticalNova.NPC.NpcOffer.DeepVault, maker.transform.position + Vector3.right);
            vault.RunStorageOp(StorageOpKind.Deposit, Cell(), -1, 2);
            Assert.AreEqual(held - 2, inv.CountItem(9001), "two went from the Pack");
            int inVault = 0;
            for (int i = 0; i < ((IBodyStorage)vault).CellCount; i++) if (vault.Get(i).itemId == 9001) inVault += vault.Get(i).count;
            Assert.AreEqual(2, inVault, "into the Vault");
            deep.RunStorageOp(StorageOpKind.Deposit, Cell(), -1, 1);
            Assert.AreEqual(held - 3, inv.CountItem(9001), "one into the Deep Vault");

            wallet.ServerSet(1000);
            vault.RunStorageOp(StorageOpKind.DepositCreds, 0, 0, 300);
            Assert.AreEqual(700, wallet.Creds, "Creds left the wallet");
            Assert.AreEqual(300, vault.Creds, "into the Vault");
            vault.RunStorageOp(StorageOpKind.WithdrawCreds, 0, 0, 100);
            Assert.AreEqual(800, wallet.Creds, "and back to the wallet");
            Assert.AreEqual(200, vault.Creds);
            vault.RunStorageOp(StorageOpKind.WithdrawCreds, 0, 0, 5000);
            Assert.AreEqual(800, wallet.Creds, "more than is kept: refused, nothing moves");
            Assert.AreEqual(200, vault.Creds);

            // the owner's view only asks, and shows the server's storage
            var viewVault = view.GetComponent<KryticalNova.Economy.PlayerVault>();
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            viewVault.RequestDepositCreds(50);
            CollectionAssert.AreEqual(new[] { "Storage 0 DepositCreds 0 0 50" }, asked.Ops, "sent to the server");
            viewVault.RunStorageOp(StorageOpKind.DepositCreds, 0, 0, 50);
            Assert.AreEqual(0, viewVault.Creds, "a view never moves anything itself");
            for (int i = 0; i < ((IBodyStorage)vault).CellCount; i++) viewVault.MirrorCell(i, vault.Get(i));
            viewVault.MirrorCreds(vault.Creds);
            Assert.AreEqual(200, viewVault.Creds, "the owner's window shows the server's Creds");
            Assert.AreEqual(vault.UsedCells, viewVault.UsedCells, "and its cells");

            string vaultText = vault.CaptureSave(), deepText = deep.CaptureSave();

            // old saves (NGO's texts) load into a fresh admitted server body unchanged and save back every byte
            const string account = "2b3c4d5e6f708192a3b4c5d6e7f80910";
            const string characterId = "d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9";
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(24));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            stored.Set("deepvault", deepText);
            var character = new CharacterSave { characterId = characterId, name = "Keeper" };
            character.Set("vault", vaultText);
            stored.characters.Add(character);
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(24), Serial = 24, NetworkId = 24, Account = account, CharacterId = characterId, Name = "Keeper",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(body, in context), "admitted");
            var agent = body.GetComponent<PlayerSaveAgent>();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(200, body.GetComponent<KryticalNova.Economy.PlayerVault>().Creds, "the saved Vault is on the server body");
            agent.Save();
            ServerSave.WaitForDisk();
            AccountSave back = ServerSave.ReadAccountFrom(ServerSave.Store, account);
            Assert.AreEqual(vaultText, back.Find(characterId).Get("vault"), "the vault part saves back byte for byte");
            Assert.AreEqual(deepText, back.Get("deepvault"), "the deepvault part saves back byte for byte");
            agent.OnServerLeaving();
            Object.Destroy(keeper);
        }

        [UnityTest]
        public IEnumerator B18_Shopping_RunsOnTheServerBody_TheWeeklyLimitHolds_LedgerLines_AndVendorWeekIsReal()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("vendorWeek"), "the vendorWeek part is real now");
            PlayerKeys.Reset();
            const string account = "3c4d5e6f708192a3b4c5d6e7f8091021", characterId = "e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0";
            GameObject body = Admitted(PlayerKeys.Mint(25).Value, account, characterId, "Shopper");
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(25));
            for (int i = 0; i < 30; i++) yield return null;

            var shop = body.GetComponent<KryticalNova.Economy.PlayerShopping>();
            var inv = body.GetComponent<PlayerInventory>();
            var wallet = body.GetComponent<KryticalNova.Economy.PlayerWallet>();
            Assert.IsNotNull(shop, "the body has its shopping");
            Assert.IsNotNull(shop.Vendors, "and the vendor catalog");

            // a listing with a weekly limit (the Armorer's forge materials, owner 17 Oct 2026)
            KryticalNova.Economy.VendorStock stock = null;
            ItemDefinition item = null;
            int limit = 0;
            foreach (var s in shop.Vendors.stocks)
            {
                if (s == null || s.vendor == null) continue;
                foreach (var listing in s.listings)
                    if (listing.item != null && listing.price > 0 && listing.weeklyLimit > 0 && inv.Catalog.Get(listing.item.itemId) != null)
                    { stock = s; item = listing.item; limit = listing.weeklyLimit; break; }
                if (stock != null) break;
            }
            Assert.IsNotNull(stock, "a vendor lists an item with a weekly limit");
            Assert.IsTrue((stock.vendor.offers & KryticalNova.NPC.NpcOffer.Shop) != 0, stock.vendor.name + " offers a shop");
            Assert.IsTrue(stock.TryGetListed(item, out long price) && price > 0, "a listed price (what NGO's TryPrice takes first)");

            wallet.ServerSet(price * (limit + 10) + 100000);
            long creds = wallet.Creds;
            int held = inv.CountItem(item.itemId);

            // no vendor in reach: refused, nothing moves
            shop.RunBuy(stock.vendor.name, item.itemId, 1);
            Assert.AreEqual(creds, wallet.Creds, "far from the vendor: no Creds spent");
            Assert.AreEqual(held, inv.CountItem(item.itemId), "and nothing given");

            // the vendor's NPC by the body
            var npcObject = new GameObject("Npc " + stock.vendor.name);
            npcObject.SetActive(false);
            var npc = npcObject.AddComponent<KryticalNova.NPC.NpcInteractable>();
            typeof(KryticalNova.NPC.NpcInteractable).GetField("definition", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(npc, stock.vendor);
            npcObject.transform.position = body.transform.position + Vector3.right;
            npcObject.SetActive(true);
            m_Made.Add(npcObject);

            // asking for more than the week allows sells only what is left of it, at NGO's price, through the wallet
            shop.RunBuy(stock.vendor.name, item.itemId, limit + 5);
            Assert.AreEqual(held + limit, inv.CountItem(item.itemId), "the week's limit is all that is sold");
            Assert.AreEqual(creds - price * limit, wallet.Creds, "paid exactly the price of what was given");
            Assert.AreEqual(limit, shop.BoughtThisWeek(item.itemId), "the week remembers it");
            long afterBuy = wallet.Creds;
            shop.RunBuy(stock.vendor.name, item.itemId, 1);
            Assert.AreEqual(held + limit, inv.CountItem(item.itemId), "sold out this week: nothing more");
            Assert.AreEqual(afterBuy, wallet.Creds, "and nothing spent");

            // selling: a stack that changed since the player saw it (another state; NGO compares item and state) is left alone; then sold
            int cell = -1;
            for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == item.itemId) { cell = i; break; }
            ItemStack stack = inv.Get(cell);
            long unit = shop.SellValue(item);
            Assert.Greater(unit, 0, "the vendor buys it back");
            shop.RunSell(cell, 1, new ItemStack(stack.itemId, stack.count, stack.data ^ 1UL), true);
            Assert.AreEqual(afterBuy, wallet.Creds, "a changed stack: not sold");
            if (shop.NeedsConfirm(item, stack))
            {
                shop.RunSell(cell, 1, stack, false);
                Assert.AreEqual(afterBuy, wallet.Creds, "a sale that must be confirmed is refused without it");
            }
            int beforeSell = inv.CountItem(item.itemId);
            shop.RunSell(cell, 1, stack, true);
            Assert.AreEqual(beforeSell - 1, inv.CountItem(item.itemId), "one sold");
            Assert.AreEqual(afterBuy + unit, wallet.Creds, "for the sell-back value, through the wallet");

            // the ledger has NGO's shop.buy and shop.sell lines
            body.GetComponent<PlayerSaveAgent>().Save();
            ServerSave.WaitForDisk();
            List<LedgerEntry> buys = ServerSave.ReadLedger(account, 0, 100).FindAll(l => l.kind == LedgerKinds.ShopBuy);
            List<LedgerEntry> sells = ServerSave.ReadLedger(account, 0, 100).FindAll(l => l.kind == LedgerKinds.ShopSell);
            Assert.AreEqual(1, buys.Count, "one shop.buy line");
            Assert.AreEqual(limit, buys[0].items.Find(x => x.item == item.itemId).count, "with what was bought");
            Assert.AreEqual(1, sells.Count, "one shop.sell line");
            Assert.AreEqual(-1, sells[0].items.Find(x => x.item == item.itemId).count, "with what was sold");

            // the owner's view only asks, and shows the server's week
            var viewShop = view.GetComponent<KryticalNova.Economy.PlayerShopping>();
            var viewInv = view.GetComponent<PlayerInventory>();
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            viewShop.RequestBuy(stock, item, 2);
            viewInv.MirrorCell(0, new ItemStack(item.itemId, 3));
            viewShop.RequestSell(0, 1, true);
            CollectionAssert.AreEqual(new[] { $"Buy {stock.vendor.name} {item.itemId} 2", $"Sell 0 1 {item.itemId}x3:0 True" }, asked.Ops, "sent to the server");
            int viewHeld = viewInv.CountItem(item.itemId);
            viewShop.RunBuy(stock.vendor.name, item.itemId, 1);
            Assert.AreEqual(viewHeld, viewInv.CountItem(item.itemId), "a view never buys itself");
            viewShop.MirrorWeek(((KryticalNova.Items.IBodyShopping)shop).BoughtWeek);
            viewShop.MirrorBought(item.itemId, limit);
            Assert.AreEqual(limit, viewShop.BoughtThisWeek(item.itemId), "the owner's window shows the server's week");
            viewShop.ServerResetWeek();
            Assert.AreEqual(limit, viewShop.BoughtThisWeek(item.itemId), "a view never resets the week itself");

            string weekText = shop.CaptureSave();

            // an old save (NGO's text) loads into a fresh admitted server body unchanged and saves back every byte
            const string account2 = "4d5e6f708192a3b4c5d6e7f809102132", characterId2 = "f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1";
            GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(26));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account2 };
            var character = new CharacterSave { characterId = characterId2, name = "Returner" };
            character.Set("vendorWeek", weekText);
            stored.characters.Add(character);
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(26), Serial = 26, NetworkId = 26, Account = account2, CharacterId = characterId2, Name = "Returner",
                RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
            var agent = other.GetComponent<PlayerSaveAgent>();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(limit, other.GetComponent<KryticalNova.Economy.PlayerShopping>().BoughtThisWeek(item.itemId), "the saved week is on the server body");
            agent.Save();
            ServerSave.WaitForDisk();
            AccountSave back = ServerSave.ReadAccountFrom(ServerSave.Store, account2);
            Assert.AreEqual(weekText, back.Find(characterId2).Get("vendorWeek"), "the vendorWeek part saves back byte for byte");
            agent.OnServerLeaving();
            body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            PlayerKeys.Reset();
        }

        [UnityTest]
        public IEnumerator B19_ATrade_RunsOnTheServerBodies_SwapsWhole_SavesBothTogether_WithBothLedgerLines_AndTradeIsReal()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("trade"), "the trade part is real now");
            IOwnerMessages oldMessages = OwnerMessages.Current;
            ITradeOwnerLink oldLink = TradeOwnerLink.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            TradeOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string accountA = "5e6f708192a3b4c5d6e7f80910213243", accountB = "6f708192a3b4c5d6e7f8091021324354";
                const string charA = "a1000000000000000000000000000031", charB = "b2000000000000000000000000000032";
                GameObject a = Admitted(PlayerKeys.Mint(31).Value, accountA, charA, "Annika");
                GameObject b = Admitted(PlayerKeys.Mint(32).Value, accountB, charB, "Bodhan");
                GameObject viewA = Make(NetRoleKind.ClientView, true, new PlayerKey(31));
                for (int i = 0; i < 30; i++) yield return null;
                var tradeA = a.GetComponent<KryticalNova.Economy.PlayerTrade>();
                var tradeB = b.GetComponent<KryticalNova.Economy.PlayerTrade>();
                var invA = a.GetComponent<PlayerInventory>();
                var invB = b.GetComponent<PlayerInventory>();
                var walletA = a.GetComponent<KryticalNova.Economy.PlayerWallet>();
                var walletB = b.GetComponent<KryticalNova.Economy.PlayerWallet>();
                Assert.IsNotNull(tradeA, "the body has its trade");
                invA.ServerGive(9001, 3);
                walletA.ServerSet(1000);
                walletB.ServerSet(500);
                int heldA = invA.CountItem(9001), heldB = invB.CountItem(9001);
                int cell = -1;
                for (int i = 0; i < PlayerInventory.Capacity; i++) if (invA.Get(i).itemId == 9001) { cell = i; break; }

                // asked by name (the menu's way on DOTS: the other body has no PlayerTrade on a client), answered, a session on both
                tradeA.RunTradeOp(TradeOpKind.RequestByName, "Bodhan", 0, 0, 0, false);
                Assert.IsTrue(told.Any(b, "prompt Annika"), "Bo is shown Ann's request: " + string.Join(" | ", told.To(b)));
                tradeB.RunTradeOp(TradeOpKind.Respond, null, 0, 0, 0, true);
                Assert.IsTrue(told.Any(a, "view True Bodhan"), "Ann's window opens on Bo");
                Assert.IsTrue(told.Any(b, "view True Annika"), "and Bo's on Ann");

                tradeA.RunTradeOp(TradeOpKind.Offer, null, (short)cell, 2, 0, false);
                tradeB.RunTradeOp(TradeOpKind.SetCreds, null, 0, 0, 100, false);
                tradeA.RunTradeOp(TradeOpKind.Confirm, null, 0, 0, 0, false);
                Assert.IsTrue(told.Any(a, "notice Both offers must be locked first."), "NGO's rule: lock first");
                tradeA.RunTradeOp(TradeOpKind.Lock, null, 0, 0, 0, true);
                tradeB.RunTradeOp(TradeOpKind.Lock, null, 0, 0, 0, true);
                tradeA.RunTradeOp(TradeOpKind.Confirm, null, 0, 0, 0, false);
                Assert.AreEqual(heldA, invA.CountItem(9001), "one confirmation moves nothing");
                tradeB.RunTradeOp(TradeOpKind.Confirm, null, 0, 0, 0, false);

                // the swap: whole, on both sides, Creds through the wallets
                Assert.AreEqual(heldA - 2, invA.CountItem(9001), "two left Ann's Pack");
                Assert.AreEqual(heldB + 2, invB.CountItem(9001), "and are in Bo's");
                Assert.AreEqual(1100, walletA.Creds, "Ann received Bo's 100 Creds");
                Assert.AreEqual(400, walletB.Creds, "Bo paid them");
                Assert.IsTrue(told.Any(a, "view False"), "the windows close");

                // saved together: both accounts hold the trade, each with its ledger line
                ServerSave.WaitForDisk();
                AccountSave savedA = ServerSave.ReadAccountFrom(ServerSave.Store, accountA), savedB = ServerSave.ReadAccountFrom(ServerSave.Store, accountB);
                StringAssert.Contains("9001", savedB.Find(charB).Get("inventory"), "Bo's saved Pack has the items");
                List<LedgerEntry> linesA = ServerSave.ReadLedger(accountA, 0, 100).FindAll(l => l.kind == LedgerKinds.Trade);
                List<LedgerEntry> linesB = ServerSave.ReadLedger(accountB, 0, 100).FindAll(l => l.kind == LedgerKinds.Trade);
                Assert.AreEqual(1, linesA.Count, "one trade line on Ann");
                Assert.AreEqual(-2, linesA[0].items.Find(x => x.item == 9001).count, "she gave two");
                Assert.AreEqual(1, linesB.Count, "one trade line on Bo");
                Assert.AreEqual(2, linesB[0].items.Find(x => x.item == 9001).count, "he received two");
                string tradeText = savedB.Get("trade");
                StringAssert.Contains("\"sent\":100", tradeText, "Bo's day total of Creds sent is saved with the account");

                // the owner's view only asks
                var asked = new Asked();
                viewA.GetComponent<BodyRoleTag>().Requests = asked;
                var viewTrade = viewA.GetComponent<KryticalNova.Economy.PlayerTrade>();
                viewTrade.Request("Bodhan");
                viewTrade.Offer(5, 2);
                viewTrade.SetLocked(true);
                viewTrade.Respond(false);
                CollectionAssert.AreEqual(new[] { "Trade RequestByName Bodhan 0 0 0 False", "Trade Offer  5 2 0 False", "Trade Lock  0 0 0 True", "Trade Respond  0 0 0 False" },
                    asked.Ops, "sent to the server");
                viewTrade.MirrorView(new BodyTradeView { active = true, partnerName = "Bodhan", mine = new[] { new BodyTradeLine(5, new ItemStack(9001, 2)) } });
                Assert.IsTrue(viewTrade.IsTrading, "the owner's window shows the server's trade");
                Assert.AreEqual("Bodhan", viewTrade.PartnerName);
                Assert.AreEqual(1, viewTrade.Mine.Count);

                // an old save (NGO's account text) loads into a fresh admitted server body unchanged and saves back every byte
                const string account2 = "708192a3b4c5d6e7f809102132435465", char2 = "c3000000000000000000000000000036";
                GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(36));
                yield return null;
                CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
                var stored = new AccountSave { accountId = account2 };
                stored.Set("trade", tradeText);
                stored.characters.Add(new CharacterSave { characterId = char2, name = "Calder" });
                Assert.IsTrue(ServerSave.WriteAccount(stored));
                ServerSave.WaitForDisk();
                var context = new ServerBodyContext
                {
                    Key = new PlayerKey(36), Serial = 36, NetworkId = 36, Account = account2, CharacterId = char2, Name = "Calder", RecipeText = RecipeText.Encode(recipe),
                };
                Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
                var agent = other.GetComponent<PlayerSaveAgent>();
                for (int i = 0; i < 30; i++) yield return null;
                agent.Save();
                ServerSave.WaitForDisk();
                Assert.AreEqual(tradeText, ServerSave.ReadAccountFrom(ServerSave.Store, account2).Get("trade"), "the trade part saves back byte for byte");
                agent.OnServerLeaving();
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
                b.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                TradeOwnerLink.Current = oldLink;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B20_LeavingMidTrade_CancelsWithNothingMoved_ANetworkIdReusedNeverGetsIt_AndAPendingRequestDiesWithItsAsker()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            ITradeOwnerLink oldLink = TradeOwnerLink.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            TradeOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string accountA = "8192a3b4c5d6e7f80910213243546576", accountB = "92a3b4c5d6e7f8091021324354657687", accountC = "a3b4c5d6e7f809102132435465768798";
                const string charA = "a1000000000000000000000000000041", charB = "b2000000000000000000000000000042", charC = "c3000000000000000000000000000043";
                GameObject a = Admitted(PlayerKeys.Mint(41).Value, accountA, charA, "Annika");
                GameObject b = Admitted(PlayerKeys.Mint(42).Value, accountB, charB, "Bodhan");
                for (int i = 0; i < 30; i++) yield return null;
                var tradeA = a.GetComponent<KryticalNova.Economy.PlayerTrade>();
                var tradeB = b.GetComponent<KryticalNova.Economy.PlayerTrade>();
                var invA = a.GetComponent<PlayerInventory>();
                var walletA = a.GetComponent<KryticalNova.Economy.PlayerWallet>();
                var walletB = b.GetComponent<KryticalNova.Economy.PlayerWallet>();
                invA.ServerGive(9001, 3);
                walletA.ServerSet(1000);
                walletB.ServerSet(500);
                a.GetComponent<PlayerSaveAgent>().Save();
                b.GetComponent<PlayerSaveAgent>().Save();
                ServerSave.WaitForDisk();
                int heldA = invA.CountItem(9001);
                string packA = ServerSave.ReadAccountFrom(ServerSave.Store, accountA).Find(charA).Get("inventory");
                string packB = ServerSave.ReadAccountFrom(ServerSave.Store, accountB).Find(charB).Get("inventory");
                int cell = -1;
                for (int i = 0; i < PlayerInventory.Capacity; i++) if (invA.Get(i).itemId == 9001) { cell = i; break; }

                // 1. disconnect mid-trade: both locked, Ann confirmed, then Bo's connection closes
                tradeA.RunTradeOp(TradeOpKind.RequestByName, "Bodhan", 0, 0, 0, false);
                tradeB.RunTradeOp(TradeOpKind.Respond, null, 0, 0, 0, true);
                tradeA.RunTradeOp(TradeOpKind.Offer, null, (short)cell, 2, 0, false);
                tradeB.RunTradeOp(TradeOpKind.SetCreds, null, 0, 0, 100, false);
                tradeA.RunTradeOp(TradeOpKind.Lock, null, 0, 0, 0, true);
                tradeB.RunTradeOp(TradeOpKind.Lock, null, 0, 0, 0, true);
                tradeA.RunTradeOp(TradeOpKind.Confirm, null, 0, 0, 0, false);
                ServerLeaving.Notify(b);   // what the server does when the connection closes, before the body goes
                ServerSave.WaitForDisk();
                Assert.IsTrue(told.Any(a, "system Bodhan left the game. The trade was cancelled."), "Ann is told: " + string.Join(" | ", told.To(a)));
                Assert.AreEqual(heldA, invA.CountItem(9001), "nothing moved from Ann");
                Assert.AreEqual(1000, walletA.Creds, "nor to her");
                Assert.AreEqual(500, walletB.Creds, "nor from Bo");
                Assert.AreEqual(packB, ServerSave.ReadAccountFrom(ServerSave.Store, accountB).Find(charB).Get("inventory"), "Bo's saved Pack is as it was");
                a.GetComponent<PlayerSaveAgent>().Save();
                ServerSave.WaitForDisk();
                Assert.AreEqual(packA, ServerSave.ReadAccountFrom(ServerSave.Store, accountA).Find(charA).Get("inventory"), "and Ann's");
                Assert.IsEmpty(ServerSave.ReadLedger(accountA, 0, 100).FindAll(l => l.kind == LedgerKinds.Trade), "no trade line");
                int toBo = told.To(b).Count;
                tradeA.RunTradeOp(TradeOpKind.Confirm, null, 0, 0, 0, false);
                Assert.IsTrue(told.Any(a, "system You are not trading."), "the session is gone on Ann too");
                Assert.AreEqual(toBo, told.To(b).Count, "nothing more reaches the body that left");

                // 2. a NetworkId given again: Cy joins on Bo's NetworkId (new PlayerKey) and never gets Bo's trade, view, notice or prompt
                Object.Destroy(b);
                yield return null;
                GameObject c = Admitted(PlayerKeys.Mint(43).Value, accountC, charC, "Cyrus", networkId: 42);
                for (int i = 0; i < 30; i++) yield return null;
                var tradeC = c.GetComponent<KryticalNova.Economy.PlayerTrade>();
                Assert.IsEmpty(told.To(c).FindAll(x => !x.StartsWith("tell ") && !x.StartsWith("system ")), "Cy is sent nothing of Bo's (no trade view, notice or prompt)");
                tradeA.RunTradeOp(TradeOpKind.RequestByName, "Bodhan", 0, 0, 0, false);
                Assert.IsTrue(told.Any(a, "system No player named 'Bodhan' is online."), "Bo's name no longer finds anyone");
                Assert.IsEmpty(told.To(c).FindAll(x => !x.StartsWith("tell ") && !x.StartsWith("system ")), "and no trade line reaches Cy");

                // 3. a request pending when its asker leaves: the prompt closes and an accept starts nothing
                tradeA.RunTradeOp(TradeOpKind.RequestByName, "Cyrus", 0, 0, 0, false);
                Assert.IsTrue(told.Any(c, "prompt Annika"), "Cy is asked");
                ServerLeaving.Notify(a);
                Assert.IsTrue(told.Any(c, "prompt closed"), "Ann left: Cy's prompt closes");
                tradeC.RunTradeOp(TradeOpKind.Respond, null, 0, 0, 0, true);
                Assert.IsTrue(told.Any(c, "system There is no trade request to answer."), "an accept finds nothing");
                Assert.IsFalse(told.Any(c, "view True"), "no session starts");
                tradeC.RunTradeOp(TradeOpKind.Offer, null, 0, 1, 0, false);
                Assert.IsTrue(told.Any(c, "system You are not trading."));
                c.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                TradeOwnerLink.Current = oldLink;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B21_AKiosk_OpensSellsCollectsAndCloses_OnTheServerBodies_WithItsLedgerLines_AndAViewOnlyAsksAndShowsTheStand()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            IKioskOwnerLink oldLink = KioskOwnerLink.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            KioskOwnerLink.Current = told;
            KryticalNova.Economy.KioskService.ServerForgetStands();
            // a trade zone where Kiosks may open, around the bodies (World is not referenced by this test assembly)
            var zoneObject = new GameObject("Kiosk zone");
            zoneObject.AddComponent(System.Type.GetType("KryticalNova.World.TradeZone, KryticalNova.World", true));
            m_Made.Add(zoneObject);
            try
            {
                PlayerKeys.Reset();
                const string accountO = "b4c5d6e7f8091021324354657687a9b0", accountB = "c5d6e7f8091021324354657687a9b0c1";
                const string charO = "a1000000000000000000000000000051", charB = "b2000000000000000000000000000052";
                GameObject owner = Admitted(PlayerKeys.Mint(51).Value, accountO, charO, "Marisol");
                GameObject buyer = Admitted(PlayerKeys.Mint(52).Value, accountB, charB, "Tobias");
                GameObject viewO = Make(NetRoleKind.ClientView, true, new PlayerKey(51));
                for (int i = 0; i < 30; i++) yield return null;
                var kioskO = owner.GetComponent<KryticalNova.Economy.PlayerKiosk>();
                var kioskB = buyer.GetComponent<KryticalNova.Economy.PlayerKiosk>();
                var invO = owner.GetComponent<PlayerInventory>();
                var invB = buyer.GetComponent<PlayerInventory>();
                var walletO = owner.GetComponent<KryticalNova.Economy.PlayerWallet>();
                var walletB = buyer.GetComponent<KryticalNova.Economy.PlayerWallet>();
                Assert.IsNotNull(kioskO, "the body has its Kiosk side");
                Assert.IsNotNull(kioskO.StandPrefab, "and its stand prefab");
                invO.ServerGive(9001, 3);
                walletO.ServerSet(100);
                walletB.ServerSet(1000);
                int heldO = invO.CountItem(9001), heldB = invB.CountItem(9001);
                int cell = -1;
                for (int i = 0; i < PlayerInventory.Capacity; i++) if (invO.Get(i).itemId == 9001) { cell = i; break; }

                // open: NGO's rules on the server body (saved character, alive, trade zone, price cap, free tier), a kept stand, the owner's view
                kioskO.RunOpen("Marisol's Goods", new[] { new BodyKioskOffer { cell = (short)cell, count = 2, unitPrice = 50 } }, 0);
                KryticalNova.Economy.KioskRecord record = KryticalNova.Economy.KioskService.Find(charO);
                Assert.IsNotNull(record, "the Kiosk opened: " + string.Join(" | ", told.To(owner)));
                Assert.IsTrue(record.open);
                Assert.AreEqual(heldO - 2, invO.CountItem(9001), "the two listed left the Pack");
                Assert.AreEqual(1, KioskStands.Server.Count, "the server keeps one stand (not network-spawned)");
                IKioskStand stand = KioskStands.Server[0];
                Assert.AreNotEqual(0u, stand.ServerId, "with a server id");
                Assert.AreEqual(1, stand.ListingCount, "showing the listing");
                Assert.IsTrue(told.Any(owner, "kiosk True True Marisol's Goods"), "the owner's window shows the open Kiosk");
                int version = stand.Version;

                // buy: the buyer's Creds through the wallet, the item to the Pack, the till after tax, both ledger sides, the owner told
                kioskB.RunBuy(stand.ServerId, 0, new BodyKioskListing(record.listings[0].stack, 50), 1);
                Assert.AreEqual(heldB + 1, invB.CountItem(9001), "the buyer has it");
                Assert.AreEqual(950, walletB.Creds, "and paid 50");
                long net = kioskO.Settings.AfterTax(50, out _);
                Assert.AreEqual(net, record.till, "the till holds the price after tax");
                Assert.Greater(stand.Version, version, "the stand's shelf changed (its list goes to the players)");
                Assert.IsTrue(told.Any(owner, "system Kiosk: Tobias bought"), "the online owner is told");
                kioskO.RunBuy(stand.ServerId, 0, new BodyKioskListing(record.listings[0].stack, 50), 1);
                Assert.IsTrue(told.Any(owner, "tell This is your own Kiosk."), "NGO's own-Kiosk rule");
                ServerSave.WaitForDisk();
                List<LedgerEntry> buys = ServerSave.ReadLedger(accountB, 0, 100).FindAll(l => l.kind == LedgerKinds.KioskBuy);
                Assert.AreEqual(1, buys.Count, "one kiosk.buy line on the buyer (saved together with the Kiosks)");
                Assert.AreEqual(1, buys[0].items.Find(x => x.item == 9001).count);
                Assert.AreEqual(accountO, buys[0].otherAccount, "with the owner as the other side");

                // collect, then close: the till to the wallet, the unsold item back to the Pack, the stand gone, the record forgotten
                kioskO.RunCollect();
                Assert.AreEqual(100 + net, walletO.Creds, "the till went to the owner's wallet");
                kioskO.RunClose();
                Assert.AreEqual(heldO - 1, invO.CountItem(9001), "the unsold one came back");
                Assert.AreEqual(0, KioskStands.Server.Count, "the stand is gone");
                Assert.IsNull(KryticalNova.Economy.KioskService.Find(charO), "a closed, empty Kiosk is forgotten");
                owner.GetComponent<PlayerSaveAgent>().Save();
                ServerSave.WaitForDisk();
                List<LedgerEntry> ownerLines = ServerSave.ReadLedger(accountO, 0, 100);
                Assert.AreEqual(1, ownerLines.FindAll(l => l.kind == LedgerKinds.KioskOpen).Count, "one kiosk.open line");
                Assert.AreEqual(-2, ownerLines.Find(l => l.kind == LedgerKinds.KioskOpen).items.Find(x => x.item == 9001).count, "with the two listed");
                Assert.IsTrue(ownerLines.Exists(l => l.kind == LedgerKinds.KioskCollect && l.items.Exists(x => x.item == 9001 && x.count == 1)),
                    "and a kiosk.collect line with the unsold one back");

                // the owner's view only asks; a stand view shows the server's sign and shelf and buys by the stand's server id
                var asked = new Asked();
                viewO.GetComponent<BodyRoleTag>().Requests = asked;
                var viewKiosk = viewO.GetComponent<KryticalNova.Economy.PlayerKiosk>();
                viewKiosk.RequestOpen("Stall", new[] { new KryticalNova.Economy.KioskOffer { cell = 4, count = 1, unitPrice = 9 } }, 0);
                viewKiosk.RequestCollect();
                IKioskStand standView = viewKiosk.ShowStandView(7, Vector3.forward, 0f);
                Assert.IsNotNull(standView, "a stand view from the body's prefab");
                standView.ApplyView("Tobias's Wares", "Tobias", 1, 0, new[] { new BodyKioskListing(new ItemStack(9001, 2), 50) });
                var shownStand = (KryticalNova.Economy.KioskStand)standView;
                Assert.AreEqual("Tobias's Wares", shownStand.ShopName, "the server's sign");
                Assert.AreEqual(1, shownStand.ListingCount, "and shelf");
                Assert.AreEqual(0, KioskStands.Server.Count, "a view is not a server stand");
                viewKiosk.RequestBuy(shownStand, 0, shownStand.GetListing(0), 1);
                CollectionAssert.AreEqual(new[] { "KioskOpen Stall 1 0", "KioskCollect", "KioskBuy 7 0 9001x2:0 50 1" }, asked.Ops, "sent to the server");
                viewKiosk.MirrorView(new BodyKioskView { exists = true, open = true, shopName = "Stall", till = 12, shelf = new[] { new BodyKioskListing(new ItemStack(9001, 1), 9) } });
                Assert.IsTrue(viewKiosk.View.exists, "the owner's window shows the server's Kiosk");
                Assert.AreEqual(12, viewKiosk.View.till);
                Assert.AreEqual(1, viewKiosk.Shelf.Count);
                standView.RemoveView();
                owner.GetComponent<PlayerSaveAgent>().OnServerLeaving();
                buyer.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                KryticalNova.Economy.KioskService.ServerForgetStands();
                OwnerMessages.Current = oldMessages;
                KioskOwnerLink.Current = oldLink;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B22_TheForgeAndTheFabricator_RunOnTheServerBody_AtTheirNpc_WithTheForgeLedgerLine_AndAViewOnlyAsks()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            IWorkshopOwnerLink oldLink = WorkshopOwnerLink.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            WorkshopOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string account = "d6e7f8091021324354657687a9b0c1d2", characterId = "a1000000000000000000000000000061";
                GameObject body = Admitted(PlayerKeys.Mint(61).Value, account, characterId, "Forgewright");
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(61));
                for (int i = 0; i < 30; i++) yield return null;
                var inv = body.GetComponent<PlayerInventory>();
                var wallet = body.GetComponent<KryticalNova.Economy.PlayerWallet>();
                var forge = body.GetComponent<IBodyGearWork>();
                var crafting = body.GetComponent<IBodyCrafting>();
                Assert.IsNotNull(forge, "the body has its Forge");
                Assert.IsNotNull(crafting, "and its Fabricator");
                GearProgressionSettings settings = forge.GearSettings;
                Assert.IsNotNull(settings, "with the Forge's numbers");

                // a ladder piece every Forge rule accepts (Temper with the first Alloy, Fusion with its twin, Tuning, Reforge into something):
                // picked here by NGO's own rules before anything runs
                AlloyGrade alloy = settings.temper.alloys[0];
                var wearer = body.GetComponent<IGearWearer>();
                var reforgeInto = new List<ushort>();
                ItemDefinition piece = null, fusePiece = null, fuseMaterial = null, tunePiece = null, reforgePiece = null;
                GearItemState fuseReady = default;
                foreach (ItemDefinition item in inv.Catalog.Items)
                {
                    if (item == null || !item.IsLadderGear) continue;
                    if (piece == null && GearProgressionRules.TemperBlockedReason(settings, item, default, alloy, TemperProtection.None) == null) piece = item;
                    // Fusion (NGO's rule): a Rare or Epic tempered to the minimum, with an untouched Rare of the same kind at the material level
                    if (fusePiece == null && (item.rarity == ItemRarity.Rare || item.rarity == ItemRarity.Epic))
                    {
                        GearItemState ready = default(GearItemState).WithTemper(item.rarity == ItemRarity.Epic ? settings.fusion.minTemperEpic : settings.fusion.minTemperRare);
                        foreach (ItemDefinition m in inv.Catalog.Items)
                            if (m != null && GearProgressionRules.FusionBlockedReason(settings, item, ready, m, default) == null)
                            { fusePiece = item; fuseMaterial = m; fuseReady = ready; break; }
                    }
                    if (tunePiece == null && GearProgressionRules.TuneBlockedReason(settings, item, default) == null) tunePiece = item;
                    if (reforgePiece == null && GearProgressionRules.ReforgeBlockedReason(settings, item, default) == null)
                    {
                        GearProgressionRules.CollectReforgeResults(inv.Gear, item, wearer.Faction, wearer.Body, reforgeInto);
                        if (reforgeInto.Count > 0) reforgePiece = item;
                    }
                }
                Assert.IsNotNull(piece, "a ladder piece Temper accepts with Alloy " + alloy.itemId);
                Assert.IsNotNull(fusePiece, "a Rare or Epic Fusion accepts (tempered to the minimum) with a Rare material");
                Assert.IsNotNull(tunePiece, "a ladder piece Tuning accepts");
                Assert.IsNotNull(reforgePiece, "a ladder piece Reforge turns into another for this body");
                inv.ServerGive(piece.itemId, 1);
                inv.ServerGive(alloy.itemId, 2);
                inv.ServerGive(9100, 8);
                long start = System.Math.Min(wallet.Cap, 2_000_000_000L);
                wallet.ServerSet(start);
                int Cell(ushort id) { for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == id) return i; return -1; }
                int cell = Cell(piece.itemId);
                long cost = GearProgressionRules.TemperCost(settings, piece, 0);
                int alloys = inv.CountItem(alloy.itemId);

                // no Armorer and no Fabricator in reach: refused, nothing moves
                forge.RunGearWork(GearWorkOpKind.Temper, cell, 0, alloy.itemId, 0);
                crafting.RunCraft(1, 2);
                Assert.IsTrue(told.Any(body, "tell Visit an Armorer to work on gear."), "NGO's Forge reach: " + string.Join(" | ", told.To(body)));
                Assert.IsTrue(told.Any(body, "tell Visit a Fabricator to craft."), "and the Fabricator's");
                Assert.AreEqual(start, wallet.Creds, "no Creds spent");
                Assert.AreEqual(alloys, inv.CountItem(alloy.itemId), "no Alloy used");

                GameObject armorer = Npc(KryticalNova.NPC.NpcOffer.GearWork | KryticalNova.NPC.NpcOffer.Craft, body.transform.position + Vector3.right);
                forge.RunGearWork(GearWorkOpKind.Temper, cell, 0, alloy.itemId, 0);
                Assert.IsTrue(told.Any(body, $"forge 0 "), "a Temper ran and its result went to the owner: " + string.Join(" | ", told.To(body)));
                Assert.AreEqual(start - cost, wallet.Creds, "NGO's Temper price through the wallet");
                Assert.AreEqual(alloys - 1, inv.CountItem(alloy.itemId), "one Alloy used");

                // the other four, each until one succeeds (master-6, 3 Nov: they handle Creds): NGO's price on every attempt and a forge line each
                var progression = body.GetComponent<PlayerCharacterProgression>();
                int GiveOne(ushort id)
                {
                    var had = new HashSet<int>();
                    for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == id) had.Add(i);
                    inv.ServerGive(id, 1);
                    for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == id && !had.Contains(i)) return i;
                    return -1;
                }
                int Results(byte kind, GearWorkOutcome? outcome = null) =>
                    told.To(body).FindAll(l => l.StartsWith($"forge {kind} ") && (outcome == null || l.StartsWith($"forge {kind} {(byte)outcome} "))).Count;

                // Fusion: a fresh piece fused with its twin
                int fuseTarget = GiveOne(fusePiece.itemId);
                // tempered to Fusion's minimum the way NGO's GM command does it (PlayerGearWork.ServerSetState), so the test needs no lucky Tempers
                Component forgePart = (Component)forge;
                Assert.IsTrue((bool)forgePart.GetType().GetMethod("ServerSetState").Invoke(forgePart, new object[] { fuseTarget, fuseReady }), "the target is tempered");
                for (int t = 0; t < 80 && Results(1, GearWorkOutcome.Success) == 0; t++)
                {
                    if (settings.fusion.catalystItemId != 0 && inv.CountItem(settings.fusion.catalystItemId) < 1) inv.ServerGive(settings.fusion.catalystItemId, 1);
                    int material = GiveOne(fuseMaterial.itemId);
                    long before = wallet.Creds;
                    int stage = GearItemState.Of(inv.Get(fuseTarget)).Fusion;
                    int ran = Results(1);
                    forge.RunGearWork(GearWorkOpKind.Fuse, fuseTarget, material, 0, 0);
                    Assert.AreEqual(ran + 1, Results(1), "every Fusion attempt finishes: " + string.Join(" | ", told.To(body)));
                    Assert.AreEqual(before - GearProgressionRules.FusionCost(settings.fusion, stage + 1), wallet.Creds, "NGO's Fusion price, success or not");
                }
                Assert.AreEqual(1, Results(1, GearWorkOutcome.Success), "a Fusion succeeded");

                // Tuning: Chips and Renown for the bracket, until one stage is won; then the reset takes the reset Chip, no Creds
                int tuneTarget = GiveOne(tunePiece.itemId);
                for (int t = 0; t < 80 && Results(2, GearWorkOutcome.Success) == 0; t++)
                {
                    int stage = GearItemState.Of(inv.Get(tuneTarget)).Tune;
                    Assert.IsTrue(GearProgressionRules.TryGetBracket(settings.tuning, stage + 1, out TuningBracket bracket), "a Tuning bracket for stage " + (stage + 1));
                    inv.ServerGive(bracket.chipItemId, bracket.chips);
                    progression.AddRenown(bracket.renown);
                    long before = wallet.Creds;
                    int renown = progression.Renown;
                    forge.RunGearWork(GearWorkOpKind.Tune, tuneTarget, 0, 0, 0);
                    Assert.AreEqual(before - bracket.creds, wallet.Creds, "NGO's Tuning price");
                    Assert.AreEqual(renown - bracket.renown, progression.Renown, "and its Renown");
                }
                Assert.AreEqual(1, Results(2, GearWorkOutcome.Success), "a Tuning stage was won");
                Assert.Greater(GearItemState.Of(inv.Get(tuneTarget)).Tune, 0);
                ushort resetChip = settings.tuning.resetChipItemId;
                inv.ServerGive(resetChip, 1);
                long beforeReset = wallet.Creds;
                int chips = inv.CountItem(resetChip);
                forge.RunGearWork(GearWorkOpKind.ResetTuning, tuneTarget, 0, 0, 0);
                Assert.AreEqual(1, Results(3, GearWorkOutcome.Success), "the Tuning was reset");
                Assert.AreEqual(0, GearItemState.Of(inv.Get(tuneTarget)).Tune, "to stage 0");
                Assert.AreEqual(chips - 1, inv.CountItem(resetChip), "with one reset Chip");
                Assert.AreEqual(beforeReset, wallet.Creds, "and no Creds (NGO's reset has no price)");

                // Reforge: a fresh piece, until it becomes another Rare of its level
                int reforgeTarget = GiveOne(reforgePiece.itemId);
                for (int t = 0; t < 80 && Results(4, GearWorkOutcome.Success) == 0; t++)
                {
                    long before = wallet.Creds;
                    forge.RunGearWork(GearWorkOpKind.Reforge, reforgeTarget, 0, 0, 0);
                    Assert.AreEqual(before - GearProgressionRules.ReforgeCost(settings.reforge, reforgePiece.itemLevel), wallet.Creds, "NGO's Reforge price");
                }
                Assert.AreEqual(1, Results(4, GearWorkOutcome.Success), "a Reforge succeeded");
                CollectionAssert.Contains(reforgeInto, inv.Get(reforgeTarget).itemId, "into one of NGO's choices");
                int forgeResults = told.To(body).FindAll(l => l.StartsWith("forge ")).Count;

                // crafting: recipe 1 (four of 9100 make one 9101), twice
                int made = inv.CountItem(9101), materials = inv.CountItem(9100);
                crafting.RunCraft(1, 2);
                Assert.AreEqual(materials - 8, inv.CountItem(9100), "the eight materials went");
                Assert.AreEqual(made + 2, inv.CountItem(9101), "two were made");
                Assert.IsTrue(told.Any(body, "crafted 1 x2"), "the owner is told what was made");

                body.GetComponent<PlayerSaveAgent>().Save();
                ServerSave.WaitForDisk();
                List<LedgerEntry> forgeLines = ServerSave.ReadLedger(account, 0, 100).FindAll(l => l.kind == LedgerKinds.Forge);
                Assert.AreEqual(forgeResults, forgeLines.Count, "one forge ledger line for every attempt that finished (NGO's only ledger line of the group)");

                // the owner's view only asks, never works or crafts itself
                var asked = new Asked();
                view.GetComponent<BodyRoleTag>().Requests = asked;
                var viewForge = view.GetComponent<IBodyGearWork>();
                var viewCrafting = view.GetComponent<IBodyCrafting>();
                Component viewForgePart = (Component)viewForge, craftPart = (Component)viewCrafting;
                viewForgePart.GetType().GetMethod("RequestTemper").Invoke(viewForgePart, new object[] { 3, alloy.itemId, (ushort)0 });
                craftPart.GetType().GetMethod("RequestCraft").Invoke(craftPart, new object[] { (ushort)1, 2 });
                CollectionAssert.AreEqual(new[] { $"GearWork Temper 3 0 {alloy.itemId} 0", "Craft 1 2" }, asked.Ops, "sent to the server");
                var viewInv = view.GetComponent<PlayerInventory>();
                int viewHeld = viewInv.CountItem(9101);
                viewForge.RunGearWork(GearWorkOpKind.Temper, 0, 0, alloy.itemId, 0);
                viewCrafting.RunCraft(1, 1);
                Assert.AreEqual(viewHeld, viewInv.CountItem(9101), "a view never crafts itself");
                viewForge.MirrorGearWorked(0, 0, new ItemStack(piece.itemId, 1), new ItemStack(piece.itemId, 1));
                viewCrafting.MirrorCrafted(1, 2);
                body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
                Object.Destroy(armorer);
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                WorkshopOwnerLink.Current = oldLink;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B23_TheHerald_RunsOnTheServerBody_RenownAndCredsAsNgo_EveryoneSeesTheNova_AndNovaIsReal()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("nova"), "the nova part is real now");
            IOwnerMessages oldMessages = OwnerMessages.Current;
            KryticalNova.Progression.INovaLink oldLink = KryticalNova.Progression.NovaLink.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            KryticalNova.Progression.NovaLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string account = "e7f8091021324354657687a9b0c1d2e3", characterId = "a1000000000000000000000000000071";
                GameObject body = Admitted(PlayerKeys.Mint(71).Value, account, characterId, "Heraldine");
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(71));
                GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(72));
                for (int i = 0; i < 30; i++) yield return null;
                var nova = body.GetComponent<KryticalNova.Progression.PlayerNova>();
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var wallet = body.GetComponent<KryticalNova.Economy.PlayerWallet>();
                Assert.IsNotNull(nova, "the body has its Nova");
                var settings = nova.Settings;
                Assert.IsNotNull(settings, "with its settings");
                progression.AddRenown(settings.ascension.renownCost + KryticalNova.Progression.NovaRules.StandingCost(settings.standings, 0) + 100);
                wallet.ServerSet(System.Math.Min(wallet.Cap, settings.ascension.credsCost * 3));
                int renown = progression.Renown;
                long creds = wallet.Creds;

                // no Herald in reach: refused, nothing spent
                nova.RunNovaOp(NovaOpKind.Ascend, 0);
                Assert.IsTrue(told.Any(body, "tell Visit a Herald to do that."), "NGO's Herald reach: " + string.Join(" | ", told.To(body)));
                Assert.AreEqual(renown, progression.Renown);
                Assert.AreEqual(creds, wallet.Creds);

                GameObject herald = Npc(KryticalNova.NPC.NpcOffer.Herald, body.transform.position + Vector3.right);
                nova.RunNovaOp(NovaOpKind.Ascend, 0);
                Assert.IsTrue(told.Any(body, "nova 0 "), "an Ascension ran and its result went to the owner: " + string.Join(" | ", told.To(body)));
                Assert.AreEqual(renown - settings.ascension.renownCost, progression.Renown, "NGO's Renown price");
                Assert.AreEqual(creds - settings.ascension.credsCost, wallet.Creds, "and Creds price, through the wallet");

                int standingCost = KryticalNova.Progression.NovaRules.StandingCost(settings.standings, 0);
                renown = progression.Renown;
                nova.RunNovaOp(NovaOpKind.BuyStanding, (byte)KryticalNova.Progression.StandingTrack.Vanguard);
                Assert.AreEqual(renown - standingCost, progression.Renown, "a Standing costs NGO's Renown");
                Assert.AreEqual(1, nova.State.Standing(KryticalNova.Progression.StandingTrack.Vanguard), "the first Vanguard level");
                Assert.IsTrue(nova.State.TryGetWorn(out var worn) && worn == KryticalNova.Progression.StandingTrack.Vanguard, "and it is worn");
                Assert.IsTrue(KryticalNova.Progression.NovaState.FromPacked(nova.State.Packed).Equals(nova.State), "the ghost field carries the whole state");

                // the views: the own one only asks; every view shows the server's Nova
                var asked = new Asked();
                view.GetComponent<BodyRoleTag>().Requests = asked;
                var viewNova = view.GetComponent<KryticalNova.Progression.PlayerNova>();
                viewNova.RequestAscend();
                viewNova.RequestBuyStanding(KryticalNova.Progression.StandingTrack.Bulwark);
                CollectionAssert.AreEqual(new[] { "Nova Ascend 0", "Nova BuyStanding 1" }, asked.Ops, "sent to the server");
                viewNova.RunNovaOp(NovaOpKind.Ascend, 0);
                Assert.AreEqual(0, viewNova.State.ascension, "a view never ascends itself");
                viewNova.MirrorState(nova.State);
                Assert.IsTrue(viewNova.State.Equals(nova.State), "the own view shows the server's Nova");
                var remoteNova = remote.GetComponent<KryticalNova.Progression.PlayerNova>();
                Assert.IsNotNull(remoteNova, "another player's view keeps its Nova (the halo and the title)");
                remoteNova.MirrorState(nova.State);
                Assert.IsTrue(remoteNova.State.Equals(nova.State), "and shows the server's");

                string novaText = nova.CaptureSave();

                // an old save (NGO's text) loads into a fresh admitted server body unchanged and saves back every byte
                const string account2 = "f8091021324354657687a9b0c1d2e3f4", character2 = "a1000000000000000000000000000073";
                GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(73));
                yield return null;
                CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
                var stored = new AccountSave { accountId = account2 };
                var character = new CharacterSave { characterId = character2, name = "Ascendant" };
                character.Set("nova", novaText);
                stored.characters.Add(character);
                Assert.IsTrue(ServerSave.WriteAccount(stored));
                ServerSave.WaitForDisk();
                var context = new ServerBodyContext
                {
                    Key = new PlayerKey(73), Serial = 73, NetworkId = 73, Account = account2, CharacterId = character2, Name = "Ascendant", RecipeText = RecipeText.Encode(recipe),
                };
                Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
                var agent = other.GetComponent<PlayerSaveAgent>();
                for (int i = 0; i < 30; i++) yield return null;
                Assert.IsTrue(other.GetComponent<KryticalNova.Progression.PlayerNova>().State.Equals(nova.State), "the saved Nova is on the server body");
                agent.Save();
                ServerSave.WaitForDisk();
                Assert.AreEqual(novaText, ServerSave.ReadAccountFrom(ServerSave.Store, account2).Find(character2).Get("nova"), "the nova part saves back byte for byte");
                agent.OnServerLeaving();
                body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
                Object.Destroy(herald);
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                KryticalNova.Progression.NovaLink.Current = oldLink;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B24_ARestyle_RunsOnTheServerBody_AtItsNpc_TakesThePass_ChangesTheLookEveryoneSees_AndAViewOnlyAsks()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            IRestyleOwnerLink oldLink = RestyleOwnerLink.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            RestyleOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string account = "091021324354657687a9b0c1d2e3f405", characterId = "a1000000000000000000000000000081";
                GameObject body = Admitted(PlayerKeys.Mint(81).Value, account, characterId, "Coachwell");
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(81));
                for (int i = 0; i < 30; i++) yield return null;
                var restyle = body.GetComponent<IBodyRestyle>();
                var identity = body.GetComponent<PlayerIdentity>();
                var inv = body.GetComponent<PlayerInventory>();
                Assert.IsNotNull(restyle, "the body has its restyle");
                byte coach = (byte)RestyleService.Coach;
                ushort pass = restyle.PassFor(coach);
                Assert.AreNotEqual(0, pass, "the Coach takes a Training Pass");
                inv.ServerGive(pass, 1);
                int passes = inv.CountItem(pass);

                // the Coach changes the build only (NGO's RestyleRules.Merge): a new height, checked like a new character
                CharacterRecipe current = identity.State.ToRecipe();
                CharacterRecipe proposed = current.Clone();
                proposed.height = (byte)(current.height == 50 ? 60 : 50);
                Assert.IsTrue(CharacterRecipeValidator.Validate(RestyleRules.Merge(current, proposed, RestyleService.Coach, identity.Database.Parts), identity.Database, out string why),
                    "the planned build is legal: " + why);
                string lookBefore = PlayerAdmission.LookOf(identity);

                restyle.RunRestyle(coach, proposed);
                Assert.IsTrue(told.Any(body, "tell Visit a Coach to do that."), "NGO's reach: " + string.Join(" | ", told.To(body)));
                Assert.AreEqual(passes, inv.CountItem(pass), "no pass used");

                GameObject trainer = Npc(KryticalNova.NPC.NpcOffer.Coach, body.transform.position + Vector3.right);
                restyle.RunRestyle(coach, proposed);
                Assert.IsTrue(told.Any(body, "tell Your new build is on."), "the build changed: " + string.Join(" | ", told.To(body)));
                Assert.IsTrue(told.Any(body, "restyled"), "and the owner's window is told");
                Assert.AreEqual(passes - 1, inv.CountItem(pass), "one Training Pass used");
                Assert.AreEqual(proposed.height, identity.State.ToRecipe().height, "the new height is the character's");
                Assert.AreNotEqual(lookBefore, PlayerAdmission.LookOf(identity), "the look text everyone reads changed with it");

                // the proposal travels in pages of RecipeText
                string text = RecipeText.Encode(proposed);
                Assert.LessOrEqual(text.Length, RestylePageRpc.PageChars * RestylePageRpc.MostPages, "a look fits the pages");
                Assert.IsTrue(RecipeText.TryDecode(text, out CharacterRecipe back) && back.height == proposed.height, "and reads back");

                // the owner's view only asks
                var asked = new Asked();
                view.GetComponent<BodyRoleTag>().Requests = asked;
                var viewRestyle = (Component)view.GetComponent<IBodyRestyle>();
                viewRestyle.GetType().GetMethod("RequestRestyle").Invoke(viewRestyle, new object[] { RestyleService.Coach, proposed });
                CollectionAssert.AreEqual(new[] { $"Restyle {coach} {proposed.height}" }, asked.Ops, "sent to the server");
                ((IBodyRestyle)viewRestyle).RunRestyle(coach, proposed);
                ((IBodyRestyle)viewRestyle).MirrorRestyled();
                body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
                Object.Destroy(trainer);
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                RestyleOwnerLink.Current = oldLink;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B25_WeavesAndTheLibrary_RunOnTheServerBody_TheFeeThroughTheWallet_BothSavePartsRealTogether_AndTheViewsMirror()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("weave"), "the weave part is real now");
            Assert.IsFalse(SavePassthrough.Is("weaves"), "and the library's, in the same change");
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string account = "1021324354657687a9b0c1d2e3f40516", characterId = "a1000000000000000000000000000091";
                GameObject body = Admitted(PlayerKeys.Mint(91).Value, account, characterId, "Weaverly");
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(91));
                for (int i = 0; i < 30; i++) yield return null;
                var weaves = body.GetComponent<IBodyWeaves>();
                var library = body.GetComponent<IBodyWeaveLibrary>();
                var equipment = body.GetComponent<PlayerEquipment>();
                var inv = body.GetComponent<PlayerInventory>();
                var wallet = body.GetComponent<KryticalNova.Economy.PlayerWallet>();
                var wearer = body.GetComponent<IGearWearer>();
                Assert.IsNotNull(weaves, "the body has its Weaves");
                Assert.IsNotNull(library, "and its library");

                // the helmet toggle: free, anywhere (NGO's Items limit)
                weaves.RunWeaveOp(WeaveOpKind.Helmet, 0, 0, 0);
                Assert.IsTrue(equipment.Looks.helmetHidden, "the helmet is hidden");

                // a Weave planned by NGO's own rule: a worn piece and another look it accepts over it
                EquipSlot slot = default;
                ItemDefinition look = null;
                foreach (EquipSlot s in WeaveRules.WeaveSlots)
                {
                    ItemDefinition worn = equipment.GetDefinition(s);
                    if (worn == null) continue;
                    foreach (ItemDefinition item in inv.Catalog.Items)
                    {
                        ushort key = WeaveRules.KeyOf(item);
                        if (key == 0 || key == WeaveRules.KeyOf(worn)) continue;
                        if (WeaveRules.ApplyBlockedReason(inv.Gear, s, worn, item, true, wearer.Faction, wearer.Body) != null) continue;
                        slot = s;
                        look = item;
                        break;
                    }
                    if (look != null) break;
                }
                Assert.IsNotNull(look, "a worn piece and a look NGO's Weave rule accepts over it");
                ushort line = WeaveRules.KeyOf(look);

                // earned: its look joins the library (NGO's Earned collect), the version goes up, the owner is told
                int version = library.Version;
                inv.ServerGive(look.itemId, 1, 0, earned: true);
                Assert.IsTrue(System.Linq.Enumerable.Contains(library.Lines, line), "the look is in the library");
                Assert.Greater(library.Version, version, "its version went up (the owner gets a new copy)");

                wallet.ServerSet(100_000);
                weaves.RunWeaveOp(WeaveOpKind.Weave, (byte)slot, line, 0);
                Assert.IsTrue(told.Any(body, "tell Visit a Holoweaver to change a Weave."), "NGO's reach: " + string.Join(" | ", told.To(body)));
                Assert.AreEqual(100_000, wallet.Creds, "no fee taken");

                GameObject weaver = Npc(KryticalNova.NPC.NpcOffer.Holoweave, body.transform.position + Vector3.right);
                string lookBefore = PlayerAdmission.LookOf(body.GetComponent<PlayerIdentity>(), equipment);
                weaves.RunWeaveOp(WeaveOpKind.Weave, (byte)slot, line, 0);
                Assert.AreEqual(look.itemId, equipment.Looks.GetWeave(slot), "woven: " + string.Join(" | ", told.To(body)));
                Assert.AreEqual(100_000 - weaves.WeaveFee, wallet.Creds, "NGO's Weave fee through the wallet");
                Assert.AreNotEqual(lookBefore, PlayerAdmission.LookOf(body.GetComponent<PlayerIdentity>(), equipment), "the look text everyone reads has the Weave");

                // the library as pages of text reads back
                string libraryText = WeaveLibraryText.Encode(library.Lines, library.Morphs);
                Assert.IsTrue(WeaveLibraryText.TryDecode(libraryText, out ushort[] backLines, out uint[] backMorphs), "the library text reads back");
                Assert.AreEqual(library.Lines.Count, backLines.Length);

                // the views: the own view only asks, and shows the server's looks and library
                var asked = new Asked();
                view.GetComponent<BodyRoleTag>().Requests = asked;
                var viewWeavesPart = (Component)view.GetComponent<IBodyWeaves>();
                viewWeavesPart.GetType().GetMethod("RequestHelmetShown").Invoke(viewWeavesPart, new object[] { true });
                viewWeavesPart.GetType().GetMethod("RequestWeave").Invoke(viewWeavesPart, new object[] { slot, line });
                CollectionAssert.AreEqual(new[] { "Weave Helmet 0 0 1", $"Weave Weave {(byte)slot} {line} 0" }, asked.Ops, "sent to the server");
                var viewEquipment = view.GetComponent<PlayerEquipment>();
                ((IBodyWeaves)viewWeavesPart).RunWeaveOp(WeaveOpKind.Helmet, 0, 0, 0);
                Assert.IsFalse(viewEquipment.Looks.helmetHidden, "a view never changes its looks itself");
                viewEquipment.MirrorLooks(equipment.Looks);
                Assert.AreEqual(look.itemId, viewEquipment.Looks.GetWeave(slot), "the own view wears the server's Weave");
                var viewLibrary = view.GetComponent<IBodyWeaveLibrary>();
                viewLibrary.MirrorLibrary(backLines, backMorphs);
                Assert.AreEqual(library.Lines.Count, viewLibrary.Lines.Count, "and shows the server's library");

                // both NGO texts load into a fresh admitted server body unchanged and save back every byte
                string weaveText = null, weavesText = null;
                foreach (ISaveParticipant part in body.GetComponents<ISaveParticipant>())
                {
                    if (part.SaveKey == "weave") weaveText = part.CaptureSave();
                    if (part.SaveKey == "weaves") weavesText = part.CaptureSave();
                }
                Assert.IsNotNull(weaveText);
                Assert.IsNotNull(weavesText);
                const string account2 = "21324354657687a9b0c1d2e3f4051627", character2 = "a1000000000000000000000000000092";
                GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(92));
                yield return null;
                CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
                var stored = new AccountSave { accountId = account2 };
                stored.Set("weaves", weavesText);
                var character = new CharacterSave { characterId = character2, name = "Returnweave" };
                character.Set("weave", weaveText);
                stored.characters.Add(character);
                Assert.IsTrue(ServerSave.WriteAccount(stored));
                ServerSave.WaitForDisk();
                var context = new ServerBodyContext
                {
                    Key = new PlayerKey(92), Serial = 92, NetworkId = 92, Account = account2, CharacterId = character2, Name = "Returnweave", RecipeText = RecipeText.Encode(recipe),
                };
                Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
                var agent = other.GetComponent<PlayerSaveAgent>();
                for (int i = 0; i < 30; i++) yield return null;
                agent.Save();
                ServerSave.WaitForDisk();
                AccountSave back = ServerSave.ReadAccountFrom(ServerSave.Store, account2);
                Assert.AreEqual(weaveText, back.Find(character2).Get("weave"), "the weave part saves back byte for byte");
                Assert.AreEqual(weavesText, back.Get("weaves"), "the library part saves back byte for byte");
                agent.OnServerLeaving();
                body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
                Object.Destroy(weaver);
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B26_Autoward_RunsOnTheServerBody_APassExtendsTheLicence_TheCellsAreKept_AutobuffIsReal_AndTheViewMirrors()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("autobuff"), "the autobuff part is real now");
            Assert.IsTrue(PlayerRequestServerSystem.EffectPorted(ItemUseEffect.AutobuffTime), "and the Pass is used again");
            PlayerKeys.Reset();
            const string account = "324354657687a9b0c1d2e3f405162738", characterId = "a1000000000000000000000000000101";
            GameObject body = Admitted(PlayerKeys.Mint(101).Value, account, characterId, "Wardkeeper");
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(101));
            for (int i = 0; i < 30; i++) yield return null;
            var autobuff = body.GetComponent<IBodyAutobuff>();
            var inv = body.GetComponent<PlayerInventory>();
            Assert.IsNotNull(autobuff, "the server body keeps its Autoward now (it was stripped as automation)");

            // an Autobuff Pass from the catalog, used on the server body by the inventory's own rules
            ItemDefinition pass = null;
            foreach (ItemDefinition item in inv.Catalog.Items) if (item != null && item.useEffect == ItemUseEffect.AutobuffTime) { pass = item; break; }
            Assert.IsNotNull(pass, "an Autobuff Pass in the catalog");
            inv.ServerGive(pass.itemId, 1);
            int held = inv.CountItem(pass.itemId);
            long before = autobuff.ExpiresUtc;
            Assert.IsTrue(PlayerRequestServerSystem.UseOn(body, pass.itemId, out _), "the Pass is used");
            Assert.AreEqual(held - 1, inv.CountItem(pass.itemId), "and taken");
            Assert.Greater(autobuff.ExpiresUtc, System.Math.Max(before, System.DateTime.UtcNow.Ticks), "the licence runs into the future");

            // the cells: the server body keeps what the owner sends (NGO's Sanitise: trimmed, no repeats)
            autobuff.RunSetSlots(new[] { " Bulwark ", "Bulwark", "Haste" });
            Assert.AreEqual("Bulwark", autobuff.SkillIds[0], "trimmed");
            Assert.AreEqual(string.Empty, autobuff.SkillIds[1], "a repeat is dropped");
            Assert.AreEqual("Haste", autobuff.SkillIds[2]);
            Assert.AreEqual(8, autobuff.SkillIds.Length, "eight cells");

            // the owner's view only asks, and shows the server's licence and cells
            var asked = new Asked();
            view.GetComponent<BodyRoleTag>().Requests = asked;
            var viewAutobuff = view.GetComponent<IBodyAutobuff>();
            Assert.IsNotNull(viewAutobuff, "the own view keeps it as a stub");
            var viewPart = (Component)viewAutobuff;
            viewPart.GetType().GetMethod("SaveSlots").Invoke(viewPart, new object[] { new[] { "Haste" } });
            Assert.AreEqual(1, asked.Ops.Count, "sent to the server");
            StringAssert.StartsWith("Autobuff Haste", asked.Ops[0]);
            viewAutobuff.RunSetSlots(new[] { "Bulwark" });
            Assert.AreEqual("Haste", viewAutobuff.SkillIds[0], "a view never keeps cells for the server");
            viewAutobuff.MirrorState(autobuff.ExpiresUtc, autobuff.SkillIds);
            Assert.AreEqual(autobuff.ExpiresUtc, viewAutobuff.ExpiresUtc, "the own view shows the server's licence");
            Assert.AreEqual("Bulwark", viewAutobuff.SkillIds[0], "and cells");
            Assert.AreEqual(AutobuffSlotsRpc.Join(autobuff.SkillIds), AutobuffSlotsRpc.Join(AutobuffSlotsRpc.Split(AutobuffSlotsRpc.Join(autobuff.SkillIds))), "the cells travel whole");

            // NGO's text loads into a fresh admitted server body unchanged and saves back every byte
            string text = null;
            foreach (ISaveParticipant part in body.GetComponents<ISaveParticipant>()) if (part.SaveKey == "autobuff") text = part.CaptureSave();
            Assert.IsNotNull(text);
            const string account2 = "4354657687a9b0c1d2e3f40516273849", character2 = "a1000000000000000000000000000102";
            GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(102));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account2 };
            var character = new CharacterSave { characterId = character2, name = "Wardreturn" };
            character.Set("autobuff", text);
            stored.characters.Add(character);
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(102), Serial = 102, NetworkId = 102, Account = account2, CharacterId = character2, Name = "Wardreturn", RecipeText = RecipeText.Encode(recipe),
            };
            Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
            var agent = other.GetComponent<PlayerSaveAgent>();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(autobuff.ExpiresUtc, other.GetComponent<IBodyAutobuff>().ExpiresUtc, "the saved licence is on the server body");
            agent.Save();
            ServerSave.WaitForDisk();
            Assert.AreEqual(text, ServerSave.ReadAccountFrom(ServerSave.Store, account2).Find(character2).Get("autobuff"), "the autobuff part saves back byte for byte");
            agent.OnServerLeaving();
            body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            PlayerKeys.Reset();
        }

        [UnityTest]
        public IEnumerator B27_TheFurnishingCrate_RunsOnTheServerBody_StoreAndWithdraw_CrateIsReal_AndTheViewMirrors()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("crate"), "the crate part is real now");
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string account = "54657687a9b0c1d2e3f405162738495a", characterId = "a1000000000000000000000000000111";
                GameObject body = Admitted(PlayerKeys.Mint(111).Value, account, characterId, "Cratewright");
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(111));
                for (int i = 0; i < 30; i++) yield return null;
                var crate = body.GetComponent<IBodyCrate>();
                var inv = body.GetComponent<PlayerInventory>();
                Assert.IsNotNull(crate, "the body has its Crate");

                // a furnishing NGO's catalog lets this character keep (picked by the rule before the run)
                ItemDefinition furnishing = null;
                foreach (ItemDefinition item in inv.Catalog.Items) if (item != null && crate.MayStore(item.itemId)) { furnishing = item; break; }
                Assert.IsNotNull(furnishing, "a furnishing this character may keep in the Crate");
                inv.ServerGive(furnishing.itemId, 3);
                int Cell(ushort id) { for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == id) return i; return -1; }
                int held = inv.CountItem(furnishing.itemId);
                int version = crate.Version;

                crate.RunStore(Cell(furnishing.itemId), 2);
                Assert.AreEqual(held - 2, inv.CountItem(furnishing.itemId), "two left the Pack");
                Assert.AreEqual(2, crate.Count(furnishing.itemId), "into the Crate");
                Assert.Greater(crate.Version, version, "its version went up (the owner gets a new copy)");
                crate.RunWithdraw(furnishing.itemId, 1);
                Assert.AreEqual(held - 1, inv.CountItem(furnishing.itemId), "one came back");
                Assert.AreEqual(1, crate.Count(furnishing.itemId));

                // not a furnishing: NGO's refusal to its player, nothing moves
                inv.ServerGive(9001, 1);
                crate.RunStore(Cell(9001), 1);
                Assert.IsTrue(told.Any(body, "tell Only furnishings go in the Furnishing Crate."), "NGO's rule: " + string.Join(" | ", told.To(body)));

                // the Crate as text reads back
                var entries = new List<(ushort, int)>();
                crate.ReadEntries(entries);
                var back = new List<(ushort, int)>();
                Assert.IsTrue(CrateText.TryDecode(CrateText.Encode(entries), back), "the Crate text reads back");
                CollectionAssert.AreEqual(entries, back);

                // the owner's view only asks, and shows the server's Crate
                var asked = new Asked();
                view.GetComponent<BodyRoleTag>().Requests = asked;
                var viewPart = (Component)view.GetComponent<IBodyCrate>();
                viewPart.GetType().GetMethod("RequestStore").Invoke(viewPart, new object[] { 4, 2 });
                viewPart.GetType().GetMethod("RequestWithdraw").Invoke(viewPart, new object[] { furnishing.itemId, 1 });
                CollectionAssert.AreEqual(new[] { "Crate Store 4 2", $"Crate Withdraw {furnishing.itemId} 1" }, asked.Ops, "sent to the server");
                var viewCrate = (IBodyCrate)viewPart;
                viewCrate.RunWithdraw(furnishing.itemId, 1);
                viewCrate.MirrorEntries(back);
                Assert.AreEqual(1, viewCrate.Count(furnishing.itemId), "the own view shows the server's Crate");

                // NGO's text loads into a fresh admitted server body unchanged and saves back every byte
                string text = null;
                foreach (ISaveParticipant part in body.GetComponents<ISaveParticipant>()) if (part.SaveKey == "crate") text = part.CaptureSave();
                Assert.IsNotNull(text);
                const string account2 = "657687a9b0c1d2e3f405162738495a6b", character2 = "a1000000000000000000000000000112";
                GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(112));
                yield return null;
                CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
                var stored = new AccountSave { accountId = account2 };
                var character = new CharacterSave { characterId = character2, name = "Cratereturn" };
                character.Set("crate", text);
                stored.characters.Add(character);
                Assert.IsTrue(ServerSave.WriteAccount(stored));
                ServerSave.WaitForDisk();
                var context = new ServerBodyContext
                {
                    Key = new PlayerKey(112), Serial = 112, NetworkId = 112, Account = account2, CharacterId = character2, Name = "Cratereturn", RecipeText = RecipeText.Encode(recipe),
                };
                Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
                var agent = other.GetComponent<PlayerSaveAgent>();
                for (int i = 0; i < 30; i++) yield return null;
                Assert.AreEqual(1, other.GetComponent<IBodyCrate>().Count(furnishing.itemId), "the saved Crate is on the server body");
                agent.Save();
                ServerSave.WaitForDisk();
                Assert.AreEqual(text, ServerSave.ReadAccountFrom(ServerSave.Store, account2).Find(character2).Get("crate"), "the crate part saves back byte for byte");
                agent.OnServerLeaving();
                body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B28_Chat_RunsOnTheServerBodies_ByNgosChannelRules_TheGmChannelByTheRoster_NoticesToChat_AndTheViewOnlyAsks()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            PlayerKey gmKey = PlayerKey.None;
            ulong gmSerial = 0;
            Action<ChatLine> shown = null;
            Type chatType = Type.GetType("KryticalNova.Social.PlayerChat, KryticalNova.Social");
            try
            {
                PlayerKeys.Reset();
                const string accA = "28a1000000000000000000000000000a", accB = "28b1000000000000000000000000000b",
                    accC = "28c1000000000000000000000000000c", accD = "28d1000000000000000000000000000d";
                GameObject a = Admitted(PlayerKeys.Mint(281).Value, accA, "c2810000000000000000000000000281", "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(282).Value, accB, "c2820000000000000000000000000282", "Brannoc");
                GameObject c = Admitted(PlayerKeys.Mint(283).Value, accC, "c2830000000000000000000000000283", "Corvane");
                GameObject d = Admitted(PlayerKeys.Mint(284).Value, accD, "c2840000000000000000000000000284", "Brannoc");
                for (int i = 0; i < 30; i++) yield return null;
                // planned before the run: A and B 1 m apart, C and D 60 m away (General carries 40 m)
                a.transform.position = new Vector3(0f, 0.08f, 0f);
                b.transform.position = new Vector3(1f, 0.08f, 0f);
                c.transform.position = new Vector3(60f, 0.08f, 0f);
                d.transform.position = new Vector3(0f, 0.08f, 60f);
                Assert.IsNotNull(chatType, "PlayerChat is in the game");
                var chatA = a.GetComponent<IBodyChat>();
                var chatB = b.GetComponent<IBodyChat>();
                var chatC = c.GetComponent<IBodyChat>();
                var chatD = d.GetComponent<IBodyChat>();
                Assert.IsNotNull(chatA, "the server body has its chat");
                bool IsGm(IBodyChat chat) => (bool)chatType.GetProperty("IsGameMaster").GetValue(chat);
                PlayerKey KeyOf(GameObject body) => body.GetComponent<BodyRoleTag>().Key;

                // 1. General: A and B (1 m) hear it, C (60 m) does not; one rate-limit slot each sender, NGO's 5 in 5 s
                chatA.RunSend(ChatChannel.General, "hello there", string.Empty, null, null);
                Assert.IsTrue(told.Any(a, "line General Talwyn: hello there"), "the speaker sees it: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(b, "line General Talwyn: hello there"), "B, 1 m away, hears it");
                Assert.IsFalse(told.Any(c, "line General"), "C, 60 m away, does not");

                // 2. Whisper: by name across the map, to self refused, unknown name refused, two of a name by Name#key
                chatA.RunSend(ChatChannel.Whisper, "psst", "Corvane", null, null);
                Assert.IsTrue(told.Any(c, "line Whisper Talwyn: psst"), "C gets the whisper");
                Assert.IsTrue(told.Any(a, "line Whisper to Corvane: psst"), "A sees it as sent");
                chatA.RunSend(ChatChannel.Whisper, "me", "Talwyn", null, null);
                Assert.IsTrue(told.Any(a, "system You cannot whisper to yourself."), "NGO's self rule");
                chatC.RunSend(ChatChannel.Whisper, "anyone", "Nobodyhere", null, null);
                Assert.IsTrue(told.Any(c, "system No player named 'Nobodyhere' is online."), "NGO's unknown name");
                chatC.RunSend(ChatChannel.Whisper, "which one", "Brannoc", null, null);
                Assert.IsTrue(told.Any(c, $"system Several players share that name. Whisper one of: Brannoc#{KeyOf(b).Value}, Brannoc#{KeyOf(d).Value}"),
                    "picked by PlayerKey, never a NetworkId: " + string.Join(" | ", told.To(c)));
                chatC.RunSend(ChatChannel.Whisper, "for you", "Brannoc#" + KeyOf(d).Value, null, null);
                Assert.IsTrue(told.Any(d, "line Whisper Corvane: for you"), "Name#key reaches that one");
                Assert.IsFalse(told.Any(b, "line Whisper Corvane"), "and not the other");

                // 3. the GM channel: an unspawned DOTS body is NOT a GM (NGO's !IsSpawned shortcut); a roster-bound one is
                Assert.IsFalse(IsGm(chatA), "a DOTS server body is not GM by being unspawned");
                chatA.RunSend(ChatChannel.Gm, "attention", string.Empty, null, null);
                Assert.IsTrue(told.Any(a, "system Only a GM can use this channel."), "refused on the server body: " + string.Join(" | ", told.To(a)));
                Assert.IsFalse(told.Any(b, "announce"), "nobody hears it");
                gmSerial = b.GetComponent<BodyRoleTag>().ConnectionSerial;
                gmKey = PlayerKeys.Mint(gmSerial);   // the key ConnectionKeySystem mints for that connection
                KryticalNova.Networking.ServerAccounts.Remember(gmKey, accB, string.Empty);
                KryticalNova.Networking.GmRoster.SetAll(new[] { new KryticalNova.Networking.GmEntry { name = "Warden", account = accB } });
                Assert.IsTrue(KryticalNova.Networking.GmRoster.SignInBound(gmSerial, out _), "B's account is bound to a GM (M7.4)");
                Assert.IsTrue(IsGm(chatB), "B is GM by the roster");
                Assert.IsFalse(IsGm(chatA), "A still is not");
                chatB.RunSend(ChatChannel.Gm, "restart soon", string.Empty, null, null);
                foreach (GameObject body in new[] { a, b, c, d })
                    Assert.IsTrue(told.Any(body, "announce Gm Brannoc: restart soon"), body.name + " hears the GM, wherever it is");
                chatB.RunSend(ChatChannel.General, "hi", string.Empty, null, null);
                Assert.IsTrue(told.Any(a, "line General [GM] Brannoc: hi"), "the [GM] tag on a GM's other lines");

                // 4. the rate limit: D's sixth line within 5 s is refused with NGO's text
                for (int i = 0; i < 6; i++) chatD.RunSend(ChatChannel.General, "spam " + i, string.Empty, null, null);
                Assert.IsTrue(told.Any(d, "line General Brannoc: spam 4"), "five are said");
                Assert.IsFalse(told.Any(d, "line General Brannoc: spam 5"), "the sixth is not");
                Assert.IsTrue(told.Any(d, "system You are sending messages too quickly."), "NGO's refusal");

                // 5. a server-wide announcement reaches every server body's player, and nobody who left
                MethodInfo announce = chatType.GetMethod("ServerAnnounce", BindingFlags.Public | BindingFlags.Static);
                int reached = (int)announce.Invoke(null, new object[] { AnnouncementKind.Gm, ChatChannel.Gm, "Nova", "all hands", null, (byte)0 });
                Assert.AreEqual(4, reached, "all four");
                Assert.AreEqual(BodyRoleTag.ServerBodies.Count, reached, "every server body");
                Object.Destroy(c);
                yield return null;
                reached = (int)announce.Invoke(null, new object[] { AnnouncementKind.Gm, ChatChannel.Gm, "Nova", "second call", null, (byte)0 });
                Assert.AreEqual(3, reached, "the leaver is not counted");
                List<(GameObject body, string what)> second = told.Lines.FindAll(l => l.what == "announce Gm Nova: second call");
                Assert.AreEqual(3, second.Count, "three told");
                Assert.IsTrue(second.TrueForAll(l => l.body == a || l.body == b || l.body == d), "nothing reaches the leaver");

                // 6. another system's notice: trade's refusal is a System line in A's chat (as NGO), only A's
                a.GetComponent<KryticalNova.Economy.PlayerTrade>().RunTradeOp(TradeOpKind.RequestByName, "Nobodyhere", 0, 0, 0, false);
                Assert.IsTrue(told.Any(a, "system No player named 'Nobodyhere' is online."), "trade tells A in chat");
                Assert.IsFalse(told.Any(a, "tell No player named 'Nobodyhere'"), "not on the message line");
                Assert.AreEqual(1, told.To(a).FindAll(x => x == "system No player named 'Nobodyhere' is online.").Count, "once, to A");
                Assert.IsFalse(told.Any(b, "system No player named 'Nobodyhere'"), "and nobody else");

                // 7. the owner's view only asks (exact request) and shows what the server sends
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(KeyOf(a).Value));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewChat = view.GetComponent<IBodyChat>();
                Assert.IsNotNull(viewChat, "the own view keeps its chat (the window reads it)");
                MethodInfo send = chatType.GetMethod("Send", new[] { typeof(ChatChannel), typeof(string), typeof(string), typeof(ItemLocation[]) });
                int linesBefore = told.Lines.Count;
                send.Invoke(viewChat, new object[] { ChatChannel.General, "from the view", null, null });
                send.Invoke(viewChat, new object[] { ChatChannel.Whisper, "look " + ChatLinks.Marker(0), "Brannoc#" + KeyOf(b).Value,
                    new[] { new ItemLocation { container = ItemContainer.Bag, index = 2 } } });
                CollectionAssert.AreEqual(new[] { "Chat General from the view||0", $"Chat Whisper look |Brannoc#{KeyOf(b).Value}|1" }, asked.Ops, "sent to the server");
                Assert.AreEqual(linesBefore, told.Lines.Count, "the view never says it itself");
                Assert.IsFalse(IsGm(viewChat), "the view is GM only when the server says so");
                var seen = new List<string>();
                shown = line => seen.Add(line.Channel + " " + line.Text);
                chatType.GetEvent("LineShown").AddEventHandler(null, shown);
                viewChat.MirrorLine(new BodyChatLine { kind = BodyChatKind.Line, channel = ChatChannel.System, text = "mirrored" });
                chatA.MirrorLine(new BodyChatLine { kind = BodyChatKind.Line, channel = ChatChannel.System, text = "not on a server body" });
                CollectionAssert.AreEqual(new[] { "System mirrored" }, seen, "the own view shows the line; a server body never does");

                foreach (GameObject body in new[] { a, b, d }) body.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                if (shown != null) chatType.GetEvent("LineShown").RemoveEventHandler(null, shown);
                KryticalNova.Networking.GmRoster.SetAll(Array.Empty<KryticalNova.Networking.GmEntry>());
                KryticalNova.Networking.GmRoster.Forget(gmSerial);
                KryticalNova.Networking.ServerAccounts.Forget(gmKey);
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
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
