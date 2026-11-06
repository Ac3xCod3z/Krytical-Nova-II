using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Items;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;
using Unity.NetCode;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// NGO PlayerEmotes.RequestPlay:193-194: an emote puts the weapon away first (the same sheathe as Shift). On the entity netcode the
    /// server does it in the emote's own tick, so every view gets the sheathe with the emote and plays it first.
    /// </summary>
    public class EmoteSheatheTests
    {
        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();

        sealed class FakeEmotes : MonoBehaviour, IBodyEmotes
        {
            public int Played, Stopped;
            public BodyEmoteState State => default;
            public void RunEmote(bool stop, string key) { if (stop) Stopped++; else Played++; }
            public void MirrorState(in BodyEmoteState state) { }
        }

        [Test]
        public void AnEmoteRequest_SheathesTheGhostsWeapon_InTheSameTick_AndAStopNeverDrawsIt()
        {
            using var rig = PlayerRig.Create(1, 0);
            Entity ghost = rig.ServerGhosts[0];
            EntityManager sem = rig.Server.EntityManager;
            var body = new GameObject("emote server body");
            var emotes = body.AddComponent<FakeEmotes>();
            GhostBodyRegistry.Register(rig.Server, ghost, KnTestWorld.NetworkIdOf(rig.Clients[0]), default, body, false);
            try
            {
                var drawn = sem.GetComponentData<CombatState>(ghost);
                MeleeRules.Draw(ref drawn, KnTestWorld.Singleton<NetworkTime>(rig.Server).ServerTick);
                drawn.TargetGhostId = 99;
                sem.SetComponentData(ghost, drawn);

                Send(rig, stop: false);
                Assert.GreaterOrEqual(rig.Tw.TickUntil(() => emotes.Played > 0, 60), 0, "the emote request reached the server body");
                CombatState after = sem.GetComponentData<CombatState>(ghost);
                Assert.AreEqual(0, after.WeaponDrawn, "the weapon was put away with the emote");
                Assert.AreEqual(0, after.TargetGhostId, "and the attack ended");

                Send(rig, stop: true);
                Assert.GreaterOrEqual(rig.Tw.TickUntil(() => emotes.Stopped > 0, 60), 0);
                Assert.AreEqual(0, sem.GetComponentData<CombatState>(ghost).WeaponDrawn, "a stop never draws it");
            }
            finally
            {
                GhostBodyRegistry.Unregister(rig.Server, ghost, out _);
                Object.DestroyImmediate(body);
            }
        }

        static void Send(PlayerRig rig, bool stop)
        {
            EntityManager cem = rig.Clients[0].EntityManager;
            Entity rpc = cem.CreateEntity(typeof(EmoteRpc), typeof(SendRpcCommandRequest));
            var ask = new EmoteRpc { Stop = (byte)(stop ? 1 : 0) };
            ask.Key.CopyFromTruncated("sheathe-test");
            cem.SetComponentData(rpc, ask);
        }
    }
}
