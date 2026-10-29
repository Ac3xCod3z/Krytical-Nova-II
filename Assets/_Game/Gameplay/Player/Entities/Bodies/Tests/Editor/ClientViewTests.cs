using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    /// <summary>
    /// M3a-2b step 7: the client view with a stand-in body (fakes named like the real input and click classes). The real prefab's
    /// Awake/Start chain, its camera and <c>PlayerInventory.Local</c> are B1 (they need Play).
    /// </summary>
    public class ClientViewTests
    {
        GameObject m_View;

        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();

        [SetUp]
        public void SetUp()
        {
            BodyClicks.Clear();
            m_View = new GameObject("StandInView");
            m_View.AddComponent<PlayerInput>();          // added by hand: fakes declared in another file get no RequireComponent help
            m_View.AddComponent<PointAndClickPlayer>();
            m_View.AddComponent<LeaveRecorder>();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_View != null) Object.DestroyImmediate(m_View);
            BodyClicks.Clear();
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
        }

        static PlayerBodyRequest Request(bool local, int id = 7) =>
            new PlayerBodyRequest { NetworkId = id, Key = PlayerKey.None, IsLocal = local, Position = new Vector3(1f, 0f, 2f), YawDegrees = 90f };

        [Test]
        public void TheFactory_MakesALocalBodyWithInputAndBanner_AndARemoteOneWithout()
        {
            int released = 0;
            var factory = new ClientBodyFactory(m_View) { BeforeLocalBody = () => released++ };
            GameObject mine = factory.Create(Request(true));
            GameObject theirs = factory.Create(Request(false, 8));
            try
            {
                BodyRoleTag mineTag = mine.GetComponent<BodyRoleTag>();
                Assert.AreEqual(NetRoleKind.ClientView, mineTag.Kind);
                Assert.IsTrue(mineTag.IsLocal);
                Assert.IsNotNull(mine.GetComponent<PlayerInput>());
                Assert.IsNotNull(mine.GetComponent<PointAndClickPlayer>());
                Assert.IsNotNull(mine.GetComponent<NotYetNetworkedBanner>());
                Assert.AreEqual(1, released, "the offline avatar is put away once, before the local body wakes");

                BodyRoleTag theirTag = theirs.GetComponent<BodyRoleTag>();
                Assert.AreEqual(NetRoleKind.ClientView, theirTag.Kind);
                Assert.IsFalse(theirTag.IsLocal);
                Assert.IsNull(theirs.GetComponent<PointAndClickPlayer>(), "no input or camera code on another player's body");
                Assert.IsNull(theirs.GetComponent<PlayerInput>());
                Assert.IsNull(theirs.GetComponent<NotYetNetworkedBanner>());
                Assert.That(Vector3.Distance(new Vector3(1f, 0f, 2f), theirs.transform.position), Is.LessThan(1e-4f));
                Assert.IsNull(mine.transform.parent, "awake at the scene root, the holder is gone");
                Assert.AreEqual(2, factory.Made);
            }
            finally
            {
                factory.Destroy(mine);
                factory.Destroy(theirs);
            }
            Assert.IsTrue(mine == null && theirs == null);
            Assert.AreEqual(2, factory.Unmade);
        }

        [Test]
        public void AMissingPrefab_GivesNoBody_SoTheSpawnSystemAsksAgain()
        {
            var factory = new ClientBodyFactory(() => null);
            Assert.IsNull(factory.Create(Request(true)));
        }

        [Test]
        public void TheRegistry_KeepsTheTagsLocalFlagInStep()
        {
            var factory = new ClientBodyFactory(m_View);
            GameObject body = factory.Create(Request(true));
            try
            {
                using var world = new World("ClientViewTests");
                GhostBodyEntry entry = GhostBodyRegistry.Register(world, default, 7, PlayerKey.None, body, true);
                BodyRoleTag tag = body.GetComponent<BodyRoleTag>();
                int changes = 0;
                tag.LocalChanged += _ => changes++;
                GhostBodyRegistry.SetLocal(entry, false);
                Assert.IsFalse(tag.IsLocal);
                Assert.AreEqual(1, changes);
                Assert.IsNull(LocalPlayer.Object);
                GhostBodyRegistry.SetLocal(entry, true);
                Assert.IsTrue(tag.IsLocal);
                Assert.AreSame(body, LocalPlayer.Object);
                Assert.AreEqual(2, changes);
            }
            finally { factory.Destroy(body); }
        }

        [Test]
        public void ClicksOfTheViewMode_ArriveAsTheSourcesClicks_NewestKept()
        {
            var source = new BodyClickSource();
            for (int i = 0; i < BodyClicks.MaxPending + 2; i++) BodyClicks.Ground(new Vector3(i, 0f, 0f), i % 2 == 0);
            BodyClicks.Stop();
            int taken = 0;
            WorldClick last = default;
            while (source.TryTake(out WorldClick click)) { last = click; taken++; }
            Assert.AreEqual(BodyClicks.MaxPending, taken, "older clicks are dropped, the newest kept");
            Assert.IsTrue(last.Stop);
        }

        [Test]
        public void TwoClients_TheOwnerHasOneLocalBody_AndItsGroundClickWalksTheServerBody()
        {
            using var rig = BodyRig.Create(2);
            var factories = new ClientBodyFactory[2];
            for (int i = 0; i < 2; i++)
            {
                factories[i] = new ClientBodyFactory(m_View);
                PlayerPresentationSetup.Install(rig.Tw.Clients[i], factories[i], i == 0 ? new BodyClickSource() : null, registerLocalPlayer: i == 0);
            }
            rig.Enter(0);
            rig.Enter(1);
            World client0 = rig.Tw.Clients[0];
            int ok = rig.Tw.TickUntil(() => CountIn(client0) == 2, 200);
            Assert.GreaterOrEqual(ok, 0, "client 0 shows both players");
            rig.Tick(30);

            GhostBodyEntry mine = null;
            int locals = 0;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, client0)) continue;
                if (e.IsLocal) { mine = e; locals++; }
                BodyRoleTag tag = e.Body.GetComponent<BodyRoleTag>();
                Assert.AreEqual(e.IsLocal, tag.IsLocal, "the tag agrees with the registry");
                Assert.AreEqual(e.IsLocal, e.Body.GetComponent<PointAndClickPlayer>() != null, "only the local body has the click code");
            }
            Assert.AreEqual(1, locals);
            Assert.AreSame(mine.Body, LocalPlayer.Object, "LocalPlayer is the local ClientView, never a server body");
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, rig.Server)) Assert.IsFalse(e.Body.GetComponent<BodyRoleTag>().IsLocal);

            GameObject serverBody = rig.BodyOf(rig.KeyOf(0));
            float before = serverBody.transform.position.x;
            BodyClicks.Ground(serverBody.transform.position + new Vector3(5f, 0f, 0f), false);
            rig.Tick(60);
            Assert.Greater(serverBody.transform.position.x, before + 2f, "the click reached the ghost and the server body followed");
            Assert.AreEqual(0, BodyClicks.Count);
        }

        static int CountIn(World w)
        {
            int n = 0;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries) if (ReferenceEquals(e.World, w)) n++;
            return n;
        }
    }
}
