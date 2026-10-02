using System;
using Unity.Netcode;

namespace KryticalNova.World
{
    /// <summary>
    /// The entire replicated world-environment state (~48 bytes). It only changes when the weather
    /// changes or a GM edits the clock; every client derives time of day and blend progress locally
    /// from <see cref="NetworkManager.ServerTime"/>.
    /// </summary>
    public struct WorldEnvironmentState : INetworkSerializable, IEquatable<WorldEnvironmentState>
    {
        /// <summary>Clock time at which time of day equals <see cref="TimeOffset01"/>.</summary>
        public double CycleEpoch;
        public float DayLengthSeconds;
        public float TimeOffset01;
        /// <summary>1 = normal, 0 = paused (GM).</summary>
        public float TimeScale;

        public byte FromWeather;
        public byte ToWeather;
        public double TransitionStart;
        public float TransitionSeconds;

        /// <summary>Shared seed for cloud-coverage drift and lightning, so every client sees the same sky.</summary>
        public int Seed;

        /// <summary>Wind heading (degrees) rolled when FromWeather / ToWeather began. Held for the whole cycle.</summary>
        public float FromWindHeading;
        public float ToWindHeading;

        /// <summary>Wind speed (m/s) rolled when FromWeather / ToWeather began. NaN = the profile's fixed speed.</summary>
        public float FromWindSpeed;
        public float ToWindSpeed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref CycleEpoch);
            serializer.SerializeValue(ref DayLengthSeconds);
            serializer.SerializeValue(ref TimeOffset01);
            serializer.SerializeValue(ref TimeScale);
            serializer.SerializeValue(ref FromWeather);
            serializer.SerializeValue(ref ToWeather);
            serializer.SerializeValue(ref TransitionStart);
            serializer.SerializeValue(ref TransitionSeconds);
            serializer.SerializeValue(ref Seed);
            serializer.SerializeValue(ref FromWindHeading);
            serializer.SerializeValue(ref ToWindHeading);
            serializer.SerializeValue(ref FromWindSpeed);
            serializer.SerializeValue(ref ToWindSpeed);
        }

        public float TimeOfDay01(double now)
        {
            if (DayLengthSeconds <= 0f) return TimeOffset01;
            double t = TimeOffset01 + (now - CycleEpoch) * TimeScale / DayLengthSeconds;
            return (float)(t - Math.Floor(t));
        }

        /// <summary>Whole days elapsed on the world clock (drives slow cycles such as moon phases).</summary>
        public long DayCount(double now)
        {
            if (DayLengthSeconds <= 0f) return 0;
            return (long)Math.Floor(TimeOffset01 + (now - CycleEpoch) * TimeScale / DayLengthSeconds);
        }

        /// <summary>Linear transition progress 0-1.</summary>
        public float TransitionProgress(double now)
        {
            if (TransitionSeconds <= 0f) return 1f;
            double p = (now - TransitionStart) / TransitionSeconds;
            return (float)Math.Max(0.0, Math.Min(1.0, p));
        }

        /// <summary>Moves every absolute timestamp from one clock to another (offline ↔ server time).</summary>
        public void Rebase(double oldNow, double newNow)
        {
            double delta = newNow - oldNow;
            CycleEpoch += delta;
            TransitionStart += delta;
        }

        public bool Equals(WorldEnvironmentState o) =>
            CycleEpoch.Equals(o.CycleEpoch) && DayLengthSeconds.Equals(o.DayLengthSeconds) &&
            TimeOffset01.Equals(o.TimeOffset01) && TimeScale.Equals(o.TimeScale) &&
            FromWeather == o.FromWeather && ToWeather == o.ToWeather &&
            TransitionStart.Equals(o.TransitionStart) && TransitionSeconds.Equals(o.TransitionSeconds) && Seed == o.Seed &&
            FromWindHeading.Equals(o.FromWindHeading) && ToWindHeading.Equals(o.ToWindHeading) &&
            FromWindSpeed.Equals(o.FromWindSpeed) && ToWindSpeed.Equals(o.ToWindSpeed);

        public override bool Equals(object obj) => obj is WorldEnvironmentState o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(CycleEpoch, DayLengthSeconds, TimeOffset01, FromWeather, ToWeather, TransitionStart, Seed);
    }
}
