using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    // ---------------------------------------------------------------------------------------------------------------
    // The "go in game" handshake of M3a-2b: an approved connection (it has a NetworkId and a PlayerKey) tells the server which
    // character it wants to play. The server hands the request to the game (IKnAdmission) and only then lets snapshots flow
    // (NetworkStreamInGame). No Gameplay type travels here: the look is opaque text the game rebuilds into its own state.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Client to server, once the connection has a NetworkId: page <see cref="Page"/> of <see cref="PageCount"/> of the character
    /// text. Every page repeats the ids. The account is NEVER in the request: the server takes it from its own records.
    /// </summary>
    public struct GoInGameRequest : IRpcCommand
    {
        public FixedString64Bytes CharacterId;
        public FixedString64Bytes Name;
        public byte Page;
        public byte PageCount;
        public FixedString512Bytes Chunk;
    }

    /// <summary>What a client sends to enter the world (the game fills it from its character session).</summary>
    public struct GoInGamePayload
    {
        public string CharacterId;
        public string Name;
        /// <summary>The character's look as ASCII text (JSON); at most <see cref="GoInGamePages.MaxChars"/> characters.</summary>
        public string RecipeText;
    }

    /// <summary>Where the client system gets its <see cref="GoInGamePayload"/> (Gameplay implements it; Networking never sees a character).</summary>
    public interface IKnGoInGameSource
    {
        /// <summary>False while the client has nothing to send yet (the system asks again next tick).</summary>
        bool TryGet(World clientWorld, out GoInGamePayload payload);
    }

    /// <summary>What the server hands the game when a connection asked to go in game.</summary>
    public struct KnAdmissionRequest
    {
        public Unity.Entities.Entity Connection;
        public KryticalNova.Core.PlayerKey Key;
        /// <summary>The registry id the key was minted for (<c>ConnectionPlayerKey.Serial</c>).</summary>
        public ulong Serial;
        public int NetworkId;
        public string CharacterId;
        public string Name;
        public string RecipeText;
    }

    /// <summary>The game's part of entering the world (Persistence implements it): builds the player's body, loads the save, spawns the ghost.</summary>
    public interface IKnAdmission
    {
        /// <summary>Null = the player is in (the connection then gets snapshots); text = the reason to refuse, which the player reads.</summary>
        string Admit(in KnAdmissionRequest request);
    }

    /// <summary>Plain C# paging of the character text (so it is easy to test). Pages travel in <see cref="GoInGameRequest"/>.</summary>
    public static class GoInGamePages
    {
        public const int PageChars = KnApprovalChunks.MaxChunkBytes;
        public const int MaxPages = 3;
        /// <summary>The longest look text the request carries.</summary>
        public const int MaxChars = PageChars * MaxPages;

        /// <summary>True when the text is ASCII and fits <see cref="MaxPages"/> pages. An empty text is one empty page.</summary>
        public static bool TrySplit(string text, List<string> pages)
        {
            pages.Clear();
            if (string.IsNullOrEmpty(text))
            {
                pages.Add(string.Empty);
                return true;
            }
            return KnApprovalChunks.TrySplit(text, MaxPages, pages);
        }

        /// <summary>Puts pages together in any order; a page count above <see cref="MaxPages"/> is bad.</summary>
        public sealed class Assembler
        {
            private readonly KnRosterPages.Assembler m_Inner = new KnRosterPages.Assembler();

            public bool IsComplete => m_Inner.IsComplete;
            public bool IsBad { get; private set; }
            public string Text => m_Inner.Text;

            public KnRosterPages.Add Take(int index, int count, string data)
            {
                if (IsBad) return KnRosterPages.Add.Bad;
                if (count > MaxPages || (data != null && data.Length > PageChars))
                {
                    IsBad = true;
                    return KnRosterPages.Add.Bad;
                }
                KnRosterPages.Add result = m_Inner.Take(index, count, data);
                if (result == KnRosterPages.Add.Bad) IsBad = true;
                return result;
            }
        }
    }

    /// <summary>Client connection entity: the go-in-game request has been sent.</summary>
    public struct KnGoInGameSent : IComponentData { }

    /// <summary>Server connection entity: the admission was decided (admitted or refused); later requests are ignored.</summary>
    public struct KnAdmissionDone : IComponentData { }

    /// <summary>Where the client gets its payload from. Set by the game; reset for Enter Play Mode without domain reload.</summary>
    public static class KnGoInGame
    {
        public static IKnGoInGameSource Source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Source = null;
    }

    /// <summary>
    /// Client: once a connection has its NetworkId, sends the character once (<see cref="GoInGameRequest"/> pages) and puts its own
    /// side in game. Waits while the game has nothing to send.
    /// </summary>
#if KN_DEV_BOTS || UNITY_EDITOR
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
#else
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
#endif
    public partial class KnClientGoInGameSystem : SystemBase
    {
        private EntityQuery m_Waiting;
        private readonly List<string> m_Pages = new List<string>();

        /// <summary>Overrides <see cref="KnGoInGame.Source"/> for this world (tests, bots).</summary>
        public IKnGoInGameSource Source;

        protected override void OnCreate()
        {
            m_Waiting = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(), ComponentType.ReadOnly<NetworkId>(),
                ComponentType.Exclude<KnGoInGameSent>());
        }

        protected override void OnUpdate()
        {
            if (m_Waiting.IsEmpty) return;
            IKnGoInGameSource source = Source ?? KnGoInGame.Source;
            if (source == null || !source.TryGet(World, out GoInGamePayload payload)) return;
            var connections = m_Waiting.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                Entity connection = connections[i];
                EntityManager.AddComponent<KnGoInGameSent>(connection);
                if (!GoInGamePages.TrySplit(payload.RecipeText, m_Pages))
                {
                    Debug.LogError("[KnClient] the character text is not ASCII or longer than " + GoInGamePages.MaxChars + " characters; not going in game.");
                    EntityManager.AddComponentData(connection, new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ApprovalFailure });
                    continue;
                }
                for (int k = 0; k < m_Pages.Count; k++)
                {
                    Entity rpc = EntityManager.CreateEntity(typeof(GoInGameRequest), typeof(SendRpcCommandRequest));
                    EntityManager.SetComponentData(rpc, new GoInGameRequest
                    {
                        CharacterId = KnFixed.Text64(payload.CharacterId),
                        Name = KnFixed.Text64(payload.Name),
                        Page = (byte)k,
                        PageCount = (byte)m_Pages.Count,
                        Chunk = new FixedString512Bytes(m_Pages[k]),
                    });
                    EntityManager.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
                EntityManager.AddComponent<NetworkStreamInGame>(connection);
            }
            connections.Dispose();
        }
    }
}
