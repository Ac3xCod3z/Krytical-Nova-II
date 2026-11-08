using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Publish to Test on the DOTS channel (owner, 8 Nov 2026): the full game (Title, sign-in, character select, the maps) built as a release
    /// with <c>KN_ENTITIES_CLIENT</c> (it lists only the ENTITY TEST server on this PC and joins it with the entity netcode) and Burst AOT on,
    /// gated by the bundle audit (<see cref="BundleAuditGate"/>), staged in <see cref="Root"/> (its own store and build numbers), uploaded to
    /// Cloud Content Delivery under the <see cref="Channel"/> badge only, never <c>test</c> or <c>live</c>; then the entity server is rebuilt
    /// with the same build number (a game joins only a server of its own build) and a launcher on the channel is made in
    /// <see cref="LauncherFolder"/>. Never promotes. Batch: <c>-executeMethod KryticalNova.EditorTools.DotsChannelPublisher.RunInBatch</c>.
    /// </summary>
    public static class DotsChannelPublisher
    {
        public const string Channel = "dots";
        public const string Root = "Builds/Release-DOTS";
        public const string GameFolder = Root + "/Game";
        public const string StoreFolder = Root + "/Store";
        public const string LauncherFolder = Root + "/DOTS Test Launcher";
        public const string LogPath = Root + "/release-log.txt";
        public static readonly string[] Defines = { "KN_ENTITIES_CLIENT" };

        public static void RunInBatch()
        {
            bool ok = false;
            try { ok = Publish(); }
            catch (Exception e) { Log("FAILED (threw): " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static int LastBuild()
        {
            string manifest = StoreFolder + "/manifest.json";
            if (!File.Exists(manifest)) return 0;
            Match match = Regex.Match(File.ReadAllText(manifest), "\"build\"\\s*:\\s*(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        public static bool Publish()
        {
            Directory.CreateDirectory(Root);
            Log("--- Publish to the DOTS channel " + DateTime.Now);
            if (Channel == "test" || Channel == "live") return Fail("the DOTS channel must not be test or live");
            string busy = ServerBuildBuilder.FolderBusy(ServerBuildBuilder.EntityOutputFolder);
            if (busy != null) return Fail("the entity server is running, so it cannot be rebuilt to match: " + busy);
            ReleasePublisher.Settings settings = ReleasePublisher.Settings.Load();
            if (!ReleasePublisher.BuildTools(settings)) return Fail("the launcher or the release tool did not build");
            int build = LastBuild() + 1;

            // Burst AOT on for this build (KnWindowsTestBuild's settings), put back afterwards
            string burstPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", KnWindowsTestBuild.BurstSettingsPath);
            string burstWas = File.Exists(burstPath) ? File.ReadAllText(burstPath) : null;
            bool built;
            try
            {
                File.WriteAllText(burstPath, KnWindowsTestBuild.BurstOnJson);
                BurstAotMarker.ClearStaleMarker();
                var output = new TestBuildBuilder.Output
                {
                    Folder = GameFolder, ReportPath = Root + "/BuildReport.txt", Title = "DOTS channel build", Options = BuildOptions.None,
                    ExtraDefines = Defines, ExtraScenes = new[] { KnLinuxTestBuild.GhostBakeScenePath },
                };
                built = ReleasePublisher.WithVersion(build, () => TestBuildBuilder.Build(ScriptingImplementation.IL2CPP, output));
            }
            finally
            {
                if (burstWas != null) File.WriteAllText(burstPath, burstWas); else File.Delete(burstPath);
            }
            if (!built) return Fail("the game did not build (see " + Root + "/BuildReport.txt)");
            Log("Built build " + build + " with " + string.Join(",", Defines) + ", Burst AOT on.");

            BundleAuditGate.Result audit = BundleAuditGate.Audit(GameFolder, Root, build);
            Log(audit.Report.TrimEnd());
            if (!audit.Ok) return Fail("the bundle audit found maps that differ from their scenes; nothing was staged or uploaded");

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

            bool server = ReleasePublisher.WithVersion(build, ServerBuildBuilder.BuildEntityServer);
            Log(server ? $"Entity server rebuilt as build {build}." : "FAILED: the entity server did not build (" + ServerBuildBuilder.EntityReportPath + ").");
            return server;
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
