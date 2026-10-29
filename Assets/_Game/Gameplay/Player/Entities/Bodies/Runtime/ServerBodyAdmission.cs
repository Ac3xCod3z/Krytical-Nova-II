using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>What the game's rules get to give a new ServerBody its character.</summary>
    public struct ServerBodyContext
    {
        public PlayerKey Key;
        /// <summary>The registry id of the connection (<see cref="BodyRoleTag.ConnectionSerial"/>).</summary>
        public ulong Serial;
        public int NetworkId;
        /// <summary>The account the server proved at the join (its own records); empty when it has none. Never from the request.</summary>
        public string Account;
        public string CharacterId;
        public string Name;
        /// <summary>The look the client sent, as text (the game decodes it).</summary>
        public string RecipeText;
    }

    /// <summary>
    /// The game's part of a ServerBody (Persistence implements it: identity + save). Kept out of this assembly so the lifecycle can be
    /// tested with a stand-in body.
    /// </summary>
    public interface IServerBodyRules
    {
        /// <summary>
        /// The body is awake and tagged, nothing else exists for the player yet. Give it its character and load its save. Null = the
        /// player may enter; text = the reason the player reads (the body is then destroyed and no ghost is ever made).
        /// </summary>
        string Begin(GameObject body, in ServerBodyContext context);

        /// <summary>The ghost exists and the body follows it; the last moment before snapshots start (look for observers).</summary>
        void Admitted(GameObject body, Entity ghost, in ServerBodyContext context);
    }

    /// <summary>A spawn spot for a joining player that keeps clear of <paramref name="occupied"/> (body roots). False = none (the origin is used).</summary>
    public delegate bool SpawnPicker(IReadOnlyList<Vector3> occupied, out Vector3 position, out Quaternion rotation);

    /// <summary>
    /// Server: lets one player in (M3a-2b design section 3, decision D1 "body first, ghost last"):
    /// 1 pick a free spawn spot; 2 build the real body as a <see cref="NetRoleKind.ServerBody"/> (tagged, stripped, awake);
    /// 3 the rules give it its character and load the save, and may refuse: then the body goes and nothing else was made;
    /// 4 spawn the ghost on the body's spot with the player's key; 5 the body's mover drives the ghost, the registry links them;
    /// 6 <see cref="KnAdmissionSystem"/> puts the connection in game (snapshots start) when this returns null.
    /// </summary>
    public sealed class ServerBodyAdmission : IKnAdmission
    {
        public const string NotReadyText = "This server is not ready for players yet. Try again in a moment.";
        public const string FailedText = "The server could not make your character. Try again in a moment.";
        public const string AlreadyInText = "This player is already in the world.";

        readonly World m_World;
        readonly Func<GameObject> m_Prefab;
        readonly IServerBodyRules m_Rules;
        readonly List<Vector3> m_Occupied = new List<Vector3>();

        /// <summary>Where a new player stands; null = the origin.</summary>
        public SpawnPicker Picker;

        /// <summary>The ghost's walk and run speed from the body (rank, gear); null or no value = the ghost prefab's own.</summary>
        public Func<GameObject, MoveStats?> MoveStatsOf;

        /// <summary>One word per step: "body", "refused", "loaded", "ghost" (tests, the trace log).</summary>
        public Action<string> Trace;

        public ServerBodyAdmission(World serverWorld, GameObject bodyPrefab, IServerBodyRules rules)
            : this(serverWorld, () => bodyPrefab, rules)
        {
        }

        /// <param name="bodyPrefab">Asked at each admission (the prefab's holder may load after the world is made).</param>
        public ServerBodyAdmission(World serverWorld, Func<GameObject> bodyPrefab, IServerBodyRules rules)
        {
            m_World = serverWorld;
            m_Prefab = bodyPrefab;
            m_Rules = rules;
        }

        public int Admitted { get; private set; }
        public int Refused { get; private set; }

        public string Admit(in KnAdmissionRequest request)
        {
            GameObject prefab = m_Prefab?.Invoke();
            if (m_World == null || !m_World.IsCreated || m_Rules == null) return Refuse(NotReadyText);
            if (prefab == null)
            {
                Debug.LogError("[KnServer] No player prefab to build bodies from: nobody can enter.");
                return Refuse(NotReadyText);
            }
            if (!TryGhostPrefab(out Entity ghostPrefab))
            {
                Debug.LogError("[KnServer] No PlayerGhostPrefab in the server world (the ghost-prefab subscene is not loaded): nobody can enter.");
                return Refuse(NotReadyText);
            }
            if (GhostBodyRegistry.TryGetServerBody(request.Key, out _)) return Refuse(AlreadyInText);

            var context = new ServerBodyContext
            {
                Key = request.Key,
                Serial = request.Serial,
                NetworkId = request.NetworkId,
                Account = KryticalNova.Networking.ServerAccounts.TryGet(request.Key, out var proven) ? proven.Account ?? string.Empty : string.Empty,
                CharacterId = request.CharacterId ?? string.Empty,
                Name = request.Name ?? string.Empty,
                RecipeText = request.RecipeText ?? string.Empty,
            };

            // 1 the spot
            CollectOccupied();
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            if (Picker != null && !Picker(m_Occupied, out position, out rotation))
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }

            // 2 the body, awake with its role in place
            GameObject body;
            try
            {
                body = PlayerBodyAssembler.Create(prefab, NetRoleKind.ServerBody, request.Key, request.Serial, false, position, rotation.eulerAngles.y);
            }
            catch (Exception e)
            {
                Debug.LogError("[KnServer] building the body of " + request.Key + " failed: " + e);
                return Refuse(FailedText);
            }
            Trace?.Invoke("body");

            // 3 character and save; a refusal leaves nothing behind
            string refusal;
            try { refusal = m_Rules.Begin(body, in context); }
            catch (Exception e)
            {
                Debug.LogError("[KnServer] the rules threw while " + request.Key + " entered: " + e);
                refusal = FailedText;
            }
            if (refusal != null)
            {
                ServerBodies.DestroyBody(body);
                Trace?.Invoke("refused");
                return Refuse(refusal);
            }
            Trace?.Invoke("loaded");

            // 4 the ghost, last, on the body's spot
            EntityManager em = m_World.EntityManager;
            Transform root = body.transform;
            Entity ghost = PlayerEntitiesSetup.SpawnServer(em, ghostPrefab, request.NetworkId, PoseMath.Mm(root.position), PoseMath.Cdeg(root.rotation));
            em.AddComponentData(ghost, new PlayerGhostKey { Key = request.Key.Value });
            MoveStats? stats = null;
            try { stats = MoveStatsOf?.Invoke(body); }
            catch (Exception e) { Debug.LogError("[KnServer] reading the walk speed of " + request.Key + " failed: " + e); }
            if (stats.HasValue) em.SetComponentData(ghost, stats.Value);

            // 5 the body follows the ghost; rule code moves the ghost through the body's mover
            BodyRoleTag tag = body.GetComponent<BodyRoleTag>();
            if (tag != null) tag.Mover = new ServerBodyMover(m_World, ghost, root);
            GhostBodyRegistry.SetServerBody(request.Key, body);
            GhostBodyRegistry.Register(m_World, ghost, request.NetworkId, request.Key, body, false);
            Trace?.Invoke("ghost");

            try { m_Rules.Admitted(body, ghost, in context); }
            catch (Exception e) { Debug.LogError("[KnServer] the rules threw after " + request.Key + " entered: " + e); }
            Admitted++;
            return null;
        }

        string Refuse(string reason)
        {
            Refused++;
            return reason;
        }

        bool TryGhostPrefab(out Entity prefab)
        {
            prefab = Entity.Null;
            using var query = m_World.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerGhostPrefab>());
            if (query.CalculateEntityCount() != 1) return false;
            prefab = query.GetSingleton<PlayerGhostPrefab>().Prefab;
            return prefab != Entity.Null;
        }

        void CollectOccupied()
        {
            m_Occupied.Clear();
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (ReferenceEquals(entry.World, m_World) && entry.Body != null) m_Occupied.Add(entry.Body.transform.position);
        }
    }
}
