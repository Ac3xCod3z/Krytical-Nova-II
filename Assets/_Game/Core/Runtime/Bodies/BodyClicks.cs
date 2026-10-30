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

        private static void Add(Click click)
        {
            while (Pending.Count >= MaxPending) Pending.Dequeue();
            Pending.Enqueue(click);
        }
    }
}
