using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Core
{
    /// <summary>
    /// Compiles the game's scripts before they are needed. A Mono build turns a method into machine code the first
    /// time it runs, in the middle of whatever frame that is: the first cast of a skill, the first window opened,
    /// the first rain all hitch for it, and a session's slow frames thin out only as fewer firsts are left (the
    /// log of 11 Oct 2026: 35 slow frames in the first five minutes, 3 in the last). Here every method of the
    /// game's own assemblies, and of the engine's largest that run in play (the interface, the render pipeline,
    /// networking, animation), is compiled in the menus and behind the Loading page, a few milliseconds a frame,
    /// so the firsts cost nothing later.
    /// <para>The methods are listed on a thread of their own (reading what an assembly holds runs none of it) and
    /// compiled on the main thread: compiling can run a class's static set-up, which may call the engine, and the
    /// engine answers on the main thread only. A method is compiled by asking for its function pointer: under
    /// Unity's Mono <c>RuntimeHelpers.PrepareMethod</c> does nothing (40,000 methods "prepared" in a millisecond),
    /// while the pointer cannot be given without the code (2,500 methods took 211 ms the first time and 0.6 ms the
    /// second). An IL2CPP build is compiled before it ships: there this does nothing and <see cref="Done"/> is true
    /// from the start. Nor does it run in the Editor, where it would only slow the first seconds of every Play
    /// after a compile, unless the EditorPref <see cref="EditorPrefKey"/> is set to try it.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScriptWarmUp : MonoBehaviour
    {
        // The game's own assemblies all start with this.
        private const string OwnPrefix = "KryticalNova";
        // The engine's and the packages' that are large and run every frame of play.
        private static readonly string[] Others =
        {
            "UnityEngine.UIElementsModule", "UnityEngine.UI", "Unity.TextMeshPro", "Unity.RenderPipelines.Core.Runtime",
            "Unity.RenderPipelines.Universal.Runtime", "Unity.Netcode.Runtime", "Unity.Cinemachine", "Animancer",
        };
        private const double CalmMilliseconds = 4.0;     // a frame's share in the menus
        private const double HurryMilliseconds = 14.0;   // and behind the Loading page, where nothing moves
        private const double PlayMilliseconds = 0.5;     // in the world, should it get there unfinished
        /// <summary>EditorPrefs, a bool: run in the Editor's Play too. Off unless set.</summary>
        public const string EditorPrefKey = "kn.scriptWarmUp.inEditor";

        /// <summary>True when there is nothing left to compile (always, in an IL2CPP build).</summary>
        public static bool Done { get; private set; } = !Needed;
        /// <summary>0 to 1.</summary>
        public static float Progress => Done ? 1f : total > 0 ? Mathf.Clamp01(next / (float)total) : 0f;
        /// <summary>Set while a screen that shows nothing moving is up: more of each frame is spent.</summary>
        public static bool Hurry { get; set; }
        /// <summary>Set while the player is playing: next to nothing of a frame is spent.</summary>
        public static bool Playing { get; set; }

#if ENABLE_IL2CPP
        private const bool Needed = false;
#else
        private const bool Needed = true;
#endif

        // These live as long as the scripts do: compiled once is compiled for every later Play of the Editor.
        private static List<RuntimeMethodHandle> methods;
        private static volatile bool listed;
        private static int next, total, failed;
        private static double spentMs;
        private static bool started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Hurry = Playing = false;
            if (!Needed || Done || Application.isBatchMode) return;
#if UNITY_EDITOR
            if (!started && !UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false))
            {
                Done = true;
                return;
            }
#endif
            var host = new GameObject("Script Warm-Up");
            DontDestroyOnLoad(host);
            host.AddComponent<ScriptWarmUp>();
            if (started) return;
            started = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var lister = new Thread(() => List(assemblies)) { IsBackground = true, Name = "Script Warm-Up", Priority = System.Threading.ThreadPriority.BelowNormal };
            lister.Start();
        }

        // Everything with a body that can be compiled as it stands: nothing generic, nothing abstract, nothing the engine implements.
        private static void List(Assembly[] assemblies)
        {
            var found = new List<RuntimeMethodHandle>(60000);
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Assembly assembly in assemblies)
            {
                string name = assembly.GetName().Name;
                if (!name.StartsWith(OwnPrefix, StringComparison.Ordinal) && Array.IndexOf(Others, name) < 0) continue;
                if (name.IndexOf("Editor", StringComparison.Ordinal) >= 0 || name.IndexOf("Tests", StringComparison.Ordinal) >= 0) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }
                foreach (Type type in types)
                {
                    if (type == null || type.IsInterface || type.ContainsGenericParameters) continue;
                    try
                    {
                        foreach (MethodInfo method in type.GetMethods(all)) Add(found, method);
                        foreach (ConstructorInfo constructor in type.GetConstructors(all))
                            if (!constructor.IsStatic) Add(found, constructor); // a static one is the class's set-up: it runs when the class is first used
                    }
                    catch (Exception) { }
                }
            }
            methods = found;
            total = found.Count;
            listed = true;
        }

        private static void Add(List<RuntimeMethodHandle> found, MethodBase method)
        {
            const MethodImplAttributes noBody = MethodImplAttributes.InternalCall | MethodImplAttributes.Native | MethodImplAttributes.Runtime;
            if (method.IsAbstract || method.ContainsGenericParameters) return;
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0 || (method.GetMethodImplementationFlags() & noBody) != 0) return;
            found.Add(method.MethodHandle);
        }

        private void Update()
        {
            if (Done) { Finish(); return; }
            if (!listed) return;
            double budget = Playing ? PlayMilliseconds : Hurry ? HurryMilliseconds : CalmMilliseconds;
            long began = Stopwatch.GetTimestamp();
            double spent = 0;
            while (next < total && spent < budget)
            {
                try { methods[next].GetFunctionPointer(); }
                catch (Exception) { failed++; } // one that cannot be compiled ahead is compiled when it runs, as before
                next++;
                spent = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
            }
            spentMs += spent;
            if (next < total) return;
            Done = true;
            methods = null;
            KryticalNova.Diagnostics.PerformanceLog.Mark("warmup.scripts", total + " methods compiled ahead, " + failed + " left for later", (long)spentMs);
            Debug.Log($"[WarmUp] {total} methods compiled ahead in {spentMs:0} ms of the menus' time ({failed} could not be).");
            Finish();
        }

        private void Finish()
        {
            enabled = false;
            Destroy(gameObject);
        }
    }
}
