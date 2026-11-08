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
        IContactsOwnerLink m_OldContacts;
        IPartyOwnerLink m_OldParty;
        IFollowOwnerLink m_OldFollow;
        ISponsorOwnerLink m_OldSponsor;
        ICrewOwnerLink m_OldCrew;
        IPetShowLink m_OldPet;
        IMountOwnerLink m_OldMount;
        IRecallShowLink m_OldRecall;

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
            m_OldContacts = ContactsOwnerLink.Current;
            m_OldParty = PartyOwnerLink.Current;
            m_OldFollow = FollowOwnerLink.Current;
            m_OldSponsor = SponsorOwnerLink.Current;
            m_OldCrew = CrewOwnerLink.Current;
            m_OldPet = PetShowLink.Current;
            m_OldMount = MountOwnerLink.Current;
            m_OldRecall = RecallShowLink.Current;
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
            ContactsOwnerLink.Current = m_OldContacts;
            PartyOwnerLink.Current = m_OldParty;
            FollowOwnerLink.Current = m_OldFollow;
            SponsorOwnerLink.Current = m_OldSponsor;
            CrewOwnerLink.Current = m_OldCrew;
            PetShowLink.Current = m_OldPet;
            MountOwnerLink.Current = m_OldMount;
            RecallShowLink.Current = m_OldRecall;
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
            // another player's body keeps only an empty, inert Pack for its pet (owner pick 8.4.7b, 3 Nov; B37 proves it never acts)
            Assert.AreNotSame(theirs.GetComponent<PlayerInventory>(), PlayerInventory.Local, "another player's Pack is never this machine's own");
            Assert.IsNull(theirs.GetComponent<PlayerSaveAgent>(), "no save agent on another player's body");
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
            public void CastSkill(string skillId, Transform target) { }
            public void Charge(string skillId, Transform aimedAt, bool started, bool completed) { }
            public void CastWards(string skillIds) => Ops.Add("Wards " + skillIds);
            public void DuelOp(DuelOpKind op, Transform target, bool accept) => Ops.Add($"Duel {op} {(target != null ? target.name : "-")} {accept}");
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
            public void ContactOp(ContactOpKind op, string a, string b, bool flag) => Ops.Add($"Contact {op} {a}|{b}|{flag}");
            public void PartyOp(PartyOpKind op, string name, bool flag) => Ops.Add($"Party {op} {name}|{flag}");
            public void Follow(string memberName) => Ops.Add("Follow " + memberName);
            public void SponsorOp(SponsorOpKind op, string a, bool flag) => Ops.Add($"Sponsor {op} {a}|{flag}");
            public void CrewOp(CrewOpKind op, string a, string b, byte b0, byte b1, byte b2, byte b3, bool flag) => Ops.Add($"Crew {op} {a}|{b}|{b0} {b1} {b2} {b3} {flag}");
            public void Emote(bool stop, string key) => Ops.Add($"Emote {stop} {key}");
            public void SelectMovement(byte skill) => Ops.Add("SelectMovement " + skill);
            public void PetTrick(int nameHash) => Ops.Add("PetTrick " + nameHash);
            public void MountOp(MountOpKind op, int box) => Ops.Add($"MountOp {op} {box}");
            public void Chat(ChatChannel channel, string text, string whisperTarget, byte[] linkContainers, byte[] linkCells) =>
                Ops.Add($"Chat {channel} {ChatLinks.Strip(text)}|{whisperTarget}|{linkContainers?.Length ?? 0}");
        }

        /// <summary>What the server bodies told their players (the message line) and sent their trade windows (view, notice, prompt).</summary>
        sealed class Told : IDuelOwnerLink, IOwnerMessages, ITradeOwnerLink, IKioskOwnerLink, IWorkshopOwnerLink, KryticalNova.Progression.INovaLink, IRestyleOwnerLink, IChatOwnerLink, IContactsOwnerLink, IPartyOwnerLink, IFollowOwnerLink, ISponsorOwnerLink, ICrewOwnerLink, IPetShowLink, IMountOwnerLink, IRecallShowLink
        {
            // Recall (M8 8.4.10): "recall show <kind> <seconds>" to every machine
            public void Show(Component part, RecallShowKind kind, float seconds) => Lines.Add((Body(part), $"recall show {kind} {seconds:0}"));
            public void Show(Component part, BodyPetShowKind kind, int value, bool toOwner) => Lines.Add((Body(part), $"pet show {kind} {value} {toOwner}"));
            // Mounts (M8 8.4.8): "mount book <owned> <xp1> <hunger1>" to its own player, "mount evolved <stage>" to every machine
            bool IMountOwnerLink.Book(Component part, byte owned, uint xp1, uint xp2, uint xp3, uint xp4, uint hunger1, uint hunger2, uint hunger3, uint hunger4)
            { Lines.Add((Body(part), $"mount book {owned} {xp1} {hunger1}")); return true; }
            void IMountOwnerLink.Evolved(Component part, int stage) => Lines.Add((Body(part), "mount evolved " + stage));
            // Crews (M8 8.4.5): the last window text each body was sent, and its invitations
            public readonly Dictionary<GameObject, string> LastCrew = new Dictionary<GameObject, string>();
            void ICrewOwnerLink.View(Component part, string viewText) { LastCrew[Body(part)] = viewText; Lines.Add((Body(part), "crew view")); }
            public void Prompt(Component part, string inviterName, string crewName, float seconds) => Lines.Add((Body(part), $"crew prompt {inviterName} {crewName}"));
            void ICrewOwnerLink.ClosePrompt(Component part) => Lines.Add((Body(part), "crew prompt closed"));
            // Sponsors (M8 8.4.4): the last view text, points and bonus each body was sent, and its prompts
            public readonly Dictionary<GameObject, (string text, int points, bool boost)> LastSponsor = new Dictionary<GameObject, (string, int, bool)>();
            public void View(Component part, string viewText, int points, bool boostOn) { LastSponsor[Body(part)] = (viewText, points, boostOn); Lines.Add((Body(part), "sponsor view")); }
            public void Prompt(Component part, string fromName, bool theyWouldSponsor, float seconds) => Lines.Add((Body(part), $"sponsor prompt {fromName} {theyWouldSponsor}"));
            public void ClosePrompt(Component part) => Lines.Add((Body(part), "sponsor prompt closed"));
            public void Following(Component part, string memberName) => Lines.Add((Body(part), "following " + memberName));
            // Squads (M8 8.4.3): "squad <id:name ...>" (0 = the player it went to), the invitation and its closing
            public void Roster(Component part, BodyPartyMember[] members) =>
                Lines.Add((Body(part), "squad " + string.Join(" ", System.Array.ConvertAll(members ?? new BodyPartyMember[0], m => m.id + ":" + m.name))));
            public void Invite(Component part, string inviterName, float seconds) => Lines.Add((Body(part), $"squad invite {inviterName} {seconds:0}"));
            public void InviteClosed(Component part) => Lines.Add((Body(part), "squad invite closed"));
            public void Show(Component part, DuelShowKind kind, string name, float seconds, bool won) => Lines.Add((Body(part), $"duel {kind} {name} {seconds:0} {won}"));
            public string LastRoster(GameObject body) => Lines.FindLast(l => l.body == body && l.what.StartsWith("squad ") && !l.what.StartsWith("squad invite")).what;
            // Contacts (M8 8.4.2): the last list each body was sent, and its prompts
            public readonly Dictionary<GameObject, string> LastView = new Dictionary<GameObject, string>();
            public void View(Component part, string viewText) { LastView[Body(part)] = viewText; Lines.Add((Body(part), "contacts view")); }
            public void Prompt(Component part, string fromId, string fromName) => Lines.Add((Body(part), $"contact prompt {fromId} {fromName}"));
            public void ClosePrompt(Component part, string fromId) => Lines.Add((Body(part), $"contact prompt closed {fromId}"));
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

            // owner's 2-player test, 6 Nov 2026, item 3: the weapon's glow reads GetTemper, which read NGO's empty netTemper on a view
            EquipmentTemper tempered = temper;
            tempered.weapon = 7;
            remoteEquipment.ApplyObserved(loadout, seen, tempered);
            if (remoteEquipment.GetDefinition(EquipSlot.Weapon) is ItemDefinition weapon && weapon.IsLadderGear)
                Assert.AreEqual(7, remoteEquipment.GetTemper(EquipSlot.Weapon), "the other player's weapon glows by the Temper it was told");
            else Assert.Inconclusive("the starter weapon is not ladder gear: no glow to check");
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

        /// <summary>The line of <paramref name="name"/> in a Contacts view text (kind, id, name, online, level, faction, class, favourite, seen, note), or null.</summary>
        static string[] ContactLine(string viewText, string name)
        {
            if (string.IsNullOrEmpty(viewText)) return null;
            foreach (string row in viewText.Split('\n'))
            {
                string[] f = row.Split('\t');
                if (f.Length == 10 && f[2] == name) return f;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator B29_Contacts_RunOnTheServerBodies_AskAcceptNoteBlockLeave_TheBlockStopsAWhisper_TheBookIsSaved_AndTheViewOnlyAsks()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            ContactsOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string idA = "c2910000000000000000000000000291", idB = "c2920000000000000000000000000292", idC = "c2930000000000000000000000000293";
                GameObject a = Admitted(PlayerKeys.Mint(291).Value, "29a1000000000000000000000000000a", idA, "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(292).Value, "29b1000000000000000000000000000b", idB, "Brannoc");
                GameObject c = Admitted(PlayerKeys.Mint(293).Value, "29c1000000000000000000000000000c", idC, "Corvane");
                for (int i = 0; i < 30; i++) yield return null;
                var ca = a.GetComponent<IBodyContacts>();
                var cb = b.GetComponent<IBodyContacts>();
                var cc = c.GetComponent<IBodyContacts>();
                Assert.IsNotNull(ca, "the server body has its Contacts");
                Assert.IsTrue(told.LastView.ContainsKey(a) && told.LastView.ContainsKey(b), "each arrived (NGO's OnNetworkSpawn -> ServerArrived) and was sent its list");
                Assert.AreEqual(a.GetComponent<PlayerIdentity>().Faction, b.GetComponent<PlayerIdentity>().Faction, "planned: one faction (no neutral ground needed)");

                // 1. A asks B by name: both told in chat, B gets the prompt, A's list shows B as asked
                ca.RunContactOp(ContactOpKind.AddByName, "Brannoc", "", false);
                Assert.IsTrue(told.Any(a, "system You asked Brannoc to be a contact."), "NGO's line in A's chat: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(b, "system Talwyn wants to add you as a contact."), "B is told");
                Assert.IsTrue(told.Any(b, $"contact prompt {idA} Talwyn"), "B gets the prompt");
                Assert.AreEqual("2", ContactLine(told.LastView[a], "Brannoc")?[0], "A's list: B as Outgoing");
                Assert.AreEqual("1", ContactLine(told.LastView[b], "Talwyn")?[0], "B's list: A as Incoming");

                // 2. B accepts: the prompt closes, both are contacts and online
                cb.RunContactOp(ContactOpKind.Respond, idA, "", true);
                Assert.IsTrue(told.Any(b, $"contact prompt closed {idA}"), "NGO's RespondRpc closes the prompt");
                Assert.IsTrue(told.Any(a, "system Brannoc accepted your contact request."), "A is told");
                string[] lineB = ContactLine(told.LastView[a], "Brannoc");
                Assert.AreEqual("0", lineB?[0], "A's list: B is a contact");
                Assert.AreEqual("1", lineB[3], "and online");
                Assert.AreEqual(idB, lineB[1]);

                // 3. a favourite and a note (with a tab, kept through the text)
                ca.RunContactOp(ContactOpKind.Favourite, idB, "", true);
                ca.RunContactOp(ContactOpKind.Note, idB, "met\tat the docks", false);
                lineB = ContactLine(told.LastView[a], "Brannoc");
                Assert.AreEqual("1", lineB[7], "favourite");
                Assert.AreEqual("met\\tat the docks", lineB[9], "the note, its tab escaped on the wire");

                // 4. C blocks A by name: A's whisper to C is refused by the gate (Contacts fills SocialGate), C hears nothing
                cc.RunContactOp(ContactOpKind.BlockByName, "Talwyn", "", false);
                Assert.IsTrue(told.Any(c, "system Talwyn is blocked"), "NGO's line: " + string.Join(" | ", told.To(c)));
                Assert.AreEqual("3", ContactLine(told.LastView[c], "Talwyn")?[0], "C's list: A blocked");
                a.GetComponent<IBodyChat>().RunSend(ChatChannel.Whisper, "hey", "Corvane", null, null);
                Assert.IsTrue(told.Any(a, "system That player isn't accepting requests from you."), "the block stops the whisper: " + string.Join(" | ", told.To(a)));
                Assert.IsFalse(told.Any(c, "line Whisper Talwyn"), "C hears nothing");

                // 5. an unknown name: NGO's notice on the message line (NotifyRpc)
                ca.RunContactOp(ContactOpKind.AddByName, "Nobodyhere", "", false);
                Assert.IsTrue(told.Any(a, "tell No character named 'Nobodyhere' is known here."), "NGO's Notify");

                // 6. B leaves (before its save): A is told and sees B offline
                ServerLeaving.Notify(b);
                Assert.IsTrue(told.Any(a, "system Brannoc went offline."), "NGO's ServerLeft");
                Assert.AreEqual("0", ContactLine(told.LastView[a], "Brannoc")[3], "B shows offline");

                // 7. the books are a world document in the store (NGO's SaveWorld on every change)
                ServerSave.WaitForDisk();
                var store = (MemorySaveStore)ServerSave.Store;
                bool saved = false;
                foreach (string key in store.Keys(string.Empty))
                    if (key.Contains("contacts") && store.TryRead(key, out string json) && json.Contains(idA) && json.Contains("met")) saved = true;
                Assert.IsTrue(saved, "the contacts world document holds A's book with the note: " + string.Join(", ", store.Keys(string.Empty)));

                // 8. the owner's view only asks (exact requests) and shows the server's list
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(a.GetComponent<BodyRoleTag>().Key.Value));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewPart = (Component)view.GetComponent<IBodyContacts>();
                Assert.IsNotNull(viewPart, "the own view keeps its Contacts (the window reads it)");
                Type type = viewPart.GetType();
                type.GetMethod("Add", new[] { typeof(string) }).Invoke(viewPart, new object[] { " Brannoc " });
                type.GetMethod("Respond").Invoke(viewPart, new object[] { idB, false });
                type.GetMethod("SetNote").Invoke(viewPart, new object[] { idB, "line one\nline two" });
                type.GetMethod("BlockByName").Invoke(viewPart, new object[] { "Corvane" });
                CollectionAssert.AreEqual(new[] { "Contact AddByName Brannoc||False", $"Contact Respond {idB}||False", $"Contact Note {idB}|line one line two|False",
                    "Contact BlockByName Corvane||False" }, asked.Ops, "sent to the server");
                ((IBodyContacts)viewPart).MirrorView(told.LastView[a]);
                var shownLines = (System.Collections.ICollection)type.GetProperty("Lines").GetValue(viewPart);
                Assert.AreEqual(told.LastView[a].Split('\n').Length, shownLines.Count, "the own view shows the server's list");
                ((IBodyContacts)viewPart).MirrorPrompt(idC, "Corvane");   // no prompt window in the test: shown nowhere, no error
                ((IBodyContacts)viewPart).MirrorClosePrompt(idC);

                foreach (GameObject body in new[] { a, c }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
                b.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B30_ASquad_RunsOnTheServerBodies_InviteJoinPromoteLeave_ThePartyChannelAndBuffShareWork_AndViewsShowTheIdAndRoster()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            PartyOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(301).Value, "30a1000000000000000000000000000a", "c3010000000000000000000000000301", "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(302).Value, "30b1000000000000000000000000000b", "c3020000000000000000000000000302", "Brannoc");
                GameObject c = Admitted(PlayerKeys.Mint(303).Value, "30c1000000000000000000000000000c", "c3030000000000000000000000000303", "Corvane");
                for (int i = 0; i < 30; i++) yield return null;
                var pa = a.GetComponent<IBodyParty>();
                var pb = b.GetComponent<IBodyParty>();
                var pc = c.GetComponent<IBodyParty>();
                Assert.IsNotNull(pa, "the server body has its Squad");
                Assert.AreEqual(a.GetComponent<PlayerIdentity>().Faction, b.GetComponent<PlayerIdentity>().Faction, "planned: one faction, one level");
                ulong keyA = a.GetComponent<BodyRoleTag>().Key.Value, keyB = b.GetComponent<BodyRoleTag>().Key.Value;

                // 1. A invites B by name: both told in chat, B gets the invitation
                pa.RunPartyOp(PartyOpKind.InviteByName, "Brannoc", false);
                Assert.IsTrue(told.Any(a, "system You invited Brannoc to your squad."), "NGO's line: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(b, "system Talwyn invites you to a squad."), "B is told");
                Assert.IsTrue(told.Any(b, "squad invite Talwyn "), "and gets the invitation");

                // 2. B joins (NGO's IsSpawned "still here" check passes for a DOTS server body): one Squad id, rosters with 0 = me
                pb.RunPartyOp(PartyOpKind.Respond, "", true);
                Assert.IsTrue(told.Any(b, "squad invite closed"), "the invitation closes");
                Assert.IsTrue(told.Any(b, "system You joined Talwyn's squad."), "joined: " + string.Join(" | ", told.To(b)));
                Assert.IsTrue(told.Any(a, "system Brannoc joined the squad."));
                Assert.AreNotEqual(0UL, pa.PartyId, "a Squad id");
                Assert.AreEqual(pa.PartyId, pb.PartyId, "the same on both");
                Assert.AreEqual(0UL, pc.PartyId, "C is in none");
                Assert.AreEqual($"squad 0:Talwyn {keyB}:Brannoc", told.LastRoster(a), "A's roster, A leads, A is 0");
                Assert.AreEqual($"squad {keyA}:Talwyn 0:Brannoc", told.LastRoster(b), "B's roster, B is 0");
                pc.RunPartyOp(PartyOpKind.Respond, "", true);
                Assert.IsTrue(told.Any(c, "system There is no squad invitation to answer."), "NGO's rule");

                // 3. the Party channel now reaches the Squad (chat reads PartyId), not C
                a.GetComponent<IBodyChat>().RunSend(ChatChannel.Party, "to squad", "", null, null);
                Assert.IsTrue(told.Any(b, "line Party Talwyn: to squad"), "B hears the Squad");
                Assert.IsFalse(told.Any(c, "line Party"), "C does not");
                c.GetComponent<IBodyChat>().RunSend(ChatChannel.Party, "anyone", "", null, null);
                Assert.IsTrue(told.Any(c, "system You are not in a squad."), "NGO's refusal");

                // 4. a Stance shared with the Squad finds B on the server body (NGO's IsSpawned && IsServer guard)
                var targets = new List<PlayerCombatSkills>();
                a.GetComponent<ISquadBuffTargets>().ServerGetBuffTargets(targets);
                Assert.AreEqual(1, targets.Count, "B, in reach and alive");
                Assert.AreSame(b.GetComponent<PlayerCombatSkills>(), targets[0]);

                // 5. leader only; then A promotes B
                pb.RunPartyOp(PartyOpKind.Kick, "Talwyn", false);
                Assert.IsTrue(told.Any(b, "system Only the squad leader can do that."), "NGO's rule");
                pa.RunPartyOp(PartyOpKind.Promote, "Brannoc", false);
                Assert.IsTrue(told.Any(a, "system Brannoc now leads the squad."));
                Assert.AreEqual($"squad 0:Brannoc {keyA}:Talwyn", told.LastRoster(b), "B leads now");

                // 6. B leaves the game: A is told, the Squad of one is disbanded, A's roster is empty and its id 0
                ServerLeaving.Notify(b);
                Assert.IsTrue(told.Any(a, "system Brannoc left the game."), "NGO's despawn line");
                Assert.IsTrue(told.Any(a, "system The squad was disbanded."));
                Assert.AreEqual("squad ", told.LastRoster(a), "an empty roster");
                Assert.AreEqual(0UL, pa.PartyId);

                // 7. the views: the own view asks (by name for a picked player) and shows the roster; any view shows the ghost's id
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(keyA));
                GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(keyB));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewPart = (Component)view.GetComponent<IBodyParty>();
                var remotePart = (Component)remote.GetComponent<IBodyParty>();
                Assert.IsNotNull(remotePart, "another player's view keeps PlayerParty (its Squad id is read by the minimap and namecards)");
                Type type = viewPart.GetType();
                type.GetMethod("Invite", new[] { typeof(string) }).Invoke(viewPart, new object[] { " Brannoc " });
                type.GetMethod("Invite", new[] { type }).Invoke(viewPart, new object[] { remotePart });
                type.GetMethod("Respond").Invoke(viewPart, new object[] { true });
                type.GetMethod("Leave").Invoke(viewPart, null);
                type.GetMethod("Promote").Invoke(viewPart, new object[] { "Corvane" });
                string remoteName = (string)type.GetProperty("MemberName").GetValue(remotePart);
                CollectionAssert.AreEqual(new[] { "Party InviteByName Brannoc|False", $"Party InviteByName {remoteName}|False", "Party Respond |True", "Party Leave |False",
                    "Party Promote Corvane|False" }, asked.Ops, "sent to the server");
                ((IBodyParty)viewPart).MirrorPartyId(7);
                ((IBodyParty)remotePart).MirrorPartyId(7);
                Assert.AreEqual(7UL, ((IBodyParty)viewPart).PartyId);
                Assert.IsTrue((bool)type.GetMethod("IsInPartyWith").Invoke(viewPart, new object[] { remotePart }), "two views with one id are grouped (namecards, minimap)");
                ((IBodyParty)viewPart).MirrorRoster(new[] { new BodyPartyMember { id = 0, name = "Talwyn", level = 1 }, new BodyPartyMember { id = keyB, name = "Brannoc", level = 1 } });
                Assert.AreEqual(2, ((System.Collections.ICollection)type.GetProperty("Roster").GetValue(viewPart)).Count, "the own view shows the roster");
                Assert.IsTrue((bool)type.GetProperty("IsLeader").GetValue(viewPart), "row 0 is this player: it leads");
                ((IBodyParty)viewPart).MirrorInvite("Corvane", 30f);   // no Squad window in the test: shown nowhere, no error
                ((IBodyParty)viewPart).MirrorInviteClosed();

                foreach (GameObject body in new[] { a, c }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
                b.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        /// <summary>The ghost's side of a server body's walk, as the test sees it (the real one issues a ServerOrder).</summary>
        sealed class MoverSeen : IBodyMover
        {
            public readonly List<Vector3> Moves = new List<Vector3>();
            public readonly List<string> Kinds = new List<string>();
            public int Stops;
            public void MoveTo(Vector3 point, bool run, bool payRun) { Moves.Add(point); Kinds.Add((run ? "run" : "walk") + (payRun ? " paid" : " free")); }
            public void Stop() => Stops++;
            public int Teleports;
            public void Teleport(Vector3 position, Quaternion rotation) => Teleports++;
            public readonly List<Vector3> Jumps = new List<Vector3>();
            public string Refuse;
            public bool JumpTo(Vector3 landing, out string reason) { reason = Refuse; if (Refuse == null) Jumps.Add(landing); return Refuse == null; }
            public bool Airborne { get; set; }
            public bool Busy { get; set; }
        }

        [UnityTest]
        public IEnumerator B31_Follow_RunsOnTheServerBody_WalksBehindTheMemberThroughTheGhost_AnOwnClickStopsIt_AndTheViewOnlyAsks()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            PartyOwnerLink.Current = told;
            FollowOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(311).Value, "31a1000000000000000000000000000a", "c3110000000000000000000000000311", "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(312).Value, "31b1000000000000000000000000000b", "c3120000000000000000000000000312", "Brannoc");
                GameObject c = Admitted(PlayerKeys.Mint(313).Value, "31c1000000000000000000000000000c", "c3130000000000000000000000000313", "Corvane");
                for (int i = 0; i < 30; i++) yield return null;
                // planned before the run: A at 0, B 10 m east (beyond NGO's 5 m set-off, inside its 60 m), C anywhere, not in the Squad
                a.transform.position = new Vector3(0f, 0.08f, 0f);
                b.transform.position = new Vector3(10f, 0.08f, 0f);
                var mover = new MoverSeen();
                a.GetComponent<BodyRoleTag>().Mover = mover;
                a.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.InviteByName, "Brannoc", false);
                b.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.Respond, "", true);
                Assert.AreNotEqual(0UL, a.GetComponent<IBodyParty>().PartyId, "A and B are one Squad");
                var fa = a.GetComponent<IBodyFollow>();
                var fc = c.GetComponent<IBodyFollow>();
                Assert.IsNotNull(fa, "the server body has its Follow");

                // 1. NGO's refusals on the server body
                fc.RunFollow("Talwyn");
                Assert.IsTrue(told.Any(c, "system Talwyn is not in your squad."), "not in one Squad: " + string.Join(" | ", told.To(c)));
                fa.RunFollow("Nobodyhere");
                Assert.IsTrue(told.Any(a, "system That player is not here."), "no such player");

                // 2. A follows B: told, its view told, and the server body walks to 3.5 m behind B through its ghost
                fa.RunFollow("Brannoc");
                Assert.IsTrue(told.Any(a, "system You are following Brannoc. Move to stop."), "NGO's line: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(a, "following Brannoc"), "the owner's view is told");
                float until = Time.time + 1.5f;
                while (mover.Moves.Count == 0 && Time.time < until) yield return null;
                Assert.Greater(mover.Moves.Count, 0, "the follower set off (PointAndClickPlayer.ServerMoveTo -> the ghost's order)");
                Vector3 stand = mover.Moves[0];
                Assert.AreEqual(6.5f, stand.x, 0.3f, "3.5 m this side of B");
                Assert.AreEqual(0f, stand.z, 0.3f);

                // 3. the player's own click (NGO's MoveOrdered; on DOTS a new ClickSeq on the ghost) ends it with NGO's line
                fa.OwnMoveOrdered();
                until = Time.time + 1.5f;
                while (!told.Any(a, "system You stopped following Brannoc.") && Time.time < until) yield return null;
                Assert.IsTrue(told.Any(a, "system You stopped following Brannoc."), "an own click stops it: " + string.Join(" | ", told.To(a)));
                Assert.AreEqual("following ", told.Lines.FindLast(l => l.body == a && l.what.StartsWith("following")).what, "the view is told it follows nobody");

                // 4. the owner's view only asks (a picked member by name) and shows whom it follows
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(a.GetComponent<BodyRoleTag>().Key.Value));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewPart = (Component)view.GetComponent<IBodyFollow>();
                Type type = viewPart.GetType();
                type.GetMethod("Follow", new[] { typeof(string) }).Invoke(viewPart, new object[] { " Brannoc " });
                CollectionAssert.AreEqual(new[] { "Follow Brannoc" }, asked.Ops, "sent to the server");
                ((IBodyFollow)viewPart).MirrorFollowing("Brannoc");
                Assert.AreEqual("Brannoc", type.GetProperty("Following").GetValue(viewPart), "the own view shows it");
                ((IBodyFollow)viewPart).OwnMoveOrdered();   // a view never decides: nothing
                Assert.AreEqual(1, asked.Ops.Count);

                foreach (GameObject body in new[] { a, b, c }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B32_AFollower_CopiesTheMembersJump_WalksToItsTakeoffThenJumpsThroughTheGhost_AndARefusedJumpEndsTheFollow()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            PartyOwnerLink.Current = told;
            FollowOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(321).Value, "32a1000000000000000000000000000a", "c3210000000000000000000000000321", "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(322).Value, "32b1000000000000000000000000000b", "c3220000000000000000000000000322", "Brannoc");
                for (int i = 0; i < 30; i++) yield return null;
                // planned before the run: A at 0, B 4 m east (inside NGO's 5 m: A holds, so only the hop moves it)
                a.transform.position = new Vector3(0f, 0.08f, 0f);
                b.transform.position = new Vector3(4f, 0.08f, 0f);
                var mover = new MoverSeen();
                a.GetComponent<BodyRoleTag>().Mover = mover;
                a.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.InviteByName, "Brannoc", false);
                b.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.Respond, "", true);
                a.GetComponent<IBodyFollow>().RunFollow("Brannoc");
                Assert.IsTrue(told.Any(a, "system You are following Brannoc."), "following: " + string.Join(" | ", told.To(a)));
                var leaderJump = b.GetComponent<PlayerJumpMotor>();
                Assert.IsNotNull(leaderJump, "the member's server body has its jump motor (its ghost's jumps are reported there)");

                // 1. B's ghost jumped (FollowServerSystem reports it): A walks to the take-off first
                var takeoff = new Vector3(2f, 0.08f, 0f);
                var landing = new Vector3(2f, 0.08f, 5f);
                leaderJump.ReportGhostJump(takeoff, landing);
                float until = Time.time + 1.5f;
                while (!mover.Moves.Exists(m => Vector3.Distance(m, takeoff) < 0.01f) && Time.time < until) yield return null;
                Assert.IsTrue(mover.Moves.Exists(m => Vector3.Distance(m, takeoff) < 0.01f), "A walks to the take-off: " + string.Join(", ", mover.Moves));
                Assert.AreEqual(0, mover.Jumps.Count, "not yet a jump");

                // 2. at the take-off, A jumps to the landing through its ghost (no stop first: a stop there is a placement)
                a.transform.position = takeoff;
                int stops = mover.Stops;
                until = Time.time + 1.5f;
                while (mover.Jumps.Count == 0 && Time.time < until) yield return null;
                Assert.AreEqual(1, mover.Jumps.Count, "the copied jump");
                Assert.AreEqual(landing, mover.Jumps[0], "to the member's landing");
                Assert.AreEqual(stops, mover.Stops, "no stop before it (it would call the jump off)");
                Assert.IsFalse(told.Any(a, "system You stopped following"), "still following");

                // in the flight (the ghost's plan), A waits for the landing: no stop (a placement would call the jump off), no new walk
                mover.Airborne = true;
                int moves = mover.Moves.Count;
                yield return new WaitForSeconds(1f);
                Assert.AreEqual(stops, mover.Stops, "nothing stops it in the air (NGO waited on its jump motor)");
                Assert.AreEqual(moves, mover.Moves.Count, "and nothing re-steers it");
                mover.Airborne = false;

                // 3. a jump the server refuses ends the Follow with NGO's line and the reason
                mover.Refuse = "Not enough Flux.";
                var takeoff2 = new Vector3(2f, 0.08f, 5f);
                a.transform.position = takeoff2;
                leaderJump.ReportGhostJump(takeoff2, new Vector3(2f, 0.08f, 10f));
                until = Time.time + 1.5f;
                while (!told.Any(a, "system You stopped following Brannoc:") && Time.time < until) yield return null;
                Assert.IsTrue(told.Any(a, "system You stopped following Brannoc: you could not follow their jump. Not enough Flux."),
                    "NGO's JumpRefused end: " + string.Join(" | ", told.To(a)));
                Assert.AreEqual("following ", told.Lines.FindLast(l => l.body == a && l.what.StartsWith("following")).what);

                foreach (GameObject body in new[] { a, b }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B33_Sponsor_RunsOnTheServerBodies_AskAcceptBonusLeaveEnd_ThePointsSaveIsReal_AndTheViewOnlyAsks()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            Assert.IsFalse(SavePassthrough.Is("sponsor"), "the sponsor part is real now");
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            SponsorOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string idA = "c3310000000000000000000000000331", idB = "c3320000000000000000000000000332";
                GameObject a = Admitted(PlayerKeys.Mint(331).Value, "33a1000000000000000000000000000a", idA, "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(332).Value, "33b1000000000000000000000000000b", idB, "Brannoc");
                for (int i = 0; i < 30; i++) yield return null;
                // planned before the run: NGO's Sponsor is level 112 and a Recruit more than 5 levels below, one faction, within 40 m
                a.GetComponent<PlayerCharacterProgression>().SetLevel(112);
                Assert.AreEqual(112, a.GetComponent<PlayerCharacterProgression>().CurrentLevel, "A is level 112 on its server body");
                Assert.AreEqual(a.GetComponent<PlayerIdentity>().Faction, b.GetComponent<PlayerIdentity>().Faction);
                var sa = a.GetComponent<IBodySponsor>();
                var sb = b.GetComponent<IBodySponsor>();
                Assert.IsNotNull(sa, "the server body has its Sponsor links");
                Type type = ((Component)sa).GetType();

                // 1. an unknown name: NGO's notice
                sa.RunSponsorOp(SponsorOpKind.RequestByName, "Nobodyhere", false);
                Assert.IsTrue(told.Any(a, "tell Nobodyhere is not online."), "NGO's Notify: " + string.Join(" | ", told.To(a)));

                // 2. A (112) asks B (1) by name: B is offered A as its Sponsor
                sa.RunSponsorOp(SponsorOpKind.RequestByName, "Brannoc", false);
                Assert.IsTrue(told.Any(a, "system Sponsor request sent to Brannoc."), "NGO's line: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(b, "sponsor prompt Talwyn True"), "B is asked (A would sponsor)");

                // 3. B accepts (NGO's "still here" check passes for a DOTS server body): one link, both views
                sb.RunSponsorOp(SponsorOpKind.Respond, "", true);
                Assert.IsTrue(told.Any(b, "sponsor prompt closed"), "the prompt closes");
                Assert.IsTrue(told.Any(a, "system Brannoc is now your Recruit."), "linked: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(b, "system Talwyn is now your Sponsor."));
                StringAssert.StartsWith(idB + "\tBrannoc\t1\t0\t1\t", told.LastSponsor[a].text, "A's window: its Recruit B, online");
                StringAssert.StartsWith(idA + "\tTalwyn\t112\t1\t1\t", told.LastSponsor[b].text, "B's window: its Sponsor A, online");

                // 4. near its Sponsor B's bonus goes on at the next look (every 5 s, NGO's Update on the server body), and kills would pay it
                float until = Time.time + 7f;
                while (!told.Any(b, "system Your Sponsor is near") && Time.time < until) yield return null;
                Assert.IsTrue(told.Any(b, "system Your Sponsor is near: +"), "the bonus line: " + string.Join(" | ", told.To(b)));
                Assert.IsTrue(told.LastSponsor[b].boost, "the owner's view is told the bonus is on");
                object[] args = { false };
                float bonus = (float)type.GetMethod("ServerExperienceBonus").Invoke(sb, args);
                Assert.Greater(bonus, 0f, "the XP bonus on the server body (NGO's IsSpawned && IsServer guard)");

                // 5. GM points; B leaves: A's window shows it offline; A ends the link by id
                type.GetMethod("ServerSetPoints").Invoke(sa, new object[] { 1234 });
                Assert.AreEqual(1234, told.LastSponsor[a].points, "the points reach the owner's window");
                ServerLeaving.Notify(b);
                StringAssert.StartsWith(idB + "\tBrannoc\t1\t0\t0\t", told.LastSponsor[a].text, "B offline in A's window");
                sa.RunSponsorOp(SponsorOpKind.End, idB, false);
                Assert.IsTrue(told.Any(a, "system Brannoc is no longer your Recruit."), "NGO's end line");

                // 6. the save: A's part is NGO's text; it loads into a fresh admitted server body and saves back byte for byte
                string text = null;
                foreach (ISaveParticipant part in a.GetComponents<ISaveParticipant>()) if (part.SaveKey == "sponsor") text = part.CaptureSave();
                Assert.AreEqual("{\"points\":1234,\"nearSeconds\":0.0}", text, "NGO's sponsor text (JsonUtility writes both fields)");
                const string account2 = "33c1000000000000000000000000000c", character2 = "c3330000000000000000000000000333";
                GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(333));
                yield return null;
                CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
                var stored = new AccountSave { accountId = account2 };
                var character = new CharacterSave { characterId = character2, name = "Sponsorback" };
                character.Set("sponsor", text);
                stored.characters.Add(character);
                Assert.IsTrue(ServerSave.WriteAccount(stored));
                ServerSave.WaitForDisk();
                var context = new ServerBodyContext
                {
                    Key = new PlayerKey(333), Serial = 333, NetworkId = 333, Account = account2, CharacterId = character2, Name = "Sponsorback", RecipeText = RecipeText.Encode(recipe),
                };
                Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
                var agent = other.GetComponent<PlayerSaveAgent>();
                for (int i = 0; i < 30; i++) yield return null;
                Assert.AreEqual(1234, (int)type.GetProperty("Points").GetValue(other.GetComponent<IBodySponsor>()), "the saved points are on the server body");
                agent.Save();
                ServerSave.WaitForDisk();
                Assert.AreEqual(text, ServerSave.ReadAccountFrom(ServerSave.Store, account2).Find(character2).Get("sponsor"), "the sponsor part saves back byte for byte");

                // 7. the owner's view only asks and shows; it never loads a save of its own
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(a.GetComponent<BodyRoleTag>().Key.Value));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewPart = (Component)view.GetComponent<IBodySponsor>();
                type.GetMethod("Request", new[] { typeof(string) }).Invoke(viewPart, new object[] { " Brannoc " });
                type.GetMethod("Respond").Invoke(viewPart, new object[] { true });
                type.GetMethod("End").Invoke(viewPart, new object[] { idB });
                CollectionAssert.AreEqual(new[] { "Sponsor RequestByName Brannoc|False", "Sponsor Respond |True", $"Sponsor End {idB}|False" }, asked.Ops, "sent to the server");
                ((IBodySponsor)viewPart).MirrorView(idB + "\tBrannoc\t1\t0\t1\t1", 77, true);
                Assert.AreEqual(77, (int)type.GetProperty("Points").GetValue(viewPart), "the server's points");
                Assert.IsTrue((bool)type.GetProperty("BoostOn").GetValue(viewPart), "the server's bonus");
                Assert.AreEqual(1, ((System.Collections.ICollection)type.GetProperty("Lines").GetValue(viewPart)).Count, "the server's links");
                ((ISaveParticipant)viewPart).RestoreSave("{\"points\":5}");
                Assert.AreEqual(77, (int)type.GetProperty("Points").GetValue(viewPart), "a view never loads a save");
                ((IBodySponsor)viewPart).MirrorPrompt("Corvane", true, 30f);   // no prompt window in the test: shown nowhere, no error
                ((IBodySponsor)viewPart).MirrorClosePrompt();

                agent.OnServerLeaving();
                foreach (GameObject body in new[] { a, b }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B34_ACrew_IsFoundedOnTheServerBody_ForItsCredsWithTheLedgerLineSavedTogether_ThenRunByNgosRules_TheTagReachesViews()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            CrewOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                const string accA = "34a1000000000000000000000000000a", idA = "c3410000000000000000000000000341", idB = "c3420000000000000000000000000342";
                GameObject a = Admitted(PlayerKeys.Mint(341).Value, accA, idA, "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(342).Value, "34b1000000000000000000000000000b", idB, "Brannoc");
                GameObject c = Admitted(PlayerKeys.Mint(343).Value, "34c1000000000000000000000000000c", "c3430000000000000000000000000343", "Corvane");
                for (int i = 0; i < 30; i++) yield return null;
                // planned before the run: NGO's founding asks level 30 and 10,000,000 Creds at a Vaultkeeper (CrewRegistry); one faction
                foreach (GameObject body in new[] { a, c })
                {
                    body.GetComponent<PlayerCharacterProgression>().SetLevel(30);
                    body.GetComponent<KryticalNova.Economy.PlayerWallet>().ServerEarn(10_000_500, KryticalNova.Wallet.CredsSource.Gm);
                }
                Assert.AreEqual(a.GetComponent<PlayerIdentity>().Faction, b.GetComponent<PlayerIdentity>().Faction);
                var wallet = a.GetComponent<KryticalNova.Wallet.IWallet>();
                var crewA = a.GetComponent<IBodyCrew>();
                var crewB = b.GetComponent<IBodyCrew>();
                var crewC = c.GetComponent<IBodyCrew>();
                Assert.IsNotNull(crewA, "the server body has its Crew");

                // 1. away from a Vaultkeeper: NGO's refusal, nothing paid
                long creds = wallet.Creds;
                crewA.RunCrewOp(CrewOpKind.Found, "Ironclad", "", 1, 2, 0, 0, false);
                Assert.IsTrue(told.Any(a, "tell Crews are registered with a Vaultkeeper."), "NGO's reach: " + string.Join(" | ", told.To(a)));
                Assert.AreEqual(creds, wallet.Creds, "nothing paid");

                // 2. at the Vaultkeeper: founded for exactly NGO's price, one crew.found line, the founder and the Crews saved together
                Npc(KryticalNova.NPC.NpcOffer.CrewRegistry, a.transform.position);
                var store = (MemorySaveStore)ServerSave.Store;
                ServerSave.WaitForDisk();
                int batches = store.Batches;
                int lines = ServerSave.ReadLedger(accA, 0, 100).FindAll(l => l.kind == LedgerKinds.CrewFound).Count;
                crewA.RunCrewOp(CrewOpKind.Found, "Ironclad", "", 1, 2, 0, 0, false);
                Assert.IsTrue(told.Any(a, "system You founded the Crew Ironclad"), "founded: " + string.Join(" | ", told.To(a)));
                Assert.AreEqual(creds - 10_000_000, wallet.Creds, "the founding fee through the wallet (CredsSink.CrewFounding)");
                ServerSave.WaitForDisk();
                Assert.AreEqual(lines + 1, ServerSave.ReadLedger(accA, 0, 100).FindAll(l => l.kind == LedgerKinds.CrewFound).Count, "one crew.found ledger line");
                Assert.Greater(store.Batches, batches, "SaveTogether wrote one batch");
                bool saved = false;
                foreach (string key in store.Keys(string.Empty))
                    if (key.Contains("crews") && store.TryRead(key, out string json) && json.Contains("Ironclad")) saved = true;
                Assert.IsTrue(saved, "the crews world document holds it: " + string.Join(", ", store.Keys(string.Empty)));
                Assert.AreEqual("Ironclad", crewA.ShownTag.crew, "A's tag");
                Assert.AreEqual(2, crewA.ShownTag.rank, "A leads (CrewRank.Leader)");
                StringAssert.StartsWith("1\tIronclad\t1\t", told.LastCrew[a], "A's window: the Crew and its symbol");

                // 3. the name is taken (any case): C is refused and pays nothing
                long credsC = c.GetComponent<KryticalNova.Wallet.IWallet>().Creds;
                crewC.RunCrewOp(CrewOpKind.Found, "ironclad", "", 0, 0, 0, 0, false);
                Assert.IsTrue(told.Any(c, "tell There is already a Crew called Ironclad."), "NGO's name rule: " + string.Join(" | ", told.To(c)));
                Assert.AreEqual(credsC, c.GetComponent<KryticalNova.Wallet.IWallet>().Creds, "nothing paid");

                // 4. the Crew channel (chat's Guild reads the Crew name ServerShow set) reaches the Crew only
                a.GetComponent<IBodyChat>().RunSend(ChatChannel.Guild, "crew talk", "", null, null);
                Assert.IsTrue(told.Any(a, "line Guild Talwyn: crew talk"), "the Crew channel");
                Assert.IsFalse(told.Any(b, "line Guild"), "B is not in it yet");

                // 5. invite B by name -> the invitation -> accept: both windows, B's tag
                crewA.RunCrewOp(CrewOpKind.InviteByName, "Brannoc", "", 0, 0, 0, 0, false);
                Assert.IsTrue(told.Any(b, "crew prompt Talwyn Ironclad"), "B is invited: " + string.Join(" | ", told.To(b)));
                crewB.RunCrewOp(CrewOpKind.Respond, "", "", 0, 0, 0, 0, true);
                Assert.IsTrue(told.Any(b, "crew prompt closed"), "the invitation closes");
                Assert.AreEqual("Ironclad", crewB.ShownTag.crew, "B is in the Crew");
                Assert.AreEqual(3, told.LastCrew[a].Split('\n').Length, "A's window: the header and two members");

                // 6. officer, title, announcement (Leader's rights)
                crewA.RunCrewOp(CrewOpKind.Officer, idB, "", 0, 0, 0, 0, true);
                Assert.AreEqual(1, crewB.ShownTag.rank, "B is an Officer");
                crewA.RunCrewOp(CrewOpKind.Title, idB, "Scout", 0, 0, 0, 0, false);
                Assert.AreEqual("Scout", crewB.ShownTag.title, "B's title");
                crewA.RunCrewOp(CrewOpKind.Announcement, "", "Muster at dawn", 0, 0, 0, 0, false);
                StringAssert.EndsWith("\tMuster at dawn", told.LastCrew[b].Split('\n')[0], "the announcement in B's window");

                // 7. the views: the own view asks (exact requests), another player's view shows the tag from the ghost
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(a.GetComponent<BodyRoleTag>().Key.Value));
                GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(b.GetComponent<BodyRoleTag>().Key.Value));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewPart = (Component)view.GetComponent<IBodyCrew>();
                Type type = viewPart.GetType();
                type.GetMethod("Found").Invoke(viewPart, new object[] { "Ashfall", 2, 3, 4, 5 });
                type.GetMethod("Invite", new[] { typeof(string) }).Invoke(viewPart, new object[] { " Corvane " });
                type.GetMethod("Respond").Invoke(viewPart, new object[] { true });
                type.GetMethod("SetTitle").Invoke(viewPart, new object[] { idB, "Scout" });
                type.GetMethod("SetAnnouncement").Invoke(viewPart, new object[] { "hi" });
                type.GetMethod("Disband").Invoke(viewPart, null);
                CollectionAssert.AreEqual(new[] { "Crew Found Ashfall||2 3 4 5 False", "Crew InviteByName Corvane||0 0 0 0 False", "Crew Respond ||0 0 0 0 True",
                    $"Crew Title {idB}|Scout|0 0 0 0 False", "Crew Announcement |hi|0 0 0 0 False", "Crew Disband ||0 0 0 0 False" }, asked.Ops, "sent to the server");
                ((IBodyCrew)viewPart).MirrorView(told.LastCrew[a]);
                Assert.IsTrue((bool)type.GetProperty("InCrew").GetValue(viewPart), "the own view shows the server's Crew");
                Assert.AreEqual(2, ((System.Collections.ICollection)type.GetProperty("Roster").GetValue(viewPart)).Count, "and its roster");
                var remoteCrew = remote.GetComponent<IBodyCrew>();
                Assert.IsNotNull(remoteCrew, "another player's view keeps PlayerCrew (its namecard reads the tag)");
                remoteCrew.MirrorTag(crewB.ShownTag);
                Assert.AreEqual("Ironclad", remoteCrew.ShownTag.crew, "the tag from the ghost");
                Assert.AreEqual("Scout", remoteCrew.ShownTag.title);
                Assert.AreEqual("Ironclad", CrewTagWire.To(CrewTagWire.From(crewB.ShownTag)).crew, "the ghost field carries it");

                // 8. B leaves the Crew; A disbands it
                crewB.RunCrewOp(CrewOpKind.Leave, "", "", 0, 0, 0, 0, false);
                Assert.AreEqual(string.Empty, crewB.ShownTag.crew, "B is out");
                crewA.RunCrewOp(CrewOpKind.Disband, "", "", 0, 0, 0, 0, false);
                Assert.AreEqual(string.Empty, crewA.ShownTag.crew, "disbanded");

                foreach (GameObject body in new[] { a, b, c }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B35_Emotes_AreDecidedOnTheServerBody_ByItsGhostStandingStill_AStanceEndsWhenItMoves_AndEveryViewShowsTheServers()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(351).Value, "35a1000000000000000000000000000a", "c3510000000000000000000000000351", "Talwyn");
                for (int i = 0; i < 30; i++) yield return null;
                var mover = new MoverSeen();
                a.GetComponent<BodyRoleTag>().Mover = mover;
                var emotes = a.GetComponent<IBodyEmotes>();
                Assert.IsNotNull(emotes, "the server body has its emotes");
                var part = (Component)emotes;
                Type type = part.GetType();

                // planned before the run: an emote and a stance NGO's wheel offers this body (EmoteCatalog.Collect)
                object settings = type.GetProperty("Settings").GetValue(part);
                Assert.IsNotNull(settings, "the prefab's emote wheel settings");
                Type catalog = Type.GetType("KryticalNova.Social.EmoteCatalog, KryticalNova.Social");
                Type entryType = Type.GetType("KryticalNova.Social.EmoteEntry, KryticalNova.Social");
                object lists = Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(List<>).MakeGenericType(entryType)));
                catalog.GetMethod("Collect").Invoke(null, new[] { settings, type.GetProperty("Body").GetValue(part), lists });
                string emoteKey = null, stanceKey = null;
                foreach (System.Collections.IEnumerable category in (System.Collections.IEnumerable)lists)
                    foreach (object entry in category)
                    {
                        string key = (string)entryType.GetField("Key").GetValue(entry);
                        if ((bool)entryType.GetField("Stance").GetValue(entry)) stanceKey ??= key; else emoteKey ??= key;
                    }
                Assert.IsNotNull(emoteKey, "an emote on the wheel");
                Assert.IsNotNull(stanceKey, "a stance on the wheel");
                float cooldown = (float)settings.GetType().GetField("cooldownSeconds").GetValue(settings);

                // 1. the ghost is walking: refused (NGO: "Stand still"; the server body has no animation motor, its ghost decides)
                mover.Busy = true;
                emotes.RunEmote(false, emoteKey);
                Assert.AreEqual(string.Empty, emotes.State.key, "not while it moves");

                // 2. standing still: shown (serial up), and the cooldown holds the next one
                mover.Busy = false;
                emotes.RunEmote(false, emoteKey);
                Assert.AreEqual(emoteKey, emotes.State.key, "played");
                Assert.IsFalse(emotes.State.stance);
                byte serial = emotes.State.serial;
                emotes.RunEmote(false, stanceKey);
                Assert.AreEqual(emoteKey, emotes.State.key, "NGO's cooldown");
                yield return new WaitForSeconds(cooldown + 0.1f);

                // 3. a stance; a stop names it or does nothing
                emotes.RunEmote(false, stanceKey);
                Assert.AreEqual(stanceKey, emotes.State.key, "the stance");
                Assert.IsTrue(emotes.State.stance);
                Assert.AreEqual((byte)(serial + 1), emotes.State.serial);
                emotes.RunEmote(true, emoteKey);
                Assert.AreEqual(stanceKey, emotes.State.key, "a stop of another key does nothing");
                emotes.RunEmote(true, stanceKey);
                Assert.AreEqual(string.Empty, emotes.State.key, "stopped");

                // 4. a held stance ends when the ghost moves (NGO's OnEmoteEnded from the motor; the server body looks every 0.5 s)
                yield return new WaitForSeconds(cooldown + 0.1f);
                emotes.RunEmote(false, stanceKey);
                Assert.AreEqual(stanceKey, emotes.State.key);
                mover.Busy = true;
                float until = Time.time + 1.5f;
                while (emotes.State.key.Length > 0 && Time.time < until) yield return null;
                Assert.AreEqual(string.Empty, emotes.State.key, "moving ended the stance");

                // 5. the views: the own view asks (never plays on its own), another player's view keeps PlayerEmotes and shows the server's
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(a.GetComponent<BodyRoleTag>().Key.Value));
                GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(352));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewPart = (Component)view.GetComponent<IBodyEmotes>();
                type.GetMethod("RequestStop").Invoke(viewPart, new object[] { stanceKey });
                type.GetMethod("RequestPlay").Invoke(viewPart, new object[] { emoteKey });
                Assert.AreEqual("Emote True " + stanceKey, asked.Ops[0], "the stop is asked");
                Assert.IsTrue(asked.Ops.Count == 1 || asked.Ops[1] == "Emote False " + emoteKey, "a play is asked (when its motor lets it): " + string.Join(" | ", asked.Ops));
                Assert.AreEqual(string.Empty, ((IBodyEmotes)viewPart).State.key, "the view never plays it on its own");
                var remoteEmotes = remote.GetComponent<IBodyEmotes>();
                Assert.IsNotNull(remoteEmotes, "another player's view keeps PlayerEmotes (it plays the motion)");
                remoteEmotes.MirrorState(new BodyEmoteState { key = stanceKey, serial = 4, stance = true });
                Assert.AreEqual(stanceKey, remoteEmotes.State.key, "the server's stance on another player's view");
                remoteEmotes.MirrorState(new BodyEmoteState { key = string.Empty, serial = 5 });
                Assert.AreEqual(string.Empty, remoteEmotes.State.key, "and its end");

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B36_ThePet_IsDecidedOnTheServerBody_ItsStateInTheRealItem_TricksAndEvolutionsArePassedOn_AndTheOwnViewOnlyShows()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            PetShowLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(361).Value, "36a1000000000000000000000000000a", "c3610000000000000000000000000361", "Talwyn");
                for (int i = 0; i < 30; i++) yield return null;
                var pet = a.GetComponent<IBodyPet>();
                Assert.IsNotNull(pet, "the server body has its pet");
                var part = (Component)pet;
                Type type = part.GetType();

                // planned before the run: the first pet of the prefab's catalog, given and put out (the Pet slot)
                object catalog = type.GetProperty("Catalog").GetValue(part);
                object first = null;
                foreach (object p in (System.Collections.IEnumerable)catalog.GetType().GetProperty("Pets").GetValue(catalog)) { if (p != null) { first = p; break; } }
                Assert.IsNotNull(first, "a pet in the catalog");
                FieldInfo idField = first.GetType().GetField("itemId");
                ushort petItem = (ushort)(idField != null ? idField.GetValue(first) : first.GetType().GetProperty("itemId").GetValue(first));
                var inv = a.GetComponent<PlayerInventory>();
                inv.ServerGive(petItem, 1);
                int cell = -1;
                for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == petItem) { cell = i; break; }
                inv.RunPackOp(PackOpKind.QuickEquip, ItemLocation.Bag(cell), default);
                yield return null;
                Assert.IsNotNull(type.GetProperty("Pet").GetValue(part), "the pet is out on the server body");
                ulong dataBefore = inv.GetEquipped(EquipSlot.Pet).data;

                // 1. the GM's growth: on the server body, written into the worn (real) item, the evolution passed on to every machine
                type.GetMethod("ServerSetGrowth").Invoke(part, new object[] { 60f });
                Assert.AreEqual(60, pet.ShownGrowth, 1, "the server's growth");
                Assert.AreNotEqual(dataBefore, inv.GetEquipped(EquipSlot.Pet).data, "the pet's state is in the item's data (the Pack is real)");
                Assert.IsTrue(told.Any(a, "pet show Evolved "), "the evolution goes to every machine: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Lines.Exists(l => l.body == a && l.what.StartsWith("pet show Evolved") && l.what.EndsWith("True")), "its own player's too");
                Assert.IsTrue(told.Any(a, "system Your "), "NGO's evolved line in its chat");

                // 2. hunger: fed goes off on the server body
                type.GetMethod("ServerSetHunger").Invoke(part, new object[] { 0f });
                Assert.IsFalse(pet.ShownFed, "starving");

                // 3. a trick the owner's game took goes to everyone else, at NGO's pace (1 s)
                pet.RunTrick(77);
                pet.RunTrick(78);
                Assert.IsTrue(told.Any(a, "pet show Trick 77 False"), "passed on, not to its own player");
                Assert.IsFalse(told.Any(a, "pet show Trick 78"), "the pace holds");
                yield return new WaitForSeconds(1.1f);
                pet.RunTrick(79);
                Assert.IsTrue(told.Any(a, "pet show Trick 79 False"), "after a second, the next one");

                // 4. the own view: never decides (NGO's !IsSpawned let it drain hunger itself), shows the server's, asks to share its tricks
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(a.GetComponent<BodyRoleTag>().Key.Value));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewPet = view.GetComponent<IBodyPet>();
                viewPet.MirrorShown(40, true);
                Assert.AreEqual(40, viewPet.ShownGrowth, "the server's growth on the own view");
                Assert.IsTrue(viewPet.ShownFed);
                viewPet.RunTrick(5);
                Assert.AreEqual(0, told.Lines.FindAll(l => l.body == view).Count, "a view never passes a trick on itself");
                type.GetMethod("ShareTrick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(viewPet, new object[] { 55 });
                CollectionAssert.AreEqual(new[] { "PetTrick 55" }, asked.Ops, "it asks the server");

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B37_AnotherPlayersView_KeepsThePet_FromTheGearTextAndTheGhost_ItsEmptyPackNeverSavesMirrorsOrActs()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ClientView));
            GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(371));
            for (int i = 0; i < 10; i++) yield return null;
            var pet = remote.GetComponent<IBodyPet>();
            var inv = remote.GetComponent<PlayerInventory>();
            Assert.IsNotNull(pet, "another player's view keeps PlayerPet (owner pick 8.4.7b)");
            Assert.IsNotNull(inv, "and the empty Pack it needs");
            Assert.IsNull(remote.GetComponent<PlayerSaveAgent>(), "it has no save agent: nothing of it is ever saved");
            int Held() { int n = 0; for (int i = 0; i < PlayerInventory.Capacity; i++) if (!inv.Get(i).IsEmpty) n++; return n; }
            Assert.AreEqual(0, Held(), "no starter kit on another player's view");
            Assert.AreNotSame(inv, PlayerInventory.Local, "never this machine's own Pack");

            // never mirrors, never loads, never changes, never asks
            inv.MirrorCell(0, new ItemStack { itemId = 9001, count = 3 });
            ((ISaveParticipant)inv).RestoreSave("{\"pack\":[{\"index\":0,\"stack\":{\"itemId\":9001,\"count\":3,\"data\":0}}]}");
            inv.ServerGive(9001, 2);
            inv.UseItem(0);
            inv.RunPackOp(PackOpKind.Sort, default, default);
            Assert.AreEqual(0, Held(), "the empty Pack stays empty: " + inv.Get(0).itemId);
            Assert.IsNull(BodyRoleTag.RequestsOf(inv), "and has nowhere to send a request");

            // the pet: growth and fed from the ghost (PlayerPetTag) on another player's view
            pet.MirrorShown(70, true);
            Assert.AreEqual(70, pet.ShownGrowth, "the server's growth");
            Assert.IsTrue(pet.ShownFed);
            pet.PlayShown(BodyPetShowKind.Trick, 123);   // a trick passed on: played by the follower (no pet out here: nothing, no error)
        }

        [UnityTest]
        public IEnumerator B38_Mounts_AreDecidedOnTheServerBody_TheItemsAreUsed_RideAndJumpOff_TheSaveIsReal_AndViewsOnlyShowAndAsk()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            MountOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                Assert.IsFalse(SavePassthrough.Is("mounts"), "the mounts part is real now");
                GameObject a = Admitted(PlayerKeys.Mint(381).Value, "38a1000000000000000000000000000a", "c3810000000000000000000000000381", "Brannagh");
                for (int i = 0; i < 30; i++) yield return null;
                var mounts = a.GetComponent<IBodyMounts>();
                Assert.IsNotNull(mounts, "the server body has its mounts");
                var part = (Component)mounts;
                Type type = part.GetType();
                var inv = a.GetComponent<PlayerInventory>();

                // planned before the run: the Brambleback (box 1, item 8101, the one built mount) and Mount Feed (item 9013), used as a click would
                inv.ServerGive(8101, 1);
                Assert.IsTrue(PlayerRequestServerSystem.UseOn(a, 8101, out RequestReplyRpc reply), "the mount item is used on the server body: " + reply.Text);
                Assert.AreEqual(1, mounts.ShownSelected, "the Brambleback is in box 1 and selected");
                Assert.IsTrue(told.Any(a, "mount book 1 "), "its player's Mount window gets the boxes: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(a, "tell The Brambleback is in your Mount window."), "NGO's toast on the message line");

                // 1. the Ride key: getting on, then riding, faster than on foot
                Component moves = a.GetComponent("PlayerMovementSkills");
                MethodInfo speedFor = moves.GetType().GetMethod("SpeedFor");
                float onFoot = (float)speedFor.Invoke(moves, new object[] { true });
                int renderersOnFoot = a.GetComponentsInChildren<Renderer>(true).Length;
                mounts.RunMountOp(MountOpKind.Ride, 0);
                Assert.AreEqual(1, mounts.ShownPhase, "getting on");
                float until = Time.time + 8f;
                while (mounts.ShownPhase != 2 && Time.time < until) yield return null;
                Assert.AreEqual(2, mounts.ShownPhase, "riding once the mount-up clip is over");
                Assert.Greater((float)speedFor.Invoke(moves, new object[] { true }), onFoot, "the ride speed is in the movement (MoveStats reads it)");
                // owner's 2-player test, 6 Nov 2026, item 5: on a host the hidden server body drew a second mount beside the own view
                Assert.AreEqual(renderersOnFoot, a.GetComponentsInChildren<Renderer>(true).Length, "the server body draws no mount (only views draw it)");
                // (batch mode draws nothing anyway: the rule itself, as a host's GUI runs it)
                MethodInfo drawsNoMount = a.GetComponent("MountRider").GetType().GetMethod("DrawsNoMount");
                bool NoMount(NetRoleKind? body) => (bool)drawsNoMount.Invoke(null, new object[] { false, body });
                Assert.IsTrue(NoMount(NetRoleKind.ServerBody), "a host's server body draws no mount");
                Assert.IsFalse(NoMount(NetRoleKind.ClientView), "every view does (own and others')");
                Assert.IsFalse(NoMount(null), "and the NGO game's bodies");

                // 2. a jump takes the rider off at once (the ghost's flight: NGO's motor Jumping)
                var tag = a.GetComponent<BodyRoleTag>();
                IBodyMover oldMover = tag.Mover;
                tag.Mover = new MoverSeen { Airborne = true };
                yield return null;
                yield return null;
                Assert.AreEqual(0, mounts.ShownPhase, "off the mount in a jump");
                yield return new WaitForSecondsRealtime(0.45f);
                mounts.RunMountOp(MountOpKind.Ride, 0);
                Assert.AreEqual(0, mounts.ShownPhase, "no getting on in a jump's flight");
                Assert.IsTrue(told.Any(a, "tell Finish what you are doing first."), "and why");
                tag.Mover = oldMover;

                // 3. growth (GM): the evolution goes to every machine; hunger and Mount Feed
                type.GetMethod("ServerSetGrowth").Invoke(part, new object[] { 60f });
                Assert.AreEqual(60, mounts.ShownGrowth, 1, "the server's growth");
                Assert.IsTrue(told.Any(a, "mount evolved "), "the evolution to every machine: " + string.Join(" | ", told.To(a)));
                type.GetMethod("ServerSetHunger").Invoke(part, new object[] { 0f });
                Assert.IsFalse(mounts.ShownFed, "starving");
                inv.ServerGive(9013, 1);
                Assert.IsTrue(PlayerRequestServerSystem.UseOn(a, 9013, out reply), "Mount Feed is used on the server body: " + reply.Text);
                Assert.IsTrue(mounts.ShownFed, "fed again");
                Assert.IsTrue(told.Any(a, "system Your Brambleback has eaten its fill."), "NGO's line in its chat");

                // 4. the save: NGO's text; it loads into a fresh admitted server body and saves back byte for byte
                string text = null;
                foreach (ISaveParticipant p in a.GetComponents<ISaveParticipant>()) if (p.SaveKey == "mounts") text = p.CaptureSave();
                StringAssert.StartsWith("{\"version\":1,\"selected\":1,\"mounts\":[{\"id\":1,\"xp\":", text, "NGO's mounts text");
                const string account2 = "38c1000000000000000000000000000c", character2 = "c3880000000000000000000000000388";
                GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(388));
                yield return null;
                CharacterRecipe recipe = AnyLegalRecipe(other.GetComponent<PlayerIdentity>().Database);
                var stored = new AccountSave { accountId = account2 };
                var character = new CharacterSave { characterId = character2, name = "Mountback" };
                character.Set("mounts", text);
                stored.characters.Add(character);
                Assert.IsTrue(ServerSave.WriteAccount(stored));
                ServerSave.WaitForDisk();
                var context = new ServerBodyContext
                {
                    Key = new PlayerKey(388), Serial = 388, NetworkId = 388, Account = account2, CharacterId = character2, Name = "Mountback", RecipeText = RecipeText.Encode(recipe),
                };
                Assert.IsNull(new PlayerAdmission().Begin(other, in context), "admitted");
                for (int i = 0; i < 30; i++) yield return null;
                var otherMounts = other.GetComponent<IBodyMounts>();
                Assert.AreEqual(1, otherMounts.ShownSelected, "the saved mount is selected on the server body");
                Assert.AreEqual(0, otherMounts.ShownPhase, "nobody comes back in the saddle");
                Assert.AreEqual(60, otherMounts.ShownGrowth, 1, "with its growth");
                other.GetComponent<PlayerSaveAgent>().Save();
                ServerSave.WaitForDisk();
                Assert.AreEqual(text, ServerSave.ReadAccountFrom(ServerSave.Store, account2).Find(character2).Get("mounts"), "the mounts part saves back byte for byte");

                // 5. the owner's view: never decides, shows the server's boxes and mount, asks for the select and the Ride key
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(tag.Key.Value));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewMounts = view.GetComponent<IBodyMounts>();
                Type viewType = ((Component)viewMounts).GetType();
                viewMounts.MirrorBook(1, 500, 0, 0, 0, 0, 0, 0, 0);
                Assert.IsTrue((bool)viewType.GetMethod("Owns").Invoke(viewMounts, new object[] { 1 }), "the server's boxes on the own view");
                viewMounts.MirrorShown(1, 2, 60, true);
                Assert.AreEqual(2, viewMounts.ShownPhase, "the server's ride phase on the own view");
                Assert.IsTrue((bool)viewType.GetProperty("IsMounted").GetValue(viewMounts));
                viewMounts.RunMountOp(MountOpKind.Ride, 0);
                Assert.AreEqual(2, viewMounts.ShownPhase, "a view never runs a request itself");
                viewType.GetMethod("RequestRide").Invoke(viewMounts, null);
                viewType.GetMethod("RequestSelect").Invoke(viewMounts, new object[] { 2 });
                CollectionAssert.AreEqual(new[] { "MountOp Ride 0", "MountOp Select 2" }, asked.Ops, "it asks the server");
                Assert.AreEqual(0, told.Lines.FindAll(l => l.body == view).Count, "and tells nobody");

                // 6. another player's view keeps the mount and its rider (owner: others see it), from the ghost
                GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(389));
                yield return null;
                var remoteMounts = remote.GetComponent<IBodyMounts>();
                Assert.IsNotNull(remoteMounts, "another player's view keeps PlayerMounts");
                Assert.IsNotNull(remote.GetComponent("MountRider"), "and MountRider, which draws the mount");
                remoteMounts.MirrorShown(1, 2, 100, true);
                for (int i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(1, remoteMounts.ShownSelected, "the server's mount on another player's view");
                Assert.AreEqual(2, remoteMounts.ShownPhase);

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B39_Recall_RunsOnTheServerBody_TheChannelAndArrivalGoToEveryMachine_AMoveStopsItWithTheTagBack_AndAViewNeverDecides()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            RecallShowLink.Current = told;
            GameObject town = null;
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(391).Value, "39a1000000000000000000000000000a", "c3910000000000000000000000000391", "Isolde");
                for (int i = 0; i < 30; i++) yield return null;
                // planned before the run: one town spot of this map (a spawn service and point made here, after the admission), the Recall Tag (item 9011)
                town = new GameObject("B39 town");
                town.transform.position = new Vector3(40f, 0f, 40f);
                town.AddComponent<KryticalNova.Networking.PlayerSpawnService>();
                town.AddComponent<KryticalNova.Networking.PlayerSpawnPoint>();
                var tag = a.GetComponent<BodyRoleTag>();
                IBodyMover oldMover = tag.Mover;
                var mover = new MoverSeen();
                tag.Mover = mover;
                var inv = a.GetComponent<PlayerInventory>();
                int Tags() { int n = 0; for (int i = 0; i < PlayerInventory.Capacity; i++) if (inv.Get(i).itemId == 9011) n += inv.Get(i).count; return n; }

                // 1. used as a click would: the channel to every machine, NGO's line to its player; after the channel, the arrival through the ghost
                inv.ServerGive(9011, 1);
                Assert.IsTrue(PlayerRequestServerSystem.UseOn(a, 9011, out RequestReplyRpc reply), "the Recall Tag is used on the server body: " + reply.Text);
                Assert.AreEqual(0, Tags(), "the Tag is spent");
                Assert.IsTrue(told.Any(a, "recall show Begun "), "the aura to every machine: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(a, "tell Recalling to town in "), "NGO's line on the message line");
                float until = Time.time + 8f;
                while (!told.Any(a, "recall show Arrived") && Time.time < until) yield return null;
                Assert.IsTrue(told.Any(a, "recall show Arrived"), "the arrival to every machine: " + string.Join(" | ", told.To(a)));
                Assert.AreEqual(1, mover.Teleports, "placed on the town spot through its ghost");
                Assert.IsTrue(told.Any(a, "tell Back in town."));

                // 2. a move stops it: the aura ends and the Tag comes back
                yield return null;
                inv.ServerGive(9011, 1);
                Assert.IsTrue(PlayerRequestServerSystem.UseOn(a, 9011, out reply), "used again: " + reply.Text);
                yield return null;
                a.transform.position += new Vector3(2f, 0f, 0f);
                yield return null;
                yield return null;
                Assert.IsTrue(told.Any(a, "recall show Ended"), "the aura ends on every machine");
                Assert.AreEqual(1, Tags(), "the Tag is back in the Pack");
                Assert.IsTrue(told.Any(a, "tell ") && told.To(a).Exists(l => l.EndsWith("Your Recall Tag is back in your Pack.")), "and why: " + string.Join(" | ", told.To(a)));
                Assert.AreEqual(1, mover.Teleports, "no trip");
                tag.Mover = oldMover;

                // 3. the own view never decides: a use there starts nothing (the Tag is used on its server body)
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(tag.Key.Value));
                yield return null;
                Component viewRecall = view.GetComponent("PlayerRecall");
                Assert.IsNotNull(viewRecall, "the own view has PlayerRecall");
                ((IItemUseHandler)viewRecall).ServerUse(default);
                Assert.IsFalse((bool)viewRecall.GetType().GetProperty("Recalling").GetValue(viewRecall), "a view never channels itself");
                Assert.AreEqual(0, told.Lines.FindAll(l => l.body == view).Count, "and tells nobody");
                RecallShowLink.Play(view.transform, RecallShowKind.Begun, 5f, true);   // a show played on a view: no game feel here, no error

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                if (town != null) Object.Destroy(town);
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B40_TheHallSandbox_StaysOfflineOnly_AViewAndAServerBodyRefuseIt_AsNgoRefusesAJoinedClient()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ClientView));
            GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(401));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(402));
            for (int i = 0; i < 5; i++) yield return null;
            foreach (GameObject body in new[] { view, server })
            {
                Component housing = body.GetComponent("PlayerHousing");
                if (housing == null) continue;   // a shape that strips it cannot enter at all
                Type type = housing.GetType();
                string reason = (string)type.GetMethod("Enter").Invoke(housing, null);
                Assert.AreEqual(type.GetField("ReasonHostOnly").GetValue(null), reason, body.name + " refuses the sandbox (NGO's joined-client line)");
                Assert.IsFalse((bool)type.GetProperty("InHome").GetValue(housing), body.name + " never builds a Hall");
            }
            Assert.IsNotNull(view.GetComponent("PlayerHousing"), "the own view keeps PlayerHousing (its windows ask it)");
        }

        [UnityTest]
        public IEnumerator B41_EveryPlayersHp_ReachesTheOtherPlayersViews_FromTheGhost_AndOnlyThere()
        {
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ClientView));
            Assert.AreEqual(12346, VitalsServerSystem.ToMilli(12.3456f), "thousandths, as OwnerFlux");
            Assert.AreEqual(0, VitalsServerSystem.ToMilli(-3f), "never below 0");

            // another player's view: NGO's client took the server's HP (netStandaloneHealth); a fall to 0 is its Died
            GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(411));
            yield return null;
            var entity = remote.GetComponent<KryticalNova.Progression.CombatEntity>();
            Assert.IsNotNull(entity, "another player's view keeps CombatEntity (nameplates, targeting)");
            int changed = 0, died = 0;
            entity.HealthChanged += () => changed++;
            entity.Died += () => died++;
            entity.MirrorHealth(300f, 500f);
            Assert.AreEqual(300f, entity.CurrentHealth, 1e-3);
            Assert.AreEqual(500f, entity.MaxHealth, 1e-3);
            Assert.AreEqual(1, changed, "its nameplate is told");
            entity.MirrorHealth(300f, 500f);
            Assert.AreEqual(1, changed, "the same HP again changes nothing");
            entity.MirrorHealth(0f, 500f);
            Assert.IsFalse(entity.IsAlive);
            Assert.AreEqual(1, died, "the fall to 0 is its death");

            // a player already down when this view arrives is no new death
            GameObject late = Make(NetRoleKind.ClientView, false, new PlayerKey(412));
            yield return null;
            var lateEntity = late.GetComponent<KryticalNova.Progression.CombatEntity>();
            int lateDied = 0;
            lateEntity.Died += () => lateDied++;
            lateEntity.MirrorHealth(0f, 500f);
            Assert.AreEqual(0, lateDied);

            // the own view's HP is its PlayerResources' (OwnerFlux, M3): untouched
            GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(413));
            yield return null;
            var ownEntity = own.GetComponent<KryticalNova.Progression.CombatEntity>();
            float hp = ownEntity.CurrentHealth;
            ownEntity.MirrorHealth(1f, 2f);
            Assert.AreEqual(hp, ownEntity.CurrentHealth, 1e-3, "the own view keeps its own");
        }

        [UnityTest]
        public IEnumerator B42_AStrike_LandsAsNgosServerStrike_OnAFoe_AndOnAPlayerOnlyWhenHostile()
        {
            // M4.4e: the entity side decides when and whether in reach; the server body applies the strike as NGO's
            // ExecuteAuthoritativeStrike does (hostility, the step's multiplier, ResolveHit, next-hit buffs)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject attacker = Make(NetRoleKind.ServerBody, false, new PlayerKey(421));
            GameObject other = Make(NetRoleKind.ServerBody, false, new PlayerKey(422));
            var foeObject = new GameObject("B42 foe");
            m_Made.Add(foeObject);
            var foe = foeObject.AddComponent<CombatEntity>();
            yield return null;
            Assert.IsNotNull(attacker.GetComponent<KryticalNova.Combat.PlayerMeleeCombat>(), "the server body keeps PlayerMeleeCombat");

            Assert.IsTrue(foe.SetMaxHealthOverride(1000000f));
            int dealt = 0, lastStep = 0; bool lastSkill = true;
            attacker.GetComponent<KryticalNova.Combat.PlayerMeleeCombat>().HitDealt += (t, r, step, skill, label) => { dealt++; lastStep = step; lastSkill = skill; };
            float full = foe.CurrentHealth;
            bool landed = false;
            for (int i = 0; i < 20 && foe.CurrentHealth >= full; i++)
                landed |= KryticalNova.Combat.Entities.MeleeStrike.Apply(attacker, foe, 1 + i % 3, 1f + 0.15f * (i % 3));
            Assert.IsTrue(landed, "a non-player is always a foe: the strike is resolved");
            Assert.Less(foe.CurrentHealth, full, "NGO's ResolveHit took HP off it (an evasion now and then, never all 20)");
            Assert.Greater(dealt, 0, "M4.5b: each resolved hit is told with its step (the hit ring's one source)");
            Assert.IsFalse(lastSkill, "a basic strike is no skill");
            Assert.That(lastStep, Is.InRange(1, 3));

            var player = other.GetComponent<CombatEntity>();
            float before = player.CurrentHealth;
            bool hostile = KryticalNova.Combat.CombatHostility.IsHostile(attacker.transform, player);
            bool struck = KryticalNova.Combat.Entities.MeleeStrike.Apply(attacker, player, 1, 1f);
            Assert.AreEqual(hostile, struck, "another player is struck exactly when NGO's hostility rule says so");
            if (!hostile) Assert.AreEqual(before, player.CurrentHealth, 1e-3, "a friend is never struck");
            Assert.IsFalse(KryticalNova.Combat.Entities.MeleeStrike.Apply(attacker, null, 1, 1f), "no target, nothing");

            // M4.4f: the attack ORDER is checked as NGO's RequestAttackEntityServerRpc does (exists, alive, hostile)
            Assert.IsTrue(KryticalNova.Combat.Entities.MeleeResolveServerSystem.AttackAllowed(attacker, foe), "a live foe may be attacked");
            Assert.AreEqual(hostile, KryticalNova.Combat.Entities.MeleeResolveServerSystem.AttackAllowed(attacker, player), "a player only when hostile");
            Assert.IsFalse(KryticalNova.Combat.Entities.MeleeResolveServerSystem.AttackAllowed(attacker, null), "no target");
            // a Strike Dummy never runs out (owner, 24 Oct): a target that is down is a stand-in
            var down = new DownTarget(foeObject.transform);
            Assert.IsFalse(KryticalNova.Combat.Entities.MeleeResolveServerSystem.AttackAllowed(attacker, down), "a dead target cannot be attacked");
            Assert.IsFalse(KryticalNova.Combat.Entities.MeleeStrike.Apply(attacker, down, 1, 1f), "nor struck");
            Assert.AreEqual(0, down.Taken, "and takes nothing");
        }

        /// <summary>A target that is already down (B42).</summary>
        sealed class DownTarget : IDamageable
        {
            public DownTarget(Transform at) { Transform = at; }
            public int Taken;
            public string EntityName => "Down";
            public bool IsAlive => false;
            public float CurrentHealth => 0f;
            public float MaxHealth => 100f;
            public Transform Transform { get; }
            public CharacterCombatStats CombatStats => null;
            public KryticalNova.Core.DamageResult TakeDamage(KryticalNova.Core.DamageResult damage, CharacterCombatStats attacker) { Taken++; return damage; }
        }

        [UnityTest]
        public IEnumerator B43_AStanceCastOnTheServerBody_ReachesTheSquad_NeverStacksLower_AndTheMembersOwnViewShowsNgosLine()
        {
            // M4.5g: NGO's ShareWithSquad / ServerReceiveSquadBuff ran only under IsSpawned && IsServer; a server body decides too,
            // and the member's clients are told through the ghost (SquadBuffGiven -> the SquadBuffEvent ring)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            PartyOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(431).Value, "43a1000000000000000000000000000a", "c4310000000000000000000000000431", "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(432).Value, "43b1000000000000000000000000000b", "c4320000000000000000000000000432", "Brannoc");
                GameObject c = Admitted(PlayerKeys.Mint(433).Value, "43c1000000000000000000000000000c", "c4330000000000000000000000000433", "Corvane");
                for (int i = 0; i < 30; i++) yield return null;
                a.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.InviteByName, "Brannoc", false);
                b.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.Respond, "", true);
                Assert.AreNotEqual(0UL, b.GetComponent<IBodyParty>().PartyId, "planned: A and B are one Squad, C is in none");

                var skillsA = a.GetComponent<PlayerCombatSkills>();
                var skillsB = b.GetComponent<PlayerCombatSkills>();
                var skillsC = c.GetComponent<PlayerCombatSkills>();
                var givenB = new List<string>();
                var givenC = new List<string>();
                skillsB.SquadBuffGiven += (id, rank, giver) => givenB.Add($"{id} {rank} {giver}");
                skillsC.SquadBuffGiven += (id, rank, giver) => givenC.Add($"{id} {rank} {giver}");
                skillsA.GmSetAllRanksToMax();
                a.GetComponent<PlayerGmState>().SetInfiniteChi(true);

                // A casts a Stance of its kit (max rank, at or past the share rank 20) on its server body, as the Cast request does
                SkillDefinitionSO stance = null;
                var refused = new List<string>();
                foreach (SkillDefinitionSO s in skillsA.AvailableSkills)
                {
                    if (s == null || !s.sharesWithSquad || s.IsAttack || !skillsA.IsInActiveKit(s)) continue;
                    if (skillsA.ServerTryCast(s, null, out string reason)) { stance = s; break; }
                    refused.Add($"{s.skillId}: {reason}");
                }
                Assert.IsNotNull(stance, "a shared Stance A can cast: " + string.Join(" | ", refused));
                int rankA = skillsA.GetRank(stance);
                Assert.GreaterOrEqual(rankA, skillsA.SquadShareRank, "planned: max rank is past the share rank");

                // B (in reach, alive) is given it with the giver's name, and its clients are told; C is not
                CollectionAssert.AreEqual(new[] { $"{stance.skillId} {rankA} Talwyn" }, givenB, "B's server body says it was given");
                Assert.IsEmpty(givenC, "C is in no Squad");
                ActiveBuff onB = b.GetComponent<CharacterBuffManager>().GetBuff(stance.skillId);
                Assert.IsNotNull(onB, "B's server body has the Stance");
                Assert.IsTrue(onB.FromSquadmate);
                Assert.AreEqual("Talwyn", onB.Giver);
                Assert.AreEqual(rankA, onB.Rank);
                Assert.IsNull(c.GetComponent<CharacterBuffManager>().GetBuff(stance.skillId), "C has nothing");

                // given again at a lower rank, it never stacks and keeps the higher one (SquadBuffRules.RankAfterRecast)
                skillsB.ServerReceiveSquadBuff(stance.skillId, 1, "Corvane");
                Assert.AreEqual($"{stance.skillId} {rankA} Corvane", givenB[givenB.Count - 1], "the higher rank stays");
                Assert.AreEqual(rankA, b.GetComponent<CharacterBuffManager>().GetBuff(stance.skillId).Rank);

                // B's own view: its copy of the buff and NGO's line (SquadBuffClientRpc's body, played by SquadBuffPresentSystem)
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(b.GetComponent<BodyRoleTag>().Key.Value));
                yield return null;
                var viewSkills = view.GetComponent<PlayerCombatSkills>();
                // in the game the faction comes with the look and PlayerWeaponClass.Refresh sets its skills; this bare view has the prefab's
                viewSkills.SetAvailableSkills(new List<SkillDefinitionSO>(skillsB.AvailableSkills).ToArray());
                var lines = new List<string>();
                viewSkills.SkillMessage += lines.Add;
                viewSkills.ShowSquadBuff(stance.skillId, rankA, "Talwyn");
                CollectionAssert.AreEqual(new[] { $"Talwyn gave you {stance.displayName} (Rank {rankA})." }, lines, "NGO's words on the own view");
                // M4.5i: the copy comes with the server's running list, the one source of a view's buffs
                var viewBuffs = view.GetComponent<CharacterBuffManager>();
                Assert.IsNull(viewBuffs.GetBuff(stance.skillId), "the line adds no copy of its own");
                var running = new List<RunningBuffEntry>();
                BuffMirror.Capture(b.GetComponent<CharacterBuffManager>(), running);
                BuffMirror.Reconcile(viewBuffs, running, viewSkills.GetSkill);
                ActiveBuff onView = viewBuffs.GetBuff(stance.skillId);
                Assert.IsNotNull(onView, "the view's copy of the buff");
                Assert.IsTrue(onView.FromSquadmate);
                Assert.AreEqual(rankA, onView.Rank);
                viewSkills.ShowSquadBuff(stance.skillId, rankA, "");
                Assert.AreEqual($"A Squad member gave you {stance.displayName} (Rank {rankA}).", lines[lines.Count - 1], "NGO's words without a giver");

                foreach (GameObject body in new[] { a, b, c }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B44_AnotherPlayersView_KeepsTheCombatParts_PlaysCastsChargesSwingsImpactsAndBuffs_AndDecidesNothing()
        {
            // M4.5h (owner pick A, copy NGO): another player's view keeps the parts an NGO client had, so what M4.4k / M4.4h / M4.5c / M4.5f /
            // M4.5g play reaches it; and on it they decide nothing (HP, Flux, casts, buffs, ranks are the server's)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IGameFeelSink oldFeel = ClientPresentationRegistry.Feel;
            var feel = new FeelSeen();
            ClientPresentationRegistry.RegisterFeel(feel);
            try
            {
                PlayerKeys.Reset();
                GameObject server = Admitted(PlayerKeys.Mint(441).Value, "44a1000000000000000000000000000a", "c4410000000000000000000000000441", "Talwyn");
                GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(442));
                GameObject struck = Make(NetRoleKind.ClientView, false, new PlayerKey(443));
                for (int i = 0; i < 30; i++) yield return null;

                // the parts, as an NGO client had them; the agent stays, switched off
                foreach (string part in new[] { "PlayerCombatSkills", "PlayerSkillAttacks", "PlayerMeleeCombat", "PlayerResources", "CharacterBuffManager", "CharacterCombatStats" })
                    Assert.IsNotNull(remote.GetComponent(part), part + " stays on another player's view");
                AssertAgentsOff(remote);
                CollectionAssert.IsEmpty(LocalStaticsOn(remote), "no X.Local points at another player's body");
                var skills = remote.GetComponent<PlayerCombatSkills>();
                var resources = remote.GetComponent<PlayerResources>();
                var buffs = remote.GetComponent<CharacterBuffManager>();
                var attacks = remote.GetComponent<KryticalNova.Combat.PlayerSkillAttacks>();
                var melee = remote.GetComponent<KryticalNova.Combat.PlayerMeleeCombat>();
                var entity = remote.GetComponent<CombatEntity>();
                Assert.IsFalse(skills.HasAuthority, "never the skills' authority");
                Assert.IsFalse(resources.HasAuthority, "never the pools' authority");
                // in the game the faction comes with the look and PlayerWeaponClass.Refresh sets its skills; this bare view has the prefab's
                skills.SetAvailableSkills(new List<SkillDefinitionSO>(server.GetComponent<PlayerCombatSkills>().AvailableSkills).ToArray());
                SkillDefinitionSO buff = null, channeled = null;
                foreach (SkillDefinitionSO s in skills.AvailableSkills)
                {
                    if (s == null || s.IsAttack) continue;
                    if (s.executionType == SkillExecutionType.Channeled) channeled ??= s;
                    else buff ??= s;
                }
                Assert.IsNotNull(buff, "planned: the faction has a buff");

                // the ghost's HP goes into its PlayerResources (as the own view's OwnerFlux); a fall to 0 is its death
                int died = 0;
                entity.Died += () => died++;
                entity.MirrorHealth(300f, 500f);
                Assert.AreEqual(300f, entity.CurrentHealth, 1e-3);
                Assert.AreEqual(300f, resources.Health, 1e-3);
                Assert.AreEqual(500f, entity.MaxHealth, 1e-3);

                // 1. a cast seen (M4.5c): the buff copy, the cast event and its presentation
                int casts = 0;
                skills.SkillCast += _ => casts++;
                var lines = new List<string>();
                skills.SkillMessage += lines.Add;
                skills.ShowCastSeen(buff.skillId, 5, null);
                Assert.AreEqual(1, casts, "the cast is played");
                Assert.IsNull(buffs.GetBuff(buff.skillId), "no copy from the cast itself: the running list brings it (M4.5i, B45)");
                Assert.IsTrue(feel.Calls.Contains("cast " + buff.PresentationId), "the cast's presentation: " + string.Join(" | ", feel.Calls));

                // 2. a charge seen (M4.5f)
                attacks.ShowChargeSeen(buff.skillId, struck.transform, true, false);
                attacks.ShowChargeSeen(buff.skillId, null, false, true);
                Assert.IsTrue(feel.Calls.Contains("charge " + buff.PresentationId), "the charge starts: " + string.Join(" | ", feel.Calls));
                Assert.IsTrue(feel.Calls.Contains("charged " + buff.PresentationId + " True"), "and ends");

                // 3. a Squad buff seen (M4.5g): no line (that is the receiver's own game), and no copy of its own (the running list's)
                skills.ShowSquadBuff(buff.skillId, 5, "Brannoc");
                Assert.IsNull(buffs.GetBuff(buff.skillId), "the copy is the running list's (M4.5i)");
                Assert.IsEmpty(lines, "no line on another player's view");

                // 4. a swing seen (M4.4k): strike 1's clip plays on its rig
                melee.ShowStrikeFromGhost(1);
                object rig = typeof(KryticalNova.Combat.PlayerMeleeCombat).GetField("animancer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(melee);
                var clip = (AnimationClip)typeof(KryticalNova.Combat.PlayerMeleeCombat).GetField("combo1Clip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(melee);
                Assert.IsNotNull(rig, "the view's swing rig");
                Assert.IsNotNull(clip, "the prefab's strike 1");
                Assert.IsTrue((bool)rig.GetType().GetMethod("IsPlayingClip", new[] { typeof(AnimationClip) }).Invoke(rig, new object[] { clip }), "strike 1 plays");

                // 5. an impact seen (M4.4h): its presentation on the struck body
                melee.PreviewImpact(struck.transform, 2, true);
                Assert.IsTrue(feel.Calls.Contains("hit " + struck.name + " 2 True"), "the impact: " + string.Join(" | ", feel.Calls));

                // it decides nothing: no hit, heal, refill or reset of its own, no regeneration, no channel ticks, no Flux spent
                float hp = resources.Health, chi = resources.Chi;
                resources.ApplyDamage(50f);
                resources.Heal(50f);
                resources.RestoreChi(50f);
                resources.RestoreAll();
                entity.ResetHealth();
                Assert.IsTrue(resources.TrySpendChi(0f));
                if (channeled != null) buffs.ApplyBuff(channeled, channeled.maxRank);
                else UnityEngine.Debug.Log("[B44] this faction has no channeled skill: its ticks are checked by the guards only");
                yield return new WaitForSeconds(1.3f);
                Assert.AreEqual(hp, resources.Health, 1e-3, "HP only from the ghost");
                Assert.AreEqual(chi, resources.Chi, 1e-3, "Flux only from the server");
                Assert.AreEqual(0, died);

                // no cast, no ranks, no Squad share of its own
                Assert.IsFalse(skills.ServerTryCast(buff, null, out _), "a cast is never made here");
                Assert.IsFalse(skills.TryCastSkill(buff, out _), "nor asked for from here");
                skills.GmSetAllRanksToMax();
                Assert.AreEqual(0, skills.GetRank(buff), "ranks are the server's");
                int given = 0;
                skills.SquadBuffGiven += (id, rank, giver) => given++;
                skills.ServerReceiveSquadBuff(buff.skillId, 5, "Brannoc");
                Assert.AreEqual(0, given, "a Squad buff is received by the server body only");

                // and the ghost still decides its HP: a fall to 0 is its death
                entity.MirrorHealth(0f, 500f);
                Assert.IsFalse(entity.IsAlive);
                Assert.AreEqual(1, died);

                ServerLeaving.Notify(server);
                server.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                if (oldFeel != null) ClientPresentationRegistry.RegisterFeel(oldFeel);
                else ClientPresentationRegistry.UnregisterFeel(feel);
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B45_EveryViewsBuffs_FollowTheServersRunningList_EarlyEndsAndALateViewMatch_AndNoCopyFights()
        {
            // M4.5i (owner pick A): the server's running buffs are one list; every view (own, another player's, one that arrives later)
            // keeps its copy equal to it, as NGO's cast / SquadBuff / BuffRemoved / ConsumeNextHit / SyncBuff messages did
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            try
            {
                PlayerKeys.Reset();
                ulong key = PlayerKeys.Mint(451).Value;
                GameObject server = Admitted(key, "45a1000000000000000000000000000a", "c4510000000000000000000000000451", "Talwyn");
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(key));
                GameObject remote = Make(NetRoleKind.ClientView, false, new PlayerKey(key));
                for (int i = 0; i < 30; i++) yield return null;

                var serverBuffs = server.GetComponent<CharacterBuffManager>();
                var serverSkills = server.GetComponent<PlayerCombatSkills>();
                SkillDefinitionSO shield = null, oneHit = null, plain = null;
                foreach (SkillDefinitionSO s in serverSkills.AvailableSkills)
                {
                    if (s == null || s.IsAttack || s.executionType == SkillExecutionType.Channeled) continue;
                    if (s.isAbsorptionShield) shield ??= s;
                    else if (s.consumedOnNextHit) oneHit ??= s;
                    else plain ??= s;
                }
                Assert.IsNotNull(shield, "planned: a shield (Heavenly Shield) in the faction's skills");
                Assert.IsNotNull(oneHit, "planned: a one-hit buff (Spiritual Rage)");
                Assert.IsNotNull(plain, "planned: a plain buff");
                SkillDefinitionSO[] kit = new List<SkillDefinitionSO>(serverSkills.AvailableSkills).ToArray();
                var ownSkills = own.GetComponent<PlayerCombatSkills>();
                var remoteSkills = remote.GetComponent<PlayerCombatSkills>();
                ownSkills.SetAvailableSkills(kit);
                remoteSkills.SetAvailableSkills(kit);
                var ownBuffs = own.GetComponent<CharacterBuffManager>();
                var remoteBuffs = remote.GetComponent<CharacterBuffManager>();
                var running = new List<RunningBuffEntry>();
                bool Sync(CharacterBuffManager copy, PlayerCombatSkills skills)
                {
                    BuffMirror.Capture(serverBuffs, running);   // what RunningBuffsServerSystem writes on the ghost
                    return BuffMirror.Reconcile(copy, running, skills.GetSkill);   // what RunningBuffsPresentSystem does on each view
                }

                // 1. the server's buffs reach both views, ranks, Squad giver and clocks included
                serverBuffs.ApplyBuff(shield, 5);
                serverBuffs.ApplyBuff(oneHit, 5);
                serverBuffs.ApplyBuff(plain, 5, -1f, true, "Brannoc");
                Assert.IsTrue(Sync(ownBuffs, ownSkills));
                Assert.IsTrue(Sync(remoteBuffs, remoteSkills));
                AssertSameBuffs(serverBuffs, ownBuffs, "own view");
                AssertSameBuffs(serverBuffs, remoteBuffs, "another player's view");

                // the same list again changes nothing, and the own view's cast reply never touches the bar: no flicker
                int barChanges = 0;
                ownBuffs.BuffsChanged += () => barChanges++;
                Assert.IsFalse(Sync(ownBuffs, ownSkills), "nothing to do");
                ownSkills.ShowServerCast(plain.skillId, 5);
                Assert.AreEqual(0, barChanges, "one source: the reply adds no second copy");

                // 2. a one-hit buff spent on the server (NGO ConsumeNextHitBuffsClientRpc)
                Assert.IsTrue(serverBuffs.ConsumeNextHitBuffs());
                Sync(ownBuffs, ownSkills);
                Sync(remoteBuffs, remoteSkills);
                Assert.IsNull(ownBuffs.GetBuff(oneHit.skillId), "spent on the own view");
                Assert.IsNull(remoteBuffs.GetBuff(oneHit.skillId), "and on another player's");
                AssertSameBuffs(serverBuffs, remoteBuffs, "after the hit");

                // 3. the shield broken on the server (NGO BuffRemovedClientRpc)
                serverBuffs.AbsorbDamage(1e9f);
                Assert.IsNull(serverBuffs.GetBuff(shield.skillId), "planned: the blow breaks the shield");
                Sync(ownBuffs, ownSkills);
                Sync(remoteBuffs, remoteSkills);
                Assert.IsNull(ownBuffs.GetBuff(shield.skillId));
                Assert.IsNull(remoteBuffs.GetBuff(shield.skillId));
                AssertSameBuffs(serverBuffs, ownBuffs, "after the shield broke");

                // 4. a view that arrives later gets what still runs, with its time left (NGO SyncBuffClientRpc)
                yield return new WaitForSeconds(1f);
                GameObject late = Make(NetRoleKind.ClientView, false, new PlayerKey(key));
                yield return null;
                var lateSkills = late.GetComponent<PlayerCombatSkills>();
                lateSkills.SetAvailableSkills(kit);
                var lateBuffs = late.GetComponent<CharacterBuffManager>();
                Sync(lateBuffs, lateSkills);
                AssertSameBuffs(serverBuffs, lateBuffs, "a late view");
                Assert.Less(lateBuffs.GetBuff(plain.skillId).RemainingDuration, lateBuffs.GetBuff(plain.skillId).MaxDuration - 0.5f, "its time left, not a fresh one");

                // 5. death clears them all on the server (PlayerDeathMotor's ClearAllBuffs): every copy follows
                serverBuffs.ClearAllBuffs();
                foreach (var (c, k) in new[] { (ownBuffs, ownSkills), (remoteBuffs, remoteSkills), (lateBuffs, lateSkills) }) Sync(c, k);
                Assert.IsEmpty(ownBuffs.ActiveBuffs);
                Assert.IsEmpty(remoteBuffs.ActiveBuffs);
                Assert.IsEmpty(lateBuffs.ActiveBuffs);

                ServerLeaving.Notify(server);
                server.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B46_ADeathOnTheServerBody_CountsDownAndRespawns_AHostileTakesXpOnce_APlayerKillNone_AndAViewNeverDecides()
        {
            // M4 step 6a: NGO's PlayerDeathMotor where it decides, on a server body (the part stays switched off: the ghost moves bodies)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            int deathsOfA = 0;
            CombatEntity watched = null;
            Action<CombatEntity, KillingBlow> count = (victim, blow) => { if (victim == watched) deathsOfA++; };
            CombatEntity.AnyKilled += count;
            ScriptableObject gaps = null;
            try
            {
                PlayerKeys.Reset();
                var spawn = new GameObject("B46 spawn service");
                m_Made.Add(spawn);
                spawn.AddComponent<KryticalNova.Networking.PlayerSpawnService>();
                var point = new GameObject("B46 spawn point");
                point.transform.SetParent(spawn.transform);
                point.transform.position = new Vector3(20f, 0f, 0f);
                point.AddComponent<KryticalNova.Networking.PlayerSpawnPoint>();

                ulong keyA = PlayerKeys.Mint(461).Value;
                GameObject a = Admitted(keyA, "46a1000000000000000000000000000a", "c4610000000000000000000000000461", "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(462).Value, "46b1000000000000000000000000000b", "c4620000000000000000000000000462", "Brannoc");
                var mover = new MoverSeen();
                a.GetComponent<BodyRoleTag>().Mover = mover;
                for (int i = 0; i < 30; i++) yield return null;

                var entity = a.GetComponent<CombatEntity>();
                watched = entity;
                var resources = a.GetComponent<PlayerResources>();
                var progression = a.GetComponent<PlayerCharacterProgression>();
                var death = a.GetComponent<PlayerDeathMotor>();
                Assert.IsNotNull(death, "the server body keeps the death motor");
                Assert.IsFalse(death.enabled, "switched off there: the ghost moves bodies");
                int required = progression.ExperienceRequired;
                Assert.Greater(required, 0, "planned: a level with an XP bar");
                progression.SetExperience(required / 2);

                // 1. killed by another player (a Callout's opponent, another faction): it falls, the countdown starts, no XP is lost
                int xpBefore = progression.Experience;
                entity.Kill(new KillingBlow { Attacker = b.GetComponent<CharacterCombatStats>(), Direction = Vector3.forward, IsCritical = true });
                Assert.IsTrue(death.IsDead, "down");
                Assert.IsFalse(entity.IsAlive);
                Assert.AreEqual(1, deathsOfA, "one kill");
                Assert.AreEqual(xpBefore, progression.Experience, "a player's kill costs no XP (NGO: only a Hostile's)");
                Assert.AreNotEqual(0, death.ServerBodyPose, "a pose for the clients");
                Assert.IsTrue(death.ServerBodyCritical);
                Assert.AreEqual(b.transform, death.ServerBodyKiller);
                entity.Kill(new KillingBlow { Direction = Vector3.forward });
                Assert.AreEqual(1, deathsOfA, "the dead die once");
                yield return null;   // NGO's chat lines go out in PlayerChat.LateUpdate
                Assert.IsTrue(told.Any(a, "line Combat : Brannoc defeated you with a critical blow."), "NGO's line: " + string.Join(" | ", told.To(a)));
                Assert.IsTrue(told.Any(b, "line Combat : You defeated Talwyn with a critical blow!"), "the killer's: " + string.Join(" | ", told.To(b)));
                Assert.IsTrue(told.Any(a, "line Combat : Respawning in 8 seconds."));

                // the clock: 3, 2, 1, then up on the map's spawn point (the server's place path), full HP and Flux
                for (int i = 0; i < 18; i++) death.ServerBodyTick(0.5f);
                yield return null;
                foreach (string line in new[] { "Respawning in 3 seconds.", "Respawning in 2 seconds.", "Respawning in 1 second.", "You are back on your feet." })
                    Assert.IsTrue(told.Any(a, "line Combat : " + line), line + ": " + string.Join(" | ", told.To(a)));
                Assert.IsFalse(death.IsDead, "up");
                Assert.AreEqual(resources.MaxHealth, resources.Health, 1e-3, "full HP");
                Assert.AreEqual(1, mover.Teleports, "put on a spawn point");
                Assert.AreEqual(0, death.ServerBodyPose);

                // 2. killed by a Hostile: 5 % of the level's XP, once (NGO HostileKillRewards), never a level
                var hostile = new GameObject("B46 hostile");
                hostile.SetActive(false);   // its Awake never runs: only what the reward reads is needed
                m_Made.Add(hostile);
                Type hostileType = Type.GetType("KryticalNova.Hostiles.HostileNet, KryticalNova.Hostiles");
                Type gapsType = Type.GetType("KryticalNova.Hostiles.LevelGapSettings, KryticalNova.Hostiles");
                Type rulesType = Type.GetType("KryticalNova.Hostiles.LevelGapRules, KryticalNova.Hostiles");
                Assert.IsNotNull(hostileType);
                Assert.IsNotNull(gapsType);
                Assert.IsNotNull(rulesType);
                Component net = hostile.AddComponent(hostileType);
                gaps = ScriptableObject.CreateInstance(gapsType);   // the code's 5 %, as LevelGapSettings.asset
                hostileType.GetField("levelGap", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(net, gaps);
                var hostileStats = hostile.GetComponent<CharacterCombatStats>();
                if (hostileStats == null) hostileStats = hostile.AddComponent<CharacterCombatStats>();
                int xpFull = progression.Experience;
                int loss = (int)rulesType.GetMethod("DeathLoss").Invoke(null, new object[] { gaps, progression.ExperienceRequired, xpFull });
                Assert.Greater(loss, 0, "planned: a loss to see");
                entity.Kill(new KillingBlow { Attacker = hostileStats, Direction = Vector3.back });
                Assert.AreEqual(2, deathsOfA);
                Assert.AreEqual(xpFull - loss, progression.Experience, "the loss, once");
                Assert.AreEqual(1, told.To(a).FindAll(l => l == $"system You lost {loss} XP for falling to a Hostile.").Count, "told once: " + string.Join(" | ", told.To(a)));

                // 3. a view never decides a death: no Killed / AnyKilled there, so nothing is rewarded or taken twice
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(keyA));
                GameObject theirs = Make(NetRoleKind.ClientView, false, new PlayerKey(keyA));
                yield return null;
                var ownEntity = own.GetComponent<CombatEntity>();
                var theirEntity = theirs.GetComponent<CombatEntity>();
                watched = ownEntity;
                ownEntity.Kill(new KillingBlow { Attacker = hostileStats, Direction = Vector3.back });
                theirEntity.MirrorHealth(500f, 500f);
                theirEntity.MirrorHealth(0f, 500f);
                watched = theirEntity;
                theirEntity.Kill(new KillingBlow { Attacker = hostileStats, Direction = Vector3.back });
                Assert.AreEqual(2, deathsOfA, "no kill decided on a view");
                Assert.AreEqual(xpFull - loss, progression.Experience, "and no XP taken there");
                watched = entity;

                // 4. HP given back while down (a GM heal): up where it lies, no spawn point (NGO Update -> Revive(false))
                Assert.IsTrue(death.IsDead);
                resources.RestoreAll();
                death.ServerBodyTick(0.1f);
                Assert.IsFalse(death.IsDead, "up in place");
                Assert.AreEqual(1, mover.Teleports, "not moved");

                foreach (GameObject body in new[] { a, b }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                CombatEntity.AnyKilled -= count;
                if (gaps != null) Object.Destroy(gaps);
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B47_EveryView_PlaysTheDeathFromTheGhost_FallLieGetUp_ALateViewLiesDown_AndNoViewMovesTheBodyOrDecides()
        {
            // M4 step 6b: NGO's DeathClientRpc / DisintegrateClientRpc / RevivedClientRpc and the late joiner's netPose, played on every view
            // from the ghost's DeathState (PlayerDeathPresentSystem); the motor stays on there and never warps, turns or respawns the body
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ClientView));
            IGameFeelSink oldFeel = ClientPresentationRegistry.Feel;
            var feel = new FeelSeen();
            ClientPresentationRegistry.RegisterFeel(feel);
            try
            {
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(471));
                GameObject theirs = Make(NetRoleKind.ClientView, false, new PlayerKey(472));
                own.name = "B47 own";
                theirs.name = "B47 theirs";
                for (int i = 0; i < 30; i++) yield return null;

                var chainType = typeof(PlayerDeathMotor).GetNestedType("DeathChain");
                AnimationClip Clip(PlayerDeathMotor m, string chain, string which) =>
                    (AnimationClip)chainType.GetField(which).GetValue(typeof(PlayerDeathMotor).GetField(chain, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(m));
                bool Playing(GameObject body, AnimationClip clip)
                {
                    object rig = typeof(PlayerAnimancerMotor).GetProperty("Animancer").GetValue(body.GetComponent<PlayerAnimancerMotor>());
                    return (bool)rig.GetType().GetMethod("IsPlayingClip", new[] { typeof(AnimationClip) }).Invoke(rig, new object[] { clip });
                }

                foreach (GameObject view in new[] { own, theirs })
                {
                    var death = view.GetComponent<PlayerDeathMotor>();
                    Assert.IsNotNull(death, view.name + " keeps the death motor");
                    Assert.IsTrue(death.enabled, view.name + ": on, it plays the death on its own clock");
                    bool mine = view == own;
                    Vector3 at = view.transform.position;
                    Quaternion facing = view.transform.rotation;

                    // the fall: a critical blow from behind (face down), killed by the other view's player
                    GameObject killer = mine ? theirs : own;
                    death.ShowDeathFromGhost(2, true, Vector3.forward, killer.transform);
                    Assert.IsTrue(death.IsDead, view.name + " is down");
                    Assert.IsTrue(feel.Calls.Contains($"death {view.name} True {mine} {!mine}"), view.name + ": " + string.Join(" | ", feel.Calls));
                    AnimationClip fall = Clip(death, "critBack", "fall");
                    Assert.IsTrue(Playing(view, fall), view.name + ": the knockdown plays");
                    death.ShowDeathFromGhost(2, true, Vector3.forward, killer.transform);
                    Assert.AreEqual(1, feel.Calls.FindAll(c => c.StartsWith($"death {view.name} ")).Count, "once");

                    // the clock lays it down; the body stays where the ghost has it (no warp, no turn on a view)
                    yield return new WaitForSeconds(fall.length + 0.7f);
                    Assert.IsTrue(Playing(view, Clip(death, "critBack", "loop")), view.name + ": lying there");
                    Assert.AreEqual(at, view.transform.position, view.name + ": not moved by the view");
                    Assert.AreEqual(facing, view.transform.rotation, view.name + ": not turned by the view");

                    // a view never decides the respawn: only the ghost's return gets it up
                    death.Respawn();
                    Assert.IsTrue(death.IsDead, view.name + ": no respawn of its own");
                    death.ShowRevivedFromGhost();
                    Assert.IsFalse(death.IsDead, view.name + " is up");
                    Assert.IsTrue(feel.Calls.Contains($"respawned {view.name} {mine}"), view.name + ": " + string.Join(" | ", feel.Calls));
                }

                // the de-rez (killed off the ground) on another player's view
                var theirDeath = theirs.GetComponent<PlayerDeathMotor>();
                theirDeath.ShowDeathFromGhost(3, false, Vector3.left, null);
                Assert.IsTrue(theirDeath.IsDead);
                Assert.IsTrue(feel.Calls.Contains("derez B47 theirs"), string.Join(" | ", feel.Calls));
                theirDeath.ShowRevivedFromGhost();
                Assert.IsTrue(feel.Calls.Contains("hidden B47 theirs False"), "back from the de-rez");

                // views that arrive while the player is down: lying there without the fall, or gone
                GameObject late = Make(NetRoleKind.ClientView, false, new PlayerKey(473));
                GameObject lateGone = Make(NetRoleKind.ClientView, false, new PlayerKey(474));
                late.name = "B47 late";
                lateGone.name = "B47 late gone";
                yield return null;
                var lateDeath = late.GetComponent<PlayerDeathMotor>();
                lateDeath.ShowLyingFromGhost(1);
                Assert.IsTrue(lateDeath.IsDead);
                Assert.IsTrue(Playing(late, Clip(lateDeath, "front", "loop")), "on its back, lying");
                Assert.IsFalse(feel.Calls.Exists(c => c.StartsWith("death B47 late")), "no fall for a late view");
                var goneDeath = lateGone.GetComponent<PlayerDeathMotor>();
                goneDeath.ShowLyingFromGhost(3);
                Assert.IsTrue(goneDeath.IsDead);
                Assert.IsTrue(feel.Calls.Contains("hidden B47 late gone True"), "de-rezzed: not shown");
            }
            finally
            {
                if (oldFeel != null) ClientPresentationRegistry.RegisterFeel(oldFeel);
                else ClientPresentationRegistry.UnregisterFeel(feel);
            }
        }

        [UnityTest]
        public IEnumerator B48_TheServerBodysDeath_PicksNgosChain_LandsAlongTheBlowWithinItsReach_AndFacesOnlyABlowFromTheFront()
        {
            // M4 6b-2: the knock's numbers come from the real body (FindLanding, Pick, the facing), and the prefab's chains bake
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(481).Value, "48a1000000000000000000000000000a", "c4810000000000000000000000000481", "Talwyn");
                a.GetComponent<BodyRoleTag>().Mover = new MoverSeen();
                for (int i = 0; i < 30; i++) yield return null;
                var death = a.GetComponent<PlayerDeathMotor>();
                var entity = a.GetComponent<CombatEntity>();
                for (int chain = 0; chain < 4; chain++)
                    Assert.IsTrue(death.TryReadKnockChain(chain, out _, out float length, out _) && length > 0f, "the prefab's chain " + chain + " is set up");

                // a critical blow from the front: chain 2, thrown along the blow at most 5.5 m (NGO critDistance), turned to face it
                Vector3 start = a.transform.position;
                float startYaw = a.transform.eulerAngles.y;
                Vector3 blow = -a.transform.forward;   // travelling toward the body's front: from the front
                entity.Kill(new KillingBlow { Direction = blow, IsCritical = true });
                Assert.IsTrue(death.IsDead);
                Assert.AreEqual(2, death.ServerBodyChain, "critical, from the front");
                Vector3 travel = death.ServerBodyLanding - start;
                travel.y = 0f;
                Assert.LessOrEqual(travel.magnitude, 5.5f + 0.01f, "within the critical reach");
                if (travel.magnitude > 0.1f) Assert.Greater(Vector3.Dot(travel.normalized, blow.normalized), 0.99f, "along the blow");
                Assert.AreEqual(Quaternion.LookRotation(-blow, Vector3.up).eulerAngles.y, death.ServerBodyFacing, 0.5f, "facing the blow");
                if (death.ServerBodyPose != 3) Assert.IsTrue(death.ServerBodyKnocks, "a fall travels");

                // up again; a plain blow from behind: chain 1, the facing kept
                a.GetComponent<PlayerResources>().RestoreAll();
                death.ServerBodyTick(0.1f);
                Assert.IsFalse(death.IsDead);
                entity.Kill(new KillingBlow { Direction = a.transform.forward, IsCritical = false });
                Assert.AreEqual(1, death.ServerBodyChain, "plain, from behind");
                Assert.AreEqual(a.transform.eulerAngles.y, death.ServerBodyFacing, 0.5f, "a blow from behind never turns the body");

                // the knock's numbers bake from the prefab for the worlds
                var curves = KryticalNova.Persistence.Entities.PlayerJumpFromSkills.KnockCurvesOf(Prefab());
                Assert.IsTrue(curves.HasValue, "baked");
                Assert.AreEqual(550, curves.Value.CritLiftMm, 1, "NGO's critLift");
                Assert.Greater(curves.Value.LengthMicros(2), 0);

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B49_AWardVolley_IsAskedForAndCastOnTheServerBody_AreaSkillsNeverStrikeAView_AndEveryViewSeesTheSkillHit()
        {
            // M4 step 7: NGO's CastWardsServerRpc / WardCastClientRpc, the area overlap in this world only, SkillHitVfxClientRpc to everyone
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IGameFeelSink oldFeel = ClientPresentationRegistry.Feel;
            var feel = new FeelSeen();
            ClientPresentationRegistry.RegisterFeel(feel);
            try
            {
                PlayerKeys.Reset();
                ulong key = PlayerKeys.Mint(491).Value;
                GameObject a = Admitted(key, "49a1000000000000000000000000000a", "c4910000000000000000000000000491", "Talwyn");
                for (int i = 0; i < 30; i++) yield return null;
                var skills = a.GetComponent<PlayerCombatSkills>();
                skills.GmSetAllRanksToMax();
                a.GetComponent<PlayerGmState>().SetInfiniteChi(true);
                a.GetComponent<PlayerGmState>().SetNoCooldowns(true);
                var wards = new List<SkillDefinitionSO>();
                SkillDefinitionSO strike = null;
                foreach (SkillDefinitionSO s in skills.AvailableSkills)
                {
                    if (s == null || !skills.IsInActiveKit(s)) continue;
                    if (s.IsAttack) { if (!s.IsAreaAttack && strike == null) strike = s; }
                    else if (s.executionType != SkillExecutionType.Channeled && wards.Count < 2) wards.Add(s);
                }
                Assert.AreEqual(2, wards.Count, "planned: two wards in the kit");
                Assert.IsNotNull(strike, "planned: a single-target attack in the kit");
                string ids = wards[0].skillId + "|" + wards[1].skillId;

                // 1. the server body casts the volley its view asked for (NGO's RPC body), each ward once; again with both up: none
                var cast = new List<SkillDefinitionSO>();
                Assert.AreEqual(2, skills.RunWards(ids + "|" + strike.skillId + "|" + wards[0].skillId, cast), "both wards; attacks and repeats are dropped");
                CollectionAssert.AreEqual(wards, cast);
                Assert.IsNotNull(a.GetComponent<CharacterBuffManager>().GetBuff(wards[0].skillId));
                Assert.AreEqual(0, skills.RunWards(ids, cast), "already up: nothing to cast");

                // 2. the own view asks instead of casting (an NGO client's CastWardsServerRpc), and shows the server's answer
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(key));
                own.name = "B49 own";
                var asked = new Asked();
                own.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewSkills = own.GetComponent<PlayerCombatSkills>();
                viewSkills.SetAvailableSkills(new List<SkillDefinitionSO>(skills.AvailableSkills).ToArray());
                for (int i = 0; i < viewSkills.AvailableSkills.Count; i++)
                    viewSkills.MirrorRank(i, (byte)Mathf.Clamp(skills.GetRank(viewSkills.AvailableSkills[i]), 0, 255));
                own.GetComponent<PlayerResources>().MirrorChi(100000f, 100000f);
                Assert.AreEqual(2, viewSkills.TryCastWardsAtOnce(wards, out string why), "both asked for: " + why);
                CollectionAssert.AreEqual(new[] { "Wards " + ids }, asked.Ops, "sent to the server");
                Assert.IsFalse(viewSkills.IsOnCooldown(wards[0]), "nothing cast on the view itself");
                Assert.IsNull(own.GetComponent<CharacterBuffManager>().GetBuff(wards[0].skillId), "and no buff of its own");
                viewSkills.ShowWardCast(wards[0].skillId, 5, true);
                viewSkills.ShowWardCast(wards[1].skillId, 5, false);
                Assert.IsTrue(viewSkills.IsOnCooldown(wards[0]) && viewSkills.IsOnCooldown(wards[1]), "the server's cooldowns");
                Assert.AreEqual(1, feel.Calls.FindAll(c => c.StartsWith("cast ")).Count, "the volley shown as one cast: " + string.Join(" | ", feel.Calls));

                // 3. an area skill strikes bodies of its own world only: never a view sharing the physics scene
                var dummyObject = new GameObject("B49 dummy");
                m_Made.Add(dummyObject);
                var dummy = dummyObject.AddComponent<CombatEntity>();
                dummyObject.transform.position = a.transform.position + a.transform.forward * 1f;
                yield return null;
                Assert.IsTrue(KryticalNova.Combat.PlayerSkillAttacks.HitsHere(a.GetComponent<CombatEntity>()), "a server body");
                Assert.IsTrue(KryticalNova.Combat.PlayerSkillAttacks.HitsHere(dummy), "a dummy (no view tag)");
                Assert.IsFalse(KryticalNova.Combat.PlayerSkillAttacks.HitsHere(own.GetComponent<CombatEntity>()), "never a view");

                // 4. a skill hit on the server body is told to every view (NGO SkillHitVfxClientRpc), not drawn on the server body
                var landed = new List<string>();
                a.GetComponent<KryticalNova.Combat.PlayerSkillAttacks>().SkillHitShown += (id, at) => landed.Add(id);
                for (int attempt = 0; attempt < 6 && landed.Count == 0; attempt++)
                {
                    skills.ServerTryCast(strike, dummyObject.transform, out _);
                    for (float t = 0f; t < 2f && landed.Count == 0; t += Time.deltaTime) yield return null;
                }
                Assert.IsNotEmpty(landed, "the hit landed on the dummy and was told");
                Assert.AreEqual(strike.skillId, landed[0]);
                Assert.IsFalse(feel.Calls.Exists(c => c.StartsWith("skillhit " + a.name)), "not drawn by the server body");
                GameObject theirs = Make(NetRoleKind.ClientView, false, new PlayerKey(key));
                theirs.name = "B49 theirs";
                yield return null;
                theirs.GetComponent<PlayerCombatSkills>().SetAvailableSkills(new List<SkillDefinitionSO>(skills.AvailableSkills).ToArray());
                theirs.GetComponent<KryticalNova.Combat.PlayerSkillAttacks>().ShowSkillHitSeen(strike.skillId, dummyObject.transform.position);
                Assert.IsTrue(feel.Calls.Contains($"skillhit B49 theirs {strike.PresentationId}"), "a view plays it: " + string.Join(" | ", feel.Calls));

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                if (oldFeel != null) ClientPresentationRegistry.RegisterFeel(oldFeel);
                else ClientPresentationRegistry.UnregisterFeel(feel);
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B50_AnAreaSkill_HitsEveryBodyInReach_FortyWithEightyColliders_AndEachHitIsANumberForTheAttacker()
        {
            // M4 step 7 (owner, 5 Nov 2026): no cap on an area skill (NGO's 64-collider buffer cut crowds), every hit's number told
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            ICombatFeedbackSink oldFeedback = ClientPresentationRegistry.Feedback;
            var numbers = new NumbersSeen();
            ClientPresentationRegistry.RegisterFeedback(numbers);
            try
            {
                PlayerKeys.Reset();
                ulong key = PlayerKeys.Mint(501).Value;
                GameObject a = Admitted(key, "50a1000000000000000000000000000a", "c5010000000000000000000000000501", "Talwyn");
                for (int i = 0; i < 30; i++) yield return null;
                var skills = a.GetComponent<PlayerCombatSkills>();
                skills.GmSetAllRanksToMax();
                a.GetComponent<PlayerGmState>().SetInfiniteChi(true);
                a.GetComponent<PlayerGmState>().SetNoCooldowns(true);
                SkillDefinitionSO area = null;
                foreach (SkillDefinitionSO s in skills.AvailableSkills)
                    if (s != null && s.IsAreaAttack && skills.IsInActiveKit(s)) { area = s; break; }
                Assert.IsNotNull(area, "planned: an area skill in the kit");
                Assert.GreaterOrEqual(area.GetRange(skills.GetRank(area)), 3f, "planned: reach past the ring of bodies");

                // 40 bodies round the player, 2 m out, each with two colliders: 80 colliders, more than NGO's 64
                var bodies = new HashSet<Transform>();
                for (int i = 0; i < 40; i++)
                {
                    var body = new GameObject("B50 dummy " + i);
                    m_Made.Add(body);
                    float angle = i * Mathf.PI * 2f / 40f;
                    body.transform.position = a.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2f;
                    body.AddComponent<CapsuleCollider>();
                    body.AddComponent<BoxCollider>().size = new Vector3(0.3f, 0.3f, 0.3f);
                    body.AddComponent<CombatEntity>();
                    bodies.Add(body.transform);
                }
                Physics.SyncTransforms();
                yield return null;

                var struck = new HashSet<Transform>();
                int told = 0;
                var melee = a.GetComponent<KryticalNova.Combat.PlayerMeleeCombat>();
                melee.HitDealt += (target, result, step, fromSkill, label) => { if (target != null) struck.Add(target.Transform); };
                melee.DamageShown += (position, result, fromSkill) => told++;
                Assert.IsTrue(skills.ServerTryCast(area, null, out string why), "cast: " + why);
                for (float t = 0f; t < 4f && struck.Count < 40; t += Time.deltaTime) yield return null;
                Assert.AreEqual(40, struck.Count, "every body in reach is struck");
                Assert.IsTrue(struck.SetEquals(bodies));
                Assert.AreEqual(40, told, "one number for the attacker per hit");
                Assert.AreEqual(0, numbers.Shown, "none drawn by the server body itself");

                // the attacker's own view shows each number it is sent (HitNumberClientSystem -> ShowDamageFeedback)
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(key));
                yield return null;
                var ownMelee = own.GetComponent<KryticalNova.Combat.PlayerMeleeCombat>();
                for (int i = 0; i < 40; i++) ownMelee.ShowDamageFeedback(Vector3.zero, new DamageResult { DamageDealt = 1f + i }, true);
                Assert.AreEqual(40, numbers.Shown);

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                if (oldFeedback != null) ClientPresentationRegistry.RegisterFeedback(oldFeedback);
                else ClientPresentationRegistry.UnregisterFeedback(numbers);
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B51_ADeathOffSolidGround_DeRezzesWithNoKnock_AndOnFlatGroundItFalls()
        {
            // owner rule (5 Nov 2026): not on solid ground (the server reads a jump from its take-off, a wall, water, a vault from the
            // ghost: DeathGround, EditMode per case) = de-rez where it is, never a fall or a drop
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(511).Value, "51a1000000000000000000000000000a", "c5110000000000000000000000000511", "Talwyn");
                a.GetComponent<BodyRoleTag>().Mover = new MoverSeen();
                for (int i = 0; i < 30; i++) yield return null;
                var death = a.GetComponent<PlayerDeathMotor>();
                var entity = a.GetComponent<CombatEntity>();
                Assert.Less(death.FeetAboveGround(), 0.15f, "planned: the body stands on the ground (or no ground layer here)");

                // off solid ground (mid-jump, a wall, water, a vault: the ghost's word): gone where it is, no travel
                death.ServerBodyOffGround = true;
                entity.Kill(new KillingBlow { Direction = -a.transform.forward, IsCritical = true });
                Assert.IsTrue(death.IsDead);
                Assert.AreEqual(3, death.ServerBodyPose, "de-rezzed");
                Assert.IsFalse(death.ServerBodyKnocks, "no knock, no drop");

                a.GetComponent<PlayerResources>().RestoreAll();
                death.ServerBodyTick(0.1f);
                Assert.IsFalse(death.IsDead);

                // on flat ground (and in the crouch before a jump's take-off): the fall
                death.ServerBodyOffGround = false;
                entity.Kill(new KillingBlow { Direction = -a.transform.forward, IsCritical = false });
                Assert.IsTrue(death.IsDead);
                Assert.AreNotEqual(3, death.ServerBodyPose, "a fall");
                Assert.AreNotEqual(0, death.ServerBodyPose);

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B52_ACallout_IsDecidedOnTheServerBodies_AskAnswerCountdownFightAndEveryEnd_AsNgo_AndTheViewOnlyAsks()
        {
            // M4 step 8a: NGO's PlayerDuel RPC bodies on server bodies; the owner told by the link; NGO's lines and numbers
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            DuelOwnerLink.Current = told;
            try
            {
                PlayerKeys.Reset();
                ulong keyA = PlayerKeys.Mint(521).Value;
                GameObject a = Admitted(keyA, "52a1000000000000000000000000000a", "c5210000000000000000000000000521", "Talwyn");
                GameObject b = Admitted(PlayerKeys.Mint(522).Value, "52b1000000000000000000000000000b", "c5220000000000000000000000000522", "Brannoc");
                GameObject c = Admitted(PlayerKeys.Mint(523).Value, "52c1000000000000000000000000000c", "c5230000000000000000000000000523", "Corvane");
                for (int i = 0; i < 30; i++) yield return null;
                Assert.AreEqual(a.GetComponent<PlayerIdentity>().Faction, b.GetComponent<PlayerIdentity>().Faction, "planned: one faction (not enemies)");
                var da = a.GetComponent<IBodyDuel>();
                var db = b.GetComponent<IBodyDuel>();
                var dc = c.GetComponent<IBodyDuel>();
                Assert.IsNotNull(da, "the server body keeps PlayerDuel");
                bool Hostile(GameObject x, GameObject y) => KryticalNova.Combat.CombatHostility.IsHostile(x.transform, y.GetComponent<CombatEntity>());
                Assert.IsFalse(Hostile(a, b), "friends before");

                // the ask: NGO's refusal and success lines, B's prompt with NGO's 30 s
                da.RunDuelOp(DuelOpKind.Request, a.transform, false);
                Assert.IsTrue(told.Any(a, "system Select another player to call out."), string.Join(" | ", told.To(a)));
                da.RunDuelOp(DuelOpKind.Request, b.transform, false);
                Assert.IsTrue(told.Any(a, "system You called out Brannoc."));
                Assert.IsTrue(told.Any(b, "system Talwyn calls you out. Accept with /accept, refuse with /decline."));
                Assert.IsTrue(told.Any(b, "duel Prompt Talwyn 30 False"), string.Join(" | ", told.To(b)));
                dc.RunDuelOp(DuelOpKind.Request, b.transform, false);
                Assert.IsTrue(told.Any(c, "system Brannoc is answering another request."));

                // the answer: the countdown on both (NGO 3 s), then the bout; only the two are enemies
                db.RunDuelOp(DuelOpKind.Respond, null, true);
                Assert.IsTrue(told.Any(b, "duel PromptClosed"));
                Assert.IsTrue(told.Any(a, "duel Countdown Brannoc 3"));
                Assert.IsTrue(told.Any(b, "duel Countdown Talwyn 3"));
                Assert.IsTrue(told.Any(a, "system Callout with Brannoc starts in 3..."));
                yield return new WaitForSeconds(3.4f);
                Assert.IsTrue(told.Any(a, "system The callout with Brannoc has begun. It ends when one of you falls."), string.Join(" | ", told.To(a)));
                Assert.IsTrue(Hostile(a, b) && Hostile(b, a), "the two fight");
                Assert.IsFalse(Hostile(a, c), "nobody else");

                // A wins by the kill: both told, the win announced round the winner, friends again
                b.GetComponent<CombatEntity>().Kill(new KillingBlow { Attacker = a.GetComponent<CharacterCombatStats>(), Direction = Vector3.forward });
                Assert.IsTrue(told.Any(a, "duel Result Brannoc 0 True"));
                Assert.IsTrue(told.Any(b, "duel Result Talwyn 0 False"));
                Assert.IsTrue(told.Any(a, "system Talwyn won the callout against Brannoc."), string.Join(" | ", told.To(a)));
                Assert.IsFalse(Hostile(a, b), "over");

                // a forfeit, a third party's kill and a leaver end it as NGO's
                var duelA = a.GetComponent<KryticalNova.Combat.PlayerDuel>();
                var duelC = c.GetComponent<KryticalNova.Combat.PlayerDuel>();
                Assert.IsTrue(duelA.ServerForceStart(duelC), "the GM start");
                dc.RunDuelOp(DuelOpKind.Forfeit, null, false);
                Assert.IsTrue(told.Any(a, "system Corvane forfeited."));
                Assert.IsTrue(told.Any(a, "duel Result Corvane 0 True"));
                Assert.IsTrue(duelA.ServerForceStart(duelC));
                c.GetComponent<CombatEntity>().Kill(new KillingBlow { Direction = Vector3.forward });
                Assert.IsTrue(told.Any(a, "system The callout was interrupted."), "killed by someone else: no winner");
                c.GetComponent<PlayerResources>().RestoreAll();
                c.GetComponent<PlayerDeathMotor>().ServerBodyTick(0.1f);
                Assert.IsTrue(duelA.ServerForceStart(duelC));
                ServerLeaving.Notify(c);
                Assert.IsTrue(told.Any(a, "system Corvane left the game."), string.Join(" | ", told.To(a)));
                da.RunDuelOp(DuelOpKind.Forfeit, null, false);
                Assert.IsTrue(told.Any(a, "system You are not in a callout."));

                // the own view only asks (NGO's owner RPCs)
                var asked = new Asked();
                GameObject view = Make(NetRoleKind.ClientView, true, new PlayerKey(keyA));
                view.GetComponent<BodyRoleTag>().Requests = asked;
                yield return null;
                var viewDuel = view.GetComponent<KryticalNova.Combat.PlayerDuel>();
                viewDuel.Request(b.GetComponent<KryticalNova.Combat.PlayerDuel>());
                viewDuel.Respond(true);
                viewDuel.Forfeit();
                CollectionAssert.AreEqual(new[] { $"Duel Request {b.name} False", "Duel Respond - True", "Duel Forfeit - False" }, asked.Ops);

                foreach (GameObject body in new[] { a, b }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                OwnerMessages.Current = oldMessages;
                DuelOwnerLink.Current = null;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B53_ACalloutsOpponent_ReachesEveryView_TheTwoAreEnemiesThereOnly_AndAnotherPlayersViewKeepsPlayerDuel()
        {
            // M4 8b: NGO's netOpponent (read by everyone) as the ghost's DuelState: the server body names the opponent, every view mirrors it
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            try
            {
                PlayerKeys.Reset();
                ulong keyA = PlayerKeys.Mint(531).Value, keyB = PlayerKeys.Mint(532).Value;
                GameObject a = Admitted(keyA, "53a1000000000000000000000000000a", "c5310000000000000000000000000531", "Talwyn");
                GameObject b = Admitted(keyB, "53b1000000000000000000000000000b", "c5320000000000000000000000000532", "Brannoc");
                for (int i = 0; i < 30; i++) yield return null;
                var duelA = a.GetComponent<KryticalNova.Combat.PlayerDuel>();
                var duelB = b.GetComponent<KryticalNova.Combat.PlayerDuel>();
                Assert.IsNull(((IBodyDuel)duelA).OpponentBody, "nobody yet");
                Assert.IsTrue(duelA.ServerForceStart(duelB));
                Assert.AreEqual(b.transform, ((IBodyDuel)duelA).OpponentBody, "the server body names its opponent for the ghost");
                Assert.AreEqual(a.transform, ((IBodyDuel)duelB).OpponentBody);

                // the views: the own view of A and another player's view of B
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(keyA));
                GameObject theirs = Make(NetRoleKind.ClientView, false, new PlayerKey(keyB));
                GameObject third = Make(NetRoleKind.ClientView, false, new PlayerKey(533));
                yield return null;
                var ownDuel = own.GetComponent<KryticalNova.Combat.PlayerDuel>();
                var theirDuel = theirs.GetComponent<KryticalNova.Combat.PlayerDuel>();
                Assert.IsNotNull(theirDuel, "another player's view keeps PlayerDuel (its opponent colours namecards and targets)");
                bool Hostile(GameObject x, GameObject y) => KryticalNova.Combat.CombatHostility.IsHostile(x.transform, y.GetComponent<CombatEntity>());
                Assert.IsFalse(Hostile(own, theirs), "not enemies before");
                int changes = 0;
                ownDuel.Changed += () => changes++;
                ((IBodyDuel)ownDuel).MirrorOpponent(theirs.transform);
                ((IBodyDuel)theirDuel).MirrorOpponent(own.transform);
                Assert.IsTrue(ownDuel.IsDuelling, "the own view knows its bout (the Showdown letterbox)");
                Assert.AreEqual(1, changes, "told once");
                ((IBodyDuel)ownDuel).MirrorOpponent(theirs.transform);
                Assert.AreEqual(1, changes, "the same opponent again changes nothing");
                Assert.IsTrue(Hostile(own, theirs) && Hostile(theirs, own), "the two are enemies on every view");
                Assert.IsFalse(Hostile(own, third), "nobody else");

                // over: the ghost names nobody
                ((IBodyDuel)ownDuel).MirrorOpponent(null);
                ((IBodyDuel)theirDuel).MirrorOpponent(null);
                Assert.IsFalse(ownDuel.IsDuelling);
                Assert.IsFalse(Hostile(own, theirs), "friends again");
                Assert.IsTrue(duelA.ServerCancel("The callout was ended by the GM."));
                Assert.IsNull(((IBodyDuel)duelA).OpponentBody);

                foreach (GameObject body in new[] { a, b }) { ServerLeaving.Notify(body); body.GetComponent<PlayerSaveAgent>().OnServerLeaving(); }
            }
            finally
            {
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B54_AHostilesBlow_LandsOnTheServerBody_HurtsOnlyTheOwnersGame_TakesXpOnDeath_AndNeverLandsOnAView()
        {
            // M4 step 9: NGO HostileBrainLink.Strike's TakeDamage on the player's server body, from the queue the attacker side fills
            // (M5's monster brain); the hurt effect is for the owner's own game (NGO DamagedRpc), never played on the server
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            IOwnerMessages oldMessages = OwnerMessages.Current;
            var told = new Told();
            OwnerMessages.Current = told;
            ChatOwnerLink.Current = told;
            IGameFeelSink oldFeel = ClientPresentationRegistry.Feel;
            var feel = new FeelSeen();
            ClientPresentationRegistry.RegisterFeel(feel);
            ScriptableObject gaps = null;
            try
            {
                PlayerKeys.Reset();
                ulong keyA = PlayerKeys.Mint(541).Value;
                GameObject a = Admitted(keyA, "54a1000000000000000000000000000a", "c5410000000000000000000000000541", "Talwyn");
                a.GetComponent<BodyRoleTag>().Mover = new MoverSeen();
                for (int i = 0; i < 30; i++) yield return null;
                var entity = a.GetComponent<CombatEntity>();
                var resources = a.GetComponent<PlayerResources>();
                var progression = a.GetComponent<PlayerCharacterProgression>();
                var death = a.GetComponent<PlayerDeathMotor>();
                var hurts = new List<float>();
                resources.Hurt += hurts.Add;

                // the stand-in attacker: what the player's side reads off a Hostile (B46's way)
                var hostile = new GameObject("B54 hostile");
                hostile.SetActive(false);
                m_Made.Add(hostile);
                Type hostileType = Type.GetType("KryticalNova.Hostiles.HostileNet, KryticalNova.Hostiles");
                Type gapsType = Type.GetType("KryticalNova.Hostiles.LevelGapSettings, KryticalNova.Hostiles");
                Type rulesType = Type.GetType("KryticalNova.Hostiles.LevelGapRules, KryticalNova.Hostiles");
                Assert.IsNotNull(hostileType);
                Assert.IsNotNull(gapsType);
                Assert.IsNotNull(rulesType);
                Component net = hostile.AddComponent(hostileType);
                gaps = ScriptableObject.CreateInstance(gapsType);
                hostileType.GetField("levelGap", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(net, gaps);
                var hostileStats = hostile.GetComponent<CharacterCombatStats>();
                if (hostileStats == null) hostileStats = hostile.AddComponent<CharacterCombatStats>();
                KryticalNova.Combat.Entities.HostileAttackers.Set(54, hostileStats);
                Assert.AreSame(hostileStats, KryticalNova.Combat.Entities.HostileAttackers.Of(54));

                // 1. a blow: HP falls by what reached life, the owner's hurt effect is raised, nothing plays on this machine
                float before = resources.Health;
                Vector3 from = a.transform.position + Vector3.forward * 2f;
                var blow = new KryticalNova.Combat.Entities.HostileStrike
                {
                    Result = new DamageResult { DamageDealt = 40f, RawPhysicalDamage = 40f, MitigatedPhysicalDamage = 40f },
                    From = new Unity.Mathematics.float3(from.x, from.y, from.z),
                    Attacker = 54,
                };
                Assert.IsTrue(KryticalNova.Combat.Entities.HostileStrikes.Apply(entity, blow, hostileStats), "lands");
                Assert.AreEqual(before - 40f, resources.Health, 1e-3, "40 off");
                Assert.AreEqual(1, hurts.Count, "the owner's hurt effect");
                Assert.AreEqual(40f / resources.MaxHealth, hurts[0], 1e-4, "as NGO: the share of Max HP");
                Assert.IsFalse(feel.Calls.Contains("hurt"), "a server body's hurt never plays here");
                Assert.AreEqual(from, hostile.transform.position, "the stand-in stands where the Hostile stood");

                // 2. evaded: it lands (the number says so) but takes nothing
                var evaded = blow;
                evaded.Result = new DamageResult { IsEvaded = true };
                Assert.IsTrue(KryticalNova.Combat.Entities.HostileStrikes.Apply(entity, evaded, hostileStats));
                Assert.AreEqual(before - 40f, resources.Health, 1e-3, "nothing taken");
                Assert.AreEqual(1, hurts.Count, "no hurt");

                // 3. a view never takes a blow: it only shows the server's HP
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(keyA));
                yield return null;
                var ownResources = own.GetComponent<PlayerResources>();
                float ownBefore = ownResources.Health;
                Assert.IsFalse(KryticalNova.Combat.Entities.HostileStrikes.Apply(own.GetComponent<CombatEntity>(), blow, hostileStats), "not on a view");
                Assert.AreEqual(ownBefore, ownResources.Health, 1e-3);

                // 4. the killing blow: down, 5 % of the level's XP lost once to a Hostile (NGO HostileKillRewards)
                progression.SetExperience(progression.ExperienceRequired / 2);
                int xpFull = progression.Experience;
                int loss = (int)rulesType.GetMethod("DeathLoss").Invoke(null, new object[] { gaps, progression.ExperienceRequired, xpFull });
                Assert.Greater(loss, 0, "planned: a loss to see");
                var lethal = blow;
                float rest = resources.Health + 10f;
                lethal.Result = new DamageResult { DamageDealt = rest, RawPhysicalDamage = rest, MitigatedPhysicalDamage = rest, IsCritical = true };
                Assert.IsTrue(KryticalNova.Combat.Entities.HostileStrikes.Apply(entity, lethal, hostileStats));
                Assert.IsTrue(death.IsDead, "down");
                Assert.AreEqual(hostile.transform, death.ServerBodyKiller, "the Hostile killed it");
                Assert.AreEqual(xpFull - loss, progression.Experience, "the loss, once");
                Assert.AreEqual(1, told.To(a).FindAll(l => l == $"system You lost {loss} XP for falling to a Hostile.").Count, "told once: " + string.Join(" | ", told.To(a)));

                // 5. the dead take no more blows
                Assert.IsFalse(KryticalNova.Combat.Entities.HostileStrikes.Apply(entity, blow, hostileStats), "not on the dead");
                Assert.AreEqual(xpFull - loss, progression.Experience);

                resources.Hurt -= hurts.Add;
                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                KryticalNova.Combat.Entities.HostileAttackers.Set(54, null);
                if (oldFeel != null) ClientPresentationRegistry.RegisterFeel(oldFeel);
                else ClientPresentationRegistry.UnregisterFeel(feel);
                if (gaps != null) Object.Destroy(gaps);
                OwnerMessages.Current = oldMessages;
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B55_TheStrikeDummy_IsAlwaysAFoe_TakesStrikesAndSkills_RefillsNeverDies_AndAClientCopyTakesTheServersHp()
        {
            // M4.2b: NGO's Strike Dummy (Zone01's: capsule r 0.5 h 2, scale 0.8 / 1 / 0.8) struck by a real server body; the ghost side is
            // StrikeDummyTests (EditMode)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            int kills = 0;
            Action<CombatEntity, KillingBlow> count = (victim, blow) => kills++;
            CombatEntity.AnyKilled += count;
            try
            {
                GameObject attacker = Make(NetRoleKind.ServerBody, false, new PlayerKey(551));
                var dummyObject = new GameObject("B55 Combat Dummy");
                m_Made.Add(dummyObject);
                dummyObject.transform.position = attacker.transform.position + attacker.transform.forward * 1f;
                dummyObject.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
                var capsule = dummyObject.AddComponent<CapsuleCollider>();   // Zone01's: r 0.5, h 2 (an added one defaults to h 1)
                capsule.radius = 0.5f;
                capsule.height = 2f;
                var dummy = dummyObject.AddComponent<CombatEntity>();
                yield return null;
                Assert.IsTrue(KryticalNova.Combat.Entities.StrikeDummies.IsSceneDummy(dummy), "NGO's IsStrikeDummy, on no player body");
                Assert.IsFalse(KryticalNova.Combat.Entities.StrikeDummies.IsSceneDummy(attacker.GetComponent<CombatEntity>()), "a player body is none");
                Assert.IsTrue(dummy.InfiniteHealth, "always infinite");
                KryticalNova.Combat.Entities.StrikeDummies.CapsuleOf(dummy, out int radius, out int height);
                Assert.AreEqual(400, radius, "the scene capsule with its scale");
                Assert.AreEqual(2000, height);

                // always a foe (NGO CombatHostility: anything not player-vs-player), the order and the strikes land, numbers to the attacker
                Assert.IsTrue(KryticalNova.Combat.Entities.MeleeResolveServerSystem.AttackAllowed(attacker, dummy), "may be attacked");
                int told = 0;
                attacker.GetComponent<KryticalNova.Combat.PlayerMeleeCombat>().HitDealt += (t, r, step, skill, label) => told++;
                float full = dummy.CurrentHealth;
                bool landed = false;
                for (int i = 0; i < 20 && dummy.CurrentHealth >= full; i++)
                    landed |= KryticalNova.Combat.Entities.MeleeStrike.Apply(attacker, dummy, 1 + i % 3, 1f + 0.15f * (i % 3));
                Assert.IsTrue(landed, "struck");
                Assert.Less(dummy.CurrentHealth, full, "HP taken");
                Assert.Greater(told, 0, "each hit told to the attacker (its number)");
                Assert.IsTrue(KryticalNova.Combat.PlayerSkillAttacks.HitsHere(dummy), "area skills include it");

                // a lethal blow: full again, still alive, no death and no kill (no XP, no reward)
                int died = 0;
                dummy.Died += () => died++;
                dummy.TakeDamage(new DamageResult { DamageDealt = 1e7f, RawPhysicalDamage = 1e7f, MitigatedPhysicalDamage = 1e7f },
                    attacker.GetComponent<CharacterCombatStats>());
                Assert.AreEqual(dummy.MaxHealth, dummy.CurrentHealth, 1e-3, "refilled");
                Assert.IsTrue(dummy.IsAlive);
                Assert.AreEqual(0, died, "never a death");
                Assert.AreEqual(0, kills, "never a kill");

                // a client's own copy takes the server's HP (NGO netStandaloneHealth); the first copy is no event, a copy never dies of it
                var copyObject = new GameObject("B55 client copy");
                m_Made.Add(copyObject);
                var copy = copyObject.AddComponent<CombatEntity>();
                yield return null;
                int changed = 0, copyDied = 0;
                copy.HealthChanged += () => changed++;
                copy.Died += () => copyDied++;
                copy.MirrorSceneHealth(4321f, 5000f);
                Assert.AreEqual(4321f, copy.CurrentHealth, 1e-3);
                Assert.AreEqual(5000f, copy.MaxHealth, 1e-3);
                Assert.AreEqual(1, changed);
                copy.MirrorSceneHealth(4321f, 5000f);
                Assert.AreEqual(1, changed, "the same HP again changes nothing");
                copy.MirrorSceneHealth(5000f, 5000f);
                Assert.AreEqual(2, changed, "refilled");
                Assert.AreEqual(0, copyDied);
                float playerHp = attacker.GetComponent<CombatEntity>().CurrentHealth;
                attacker.GetComponent<CombatEntity>().MirrorSceneHealth(1f, 5000f);
                Assert.AreEqual(playerHp, attacker.GetComponent<CombatEntity>().CurrentHealth, 1e-3, "never on a player");
            }
            finally
            {
                CombatEntity.AnyKilled -= count;
            }
        }

        [UnityTest]
        public IEnumerator B56_AnNpcOnTheOwnView_IsWalkedToAndOpensOnArrival_AsNgosWalkToTalk()
        {
            // owner's hand test p2 (5 Nov 2026): a vendor did nothing on a DOTS view. Now NGO's walk-to-talk: in reach it opens at once,
            // out of reach the ghost walks to the stand-off point (a ground click of the player's own) and it opens on arrival; its
            // services are the ported server requests
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ClientView));
            INpcDialogueSink oldDialogue = ClientPresentationRegistry.Dialogue;
            var dialogue = new DialogueSeen();
            ClientPresentationRegistry.RegisterDialogue(dialogue);
            try
            {
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(561));
                var npcObject = new GameObject("B56 Apothecary");
                m_Made.Add(npcObject);
                npcObject.transform.position = own.transform.position + Vector3.right * 9f;
                var npc = npcObject.AddComponent<KryticalNova.NPC.NpcInteractable>();
                yield return null;
                Assert.IsInstanceOf<IEntityViewInteractable>(npc, "an NPC works on a view");
                var controls = own.GetComponent<PointAndClickPlayer>();
                Assert.IsTrue(controls.ViewMode);
                MethodInfo begin = typeof(PointAndClickPlayer).GetMethod("ViewBeginInteract", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(begin);

                // out of reach: a walk to its stand-off point goes to the ghost, nothing opens yet
                BodyClicks.Clear();
                begin.Invoke(controls, new object[] { npc });
                Assert.IsTrue(BodyClicks.TryTake(out BodyClicks.Click walk), "a walk is asked of the ghost");
                Assert.Less(Vector3.Distance(walk.Point, npc.InteractPoint), 0.01f, "to the NPC's stand-off point");
                yield return null;
                Assert.AreEqual(0, dialogue.Shown, "not yet");

                // the ghost brings the body there: it opens once, on arrival
                own.transform.position = npc.Position + Vector3.left * 1f;
                yield return null;
                yield return null;
                Assert.AreEqual(1, dialogue.Shown, "opened on arrival");
                yield return null;
                Assert.AreEqual(1, dialogue.Shown, "once");

                // in reach: at once, no walk
                BodyClicks.Clear();
                begin.Invoke(controls, new object[] { npc });
                Assert.AreEqual(2, dialogue.Shown, "opened at once");
                Assert.IsFalse(BodyClicks.TryTake(out _), "no walk");
            }
            finally
            {
                BodyClicks.Clear();
                if (oldDialogue != null) ClientPresentationRegistry.RegisterDialogue(oldDialogue);
                else ClientPresentationRegistry.UnregisterDialogue(dialogue);
            }
        }

        [UnityTest]
        public IEnumerator B57_PlayDotsHereGivesTheTester_EverySkillAtMaxRank_OnTheServerBody()
        {
            // owner, 5 Nov 2026: "when taking me into test make sure I have all skills unlocked" (gm.skills.max on the server's body)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            try
            {
                PlayerKeys.Reset();
                GameObject a = Admitted(PlayerKeys.Mint(571).Value, "57a1000000000000000000000000000a", "c5710000000000000000000000000571", "Talwyn");
                a.GetComponent<BodyRoleTag>().Mover = new MoverSeen();
                for (int i = 0; i < 30; i++) yield return null;
                var skills = a.GetComponent<PlayerCombatSkills>();
                var movement = a.GetComponent<PlayerMovementSkills>();
                Assert.Greater(skills.AvailableSkills.Count, 0, "planned: a skill list");
                Assert.IsTrue(KryticalNova.Persistence.Entities.KnPlayHere.GiveEverySkill(a));
                foreach (SkillDefinitionSO skill in skills.AvailableSkills)
                    if (skill != null) Assert.AreEqual(skill.maxRank, skills.GetRank(skill), skill.skillId + " at its max rank");
                Assert.Greater(movement.GetRank(MovementSkillId.Run), 0, "Run learned");
                Assert.Greater(movement.GetRank(MovementSkillId.Jump), 0, "Jump learned");
                int run = movement.GetRank(MovementSkillId.Run), jump = movement.GetRank(MovementSkillId.Jump);
                movement.GmSetMaxRanks();
                Assert.AreEqual(run, movement.GetRank(MovementSkillId.Run), "Run already at its max");
                Assert.AreEqual(jump, movement.GetRank(MovementSkillId.Jump), "Jump already at its max");
                Assert.IsFalse(KryticalNova.Persistence.Entities.KnPlayHere.GiveEverySkill(null), "nobody, nothing");

                // owner 5-6 Nov 2026 (findings item 20): every test character, the Tester and whoever joins Play DOTS here (Player2), enters at
                // the MAXIMUM level (was 60) with every skill at max rank
                var progression = a.GetComponent<PlayerCharacterProgression>();
                Assert.Less(progression.CurrentLevel, progression.MaxLevel, "the case under test: not at the top yet");
                Assert.IsTrue(KryticalNova.Persistence.Entities.KnPlayHere.MakeTestReady(a));
                Assert.AreEqual(progression.MaxLevel, progression.CurrentLevel, "at the maximum level");
                foreach (SkillDefinitionSO skill in skills.AvailableSkills)
                    if (skill != null) Assert.AreEqual(skill.maxRank, skills.GetRank(skill), skill.skillId + " still at its max rank");
                Assert.IsTrue(KryticalNova.Persistence.Entities.KnPlayHere.MakeTestReady(a), "again: no harm");
                Assert.AreEqual(progression.MaxLevel, progression.CurrentLevel);
                Assert.IsFalse(KryticalNova.Persistence.Entities.KnPlayHere.MakeTestReady(null));

                ServerLeaving.Notify(a);
                a.GetComponent<PlayerSaveAgent>().OnServerLeaving();
            }
            finally
            {
                PlayerKeys.Reset();
            }
        }

        [UnityTest]
        public IEnumerator B58_AStruckStrikeDummy_PutsTheServerBodyInAFight_AndTheOwnViewHoldsTheMirroredFight()
        {
            // Cadence step 1 (owner, 5 Nov 2026: Cadence on the dummy as NGO): NGO PlayerCombatState decides on the server body (a landed
            // blow on a Strike Dummy holds the fight while selected or within 30 m); the view only mirrors it (the fight music reads it)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject attacker = Make(NetRoleKind.ServerBody, false, new PlayerKey(581));
            var dummyObject = new GameObject("B58 Combat Dummy");
            m_Made.Add(dummyObject);
            dummyObject.transform.position = attacker.transform.position + attacker.transform.forward * 1f;
            var dummy = dummyObject.AddComponent<CombatEntity>();
            yield return null;
            var fight = attacker.GetComponent<KryticalNova.Combat.PlayerCombatState>();
            Assert.IsNotNull(fight, "the server body keeps PlayerCombatState");
            Assert.IsFalse(fight.InCombat, "no fight yet");
            bool landed = false;
            for (int i = 0; i < 20 && !landed; i++) landed = KryticalNova.Combat.Entities.MeleeStrike.Apply(attacker, dummy, 1, 1f);
            Assert.IsTrue(landed, "a blow on the dummy");
            for (float t = 0f; t < 0.6f; t += Time.deltaTime) yield return null;
            Assert.IsTrue(fight.InCombat, "the server body is in a fight (NGO's Blow and Dummy reasons)");

            GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(581));
            yield return null;
            var mirrored = own.GetComponent<KryticalNova.Combat.PlayerCombatState>();
            Assert.IsNotNull(mirrored, "the own view keeps PlayerCombatState (the fight music reads it)");
            int changes = 0;
            mirrored.Changed += _ => changes++;
            mirrored.MirrorInCombat(true);
            Assert.IsTrue(mirrored.InCombat);
            Assert.AreEqual(1, changes);
            for (float t = 0f; t < 0.6f; t += Time.deltaTime) yield return null;
            Assert.IsTrue(mirrored.InCombat, "the view never decides it away (its checks do not run there)");
            mirrored.MirrorInCombat(false);
            Assert.IsFalse(mirrored.InCombat, "the server's end of the fight");
            fight.MirrorInCombat(false);
            Assert.IsTrue(fight.InCombat, "the server body ignores a mirror: it decides");
        }

        [UnityTest]
        public IEnumerator B59_ACadencePress_IsJudgedOnTheServerBody_NeverOnAView_AndTheServersWordsShowOnTheOwnView()
        {
            // Cadence step 2 (NGO PressRpc -> ServerNotePress on the server; EarnedRpc / ComboRpc to the owner)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            ICombatFeedbackSink oldFeedback = ClientPresentationRegistry.Feedback;
            var texts = new TextsSeen();
            ClientPresentationRegistry.RegisterFeedback(texts);
            try
            {
                GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(591));
                GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(591));
                yield return null;
                var judge = server.GetComponent<KryticalNova.Combat.PlayerCadence>();
                var view = own.GetComponent<KryticalNova.Combat.PlayerCadence>();
                Assert.IsNotNull(judge, "the server body keeps PlayerCadence");
                Assert.IsNotNull(view, "the own view keeps PlayerCadence (the key and the words)");
                SkillDefinitionSO attack = null;
                foreach (SkillDefinitionSO s in server.GetComponent<PlayerCombatSkills>().AvailableSkills)
                    if (s != null && s.IsAttack && !s.IsCharged) { attack = s; break; }
                Assert.IsNotNull(attack, "planned: an attack skill to press");

                string before = judge.LastPress, viewBefore = view.LastPress;
                judge.ServerNotePressFromView(attack.skillId, 1.0);
                Assert.AreNotEqual(before, judge.LastPress, "the server body judged it (here: no fight, no beat)");
                view.ServerNotePressFromView(attack.skillId, 1.0);
                Assert.AreEqual(viewBefore, view.LastPress, "a view never judges");

                // the server's words on the own view: "ON CADENCE" and the combo step
                int combos = 0;
                System.Action<int, float, bool> count = (step, seconds, redline) => { if (step == 2 && redline) combos++; };
                KryticalNova.Combat.PlayerCadence.LocalCombo += count;
                try
                {
                    view.ShowEarnedFromServer(attack.skillId);
                    view.ShowComboFromServer(2, true);
                }
                finally { KryticalNova.Combat.PlayerCadence.LocalCombo -= count; }
                Assert.AreEqual(1, texts.Texts.FindAll(t => t == "ON CADENCE").Count, "ON CADENCE on the own view: " + string.Join(" | ", texts.Texts));
                Assert.AreEqual(1, combos, "COMBO x2 with REDLINE held");
            }
            finally
            {
                if (oldFeedback != null) ClientPresentationRegistry.RegisterFeedback(oldFeedback);
                else ClientPresentationRegistry.UnregisterFeedback(texts);
            }
        }

        [UnityTest]
        public IEnumerator B60_AStruckDummy_HoldsTheFightWhileSelected_BeyondThirtyMetres_AndEndsWhenDeselectedAndFar()
        {
            // owner (5 Nov 2026): Cadence on the dummy exactly as NGO: the fight holds while the dummy stays selected OR within 30 m;
            // the server body learns the owner's selection (NGO SelectionRpc) through ServerSetSelected
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject attacker = Make(NetRoleKind.ServerBody, false, new PlayerKey(601));
            var dummyObject = new GameObject("B60 Combat Dummy");
            m_Made.Add(dummyObject);
            dummyObject.transform.position = attacker.transform.position + attacker.transform.forward * 1f;
            var dummy = dummyObject.AddComponent<CombatEntity>();
            yield return null;
            var fight = attacker.GetComponent<KryticalNova.Combat.PlayerCombatState>();
            // the blow's own 6 s linger shortened, so the dummy rule alone is seen
            typeof(KryticalNova.Combat.PlayerCombatState).GetField("lingerSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(fight, 0.5f);
            bool landed = false;
            for (int i = 0; i < 20 && !landed; i++) landed = KryticalNova.Combat.Entities.MeleeStrike.Apply(attacker, dummy, 1, 1f);
            Assert.IsTrue(landed);
            fight.ServerSetSelected(dummyObject.transform);
            for (float t = 0f; t < 0.4f; t += Time.deltaTime) yield return null;
            Assert.IsTrue(fight.InCombat, "in a fight with the dummy");

            dummyObject.transform.position = attacker.transform.position + Vector3.forward * 40f;   // 40 m off
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) yield return null;
            Assert.IsTrue(fight.InCombat, "still selected: the fight holds beyond 30 m");

            fight.ServerSetSelected(null);
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) yield return null;
            Assert.IsFalse(fight.InCombat, "deselected and 40 m away: over");
        }

        [UnityTest]
        public IEnumerator B61_AnEmoteOnTheOwnView_AsksTheGhostToSheathe_AndTheViewNeverSheathesByItself()
        {
            // NGO PlayerEmotes.RequestPlay:193-194 (RequestSetWeaponDraw(false) before the emote): on a view the ghost sheathes
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(611));
            yield return null;
            var emotes = own.GetComponent<KryticalNova.Social.PlayerEmotes>();
            var motor = own.GetComponent<PlayerAnimancerMotor>();
            Assert.IsNotNull(emotes, "the own view keeps PlayerEmotes");
            Assert.IsNotNull(motor);
            Assert.IsNull(emotes.Blocked(), "planned: the view may start an emote here");
            BodyClicks.Clear();
            try
            {
                emotes.RequestPlay("b61");
                Assert.AreEqual(0, BodyClicks.Count, "sheathed already: nothing asked");

                motor.ShowWeaponStanceFromGhost(true, animate: false);
                Assert.IsTrue(motor.IsWeaponDrawn);
                emotes.RequestPlay("b61");
                Assert.IsTrue(BodyClicks.TryTake(out BodyClicks.Click click) && click.SheatheWeapon, "the view asked its ghost to sheathe");
                Assert.IsTrue(motor.IsWeaponDrawn, "the view never sheathes by itself: the ghost decides and every view shows it");
            }
            finally { BodyClicks.Clear(); }
        }

        [UnityTest]
        public IEnumerator B62_TestSavesLogOut_SavesEveryServerBodyBeforeTheWorldsStop_AndTheCharacterEntersAgain()
        {
            // A2 (Nova's condition 3): Log Out in "Play DOTS here (test saves)" = each server body leaves as a disconnect does (saved, let
            // go), the store has everything, and only then do the worlds stop; Enter again loads what was saved
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            const string account = "62112233445566778899aabbccddeeff";
            const string characterId = "62eeddccbbaa99887766554433221100";
            GameObject first = Make(NetRoleKind.ServerBody, false, new PlayerKey(621));
            yield return null;
            CharacterRecipe recipe = AnyLegalRecipe(first.GetComponent<PlayerIdentity>().Database);
            var stored = new AccountSave { accountId = account };
            stored.characters.Add(new CharacterSave { characterId = characterId, name = "Relog" });
            Assert.IsTrue(ServerSave.WriteAccount(stored));
            ServerSave.WaitForDisk();
            ServerBodyContext Context(int key) => new ServerBodyContext
            {
                Key = new PlayerKey((ulong)key), Serial = (ulong)key, NetworkId = key, Account = account, CharacterId = characterId, Name = "Relog",
                RecipeText = RecipeText.Encode(recipe),
            };
            var rules = new PlayerAdmission();
            Assert.IsNull(rules.Begin(first, Context(621)), "admitted");
            for (int i = 0; i < 10; i++) yield return null;
            var wallet = first.GetComponent<KryticalNova.Economy.PlayerWallet>();
            wallet.ServerSet(621);
            string walletText = wallet.CaptureSave();
            // the relog spot (owner rule, 6 Nov 2026): where the body stands when it leaves
            Vector3 stood = new Vector3(12.3f, 0.5f, -4.5f);
            KryticalNova.Networking.NetworkPlacement.Place(first.transform, stood, first.transform.rotation);   // the game's way

            string savedWhenStopped = null;
            Vector3 storedSpot = default;
            int stops = 0;
            PlayHereLeave.LeaveAll(new[] { first }, ServerSave.Store, () =>
            {
                stops++;
                CharacterSave c = ServerSave.ReadAccountFrom(ServerSave.Store, account)?.Find(characterId);
                savedWhenStopped = c?.Get("wallet");
                if (c != null) storedSpot = new Vector3(c.x, c.y, c.z);
            });
            Assert.Less(Vector3.Distance(stood, storedSpot), 0.11f, "the leave save holds where the body stood");
            Assert.AreEqual(1, stops, "the worlds stop once");
            Assert.AreEqual(walletText, savedWhenStopped, "the leave save was in the store BEFORE the worlds stopped");
            Object.Destroy(first);
            yield return null;

            GameObject second = Make(NetRoleKind.ServerBody, false, new PlayerKey(622));
            yield return null;
            Vector3 spawnPoint = second.transform.position;
            string askedMap = null, askedHere = null;
            Vector3 askedAt = default;
            // this scene has no NavMesh: the check stands in (its NavMesh / blocked parts: RelogAndMirrorReadyTests), the rest is real
            rules.ReturnSpot = (map, here, at, offset) => { askedMap = map; askedHere = here; askedAt = at; return at; };
            Assert.IsNull(rules.Begin(second, Context(622)), "the account was let go: the character enters again");
            // ServerBodyAdmission makes the ghost on the body's spot right after Begin: that is where every view starts
            Vector3 whenGhostIsMade = second.transform.position;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(walletText, second.GetComponent<KryticalNova.Economy.PlayerWallet>().CaptureSave(), "as it was saved");
            Assert.IsNotNull(askedMap, "a spot was saved on leaving");
            Assert.AreEqual(askedHere, askedMap, "on this map (its scene name)");
            Assert.Less(Vector3.Distance(stood, askedAt), 0.11f, $"the load keeps the saved spot (the save right after the load must not write the spawn over it): {askedAt}");
            CharacterSave afterEntry = ServerSave.ReadAccountFrom(ServerSave.Store, account)?.Find(characterId);
            Assert.Less(Vector3.Distance(stood, new Vector3(afterEntry.x, afterEntry.y, afterEntry.z)), 0.11f, "nor in the store");
            Assert.Less(Vector3.Distance(stood, whenGhostIsMade), 0.11f, $"the body is back there when its ghost is made, not on the spawn point {spawnPoint}");
            second.GetComponent<PlayerSaveAgent>().OnServerLeaving();
        }

        [UnityTest]
        public IEnumerator B64_SquadAndCalloutChatCommands_WorkOnTheOwnView_AndCalloutFindsAnotherPlayersView()
        {
            // owner's 2-player test, 6 Nov 2026, items 6 and 16a: /follow and the Squad commands answered "Squads need an online
            // session" and /callout found nobody, because they asked NGO's IsSpawned; the own view of the entity netcode counts as online
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(641));
            GameObject theirs = Make(NetRoleKind.ClientView, false, new PlayerKey(642));
            yield return null;
            var asked = new Asked();
            own.GetComponent<BodyRoleTag>().Requests = asked;
            LocalPlayer.Set(own);
            try
            {
                string Said(string line)
                {
                    KryticalNova.Social.ChatInput input = KryticalNova.Social.ChatCommands.Parse(line, ChatChannel.General, null, null);
                    return input.Notice;
                }

                string follow = Said("/follow Vesper");
                Assert.IsTrue(follow == null || !follow.Contains("online session"), "/follow on the own view: " + follow);
                Assert.Contains("Follow Vesper", asked.Ops, "/follow asks the server body by name");

                string promote = Said("/promote Vesper");
                Assert.IsTrue(promote == null || !promote.Contains("online session"), "/promote on the own view: " + promote);

                string accept = Said("/accept");
                Assert.IsTrue(accept == null || !accept.Contains("online session"), "/accept on the own view: " + accept);
                Assert.IsTrue(asked.Ops.Exists(op => op.StartsWith("Duel Respond", StringComparison.Ordinal)), "/accept answers through the server body");

                string theirName = theirs.GetComponent<KryticalNova.Combat.PlayerDuel>().DuelistName;
                Assume.That(!string.IsNullOrEmpty(theirName), "the other view has a duelist name");
                string callout = Said("/callout " + theirName);
                Assert.IsTrue(callout == null || !callout.Contains("No player named"), "/callout finds another player's view: " + callout);
                Assert.IsTrue(asked.Ops.Exists(op => op.StartsWith("Duel Request " + theirs.name, StringComparison.Ordinal)), "and asks the server to call them out");
            }
            finally { LocalPlayer.Set(null); }
        }

        [UnityTest]
        public IEnumerator B63_TheRunSelection_IsDecidedOnTheServerBody_ARefusalIsCorrectedOnTheView_AndServerWalksRunAndPayAsNgo()
        {
            // NGO PlayerMovementSkills.Select / SelectServerRpc / CorrectSelectionRpc and PointAndClickPlayer.ServerMoveTo (run = Run selected,
            // pay = payRun && Run selected)
            NetRole.Install(new BodyRoleNetRoleAdapter(null, () => NetRoleKind.ServerBody));
            GameObject server = Make(NetRoleKind.ServerBody, false, new PlayerKey(631));
            GameObject own = Make(NetRoleKind.ClientView, true, new PlayerKey(631));
            yield return null;
            var decides = server.GetComponent<PlayerMovementSkills>();
            var shows = own.GetComponent<PlayerMovementSkills>();
            var asked = new Asked();
            own.GetComponent<BodyRoleTag>().Requests = asked;
            decides.RestoreSave(JsonUtility.ToJson(new MovementSkillState { runRank = 0, jumpRank = 0 }));
            shows.MirrorRanks(1, 0);   // the view believes Run is learned: the server knows better

            Assert.IsTrue(shows.ToggleSelection(MovementSkillId.Run), "the view shows the selection at once");
            Assert.IsTrue(shows.IsRunning);
            Assert.Contains("SelectMovement " + (byte)MovementSkillId.Run, asked.Ops, "and asks its server body");

            decides.RunSelect(MovementSkillId.Run);
            Assert.IsFalse(decides.IsRunning, "Run not learned on the server: refused");
            MoveRanks answer = PlayerMoveFromSkills.RanksOf(server).Value;
            Assert.AreEqual((byte)MovementSkillId.None, answer.Selected);
            Assert.AreEqual(1, answer.SelectSeq, "the refusal is an answer too (NGO's CorrectSelectionRpc)");
            shows.MirrorSelection((MovementSkillId)answer.Selected);
            Assert.IsFalse(shows.IsRunning, "the view is put back");

            decides.RestoreSave(JsonUtility.ToJson(new MovementSkillState { runRank = 1, jumpRank = 0 }));
            decides.RunSelect(MovementSkillId.Run);
            Assert.IsTrue(decides.IsRunning, "learned: the server body runs");
            Assert.AreEqual(MovementSkillId.Run, JsonUtility.FromJson<MovementSkillState>(decides.CaptureSave()).selected, "and the selection is saved");
            Assert.AreEqual(2, PlayerMoveFromSkills.RanksOf(server).Value.SelectSeq);

            var mover = new MoverSeen();
            server.GetComponent<BodyRoleTag>().Mover = mover;
            var controls = server.GetComponent<PointAndClickPlayer>();
            Vector3 to = server.transform.position + Vector3.forward * 6f;
            Assert.IsTrue(controls.ServerMoveTo(to));
            Assert.IsTrue(controls.ServerMoveTo(to, payRun: false));
            decides.RunSelect(MovementSkillId.None);
            Assert.IsTrue(controls.ServerMoveTo(to));
            Assert.AreEqual(new[] { "run paid", "run free", "walk free" }, mover.Kinds.ToArray(),
                "a paid destination, a free steering step at run speed, a walk");
        }

        sealed class TextsSeen : ICombatFeedbackSink
        {
            public readonly List<string> Texts = new List<string>();
            public void ShowCombatFeedback(Vector3 worldPosition, DamageResult result) { }
            public void ShowFloatingText(Vector3 worldPosition, string text, string styleClass = "crit") => Texts.Add(text);
        }

        sealed class DialogueSeen : INpcDialogueSink
        {
            public int Shown;
            public void ShowDialogue(INpcDialogueSource npc, Transform player) => Shown++;
            public void HideDialogue() { }
        }

        sealed class NumbersSeen : ICombatFeedbackSink
        {
            public int Shown;
            public void ShowCombatFeedback(Vector3 worldPosition, DamageResult result) => Shown++;
            public void ShowFloatingText(Vector3 worldPosition, string text, string styleClass = "crit") { }
        }

        static void AssertSameBuffs(CharacterBuffManager server, CharacterBuffManager copy, string who)
        {
            Assert.AreEqual(server.ActiveBuffs.Count, copy.ActiveBuffs.Count, who + ": as many buffs");
            foreach (ActiveBuff s in server.ActiveBuffs)
            {
                ActiveBuff c = copy.GetBuff(s.Skill.skillId);
                Assert.IsNotNull(c, who + " has " + s.Skill.skillId);
                Assert.AreEqual(s.Rank, c.Rank, who + ": rank of " + s.Skill.skillId);
                Assert.AreEqual(s.FromSquadmate, c.FromSquadmate, who + ": from the Squad");
                if (s.FromSquadmate) Assert.AreEqual(s.Giver, c.Giver, who + ": the giver");
                Assert.AreEqual(s.RemainingDuration, c.RemainingDuration, 0.25f, who + ": time left of " + s.Skill.skillId);
            }
        }

        /// <summary>The game-feel calls a body made (B44).</summary>
        sealed class FeelSeen : IGameFeelSink
        {
            public readonly List<string> Calls = new List<string>();
            public void Swing(Transform attacker, int comboStep, bool isLocalPlayer, float seconds = 0f) => Calls.Add($"swing {comboStep}");
            public void Hit(in HitFeel hit) => Calls.Add($"hit {(hit.Target != null ? hit.Target.name : "")} {hit.ComboStep} {hit.IsCritical}");
            public void Missed(Transform attacker, Vector3 point, bool skill, bool isLocalPlayer) { }
            public void LocalPlayerDamaged(float fraction01) => Calls.Add("hurt");
            public void MoveCommand(Vector3 point, Vector3 normal) { }
            public void TargetSelected(Transform target, WorldTargetKind kind) { }
            public void RunStarted(Transform actor, bool isLocalPlayer) { }
            public void RunStopped(Transform actor) { }
            public void RideWalk(Transform actor, bool walking) { }
            public void Dismount(in DismountFeel moment) { }
            public void JumpCharge(Transform actor, bool isLocalPlayer) { }
            public void JumpTakeoff(Transform actor, bool isLocalPlayer) { }
            public void WallStanding(Transform actor, bool onWall) { }
            public void WaterStriding(Transform actor, bool onWater) { }
            public void WaterSplash(Transform actor, Vector3 at, bool isLocalPlayer) { }
            public void Landed(Transform actor, bool isLocalPlayer) { }
            public void Airborne(Transform actor, bool airborne) { }
            public void SkillCast(Transform caster, string skillId, bool isAttack, bool isLocalPlayer, float areaRadius = 0f, float areaSeconds = 0f) => Calls.Add("cast " + skillId);
            public void ChargeStarted(Transform actor, string skillId, float seconds, bool isLocalPlayer) => Calls.Add("charge " + skillId);
            public void ChargeEnded(Transform actor, string skillId, bool completed) => Calls.Add($"charged {skillId} {completed}");
            public void SkillHit(Transform caster, string skillId, Vector3 point, int classSlot = 0) => Calls.Add($"skillhit {caster.name} {skillId}");
            public void BuffStarted(Transform actor, string skillId, float duration) { }
            public void BuffEnded(Transform actor, string skillId) { }
            public void Consumed(Transform actor, bool restoredHealth, bool restoredChi) { }
            public void PetEvolved(Transform pet, Color glow, bool ownerIsLocalPlayer) { }
            public void Death(in DeathFeel death) => Calls.Add($"death {death.Target.name} {death.IsCritical} {death.VictimIsLocalPlayer} {death.KillerIsLocalPlayer}");
            public void BodyLanded(Transform actor, bool critical, bool involvesLocalPlayer) { }
            public void Respawned(Transform actor, bool isLocalPlayer) => Calls.Add($"respawned {actor.name} {isLocalPlayer}");
            public void Disintegrate(in DeathFeel death) => Calls.Add($"derez {death.Target.name}");
            public void SetDisintegrated(Transform actor, bool gone) => Calls.Add($"hidden {actor.name} {gone}");
            public void GearWorked(Transform actor, int outcome, bool isLocalPlayer) { }
            public void NovaWorked(in NovaFeel moment) { }
            public void LevelUp(Transform actor, bool isLocalPlayer) { }
            public void PickedUp(Transform taker, Vector3 point, int rarity, bool isLocalPlayer) { }
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
