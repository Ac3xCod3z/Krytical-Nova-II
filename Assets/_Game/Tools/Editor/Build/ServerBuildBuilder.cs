using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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
    /// before, except in the server checkout (<see cref="BackgroundPublisher.ServerCheckoutFolder"/>), which stays on the
    /// Server subtarget so its scripts are never compiled again for nothing.</para>
    /// <para>The scripting backend is the build's own (Mono unless a caller asks for IL2CPP), set for the build and put back,
    /// whatever a client build left in ProjectSettings; the size report names the backend the build really used.</para>
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

        public static string OutputFolder(Os os) => $"{Root}/{os}/{ExeName}";
        public static string ReportPath(Os os) => $"{Root}/BuildReport-{os}.txt";

        private static string s_Root;

        /// <summary>
        /// Where servers are written: <see cref="BuildRoot"/>, except in a checkout whose <c>Builds</c> is a link to the main
        /// project's (the build and server checkouts), where it is the main project's own folder by its real path. Norton
        /// watches the server's exe by path (19 Oct 2026 it quarantined it and blocked that path) and its exclusions name the
        /// main project's <c>Builds</c>: a server written through a new link would be a new path to it.
        /// </summary>
        public static string Root => s_Root ?? (s_Root = Path.Combine(RealBuildsFolder(), "Server").Replace('\\', '/'));

        /// <summary>
        /// This project's <c>Builds</c>, or, when it is a link (a checkout made by BackgroundPublisher), the main project's
        /// <c>Builds</c> by its real path, found from the checkout's <c>.git</c> file
        /// (<c>gitdir: &lt;main&gt;/.git/worktrees/&lt;name&gt;</c>).
        /// </summary>
        public static string RealBuildsFolder()
        {
            string builds = Path.GetFullPath("Builds");
            if (!Directory.Exists(builds) || (new DirectoryInfo(builds).Attributes & FileAttributes.ReparsePoint) == 0) return builds;
            string dotGit = Path.GetFullPath(".git");
            if (!File.Exists(dotGit)) return builds;
            string line = File.ReadAllText(dotGit).Trim();
            if (!line.StartsWith("gitdir:", StringComparison.Ordinal)) return builds;
            // <main>/.git/worktrees/<name>: three folders up is the main project.
            string gitDir = Path.GetFullPath(line.Substring("gitdir:".Length).Trim());
            string main = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(gitDir)));
            string real = main == null ? null : Path.Combine(main, "Builds");
            return real != null && Directory.Exists(real) ? real : builds;
        }

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
        public static string ModuleMissing(Os os, ScriptingImplementation backend = ScriptingImplementation.Mono2x)
        {
            string engine = os == Os.Windows ? "windowsstandalonesupport" : "LinuxStandaloneSupport";
            string prefix = os == Os.Windows ? "win64_server_" : "linux64_server_";
            string variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", engine, "Variations");
            string kind = backend == ScriptingImplementation.IL2CPP ? "*il2cpp" : "*mono";
            bool there = Directory.Exists(variations) && Directory.GetDirectories(variations, prefix + kind).Length > 0;
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

        /// <summary>
        /// The same, into another folder (the measuring tools' own server, chunk 7; null is the owner's server folder) and
        /// with the scripting backend named. None named: Mono for Windows (the server's own); for Linux the project's Server
        /// backend, as before (ServerBuildBatch.BuildLinux's -il2cpp / -mono set it).
        /// </summary>
        public static bool Build(Os os, string intoFolder, ScriptingImplementation? backend = null)
        {
            ScriptingImplementation chosen = backend ?? (os == Os.Windows ? ScriptingImplementation.Mono2x : PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server));
            return ReleasePublisher.WithVersion(ReleasePublisher.LastReleaseBuild(), () => BuildAsIs(os, intoFolder, chosen));
        }

        private static string BackendName(ScriptingImplementation backend) => backend == ScriptingImplementation.IL2CPP ? "IL2CPP" : "Mono";

        // ------------------------------------------------------------------ the entity server (DOTS, 8 Nov 2026)

        /// <summary>
        /// The Netcode for Entities dedicated server (DOTS port), a NEW build beside the NGO one (owner, 8 Nov 2026): its own folder
        /// <c>Builds/EntityServer</c> (with its own save service copy and its own Krytical Nova Server app), never the NGO server's
        /// <c>Builds/Server</c>. Started by <c>KnBootstrap</c> / <c>KnServerEntry</c> (port, page port, save service, stop signals).
        /// </summary>
        public const string EntityBuildRoot = "EntityServer";
        /// <summary>
        /// <c>KN_ENTITIES_SERVER</c>: Netcode for GameObjects' server parts stay off (NetworkConnectionManager, ServerLifetime,
        /// ServerControlPage). <c>KN_TEST_LANE</c>: the server refuses the real server's ports (7777, 7778, 7801, 8090) until the
        /// cutover makes it the real one.
        /// </summary>
        public static readonly string[] EntityDefines = { "KN_ENTITIES_SERVER", "KN_TEST_LANE" };
        /// <summary>The test-lane ports the entity server gets in its first settings file (free of the real, test, measure and Play DOTS ports).</summary>
        public const int EntityTestPort = 7799, EntityTestPagePort = 8099;
        /// <summary>Marks the settings of an entity server, so the Krytical Nova Server app beside it runs it the entity way.</summary>
        public const string EntityNetcodeKey = "netcode";

        public static string EntityRootFolder => Path.Combine(RealBuildsFolder(), EntityBuildRoot).Replace('\\', '/');
        public static string EntityOutputFolder => $"{EntityRootFolder}/{ExeName}";
        public static string EntityReportPath => $"{EntityRootFolder}/BuildReport-Windows.txt";

        /// <summary>The entity server's scenes: the map (it boots there) and the scene that bakes the player ghost subscene into the build.</summary>
        public static string[] EntityScenes() => new[] { KnLinuxTestBuild.MapScenePath, KnLinuxTestBuild.GhostBakeScenePath };

        [MenuItem("Krytical Nova/Build/Entity Server (Windows, test lane)", priority = 55)]
        private static void MenuEntityServer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Entity Server", "Stop Play first.", "OK"); return; }
            string busy = FolderBusy(EntityOutputFolder);
            if (busy != null) { EditorUtility.DisplayDialog("Entity Server", busy, "OK"); return; }
            BuildEntityServer();
        }

        /// <summary>Batch: <c>-executeMethod KryticalNova.EditorTools.ServerBuildBuilder.BuildEntityServerBatch</c> (exits 0 when built, 1 when not).</summary>
        public static void BuildEntityServerBatch()
        {
            bool ok = false;
            try { ok = BuildEntityServer(); }
            catch (Exception e) { Debug.LogError("[Build] Entity server build threw " + e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Builds the entity server (Windows, Mono, Burst AOT on, not development) into <see cref="EntityOutputFolder"/>, with the save
        /// service and the Krytical Nova Server app beside it and a test-lane <c>server-settings.json</c> when it has none yet.
        /// </summary>
        public static bool BuildEntityServer() => BuildEntityServer(ScriptingImplementation.Mono2x);

        /// <summary>
        /// <see cref="BuildEntityServer()"/> with the backend named (the DOTS channel's final server is IL2CPP + Burst, owner 8 Nov 2026). For
        /// IL2CPP the Server target's stripping is Minimal (the game's assemblies stay whole: Odin, Netcode and the page find types by
        /// reflection) and its compiler Release, both put back afterwards, as the release client does.
        /// </summary>
        public static bool BuildEntityServer(ScriptingImplementation backend)
        {
            foreach (string needed in new[] { KnLinuxTestBuild.WorldAssetsPath, KnLinuxTestBuild.GhostBakeScenePath, KnLinuxTestBuild.MapScenePath })
                if (AssetDatabase.LoadMainAssetAtPath(needed) == null)
                {
                    Debug.LogError($"[Build] Entity server: {needed} is missing (Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets makes the ghost assets).");
                    return false;
                }
            Directory.CreateDirectory(EntityRootFolder);
            var entity = new Variant
            {
                Label = "Entity server (Windows, test lane)", ExtraDefines = EntityDefines, Scenes = EntityScenes(), BurstOn = true,
                ReportPath = EntityReportPath, Title = "Krytical Nova ENTITY TEST Server", Entity = true,
            };
            ManagedStrippingLevel strippingWas = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Server);
            Il2CppCompilerConfiguration compilerWas = PlayerSettings.GetIl2CppCompilerConfiguration(NamedBuildTarget.Server);
            bool ok;
            try
            {
                if (backend == ScriptingImplementation.IL2CPP)
                {
                    PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Server, ManagedStrippingLevel.Minimal);
                    PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Server, Il2CppCompilerConfiguration.Release);
                }
                ok = ReleasePublisher.WithVersion(ReleasePublisher.LastReleaseBuild(), () => BuildAsIs(Os.Windows, EntityOutputFolder, backend, entity));
            }
            finally
            {
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Server, strippingWas);
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Server, compilerWas);
            }
            if (!ok) return false;
            string settings = Path.Combine(EntityOutputFolder, SettingsFileName);
            if (!File.Exists(settings))
            {
                File.WriteAllText(settings, EntityTestSettingsJson());
                Debug.Log($"[Build] Entity server: wrote test-lane settings ({settings}: port {EntityTestPort}, page {EntityTestPagePort}, TEST save service 7802).");
            }
            return true;
        }

        // ------------------------------------------------------------------ the release lane (cutover, 12 Nov 2026)

        [MenuItem("Krytical Nova/Build/Entity Server (Windows, RELEASE lane)", priority = 56)]
        private static void MenuEntityServerRelease()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Entity Server (release)", "Stop Play first.", "OK"); return; }
            string busy = FolderBusy(EntityReleaseServer.OutputFolder);
            if (busy != null) { EditorUtility.DisplayDialog("Entity Server (release)", busy, "OK"); return; }
            BuildEntityServerRelease(ScriptingImplementation.IL2CPP);
        }

        /// <summary>
        /// The entity server as the REAL server (<see cref="EntityReleaseServer"/>): no <c>KN_TEST_LANE</c>, the Sky Lift off, into
        /// <c>Builds/Server/Windows</c> of this project, with its real-lane settings (written only when the folder has none, so a rebuild keeps
        /// the owner's), the NGO server's certificate copied beside it, the save service and the Server app. Never builds into the NGO
        /// server's folder (<c>Builds/Server</c> of the MAIN project). The app's shortcuts are not touched here.
        /// </summary>
        public static bool BuildEntityServerRelease(ScriptingImplementation backend)
        {
            foreach (string needed in new[] { KnLinuxTestBuild.WorldAssetsPath, KnLinuxTestBuild.GhostBakeScenePath, KnLinuxTestBuild.MapScenePath })
                if (AssetDatabase.LoadMainAssetAtPath(needed) == null)
                {
                    Debug.LogError($"[Build] Entity server (release): {needed} is missing (Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets makes the ghost assets).");
                    return false;
                }
            string folder = EntityReleaseServer.OutputFolder;
            if (!string.IsNullOrEmpty(MainBuildsGuard(folder))) { Debug.LogError("[Build] " + MainBuildsGuard(folder)); return false; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? ".");
            var entity = new Variant
            {
                Label = "Entity server (Windows, release lane)", ExtraDefines = EntityReleaseServer.Defines, Scenes = EntityScenes(), BurstOn = true,
                ReportPath = EntityReleaseServer.ReportPath, Title = "Krytical Nova Server", Entity = true, Release = true,
            };
            ManagedStrippingLevel strippingWas = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Server);
            Il2CppCompilerConfiguration compilerWas = PlayerSettings.GetIl2CppCompilerConfiguration(NamedBuildTarget.Server);
            bool ok;
            try
            {
                if (backend == ScriptingImplementation.IL2CPP)
                {
                    PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Server, ManagedStrippingLevel.Minimal);
                    PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Server, Il2CppCompilerConfiguration.Release);
                }
                // the build number is the caller's (ReleasePublisher.WithVersion around this call), never LastReleaseBuild() again
                ok = BuildAsIs(Os.Windows, folder, backend, entity);
            }
            finally
            {
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Server, strippingWas);
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Server, compilerWas);
            }
            if (!ok) return false;
            string settings = Path.Combine(folder, SettingsFileName);
            if (!File.Exists(settings))
            {
                File.WriteAllText(settings, EntityReleaseServer.SettingsJson(EntityReleaseServer.ReadNgoSettings()));
                Debug.Log($"[Build] Entity server (release): wrote real-lane settings ({settings}: port {EntityReleaseServer.GamePort}, page {EntityReleaseServer.PagePort}, save service {EntityReleaseServer.SaveService}, DTLS, list entry \"{EntityReleaseServer.ListName}\"; GMs and secrets carried, not printed).");
            }
            if (!EntityReleaseServer.CopyCertificates(Path.Combine(EntityReleaseServer.NgoWindowsFolder, "certificates"), EntityReleaseServer.CertificatesFolder, out string problem))
            {
                Debug.LogError("[Build] Entity server (release): " + problem + ". The server would not start without encryption; fix it before step 9.");
                return false;
            }
            Debug.Log($"[Build] Entity server (release): certificate and key copied beside the build ({EntityReleaseServer.CertificatesFolder}); never into the repository.");
            return true;
        }

        /// <summary>Null when <paramref name="folder"/> is not inside the MAIN project's <c>Builds/Server</c> (the NGO server's, never written); else why not.</summary>
        public static string MainBuildsGuard(string folder)
        {
            string ngo = Path.GetFullPath(Path.Combine(EntityReleaseServer.NgoWindowsFolder, ".."));
            string target = Path.GetFullPath(folder);
            return target.StartsWith(ngo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(target, ngo, StringComparison.OrdinalIgnoreCase)
                ? $"refusing to build into {target}: it is the NGO server's folder, which is never written"
                : null;
        }

        /// <summary>The entity server's first settings: test ports, the TEST save service (krytical_nova_test), never listed, plain UDP.</summary>
        public static string EntityTestSettingsJson() =>
            "{\n" +
            $"  \"{EntityNetcodeKey}\": \"entities\",\n" +
            $"  \"port\": {EntityTestPort},\n" +
            $"  \"pagePort\": {EntityTestPagePort},\n" +
            "  \"maxPlayers\": 20,\n" +
            "  \"map\": \"Zone01_StarterTown\",\n" +
            "  \"saveService\": \"http://127.0.0.1:7802\",\n" +
            "  \"serverName\": \"Krytical Nova ENTITY TEST\",\n" +
            "  \"plainTransport\": true,\n" +
            "  \"serverList\": [],\n" +
            // The keys the NGO settings loader adds when missing (ServerLaunchOptions.Load): present, so a build that still rewrites
            // the file never does, and no map server (Transit Row) is started beside this test server by the app.
            "  \"mapServers\": [],\n" +
            "  \"ledgerDays\": 90,\n" +
            "  \"recordMonths\": 6,\n" +
            "  \"callLimits\": {}\n" +
            "}\n";

        /// <summary>What sets a build apart from the NGO server's own (null = the NGO server exactly as before).</summary>
        private sealed class Variant
        {
            public string Label, ReportPath, Title;
            public string[] ExtraDefines, Scenes;
            public bool BurstOn, Entity, Release;
        }

        private static bool BuildAsIs(Os os, string intoFolder, ScriptingImplementation backend, Variant variant = null)
        {
            string missing = ModuleMissing(os, backend);
            if (missing != null)
            {
                Debug.LogError("[Build] " + missing);
                return false;
            }

            // The development scenes (the Cadence arena) never ship, on a server either. The maps are in its build list:
            // a server runs one and never changes it, so it has no use for the game's Addressable bundles.
            string[] scenes = variant?.Scenes ?? BuildSceneOrder.ShippedForServer();
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
                ? Path.Combine(Root, os + "-" + SettingsFileName + ".kept")
                : folder.TrimEnd('/', '\\') + "-" + SettingsFileName + ".kept";
            if (File.Exists(settings)) File.Copy(settings, keptSettings, true);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            BuildTarget target = os == Os.Windows ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneLinux64;
            string exe = folder + "/" + ExeName + (os == Os.Windows ? ".exe" : ".x86_64");
            BuildTarget targetWas = EditorUserBuildSettings.activeBuildTarget;

            BuildReport report = null;
            // The server's backend is set for this build: a client publish can leave IL2CPP in the checkout's ProjectSettings when
            // its Unity is stopped before it saves the put-back value, and the Server target can fall back to Standalone's.
            ScriptingImplementation backendWas = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);
            if (backendWas != backend) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, backend);
            string backendUsed = BackendName(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server));
            bool serverCheckout = BackgroundPublisher.InServerCheckout;
            // The game builds its map bundles with itself; a server loads its maps from its build list and needs none.
            var addressables = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            var bundlesWere = addressables != null ? addressables.BuildAddressablesWithPlayerBuild : default;
            if (addressables != null) addressables.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            // Burst's own settings file, stated outright for a Burst build and put back afterwards (KnWindowsTestBuild, KnLinuxTestBuild).
            string burstPath = Path.GetFullPath(KnWindowsTestBuild.BurstSettingsPath);
            string burstWas = variant != null && variant.BurstOn && File.Exists(burstPath) ? File.ReadAllText(burstPath) : null;
            try
            {
                IsBuilding = true;
                if (variant != null && variant.BurstOn)
                {
                    File.WriteAllText(burstPath, KnWindowsTestBuild.BurstOnJson);
                    // Burst reads the Editor's own subtarget, not BuildPlayerOptions.subtarget (KnLinuxTestBuild, 30 Oct 2026); the
                    // finally below puts the Editor back on Player.
                    EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
                }
                BurstAotMarker.ClearStaleMarker();   // an earlier Burst-off build's leftover would stop Burst AOT for good (8 Nov 2026)
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = exe,
                    targetGroup = BuildTargetGroup.Standalone,
                    target = target,
                    subtarget = (int)StandaloneBuildSubtarget.Server,
                    // A clean build: the scenes must go through ServerSceneStripper, not come from the cache of a client build.
                    // (It clears the player build's cache, Library/PlayerDataCache; the map bundles' Library/BuildCache was
                    // seen to survive it, 31 Oct 2026.) LZ4, not LZ4HC: a server is unpacked on this PC, never downloaded,
                    // and LZ4HC cost 1.5 to 2 minutes a build (owner's pick, 31 Oct 2026).
                    options = BuildOptions.CompressWithLz4 | BuildOptions.CleanBuildCache,
                    extraScriptingDefines = variant?.ExtraDefines,
                });
            }
            finally
            {
                IsBuilding = false;
                if (variant != null && variant.BurstOn)
                {
                    try
                    {
                        if (burstWas != null) File.WriteAllText(burstPath, burstWas);
                        else if (File.Exists(burstPath)) File.Delete(burstPath);
                    }
                    catch (Exception e) { Debug.LogError($"[Build] Could not put back {KnWindowsTestBuild.BurstSettingsPath} ({e.Message}): remove it by hand."); }
                }
                if (addressables != null) addressables.BuildAddressablesWithPlayerBuild = bundlesWere;
                if (backendWas != backend) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, backendWas);
                // Put back after the build, and even when it failed: the settings are the owner's, never the build's.
                if (File.Exists(keptSettings))
                {
                    Directory.CreateDirectory(folder);
                    if (File.Exists(settings)) File.Delete(settings);
                    File.Move(keptSettings, settings);
                }
                // A server build may leave the Editor on the server target, where the HUD and menu tools are left out. It goes
                // back to Player, never to "what it was": a checkout that already stood on Server (a killed build left it)
                // would otherwise stay there and compile every game build as a server (ClientBuildGuard). The server checkout
                // builds servers only and stays on Server, so its next server build compiles nothing again.
                if (!serverCheckout && (EditorUserBuildSettings.activeBuildTarget != targetWas || EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player))
                {
                    EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, targetWas);
                }
            }

            string reportPath = variant?.ReportPath ?? ReportPath(os);
            string label = variant?.Label ?? $"Dedicated server ({os})";
            if (report == null)
            {
                Debug.LogError($"[Build] {label}: no build report (the build threw before it ran).");
                return false;
            }
            TestBuildBuilder.WriteSizeReport(report, scenes, label, $"Scripting backend: {backendUsed}, Dedicated Server target" +
                (variant?.ExtraDefines != null ? ", defines " + string.Join(" ", variant.ExtraDefines) : ""), reportPath);
            bool ok = report.summary.result == BuildResult.Succeeded;
            if (ok) AddBesideTheServer(os, folder, variant?.Title ?? "Krytical Nova Server");
            if (ok && os == Os.Windows) BuildSaveService(folder);
            if (ok && os == Os.Windows) BuildServerApp(folder, shortcuts: intoFolder == null);
            if (ok && variant != null && variant.Entity && !variant.Release) EntityShortcut(folder);
            if (ok && variant != null && variant.BurstOn)
            {
                string plugins = Path.Combine(folder, ExeName + "_Data", "Plugins");
                bool hasBurst = File.Exists(Path.Combine(plugins, "x86_64", "lib_burst_generated.dll")) || File.Exists(Path.Combine(plugins, "lib_burst_generated.dll"));
                if (hasBurst) Debug.Log($"[Build] {label}: Burst AOT built (lib_burst_generated.dll).");
                else { Debug.LogError($"[Build] {label} FAILED: Burst AOT was asked for but lib_burst_generated.dll is not in {plugins}."); ok = false; }
            }
            if (ok) Debug.Log($"[Build] {label} ({backendUsed}) written to {folder} ({TestBuildBuilder.Megabytes(report.summary.totalSize)}). Sizes: {reportPath}");
            else Debug.LogError($"[Build] {label} {report.summary.result}: {report.summary.totalErrors} errors. See {reportPath}");
            return ok;
        }

        /// <summary>The entity server's app gets its own desktop shortcut (never the NGO server's "Krytical Nova Server" one).</summary>
        private static void EntityShortcut(string serverFolder)
        {
            string exe = Path.Combine(Path.GetFullPath(Path.Combine(serverFolder, "..")), ServerAppName + ".exe");
            if (!File.Exists(exe)) return;
            MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Krytical Nova ENTITY Server (TEST).lnk"), exe);
        }
    
        /// <summary>The control page's files and fonts, and the start script.</summary>
        private const string SaveServiceProject = "Assets/_Game/Gameplay/Persistence/Service~/SaveService/SaveService.csproj";

        /// <summary>
        /// Online phase 4: the save service, published as one exe into a SaveService folder beside the server's own (so a
        /// new server build never replaces it while it runs), where Start Server.bat starts it. Kept as it was when it is
        /// running or .NET is missing; the server still builds.
        /// </summary>
        private static void BuildSaveService(string serverFolder)
        {
            string output = Path.GetFullPath(Path.Combine(serverFolder, "..", "SaveService"));
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("dotnet",
                    $"publish \"{Path.GetFullPath(SaveServiceProject)}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"{output}\" --nologo")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    string said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        Debug.Log("[Build] Save service published to " + output + ".");
                        // The backup's double-click files (online phase 4, milestone 6), beside the real server's save service
                        // only: a test server's copy of the service must never be pointed at the real saves by a click.
                        if (string.Equals(Path.GetFullPath(serverFolder).TrimEnd('\\', '/'), Path.GetFullPath(OutputFolder(Os.Windows)).TrimEnd('\\', '/'),
                                StringComparison.OrdinalIgnoreCase))
                            WriteBackupScripts(output);
                    }
                    else Debug.LogWarning("[Build] The save service was not published (is it running? stop it first): " + said.Trim());
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Build] The save service was not published (.NET SDK missing?): " + e.Message);
            }
        }

        /// <summary>
        /// Back Up Now, Restore Drill and Restore Saves beside the save service (online phase 4, milestone 6; the Server Guide
        /// says when to use each). Each runs the service's own command and waits for a key, so its last words stay on screen.
        /// </summary>
        private static void WriteBackupScripts(string serviceFolder)
        {
            void Script(string name, string about, string arguments) => File.WriteAllText(Path.Combine(serviceFolder, name + ".bat"),
                "@echo off\r\nrem " + about + " (online phase 4, milestone 6; Server Guide, \"Nightly backups\").\r\n" +
                "\"%~dp0SaveService.exe\" " + arguments + "\r\necho.\r\npause\r\n");
            Script("Back Up Now", "A copy of the save database now, as the nightly one (into Documents and Google Drive)", "--backup-now");
            Script("Restore Drill", "Puts the newest copy into the practice database and checks every save came back", "--restore-drill");
            Script("Restore Saves", "Last resort: puts the newest copy back into the real saves (close the Server app first)", "--restore-real");
        }

        /// <summary>Only the app (and its shortcuts), for a server already built: no server build needed.</summary>
        [MenuItem("Krytical Nova/Build/Krytical Nova Server App Only", priority = 54)]
        public static void BuildServerAppOnly()
        {
            string folder = OutputFolder(Os.Windows);
            if (!File.Exists(Path.Combine(folder, ExeName + ".exe"))) { Debug.LogError("[Build] Build the Windows dedicated server first: " + folder); return; }
            BuildServerApp(folder, shortcuts: true);
        }

        private const string ServerAppProject = "Assets/_Game/Networking/ServerApp~/ServerApp.csproj";
        public const string ServerAppName = "Krytical Nova Server";

        /// <summary>
        /// The Krytical Nova Server app (owner, 18 Oct 2026), beside the server's folder and the save service's: opening it
        /// starts both hidden inside one window with SERVER, SAVE SERVICE and DATABASE tabs. For the owner's server it also
        /// gets a shortcut on the desktop and in the Start menu, with the KN logo. Kept as it was while it is open.
        /// </summary>
        private static void BuildServerApp(string serverFolder, bool shortcuts)
        {
            string beside = Path.GetFullPath(Path.Combine(serverFolder, ".."));
            string scratch = Path.Combine(Path.GetFullPath("Temp"), "ServerAppBuild");
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("dotnet",
                    $"build \"{Path.GetFullPath(ServerAppProject)}\" -c Release -o \"{scratch}\" --nologo")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    string said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        Debug.LogWarning("[Build] The Krytical Nova Server app was not built: " + said.Trim());
                        return;
                    }
                }
                foreach (string name in new[] { ServerAppName + ".exe", ServerAppName + ".exe.config" })
                    File.Copy(Path.Combine(scratch, name), Path.Combine(beside, name), true);
                Debug.Log($"[Build] {ServerAppName} app written to {beside}.");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[Build] {ServerAppName} was not replaced (is it open? close it and build again): " + e.Message);
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Build] {ServerAppName} was not built (.NET SDK missing?): " + e.Message);
                return;
            }
            if (!shortcuts) return;
            string exe = Path.Combine(beside, ServerAppName + ".exe");
            foreach (Environment.SpecialFolder place in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
                MakeShortcut(Path.Combine(Environment.GetFolderPath(place), ServerAppName + ".lnk"), exe);
        }

        private static void MakeShortcut(string link, string exe)
        {
            try
            {
                string Quote(string text) => "'" + text.Replace("'", "''") + "'";
                string script = $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut({Quote(link)}); $s.TargetPath = {Quote(exe)}; " +
                                $"$s.WorkingDirectory = {Quote(Path.GetDirectoryName(exe))}; $s.IconLocation = {Quote(exe + ",0")}; " +
                                "$s.Description = 'Runs the Krytical Nova server and its save service'; $s.Save()";
                var start = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + script.Replace("\"", "\\\"") + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using (var process = System.Diagnostics.Process.Start(start)) process.WaitForExit(20000);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Build] No shortcut at " + link + ": " + e.Message);
            }
        }

        private static void AddBesideTheServer(Os os, string folder, string title)
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
                    "title " + title + "\r\n" +
                    "cd /d \"%~dp0\"\r\n" +
                    // Online phase 4: the save service this server is set to use starts first: the real one (port 7801) or
                    // the tests' (7802, --test). A second copy finds its port taken and closes. None when it saves to files.
                    "set SAVES=%~dp0..\\SaveService\\SaveService.exe\r\n" +
                    "if exist \"%SAVES%\" findstr /c:\"127.0.0.1:7801\" server-settings.json > nul 2>&1 && (\r\n" +
                    "  start \"Krytical Nova Save Service\" /min \"%SAVES%\"\r\n" +
                    "  timeout /t 3 /nobreak > nul\r\n" +
                    ")\r\n" +
                    "if exist \"%SAVES%\" findstr /c:\"127.0.0.1:7802\" server-settings.json > nul 2>&1 && (\r\n" +
                    "  start \"Krytical Nova Save Service (TEST saves)\" /min \"%SAVES%\" --test\r\n" +
                    "  timeout /t 3 /nobreak > nul\r\n" +
                    ")\r\n" +
                    ":start\r\n" +
                    // By its full path: a bare name is not looked for in the current folder where
                    // NoDefaultCurrentDirectoryInExePath is set.
                    "\"%~dp0" + ExeName + ".exe\" -logFile -\r\n" +
                    $"if %errorlevel%=={RestartExitCode} goto start\r\n" +
                    // Started by a tool (the measure server, MeasureTools): the window closes with the server.
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
