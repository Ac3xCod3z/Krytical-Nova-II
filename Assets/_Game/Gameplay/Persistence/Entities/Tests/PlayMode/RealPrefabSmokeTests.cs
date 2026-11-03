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

            // 8.1.1: the owner's view wallet only shows; it never earns, spends or sets (NGO: only the server's does)
            var viewWallet = view.GetComponent<KryticalNova.Economy.PlayerWallet>();
            Assert.AreEqual(0, viewWallet.ServerEarn(500, KryticalNova.Wallet.CredsSource.Gm), "a view earns nothing");
            Assert.IsFalse(viewWallet.ServerSpend(100, KryticalNova.Wallet.CredsSink.Gm), "a view spends nothing");
            viewWallet.ServerSet(1);
            Assert.AreEqual(1234, viewWallet.Creds, "the view still shows the server's balance");
        }

        sealed class Asked : IBodyRequests
        {
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

        GameObject Admitted(ulong key, string account, string characterId, string name)
        {
            GameObject body = Make(NetRoleKind.ServerBody, false, new PlayerKey(key));
            CharacterRecipe recipe = AnyLegalRecipe(body.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            stored.characters.Add(new CharacterSave { characterId = characterId, name = name });
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            var context = new ServerBodyContext
            {
                Key = new PlayerKey(key), Serial = key, NetworkId = (int)key, Account = account, CharacterId = characterId, Name = name,
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
