using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// A world target's stable key for as long as it lives in the session (today the object's network id; later whatever
    /// the netcode gives a ghost). Zero is "none". Never a pointer into a netcode type.
    /// </summary>
    public readonly struct TargetId : System.IEquatable<TargetId>
    {
        public readonly ulong Value;
        public TargetId(ulong value) => Value = value;
        public bool IsNone => Value == 0UL;
        public bool Equals(TargetId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TargetId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(TargetId a, TargetId b) => a.Value == b.Value;
        public static bool operator !=(TargetId a, TargetId b) => a.Value != b.Value;
    }

    public enum TargetKind
    {
        Other,
        Player,
        /// <summary>A creature of a Hostile Zone (carries <see cref="IHostile"/>).</summary>
        Hostile,
    }

    /// <summary>One thing the session keeps in step in the world: its key, what it is, where it is and whether it is this machine's.</summary>
    public readonly struct WorldTarget
    {
        public readonly TargetId Id;
        public readonly TargetKind Kind;
        /// <summary>The root of the actor.</summary>
        public readonly Transform Transform;
        /// <summary>False for an object of the session that has not been spawned into it (yet, or any more).</summary>
        public readonly bool IsLive;
        /// <summary>A player this machine controls.</summary>
        public readonly bool IsLocal;

        public WorldTarget(TargetId id, TargetKind kind, Transform transform, bool isLive, bool isLocal)
        {
            Id = id;
            Kind = kind;
            Transform = transform;
            IsLive = isLive;
            IsLocal = isLocal;
        }
    }

    /// <summary>
    /// The world's targets (players, hostiles, a training dummy, anything the session keeps in step), found by a stable
    /// key instead of through a netcode object. Networking implements it over the netcode of the day; UI and GameFeel
    /// read <see cref="WorldTargets"/>.
    /// </summary>
    public interface IWorldTargets
    {
        /// <summary>The target with this key, if the session has it.</summary>
        bool TryGet(TargetId id, out WorldTarget target);

        /// <summary>The target a transform (the actor itself or any child of it) belongs to.</summary>
        bool TryFind(Transform anyPart, out WorldTarget target);

        /// <summary>Adds every target of the session to <paramref name="into"/> (not cheap: not for a per-frame loop).</summary>
        void Collect(List<WorldTarget> into);

        /// <summary>Whether the session moves this object's transform itself (so its scale and root must be left alone).</summary>
        bool SyncsTransform(GameObject body);
    }

    /// <summary>The world's targets; answers "none" until Networking registers its source, so Editor tests and a game with no network read it safely.</summary>
    public static class WorldTargets
    {
        private static IWorldTargets source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => source = null;

        public static void Use(IWorldTargets newSource) => source = newSource;

        public static bool HasSource => source != null;

        public static bool TryGet(TargetId id, out WorldTarget target)
        {
            target = default;
            return source != null && !id.IsNone && source.TryGet(id, out target);
        }

        public static bool TryFind(Transform anyPart, out WorldTarget target)
        {
            target = default;
            return anyPart != null && source != null && source.TryFind(anyPart, out target);
        }

        public static void Collect(List<WorldTarget> into)
        {
            if (into == null) return;
            if (source != null) source.Collect(into);
        }

        public static bool SyncsTransform(GameObject body) => body != null && source != null && source.SyncsTransform(body);

        /// <summary>The root of the actor a transform belongs to, or the transform itself when no session knows it.</summary>
        public static Transform RootOf(Transform anyPart) =>
            TryFind(anyPart, out WorldTarget target) && target.Transform != null ? target.Transform : anyPart;

        /// <summary>A player (of any machine).</summary>
        public static bool IsPlayer(Transform actor) => TryFind(actor, out WorldTarget target) && target.Kind == TargetKind.Player;

        /// <summary>The player of this machine, or anyone in a game with no live session around the actor.</summary>
        public static bool IsLocalPlayer(Transform actor)
        {
            if (actor == null) return false;
            if (!TryFind(actor, out WorldTarget target) || !target.IsLive) return true;
            return target.Kind == TargetKind.Player && target.IsLocal;
        }
    }
}
