using System;
using System.IO;
using System.Text.RegularExpressions;
using KryticalNova.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The cutover publish (12 Nov 2026): the DOTS game and the RELEASE server of the entity netcode built ONCE with ONE build number and
    /// uploaded to the owner's <c>test</c> channel (never <c>live</c>: that is promoted by hand after his test). Three fresh Unity runs, as
    /// <see cref="DotsChannelPublisher"/> (a server built in the client's process crashed Netcode's type manager): <see cref="ClientBatch"/>
    /// (release client, <c>KN_ENTITIES_CLIENT</c>, the Sky Lift on since build 16, the news gate <see cref="LauncherNewsGate"/>, server-define leak check, bundle audit), <see cref="ServerBatch"/>
    /// (<see cref="EntityReleaseServer"/>: no <c>KN_TEST_LANE</c>), <see cref="UploadBatch"/> (stage, verify the audit, upload, never promote).
    /// The number continues the REAL store's numbering (the main project's <c>Builds/Release/Store</c>, read only) and is never below the
    /// one staged here, so the owner's launcher on <c>test</c> sees the build as newer. This project's own store keeps the staged files;
    /// the main project's <c>Builds</c> is never written. <c>Publish~/publish-cutover.ps1</c> runs the three in order.
    /// </summary>
    public static class CutoverPublisher
    {
        public const string Channel = "test";
        public const string Root = "Builds/Release-Cutover";
        public const string GameFolder = Root + "/Game";
        public const string StoreFolder = Root + "/Store";
        public const string LauncherFolder = Root + "/Test Launcher";
        public const string LogPath = Root + "/release-log.txt";
        /// <summary>The build number the running publish is on, and which steps passed ("client", "server").</summary>
        public const string PendingPath = Root + "/pending.txt";
        /// <summary>The client's defines: the entity netcode game. No server define (checked); no <see cref="SkyLiftLink.OffDefine"/> since build 16 (owner, 12 Nov 2026: the Sky Lift is on).</summary>
        public static readonly string[] Defines = { "KN_ENTITIES_CLIENT" };

        // ------------------------------------------------------------------ rules (pure, tested)

        /// <summary>Null when <paramref name="channel"/> may be uploaded to by this publisher: only <c>test</c>. Else why not.</summary>
        public static string ChannelProblem(string channel) =>
            channel == "test" ? null : $"the cutover publish uploads to the test channel only (asked for \"{channel}\"); live is promoted by hand";

        /// <summary>The cutover's build number: one above the real store's last and above anything staged here.</summary>
        public static int NextBuild(int realStoreLast, int stagedHere) => Math.Max(Math.Max(realStoreLast, stagedHere), 0) + 1;

        /// <summary>The <c>"build"</c> of a store manifest's text, or 0.</summary>
        public static int BuildOf(string manifestText)
        {
            if (string.IsNullOrEmpty(manifestText)) return 0;
            Match match = Regex.Match(manifestText, "\"build\"\\s*:\\s*(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        /// <summary>The real store (the main project's Builds/Release/Store): where the numbering continues from. Read only.</summary>
        public static string RealStoreManifest =>
            Path.Combine(Environment.GetEnvironmentVariable("KN_NGO_PROJECT") ?? @"C:\Users\dlaco\Krytical Nova II", "Builds", "Release", "Store", "manifest.json");

        static int ReadBuild(string manifestPath)
        {
            try { return File.Exists(manifestPath) ? BuildOf(File.ReadAllText(manifestPath)) : 0; }
            catch (Exception) { return 0; }
        }

        public static int RealStoreLast() => ReadBuild(RealStoreManifest);
        public static int StagedHere() => ReadBuild(StoreFolder + "/manifest.json");

        // ------------------------------------------------------------------ the three steps

        public static void ClientBatch() => Exit(Step("client", Client));
        public static void ServerBatch() => Exit(Step("server", Server));
        public static void UploadBatch() => Exit(Step("upload", Upload));

        static bool Step(string name, Func<bool> work)
        {
            Directory.CreateDirectory(Root);
            Log($"--- cutover publish step '{name}' {DateTime.Now}");
            try { return work(); }
            catch (Exception e) { return Fail("threw: " + e); }
        }

        static void Exit(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Client()
        {
            string channel = ChannelProblem(Channel);
            if (channel != null) return Fail(channel);
            string busy = ServerBuildBuilder.FolderBusy(EntityReleaseServer.OutputFolder);
            if (busy != null) return Fail("the release server is running; stop it first (it is rebuilt to match): " + busy);
            string leak = DotsChannelPublisher.ServerLeakProblem(Defines);
            if (leak != null) return Fail(leak);
            int real = RealStoreLast();
            if (real <= 0) return Fail("the real store's manifest could not be read (" + RealStoreManifest + "): the build number cannot continue its numbering");
            int build = NextBuild(real, StagedHere());
            // The news gate first (12 Nov 2026: build 16 went live with no news): no long build for news that would be refused.
            string news = LauncherNewsGate.FileProblem(build);
            if (news != null) return Fail("no launcher news for build " + build + ": " + news);
            File.WriteAllText(PendingPath, build + "\n");

            bool built = DotsChannelPublisher.WithBurst(() =>
            {
                var output = new TestBuildBuilder.Output
                {
                    Folder = GameFolder, ReportPath = Root + "/BuildReport.txt", Title = "DOTS cutover build", Options = BuildOptions.None,
                    ExtraDefines = Defines, ExtraScenes = new[] { KnLinuxTestBuild.GhostBakeScenePath },
                };
                return ReleasePublisher.WithVersion(build, () => TestBuildBuilder.Build(ScriptingImplementation.IL2CPP, output));
            });
            if (!built) return Fail("the game did not build (see " + Root + "/BuildReport.txt)");
            Log($"Client built as build {build} (real store was {real}): IL2CPP, Burst AOT on, defines {string.Join(",", Defines)} (server-only defines checked: none).");

            BundleAuditGate.Result audit = BundleAuditGate.Audit(GameFolder, Root, build);
            Log(audit.Report.TrimEnd());
            if (!audit.Ok) return Fail("the bundle audit found maps that differ from their scenes; nothing will be uploaded");
            File.AppendAllText(PendingPath, "client\n");
            return true;
        }

        static string ServerBuildFile => EntityReleaseServer.OutputFolder + "/dots-build.txt";

        static bool Server()
        {
            int build = PendingBuild("client");
            if (build <= 0) return Fail("no client step passed for this publish");
            string busy = ServerBuildBuilder.FolderBusy(EntityReleaseServer.OutputFolder);
            if (busy != null) return Fail("the release server is running: " + busy);
            bool ok = ReleasePublisher.WithVersion(build, () => ServerBuildBuilder.BuildEntityServerRelease(ScriptingImplementation.IL2CPP));
            if (!ok) return Fail("the release server did not build (" + EntityReleaseServer.ReportPath + ")");
            Log($"Release server built as build {build}: IL2CPP, Burst AOT on, defines {string.Join(",", EntityReleaseServer.Defines)} (no KN_TEST_LANE).");
            File.WriteAllText(ServerBuildFile, build + "\n");
            File.AppendAllText(PendingPath, "server\n");
            return true;
        }

        static bool Upload()
        {
            string channel = ChannelProblem(Channel);
            if (channel != null) return Fail(channel);
            int build = PendingBuild("client", "server");
            if (build <= 0) return Fail("the client and server steps did not both pass for this publish");
            if (!File.Exists(ServerBuildFile) || File.ReadAllText(ServerBuildFile).Trim() != build.ToString())
                return Fail($"the release server in {EntityReleaseServer.OutputFolder} is not build {build}: the game would be told the server runs another build");
            ReleasePublisher.Settings settings = ReleasePublisher.Settings.Load();
            string news = LauncherNewsGate.FileProblem(build);
            if (news != null) return Fail("no launcher news for build " + build + ": " + news);
            if (!ReleasePublisher.BuildTools(settings)) return Fail("the launcher or the release tool did not build");
            string launcher = Path.GetFullPath(ReleasePublisher.ToolsFolder + "/Launcher/" + ReleasePublisher.LauncherName);
            if (!ReleasePublisher.RunTool($"stage --game \"{Path.GetFullPath(GameFolder)}\" --launcher \"{launcher}\" --out \"{Path.GetFullPath(StoreFolder)}\" --version \"v{ReleasePublisher.BaseVersion()}\" --build {build}{LauncherNewsGate.StageArgument()}", out string staged))
                return Fail("staging failed: " + staged);
            Log(staged.Trim());
            string stagedNews = LauncherNewsGate.StagedProblem(staged, build);
            if (stagedNews != null) return Fail(stagedNews + "; nothing will be uploaded");
            // The gate again, read-only: the staged bundles are the ones that passed the audit.
            string unsafeReason = BundleAuditGate.VerifyStaged(Root, StoreFolder + "/manifest.json");
            if (unsafeReason != null) return Fail("the staged release is not the audited one: " + unsafeReason);
            if (!settings.HasBucket) return Fail("Cloud Content Delivery is not set up");
            if (!ReleasePublisher.RunUgs($"ccd entries sync \"{Path.GetFullPath(StoreFolder)}\" -b {settings.bucketName} -x -r -u {Channel} -n \"DOTS cutover build {build}\" -e {settings.environment} -p {settings.projectId}", out string synced))
                return Fail("the upload to Cloud Content Delivery failed: " + synced);
            Log($"Uploaded DOTS cutover build {build} to the {Channel} badge (live untouched).");
            Directory.CreateDirectory(LauncherFolder);
            File.Copy(launcher, LauncherFolder + "/" + ReleasePublisher.LauncherName, true);
            File.WriteAllText(LauncherFolder + "/launcher.json", "{\"channel\":\"" + Channel + "\"}");
            Log("Launcher on the " + Channel + " channel: " + Path.GetFullPath(LauncherFolder));
            File.Delete(PendingPath);
            return true;
        }

        // ------------------------------------------------------------------ helpers

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
            Debug.LogError("[Release cutover] " + why);
            return false;
        }

        static void Log(string line)
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(LogPath, line + Environment.NewLine);
            Debug.Log("[Release cutover] " + line);
        }
    }
}
