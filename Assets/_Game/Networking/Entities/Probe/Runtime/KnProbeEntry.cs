#if KN_PROBE || UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Probe
{
    /// <summary>
    /// The only door into the probe (M2c): a <c>-knprobe server|client</c> on the command line. Any tiny scene will do; the build
    /// script (<c>KnProbeBuild</c>) makes an empty one. Without the flag nothing here runs, in the Editor or in a build. A probe
    /// <i>server</i> build that is started without the flag exits at once, because the server world of
    /// <see cref="KnBootstrap"/> would otherwise listen on the game's own default ports. Every static is reset for Enter Play Mode
    /// without domain reload.
    /// </summary>
    public static class KnProbeEntry
    {
        private static bool s_Booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_Booted = false;

        /// <summary>The folder of the result file: <c>-knout</c>, else beside the exe.</summary>
        public static string ResultFolder(KnProbeArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.OutDir)) return args.OutDir;
            try { return Path.GetDirectoryName(Application.dataPath) ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        // Before the world bootstrap (BeforeSceneLoad): a probe server on a port that is not a probe port, or started with no flags,
        // must never get as far as listening. Environment.Exit because Application.Quit would let the bootstrap run first.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Guard()
        {
#if UNITY_SERVER && KN_PROBE
            KnProbeArgs args = KnProbeArgs.Parse(Environment.GetCommandLineArgs());
            string problem = null;
            if (args.Role != KnProbeRole.Server) problem = "this is a probe server build: start it with -knprobe server -port 7797 -pageport 7798";
            else if (args.Error != null) problem = args.Error;
            else
            {
                KnServerSettings settings = KnServerSettings.LoadForThisServer();
                problem = KnProbeArgs.ServerPortProblem(settings.port, settings.PagePort);
            }
            if (problem == null) return;
            Debug.LogError("[KnProbe] refusing to start: " + problem);
            Console.Error.WriteLine("[KnProbe] refusing to start: " + problem);
            Environment.Exit(1);
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (s_Booted) return;
            KnProbeArgs args = KnProbeArgs.Parse(Environment.GetCommandLineArgs());
            if (!args.Requested) return;
            s_Booted = true;
            string folder = ResultFolder(args);
            if (args.Error != null || !args.Enabled)
            {
                Fail(args, folder, args.Error ?? "-knprobe was not understood");
                return;
            }
            try
            {
                if (args.Role == KnProbeRole.Server)
                {
                    KnServerSettings settings = KnServerSettings.LoadForThisServer();
                    string problem = KnProbeArgs.ServerPortProblem(settings.port, settings.PagePort);
                    if (problem != null) { Fail(args, folder, problem); return; }
                    KnProbeServer.Install(args, folder, settings);
                }
                else KnProbeClient.Create(args, folder);
            }
            catch (Exception e)
            {
                Fail(args, folder, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Fail(KnProbeArgs args, string folder, string why)
        {
            Debug.LogError("[KnProbe] " + why);
            var result = new KnProbeResult
            {
                role = args.Role == KnProbeRole.Server ? "server" : "client",
                name = args.Name,
                startedUtcMs = KnProbeResult.NowMs(),
                endedUtcMs = KnProbeResult.NowMs(),
                exitReason = KnProbeExit.Name(KnProbeExitReason.Error),
                exitCode = KnProbeExit.Code(KnProbeExitReason.Error),
                error = why,
            };
            KnProbeFiles.Write(folder, args.Role == KnProbeRole.Server ? KnProbeRole.Server : KnProbeRole.Client, args.Name, result);
            Application.Quit(1);
        }
    }
}
#endif
