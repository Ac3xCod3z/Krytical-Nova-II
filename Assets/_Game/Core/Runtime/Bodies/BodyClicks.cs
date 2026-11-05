using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// The ground clicks of a ClientView body (M3a-2b decision D4): <c>PointAndClickPlayer</c> in its view mode keeps the camera, the
    /// selection and the ray it always cast, and puts the ground point here instead of steering an agent; the entity netcode's click
    /// source takes them out once per frame and turns them into the ghost's command. Main thread only; a few at most are kept (older
    /// ones are dropped: only the newest intention matters). Reset for Enter Play Mode without a domain reload.
    /// </summary>
    public static class BodyClicks
    {
        public struct Click
        {
            public Vector3 Point;
            public bool Run;
            public bool Stop;
            /// <summary>The click asks to jump there (the Jump movement skill is selected).</summary>
            public bool Jump;
            /// <summary>A click on a wall for Wallstride's wall walk, with the surface's normal there.</summary>
            public bool Wall;
            public Vector3 Normal;
            /// <summary>The second click on a hostile (M4.4j): attack this body. Null for every other click.</summary>
            public Transform AttackTarget;
        }

        /// <summary>Most clicks kept between two takes.</summary>
        public const int MaxPending = 4;

        private static readonly Queue<Click> Pending = new Queue<Click>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pending.Clear();
            LastGroundRealtime = 0d;
            LastGroundFrame = 0;
            GroundClicks = 0;
            LocalOnWall = false;
            LocalPerched = false;
        }

        public static int Count => Pending.Count;

        /// <summary>When the newest ground click was made (real time, frame) and how many there were: the click timing log reads them.</summary>
        public static double LastGroundRealtime { get; private set; }
        public static int LastGroundFrame { get; private set; }
        public static int GroundClicks { get; private set; }

        /// <summary>A click on the ground at <paramref name="point"/> (metres); <paramref name="run"/> = the run movement skill is selected.</summary>
        public static void Ground(Vector3 point, bool run)
        {
            LastGroundRealtime = Time.realtimeSinceStartupAsDouble;
            LastGroundFrame = Time.frameCount;
            GroundClicks++;
            Add(new Click { Point = point, Run = run });
        }

        /// <summary>A jump to <paramref name="point"/> (metres): the Jump movement skill is selected.</summary>
        public static void Jump(Vector3 point)
        {
            LastGroundRealtime = Time.realtimeSinceStartupAsDouble;
            LastGroundFrame = Time.frameCount;
            GroundClicks++;
            Add(new Click { Point = point, Jump = true });
        }

        /// <summary>
        /// A click for Wallstride's wall walk at <paramref name="point"/> on a surface facing <paramref name="normal"/>: onto a wall from the
        /// ground, or any click while the body is on a wall (NGO's <c>ServerWallClick</c> decides what it is).
        /// </summary>
        public static void Wall(Vector3 point, Vector3 normal, bool run, bool jump)
        {
            LastGroundRealtime = Time.realtimeSinceStartupAsDouble;
            LastGroundFrame = Time.frameCount;
            GroundClicks++;
            Add(new Click { Point = point, Normal = normal, Run = run, Jump = jump, Wall = true });
        }

        /// <summary>The local body stands or moves on a wall (its ghost says so): the view sends every click as a wall click.</summary>
        public static bool LocalOnWall { get; set; }

        /// <summary>The local body is perched in a tree: a click that is not on the ground is refused (NGO), any other drops down.</summary>
        public static bool LocalPerched { get; set; }

        /// <summary>The player asked to stop where they stand.</summary>
        public static void Stop() => Add(new Click { Stop = true });

        public static bool TryTake(out Click click)
        {
            if (Pending.Count > 0)
            {
                click = Pending.Dequeue();
                return true;
            }
            click = default;
            return false;
        }

        public static void Clear() => Pending.Clear();

        /// <summary>The second click on a hostile <paramref name="target"/> (NGO's StartAttackTarget and RequestAttackEntityServerRpc).</summary>
        public static void Attack(Transform target)
        {
            if (target != null) Add(new Click { AttackTarget = target });
        }

        private static void Add(Click click)
        {
            while (Pending.Count >= MaxPending) Pending.Dequeue();
            Pending.Enqueue(click);
        }
    }
}
