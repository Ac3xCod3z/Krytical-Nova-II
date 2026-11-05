using System;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>
    /// The clicks of the local ClientView (<see cref="BodyClicks"/>, filled by <c>PointAndClickPlayer</c> in view mode) as the
    /// presentation's <see cref="IClickSource"/>: one per frame, a stop as a stop.
    /// </summary>
    public sealed class BodyClickSource : IClickSource
    {
        public bool TryTake(out WorldClick click)
        {
            click = default;
            if (!BodyClicks.TryTake(out BodyClicks.Click taken)) return false;
            if (taken.AttackTarget != null)
            {
                // the clicked body's ghost; a body that is no ghost's (gone meanwhile) is no attack
                click = new WorldClick { AttackGhostId = GhostIdOf(taken.AttackTarget) };
                return click.AttackGhostId != 0;
            }
            click = new WorldClick { Point = taken.Point, Run = taken.Run, Stop = taken.Stop, Jump = taken.Jump, Wall = taken.Wall, Normal = taken.Normal };
            return true;
        }

        /// <summary>The ghost id of the player body <paramref name="body"/> belongs to, or 0.</summary>
        public static int GhostIdOf(Transform body)
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (entry.Body == null || entry.World == null || !entry.World.IsCreated) continue;
                if (body != entry.Body.transform && !body.IsChildOf(entry.Body.transform)) continue;
                var em = entry.World.EntityManager;
                if (em.Exists(entry.Ghost) && em.HasComponent<Unity.NetCode.GhostInstance>(entry.Ghost))
                    return em.GetComponentData<Unity.NetCode.GhostInstance>(entry.Ghost).ghostId;
            }
            return 0;
        }
    }

    /// <summary>
    /// Client: makes the body of each player ghost from the real Player.prefab as a <see cref="NetRoleKind.ClientView"/> (M3a-2b step 7)
    /// through <see cref="PlayerBodyAssembler"/>: the local player's keeps input, camera and the offline-style stubs (decision D2: HUD
    /// works, nothing is saved, a banner says so); another player's keeps only what shows it (look, animation, nameplate, targeting).
    /// Before the local body wakes up, the scene's offline avatar is put away (it would keep <c>PlayerInventory.Local</c> and the input).
    /// </summary>
    public sealed class ClientBodyFactory : IPlayerBodyFactory
    {
        readonly Func<GameObject> m_Prefab;

        /// <summary>Put away the scene's offline avatar (the game: <c>OfflinePlayerHandoff.ReleaseOfflineAvatar</c>).</summary>
        public Action BeforeLocalBody;

        /// <summary>A body was made (the game configures its planner from the local body's agent, asks for looks).</summary>
        public Action<GameObject, PlayerBodyRequest> BodyMade;

        /// <summary>The local body shows the "not yet networked" banner (decision D2).</summary>
        public bool ShowBanner = true;

        public int Made { get; private set; }
        public int Unmade { get; private set; }

        public ClientBodyFactory(Func<GameObject> prefab) => m_Prefab = prefab;

        public ClientBodyFactory(GameObject prefab) : this(() => prefab) { }

        public GameObject Create(in PlayerBodyRequest request)
        {
            GameObject prefab = m_Prefab?.Invoke();
            if (prefab == null) return null; // the spawn system says so once and asks again
            if (request.IsLocal) BeforeLocalBody?.Invoke();
            GameObject body = PlayerBodyAssembler.Create(prefab, NetRoleKind.ClientView, PlayerKey.None, 0UL, request.IsLocal,
                request.Position, request.YawDegrees);
            body.name = request.IsLocal ? "Player (you)" : "Player " + request.NetworkId;
            if (request.IsLocal && ShowBanner) body.AddComponent<NotYetNetworkedBanner>();
            Made++;
            BodyMade?.Invoke(body, request);
            return body;
        }

        public void Destroy(GameObject body)
        {
            if (body == null) return;
            Unmade++;
            ServerBodies.DestroyBody(body);
        }
    }
}
