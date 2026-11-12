using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Publish to Test on the DOTS channel (owner, 8 Nov 2026), in THREE fresh Unity processes so nothing one build leaves in memory reaches
    /// the next (run 1: the entity server built right after the client in one process crashed in Netcode's GhostBehaviourTypeManager):
    /// <list type="number">
    /// <item><see cref="ClientBatch"/>: the full game as a release, IL2CPP + Burst, <c>KN_ENTITIES_CLIENT</c> plus the ghost bake scene;
    /// checks that no server define or headless switch reached it (<see cref="ServerLeakProblem"/>); the bundle gate (maps match their
    /// scenes, <see cref="BundleAuditGate"/>) must pass. Writes the build number to <see cref="PendingPath"/>.</item>
    /// <item><see cref="ServerBatch"/>: the entity server as the same build, IL2CPP + Burst.</item>
    /// <item><see cref="UploadBatch"/>: stage, upload under the <see cref="Channel"/> badge only (never test or live), the launcher on the
    /// channel. Never promotes.</item>
    /// </list>
    /// Each step refuses to run unless the one before it succeeded. <c>Publish~/publish-dots.ps1</c> runs the three in order.
    /// </summary>
    public static class DotsChannelPublisher
    {
        public const string Channel = "dots";
        public const string Root = "Builds/Release-DOTS";
        public const string GameFolder = Root + "/Game";
        public const string StoreFolder = Root + "/Store";
        public const string LauncherFolder = Root + "/DOTS Test Launcher";
        public const string LogPath = Root + "/release-log.txt";
        /// <summary>The build number the running publish is on, and which steps passed ("client", "server").</summary>
        public const string PendingPath = Root + "/pending.txt";
        public static readonly string[] Defines = { "KN_ENTITIES_CLIENT" };
        /// <summary>Defines that belong to servers only: none may be in the client build (owner, 25 Oct black sky; 8 Nov).</summary>
        public static readonly string[] ServerOnlyDefines = { "KN_ENTITIES_SERVER", "KN_TEST_LANE", "KN_DEV_BOTS", "UNITY_SERVER", "KN_JOIN_HERE", "KN_PROBE" };

        // ------------------------------------------------------------------ the three steps

        public static void ClientBatch() => Exit(Step("client", Client));
        public static void ServerBatch() => Exit(Step("server", Server));
        public static void UploadBatch() => Exit(Step("upload", Upload));

        static bool Step(string name, Func<bool> work)
        {
            Directory.CreateDirectory(Root);
            Log($"--- DOTS publish step '{name}' {DateTime.Now}");
            try { return work(); }
            catch (Exception e) { return Fail("threw: " + e); }
        }

        static void Exit(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Client()
        {
            if (Channel == "test" || Channel == "live") return Fail("the DOTS channel must not be test or live");
            string busy = ServerBuildBuilder.FolderBusy(ServerBuildBuilder.EntityOutputFolder);
            if (busy != null) return Fail("the entity server is running; stop it first (it is rebuilt to match): " + busy);
            string leak = ServerLeakProblem();
            if (leak != null) return Fail(leak);
            // -dotsreuse (client-only mode, owner 8 Nov 2026): rebuild the game as the LAST build number; the entity server of that number is
            // kept as it is (checked: its dots-build.txt), so the two still match.
            bool reuse = Array.IndexOf(Environment.GetCommandLineArgs(), "-dotsreuse") >= 0;
            int build = reuse ? LastBuild() : LastBuild() + 1;
            if (reuse && (build <= 0 || ServerBuildNumber() != build)) return Fail($"client-only mode: the entity server is not build {build} (rebuild both instead)");
            File.WriteAllText(PendingPath, build + "\n");

            bool built = WithBurst(() =>
            {
                var output = new TestBuildBuilder.Output
                {
                    Folder = GameFolder, ReportPath = Root + "/BuildReport.txt", Title = "DOTS channel build", Options = BuildOptions.None,
                    ExtraDefines = Defines, ExtraScenes = new[] { KnLinuxTestBuild.GhostBakeScenePath },
                };
                return ReleasePublisher.WithVersion(build, () => TestBuildBuilder.Build(ScriptingImplementation.IL2CPP, output));
            });
            if (!built) return Fail("the game did not build (see " + Root + "/BuildReport.txt)");
            Log($"Client built as build {build}: IL2CPP, Burst AOT on, defines {string.Join(",", Defines)} (server-only defines checked: none).");

            BundleAuditGate.Result audit = BundleAuditGate.Audit(GameFolder, Root, build);
            Log(audit.Report.TrimEnd());
            if (!audit.Ok) return Fail("the bundle audit found maps that differ from their scenes; nothing will be uploaded");
            File.AppendAllText(PendingPath, reuse ? "client\nserver\n" : "client\n");
            if (reuse) Log($"Client-only mode: the entity server stays build {build}.");
            return true;
        }

        static string ServerBuildFile => ServerBuildBuilder.EntityOutputFolder + "/dots-build.txt";

        /// <summary>The DOTS build number the entity server was last built as (written by the server step), or 0.</summary>
        static int ServerBuildNumber() =>
            File.Exists(ServerBuildFile) && int.TryParse(File.ReadAllText(ServerBuildFile).Trim(), out int n) ? n : 0;

        static bool Server()
        {
            int build = PendingBuild("client");
            if (build <= 0) return Fail("no client step passed for this publish");
            string busy = ServerBuildBuilder.FolderBusy(ServerBuildBuilder.EntityOutputFolder);
            if (busy != null) return Fail("the entity server is running: " + busy);
            bool ok = ReleasePublisher.WithVersion(build, () => ServerBuildBuilder.BuildEntityServer(ScriptingImplementation.IL2CPP));
            if (!ok) return Fail("the entity server did not build (" + ServerBuildBuilder.EntityReportPath + ")");
            Log($"Entity server built as build {build}: IL2CPP, Burst AOT on.");
            File.WriteAllText(ServerBuildFile, build + "\n");
            File.AppendAllText(PendingPath, "server\n");
            return true;
        }

        static bool Upload()
        {
            int build = PendingBuild("client", "server");
            if (build <= 0) return Fail("the client and server steps did not both pass for this publish");
            ReleasePublisher.Settings settings = ReleasePublisher.Settings.Load();
            if (!ReleasePublisher.BuildTools(settings)) return Fail("the launcher or the release tool did not build");
            string launcher = Path.GetFullPath(ReleasePublisher.ToolsFolder + "/Launcher/" + ReleasePublisher.LauncherName);
            if (!ReleasePublisher.RunTool($"stage --game \"{Path.GetFullPath(GameFolder)}\" --launcher \"{launcher}\" --out \"{Path.GetFullPath(StoreFolder)}\" --version \"v{ReleasePublisher.BaseVersion()}\" --build {build}", out string staged))
                return Fail("staging failed: " + staged);
            Log(staged.Trim());
            if (!settings.HasBucket) return Fail("Cloud Content Delivery is not set up");
            if (!ReleasePublisher.RunUgs($"ccd entries sync \"{Path.GetFullPath(StoreFolder)}\" -b {settings.bucketName} -x -r -u {Channel} -n \"DOTS build {build}\" -e {settings.environment} -p {settings.projectId}", out string synced))
                return Fail("the upload to Cloud Content Delivery failed: " + synced);
            Log($"Uploaded DOTS build {build} to the {Channel} badge.");
            Directory.CreateDirectory(LauncherFolder);
            File.Copy(launcher, LauncherFolder + "/" + ReleasePublisher.LauncherName, true);
            File.WriteAllText(LauncherFolder + "/launcher.json", "{\"channel\":\"" + Channel + "\"}");
            Log("Launcher on the " + Channel + " channel: " + Path.GetFullPath(LauncherFolder));
            File.Delete(PendingPath);
            return true;
        }

        // ------------------------------------------------------------------ checks and helpers

        /// <summary>
        /// Null when the client build cannot carry server-only code: no server define in the Standalone defines or in this build's own
        /// list, and the Editor on the Player subtarget (a Server subtarget makes headless switch-offs; TestBuildBuilder's ClientBuildGuard
        /// forces Player again). Else what is wrong.
        /// </summary>
        public static string ServerLeakProblem() => ServerLeakProblem(Defines);

        /// <summary><see cref="ServerLeakProblem()"/> for a client build with its own list of defines (the cutover's carries the Sky Lift switch too).</summary>
        public static string ServerLeakProblem(string[] clientDefines)
        {
            string standalone = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone) ?? string.Empty;
            foreach (string define in ServerOnlyDefines)
            {
                if (Array.IndexOf(standalone.Split(';'), define) >= 0) return $"the Standalone defines hold the server-only {define}";
                if (Array.IndexOf(clientDefines, define) >= 0) return $"the DOTS client's own defines hold the server-only {define}";
            }
            return null;
        }

        internal static bool WithBurst(Func<bool> work)
        {
            string burstPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", KnWindowsTestBuild.BurstSettingsPath);
            string burstWas = File.Exists(burstPath) ? File.ReadAllText(burstPath) : null;
            try
            {
                File.WriteAllText(burstPath, KnWindowsTestBuild.BurstOnJson);
                BurstAotMarker.ClearStaleMarker();
                return work();
            }
            finally
            {
                if (burstWas != null) File.WriteAllText(burstPath, burstWas); else File.Delete(burstPath);
            }
        }

        public static int LastBuild()
        {
            string manifest = StoreFolder + "/manifest.json";
            if (!File.Exists(manifest)) return 0;
            Match match = Regex.Match(File.ReadAllText(manifest), "\"build\"\\s*:\\s*(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        /// <summary>The pending build number when every named step passed; 0 otherwise.</summary>
        static int PendingBuild(params string[] passed)
        {
            if (!File.Exists(PendingPath)) return 0;
            string[] lines = File.ReadAllLines(PendingPath);
            if (lines.Length == 0 || !int.TryParse(lines[0].Trim(), out int build)) return 0;
            foreach (string step in passed) if (Array.IndexOf(lines, step) < 0) return 0;
            return build;
        }

        static bool Fail(string why)
        {
            Log("FAILED: " + why);
            Debug.LogError("[Release DOTS] " + why);
            return false;
        }

        static void Log(string line)
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(LogPath, line + Environment.NewLine);
            Debug.Log("[Release DOTS] " + line);
        }
    }
}
