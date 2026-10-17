using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The dedicated server: the game built with Unity's Dedicated Server target, so it runs as a console program with
    /// no window, no graphics and no sound, and with UNITY_SERVER defined (the HUD, menus and game feel are left out;
    /// <c>NetworkConnectionManager</c> listens as soon as the world loads). Same scenes as the test build
    /// (<see cref="BuildSceneOrder"/>). Windows for this PC and the OptiPlex, Linux for a rented machine later.
    /// Next to the build it writes a size report like <see cref="TestBuildBuilder"/>'s.
    /// <para>Beside the exe it puts the control page's files (<c>ControlPage</c>, from <c>Networking/ControlPage~</c> and
    /// the comic fonts) and a start script (<c>Start Server.bat</c>, or <c>start-server.sh</c> on Linux) that shows the log in
    /// its window and starts the server again after a restart. The Editor is put back on the build target it was on
    /// before.</para>
    /// </summary>
    public static class ServerBuildBuilder
    {
        public const string BuildRoot = "Builds/Server";
        public const string ExeName = "KryticalNovaServer";
        /// <summary>The server's settings (KryticalNova.Networking.ServerLaunchOptions.FileName): kept across a rebuild.</summary>
        private const string SettingsFileName = "server-settings.json";
        private const string PageSource = "Assets/_Game/Networking/ControlPage~";
        private static readonly string[] PageFonts = { "Assets/_Game/UI/Travel/Fonts/Bangers.ttf", "Assets/_Game/UI/Travel/Fonts/Roboto-Bold.ttf" };
        /// <summary>KryticalNova.Networking.ServerLifetime.RestartExitCode: the start script starts the server again.</summary>
        private const int RestartExitCode = 3;

        public enum Os { Windows, Linux }

        /// <summary>True while a server build runs (<see cref="ServerSceneStripper"/> reads it).</summary>
        public static bool IsBuilding { get; private set; }

        public static string OutputFolder(Os os) => $"{BuildRoot}/{os}/{ExeName}";
        public static string ReportPath(Os os) => $"{BuildRoot}/BuildReport-{os}.txt";

        [MenuItem("Krytical Nova/Build/Dedicated Server (Windows)", priority = 52)]
        private static void MenuWindows() => MenuBuild(Os.Windows);

        [MenuItem("Krytical Nova/Build/Dedicated Server (Linux)", priority = 53)]
        private static void MenuLinux() => MenuBuild(Os.Linux);

        private static void MenuBuild(Os os)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Dedicated Server", "Stop Play first.", "OK");
                return;
            }
            string missing = ModuleMissing(os);
            if (missing != null)
            {
                EditorUtility.DisplayDialog($"Dedicated Server ({os})", missing, "OK");
                return;
            }
            string busy = FolderBusy(OutputFolder(os));
            if (busy != null)
            {
                EditorUtility.DisplayDialog($"Dedicated Server ({os})", busy, "OK");
                return;
            }
            Build(os);
        }

        /// <summary>What Unity lacks for a server build for this system, in words; null when the module is there.</summary>
        public static string ModuleMissing(Os os)
        {
            string engine = os == Os.Windows ? "windowsstandalonesupport" : "LinuxStandaloneSupport";
            string prefix = os == Os.Windows ? "win64_server_" : "linux64_server_";
            string variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", engine, "Variations");
            bool there = Directory.Exists(variations) && Directory.GetDirectories(variations, prefix + "*mono").Length > 0;
            return there ? null
                : $"A {os} dedicated server build needs Unity's module \"{os} Dedicated Server Build Support\" for {Application.unityVersion} " +
                  "(Unity Hub > Installs > the gear on this version > Add modules). Restart the Editor after installing.";
        }

        /// <summary>
        /// Why the server's folder cannot be replaced now, or null. A running server holds its exe and UnityPlayer.dll
        /// open, so neither can be opened for writing; nothing is moved or changed to find out. Deleting into a running
        /// server's folder would leave it half gone.
        /// </summary>
        public static string FolderBusy(string folder)
        {
            foreach (string name in new[] { ExeName + ".exe", ExeName + ".x86_64", "UnityPlayer.dll" })
            {
                string file = Path.Combine(folder, name);
                if (!File.Exists(file)) continue;
                try
                {
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return $"The server in {folder} is still running ({name} is in use). Stop the server (close its window, " +
                           "or Stop on the control page), then build again. Nothing was changed.";
                }
            }
            return null;
        }

        /// <summary>
        /// Builds without asking, as the build of the newest release (online step 4): a game joins only a server of its own
        /// build, so the server carries the number of the release staged last (none: a development build). True when the
        /// build succeeded; the report is written either way.
        /// </summary>
        public static bool Build(Os os) => Build(os, null);

        /// <summary>The same, into another folder (the measuring tools' own server, chunk 7); null is the owner's server folder.</summary>
        public static bool Build(Os os, string intoFolder) =>
            ReleasePublisher.WithVersion(ReleasePublisher.LastReleaseBuild(), () => BuildAsIs(os, intoFolder));

        private static bool BuildAsIs(Os os, string intoFolder)
        {
            string missing = ModuleMissing(os);
            if (missing != null)
            {
                Debug.LogError("[Build] " + missing);
                return false;
            }

            BuildSceneOrder.Apply();
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] There are no scenes to build.");
                return false;
            }

            // Only our own output folder: a file left from an older build must not travel with this one. The server's
            // settings file is the owner's, not the build's: it is kept across the rebuild.
            string folder = intoFolder ?? OutputFolder(os);
            string busy = FolderBusy(folder);
            if (busy != null)
            {
                Debug.LogError("[Build] " + busy);
                return false;
            }
            string settings = Path.Combine(folder, SettingsFileName), keptSettings = intoFolder == null
                ? Path.Combine(BuildRoot, os + "-" + SettingsFileName + ".kept")
                : folder.TrimEnd('/', '\\') + "-" + SettingsFileName + ".kept";
            if (File.Exists(settings)) File.Copy(settings, keptSettings, true);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            BuildTarget target = os == Os.Windows ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneLinux64;
            string exe = folder + "/" + ExeName + (os == Os.Windows ? ".exe" : ".x86_64");
            BuildTarget targetWas = EditorUserBuildSettings.activeBuildTarget;
            StandaloneBuildSubtarget subtargetWas = EditorUserBuildSettings.standaloneBuildSubtarget;

            BuildReport report;
            try
            {
                IsBuilding = true;
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = exe,
                    targetGroup = BuildTargetGroup.Standalone,
                    target = target,
                    subtarget = (int)StandaloneBuildSubtarget.Server,
                    // A clean build: the scenes must go through ServerSceneStripper, not come from the cache of a client build.
                    options = BuildOptions.CompressWithLz4HC | BuildOptions.CleanBuildCache
                });
            }
            finally
            {
                IsBuilding = false;
                // Put back after the build, and even when it failed: the settings are the owner's, never the build's.
                if (File.Exists(keptSettings))
                {
                    Directory.CreateDirectory(folder);
                    if (File.Exists(settings)) File.Delete(settings);
                    File.Move(keptSettings, settings);
                }
                // A server build may leave the Editor on the server target, where the HUD and menu tools are left out.
                if (EditorUserBuildSettings.activeBuildTarget != targetWas || EditorUserBuildSettings.standaloneBuildSubtarget != subtargetWas)
                {
                    EditorUserBuildSettings.standaloneBuildSubtarget = subtargetWas;
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, targetWas);
                }
            }

            TestBuildBuilder.WriteSizeReport(report, scenes, $"Dedicated server ({os})", "Scripting backend: Mono, Dedicated Server target", ReportPath(os));
            bool ok = report.summary.result == BuildResult.Succeeded;
            if (ok) AddBesideTheServer(os, folder);
            if (ok) Debug.Log($"[Build] Dedicated server ({os}) written to {folder} ({TestBuildBuilder.Megabytes(report.summary.totalSize)}). Sizes: {ReportPath(os)}");
            else Debug.LogError($"[Build] Dedicated server ({os}) {report.summary.result}: {report.summary.totalErrors} errors. See {ReportPath(os)}");
            return ok;
        }
    
        /// <summary>The control page's files and fonts, and the start script.</summary>
        private static void AddBesideTheServer(Os os, string folder)
        {
            string page = Path.Combine(folder, "ControlPage");
            Directory.CreateDirectory(Path.Combine(page, "fonts"));
            foreach (string file in Directory.GetFiles(PageSource))
                File.Copy(file, Path.Combine(page, Path.GetFileName(file)), true);
            foreach (string font in PageFonts)
                File.Copy(font, Path.Combine(page, "fonts", Path.GetFileName(font)), true);

            if (os == Os.Windows)
            {
                File.WriteAllText(Path.Combine(folder, "Start Server.bat"),
                    "@echo off\r\n" +
                    "rem Starts the Krytical Nova server with its log in this window. Type commands here (gm.help).\r\n" +
                    "rem Closing this window stops the server safely: everyone is saved first.\r\n" +
                    "title Krytical Nova Server\r\n" +
                    "cd /d \"%~dp0\"\r\n" +
                    ":start\r\n" +
                    // By its full path: a bare name is not looked for in the current folder where
                    // NoDefaultCurrentDirectoryInExePath is set.
                    "\"%~dp0" + ExeName + ".exe\" -logFile -\r\n" +
                    $"if %errorlevel%=={RestartExitCode} goto start\r\n" +
                    // Started by the owner's game (LocalServer): the window closes with the server.
                    "if /i \"%~1\"==\"--close\" exit /b\r\n" +
                    "echo.\r\n" +
                    "echo The server has stopped. Press a key to close this window.\r\n" +
                    "pause > nul\r\n");
            }
            else
            {
                File.WriteAllText(Path.Combine(folder, "start-server.sh"),
                    "#!/bin/sh\n" +
                    "# Starts the Krytical Nova server; starts it again after a restart.\n" +
                    "cd \"$(dirname \"$0\")\"\n" +
                    $"while true; do ./{ExeName}.x86_64 -logFile -; [ $? -eq {RestartExitCode} ] || break; done\n");
            }
        }
    }
}
