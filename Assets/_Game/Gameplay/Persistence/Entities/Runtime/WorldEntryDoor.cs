using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// When the entity netcode's world may be shown (Master, 31 Oct): the player's own body is made, the camera is on it and the frames
    /// are steady, so the first thing the player sees and does is the game, not the warm-up (GUI run clicktick-1: a press in 779 ms
    /// frames reached the server 11 ticks late). A slow PC is never kept out: <see cref="CapSeconds"/> after the body and the camera
    /// are ready the door opens anyway, and says so. Pure: <see cref="WorldEntryDoorSystem"/> ticks it every frame.
    /// </summary>
    public sealed class WorldEntryDoor
    {
        /// <summary>Steady = this long with every frame under <see cref="SteadyFrameMs"/>.</summary>
        public const float SteadySeconds = 1f, SteadyFrameMs = 50f;
        /// <summary>The door opens this long after the body and the camera are ready, steady or not.</summary>
        public const float CapSeconds = 10f;

        float m_ReadySince = -1f, m_SteadySince = -1f;

        public bool IsOpen { get; private set; }
        /// <summary>Why it opened ("steady frames" or "the cap"), null while shut.</summary>
        public string OpenedBy { get; private set; }
        /// <summary>Seconds from the body and camera being ready to the door opening.</summary>
        public float WaitedSeconds { get; private set; }
        /// <summary>The longest frame while it waited, milliseconds.</summary>
        public float WorstFrameMs { get; private set; }

        /// <summary>One frame: <paramref name="now"/> in seconds, the frame's length in milliseconds. Returns true on the frame it opens.</summary>
        public bool Tick(float now, float frameMs, bool bodyMade, bool cameraOnBody)
        {
            if (IsOpen) return false;
            if (!bodyMade || !cameraOnBody) { m_ReadySince = -1f; return false; }
            if (m_ReadySince < 0f) { m_ReadySince = now; m_SteadySince = now; WorstFrameMs = 0f; }
            WorstFrameMs = Mathf.Max(WorstFrameMs, frameMs);
            if (frameMs >= SteadyFrameMs) m_SteadySince = now;
            if (now - m_SteadySince >= SteadySeconds) return Open("steady frames", now);
            if (now - m_ReadySince >= CapSeconds) return Open("the cap", now);
            return false;
        }

        bool Open(string why, float now)
        {
            IsOpen = true;
            OpenedBy = why;
            WaitedSeconds = now - m_ReadySince;
            return true;
        }
    }

    /// <summary>
    /// Client: ticks the <see cref="WorldEntryDoor"/> the client world holds the join's door with (<c>JoinFlow.HoldUntilWorldReady</c>,
    /// installed by <see cref="PlayerWorldsSetup"/>): the local body from the registry, the camera from its <c>PointAndClickPlayer</c>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class WorldEntryDoorSystem : SystemBase
    {
        public WorldEntryDoor Door { get; set; }
        float m_Started = -1f;

        protected override void OnUpdate()
        {
            if (Door == null || Door.IsOpen) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (m_Started < 0f) m_Started = now;
            GameObject body = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null) { body = entry.Body; break; }
            var click = body != null ? body.GetComponent<KryticalNova.Player.PointAndClickPlayer>() : null;
            bool camera = click != null && click.IsCameraOnThisBody;
            if (Door.Tick(now, UnityEngine.Time.unscaledDeltaTime * 1000f, body != null, camera))
                Debug.Log($"[KnJoin] the world may be shown: {Door.OpenedBy} after {Door.WaitedSeconds:F1} s with the body and the camera ready " +
                          $"({now - m_Started:F1} s after the client world began; worst frame meanwhile {Door.WorstFrameMs:F0} ms)" +
                          (Door.OpenedBy == "the cap" ? $"; the frames never held under {WorldEntryDoor.SteadyFrameMs:F0} ms for {WorldEntryDoor.SteadySeconds:F0} s" : ""));
        }
    }
}
