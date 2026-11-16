using System;
using System.Collections.Generic;
using KryticalNova.Core;
#if !UNITY_SERVER
using KryticalNova.Player.Entities.Presentation;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>What <see cref="PlayerBodyAssembler.Apply"/> did to the components of one body (type names).</summary>
    public sealed class BodyBuildReport
    {
        public readonly List<string> Stripped = new List<string>();
        public readonly List<string> Disabled = new List<string>();
        /// <summary>Wanted gone, but another kept component requires it (RequireComponent), so it was only switched off.</summary>
        public readonly List<string> Downgraded = new List<string>();
    }

    /// <summary>A body that exists but has not woken up: under an inactive holder, tagged and stripped (M3a-2b design 2.1).</summary>
    public readonly struct BodyBuild
    {
        public readonly GameObject Body;
        public readonly GameObject Holder;
        public readonly BodyRoleTag Tag;
        public readonly BodyBuildReport Report;

        public BodyBuild(GameObject body, GameObject holder, BodyRoleTag tag, BodyBuildReport report)
        {
            Body = body;
            Holder = holder;
            Tag = tag;
            Report = report;
        }
    }

    /// <summary>
    /// Builds a player body from the real prefab without any network spawn: instantiate under an inactive holder (so no Awake or
    /// OnEnable has run), write the <see cref="BodyRoleTag"/>, strip the parts the role does not use (<see cref="PlayerRoleTable"/>),
    /// then activate. Everything a component asks <see cref="NetRole"/> in Awake, OnEnable and Start already gets the answer.
    /// </summary>
    public static class PlayerBodyAssembler
    {
        /// <summary>Build and activate in one go.</summary>
        public static GameObject Create(GameObject prefab, NetRoleKind role, PlayerKey key, ulong connectionSerial, bool isLocal,
            Vector3 position, float yawDegrees)
        {
            return Activate(Build(prefab, role, key, connectionSerial, isLocal, position, yawDegrees));
        }

        /// <summary>Steps 1 to 4 of the design: the body is still asleep (inactive in the hierarchy) when this returns.</summary>
        public static BodyBuild Build(GameObject prefab, NetRoleKind role, PlayerKey key, ulong connectionSerial, bool isLocal,
            Vector3 position, float yawDegrees)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            BodyShape shape = PlayerRoleTable.ShapeOf(role, isLocal);

            var holder = new GameObject("BodyHolder");
            holder.SetActive(false);
            GameObject body = Object.Instantiate(prefab, holder.transform);
            body.name = prefab.name;
#if !UNITY_SERVER
            BodySpawnCost.Mark(BodySpawnCost.Step.Instantiate);
#endif

            BodyRoleTag tag = body.AddComponent<BodyRoleTag>();
            tag.Assign(role, key, connectionSerial, isLocal);

            BodyBuildReport report = Apply(body, shape);

            body.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            body.GetComponentsInChildren(true, s_Rigidbodies);
            foreach (Rigidbody rb in s_Rigidbodies)
            {
                rb.isKinematic = true; // a dynamic body would be pushed by physics against the ghost that moves it
                rb.useGravity = false;
            }
            s_Rigidbodies.Clear();
#if !UNITY_SERVER
            BodySpawnCost.Mark(BodySpawnCost.Step.Strip);
#endif
            return new BodyBuild(body, holder, tag, report);
        }

        /// <summary>Wakes the body up: it leaves the holder (Awake, OnEnable and Start run with the role in place) and the holder goes.</summary>
        public static GameObject Activate(BodyBuild build)
        {
            if (build.Body == null) return null;
            build.Body.transform.SetParent(null, true);
            if (build.Holder != null)
            {
                if (Application.isPlaying) Object.Destroy(build.Holder);
                else Object.DestroyImmediate(build.Holder);
            }
#if !UNITY_SERVER
            BodySpawnCost.Mark(BodySpawnCost.Step.Wake);   // the parts' Awake / OnEnable ran just now
#endif
            return build.Body;
        }

        /// <summary>
        /// Strips and disables the parts of <paramref name="root"/> for a shape. Parts are destroyed dependants first: a part another
        /// kept part requires (RequireComponent) is only switched off, never destroyed (Unity refuses, and the dependant would break).
        /// </summary>
        public static BodyBuildReport Apply(GameObject root, BodyShape shape)
        {
            var report = new BodyBuildReport();
            // the lists are kept between bodies and the type facts cached (R3-5 B: ~2.7 MB a client body; this ran reflection and a
            // name string per component of the whole prefab for every body, with the same answer each time). Same result as before.
            List<Component> strip = s_Strip, disable = s_Disable;
            strip.Clear();
            disable.Clear();
            root.GetComponentsInChildren(true, s_All);
            foreach (Component c in s_All)
            {
                if (c == null || c is Transform) continue;
                PartAction action = PlayerRoleTable.ActionFor(shape, NameOf(c.GetType()));
                if (action == PartAction.Strip) strip.Add(c);
                else if (action == PartAction.Disable) disable.Add(c);
            }

            bool progress = true;
            while (progress && strip.Count > 0)
            {
                progress = false;
                for (int i = strip.Count - 1; i >= 0; i--)
                {
                    Component c = strip[i];
                    if (c == null) { strip.RemoveAt(i); continue; }
                    if (IsRequiredByAnother(c)) continue;
                    string name = NameOf(c.GetType());
                    Object.DestroyImmediate(c);
                    report.Stripped.Add(name);
                    strip.RemoveAt(i);
                    progress = true;
                }
            }
            foreach (Component c in strip)
            {
                if (c == null) continue;
                SwitchOff(c);
                report.Downgraded.Add(NameOf(c.GetType()));
            }
            foreach (Component c in disable)
            {
                if (c == null) continue;
                SwitchOff(c);
                report.Disabled.Add(NameOf(c.GetType()));
            }
            s_All.Clear();
            strip.Clear();
            disable.Clear();
            return report;
        }

        /// <summary>True when another component on the same object has a RequireComponent that this one satisfies.</summary>
        public static bool IsRequiredByAnother(Component c)
        {
            Type have = c.GetType();
            c.gameObject.GetComponents(s_Siblings);
            try
            {
                foreach (Component other in s_Siblings)
                {
                    if (other == null || other == c) continue;
                    foreach (RequireComponent attribute in RequiresOf(other.GetType()))
                    {
                        if (Satisfies(attribute.m_Type0, have) || Satisfies(attribute.m_Type1, have) || Satisfies(attribute.m_Type2, have))
                            return true;
                    }
                }
                return false;
            }
            finally { s_Siblings.Clear(); }
        }

        // kept between bodies (main thread only; Apply never runs inside another Apply)
        static readonly List<Component> s_All = new List<Component>(512);
        static readonly List<Component> s_Strip = new List<Component>(64);
        static readonly List<Component> s_Disable = new List<Component>(64);
        static readonly List<Component> s_Siblings = new List<Component>(32);
        static readonly List<Rigidbody> s_Rigidbodies = new List<Rigidbody>(8);
        // facts about a type never change while the game runs: asked once per type, not once per component of every body
        static readonly Dictionary<Type, string> s_Names = new Dictionary<Type, string>(256);
        static readonly Dictionary<Type, RequireComponent[]> s_Requires = new Dictionary<Type, RequireComponent[]>(256);

        static string NameOf(Type type)
        {
            if (!s_Names.TryGetValue(type, out string name)) s_Names[type] = name = type.Name;
            return name;
        }

        static RequireComponent[] RequiresOf(Type type)
        {
            if (!s_Requires.TryGetValue(type, out RequireComponent[] requires))
                s_Requires[type] = requires = (RequireComponent[])type.GetCustomAttributes(typeof(RequireComponent), true);
            return requires;
        }

        private static bool Satisfies(Type required, Type have) => required != null && required.IsAssignableFrom(have);

        private static void SwitchOff(Component c)
        {
            if (c is Behaviour behaviour) behaviour.enabled = false;
            else if (c is Collider collider) collider.enabled = false;
            else if (c is Renderer renderer) renderer.enabled = false;
        }
    }
}
