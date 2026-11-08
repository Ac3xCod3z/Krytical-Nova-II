using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Item 10 (Burst AOT never runs in our player builds since 30 Oct 2026): a read-only check of what Unity 6000.6's own Burst player
    /// step (<c>BurstAOTCompilerPostprocessor</c> -> <c>OnPostBuildPlayerScriptDLLsImpl</c>, DOTS 18's ILSpy read) tests before it compiles:
    /// <c>BurstCompilerOptions.ForceDisableBurstCompilation</c> (the <c>-readonly</c> / <c>-burst-disable-compilation</c> argument, or a
    /// secondary Editor process) and the target's <c>BurstPlatformAotSettings.EnableBurstCompilation</c> as Burst itself reads it, before
    /// and after the build tools' Burst-on file is written (put back afterwards). Builds nothing. Lines start with <c>[KnBurst]</c>.
    /// Batch: <c>-batchmode -quit -executeMethod KryticalNova.EditorTools.KnBurstCheck.Run</c>.
    /// </summary>
    public static class KnBurstCheck
    {
        public static void Run()
        {
            try { Check(); }
            catch (Exception e) { Debug.LogError("[KnBurst] the check failed: " + e); }
        }

        static void Check()
        {
            Log("command line: " + string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
            Log($"batch mode {Application.isBatchMode}, active target {EditorUserBuildSettings.activeBuildTarget}, standalone subtarget {EditorUserBuildSettings.standaloneBuildSubtarget}");
            Log("UNITY_BURST_DEBUG=" + (Environment.GetEnvironmentVariable("UNITY_BURST_DEBUG") ?? "(unset)"));

            Type options = FindType("Unity.Burst.BurstCompilerOptions");
            if (options == null) Log("BurstCompilerOptions: NOT FOUND");
            else
            {
                Log("BurstCompilerOptions in " + options.Assembly.GetName().Name);
                Log("  ForceDisableBurstCompilation = " + Static(options, "ForceDisableBurstCompilation"));
                Log("  _forceDisableBurstCompilation (the argument) = " + Static(options, "_forceDisableBurstCompilation"));
                Log("  IsSecondaryUnityProcess = " + Static(options, "IsSecondaryUnityProcess"));
            }
            Type compiler = FindType("Unity.Burst.BurstCompiler");
            object global = compiler?.GetProperty("Options", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (global != null)
                Log($"BurstCompiler.Options: EnableBurstCompilation {Member(global, "EnableBurstCompilation")}, IsEnabled {Member(global, "IsEnabled")}");

            // who answers the player step (Burst's postprocessor and anything else that generates native plugins)
            Type step = FindType("UnityEditor.Build.IGenerateNativePluginsForAssemblies");
            if (step == null) Log("IGenerateNativePluginsForAssemblies: NOT FOUND");
            else
                foreach (Type t in TypeCache.GetTypesDerivedFrom(step))
                    Log($"native plugin step: {t.FullName} ({t.Assembly.GetName().Name}){(t.IsAbstract ? " abstract" : "")}");

            Type aot = FindType("Unity.Burst.Editor.BurstPlatformAotSettings");
            if (aot == null) { Log("BurstPlatformAotSettings: NOT FOUND"); return; }
            Settings(aot, BuildTarget.StandaloneWindows64, "as the project has it");
            Settings(aot, BuildTarget.StandaloneLinux64, "as the project has it");

            // the Windows test build's Burst-on file written now, read again by Burst, then put back as it was
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", KnWindowsTestBuild.BurstSettingsPath);
            string before = File.Exists(path) ? File.ReadAllText(path) : null;
            Log($"{KnWindowsTestBuild.BurstSettingsPath}: {(before == null ? "absent" : before.Replace("\n", " "))}");
            try
            {
                File.WriteAllText(path, KnWindowsTestBuild.BurstOnJson);   // the build's own file, word for word
                Settings(aot, BuildTarget.StandaloneWindows64, "after the Burst-on file was written in this session");
            }
            finally
            {
                if (before == null) File.Delete(path); else File.WriteAllText(path, before);
            }
        }

        static void Settings(Type aot, BuildTarget target, string when)
        {
            MethodInfo get = aot.GetMethod("GetOrCreateSettings", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (get == null) { Log("GetOrCreateSettings: NOT FOUND"); return; }
            object[] args = get.GetParameters().Length == 2 ? new object[] { (BuildTarget?)target, false } : new object[] { (BuildTarget?)target };
            object s = get.Invoke(null, args);
            Log($"{target} {when}: EnableBurstCompilation {Member(s, "EnableBurstCompilation")}, EnableOptimisations {Member(s, "EnableOptimisations")}");
        }

        static Type FindType(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic;

        static string Static(Type type, string name)
        {
            PropertyInfo p = type.GetProperty(name, Any | BindingFlags.Static);
            if (p != null) return p.GetValue(null)?.ToString() ?? "null";
            FieldInfo f = type.GetField(name, Any | BindingFlags.Static);
            return f != null ? f.GetValue(null)?.ToString() ?? "null" : "(no such member)";
        }

        static string Member(object o, string name)
        {
            if (o == null) return "(null)";
            Type type = o.GetType();
            PropertyInfo p = type.GetProperty(name, Any | BindingFlags.Instance);
            if (p != null) return p.GetValue(o)?.ToString() ?? "null";
            FieldInfo f = type.GetField(name, Any | BindingFlags.Instance);
            return f != null ? f.GetValue(o)?.ToString() ?? "null" : "(no such member)";
        }

        static void Log(string line) => Debug.Log("[KnBurst] " + line);
    }
}
