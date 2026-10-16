using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace KryticalNova.Release
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 8; // .NET Framework allows 2 per server; the updater downloads 4 at once
            Updater.RemoveOldLauncher();
            bool updated = Array.IndexOf(args, "--updated") >= 0;
            if (Array.IndexOf(args, "--headless") >= 0) return Headless(updated, Array.IndexOf(args, "--repair") >= 0);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(updated));
            return 0;
        }

        /// <summary>
        /// No window: check, update the launcher or the game, and say what happened in <c>launcher.log</c>. For tests and
        /// for scripts. Exit code 0 up to date, 3 the launcher replaced itself (run it again), 1 failed.
        /// </summary>
        private static int Headless(bool updated, bool repair)
        {
            string folder = Path.GetDirectoryName(Updater.LauncherExe);
            var config = LauncherConfig.Load(folder);
            var updater = new Updater(folder, config, LauncherConfig.BuiltIn("UpdateKey.pub"));
            try
            {
                UpdatePlan plan = updater.Check(repair, CancellationToken.None).GetAwaiter().GetResult();
                Log.Write($"checked {config.channel}: latest {plan.Manifest.version} (build {plan.Manifest.build}), installed build {plan.InstalledBuild}, " +
                          $"{plan.Needed.Count} file(s) to get ({plan.DownloadBytes} bytes), launcher outdated {plan.LauncherOutdated}");
                if (plan.LauncherOutdated && !updated)
                {
                    updater.UpdateLauncher(plan.Manifest.launcher, null, CancellationToken.None).GetAwaiter().GetResult();
                    Log.Write("launcher replaced by the new one");
                    return 3;
                }
                if (plan.Needed.Count > 0)
                {
                    updater.Apply(plan, null, CancellationToken.None).GetAwaiter().GetResult();
                    Log.Write($"game updated to build {plan.Manifest.build}");
                }
                return 0;
            }
            catch (Exception e)
            {
                Log.Write("failed: " + e.GetBaseException().Message);
                return 1;
            }
        }
    }

    /// <summary>What the launcher did, in <c>launcher.log</c> beside it (the newest 200 lines), for when a player asks for help.</summary>
    public static class Log
    {
        public static void Write(string line)
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(Updater.LauncherExe), "launcher.log");
                var lines = File.Exists(file) ? new System.Collections.Generic.List<string>(File.ReadAllLines(file)) : new System.Collections.Generic.List<string>();
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line);
                if (lines.Count > 200) lines.RemoveRange(0, lines.Count - 200);
                File.WriteAllLines(file, lines);
            }
            catch (Exception)
            {
                // a log that cannot be written must never stop the launcher
            }
        }
    }
}
