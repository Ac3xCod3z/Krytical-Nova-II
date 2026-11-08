using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace KryticalNova.Publisher
{
    public static class Program
    {
        /// <summary>
        /// One Publisher at a time (two would race on the same line). <c>--selftest &lt;folder&gt; --store &lt;A&gt; --store2 &lt;B&gt;
        /// [--launcher &lt;folder&gt;]</c> runs the whole round on the LOCAL stand-in only (upload, promote, roll back, a launcher
        /// download after each step) and saves a picture of every step plus report.txt in the folder.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            string root = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            using (var single = new Mutex(true, "KryticalNovaPublisher", out bool first))
            {
                if (!first)
                {
                    MessageBox.Show("Krytical Nova Publisher is already open. Look for it on the taskbar.", "Krytical Nova Publisher",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Crash(root, e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(root, e.ExceptionObject as Exception);
                Quiet = Array.IndexOf(args, "--selftest") >= 0;
                SelfTest test = null;
                string folder = Option(args, "--selftest");
                if (folder != null) test = new SelfTest { Folder = folder, StoreA = Option(args, "--store"), StoreB = Option(args, "--store2"), Launcher = Option(args, "--launcher") };
                Application.Run(new MainWindow(root, test));
            }
        }

        private static bool Quiet;

        private static void Crash(string root, Exception e)
        {
            try { File.AppendAllText(Path.Combine(root, "publisher.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + "CRASH " + e + Environment.NewLine); } catch (Exception) { }
            if (Quiet) { Environment.Exit(2); return; } // self-test: log only, never a dialog
            MessageBox.Show("Something went wrong: " + e?.Message + " (details are in publisher.log beside the app)", "Krytical Nova Publisher",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static string Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }

    public sealed class SelfTest
    {
        public string Folder, StoreA, StoreB, Launcher;
    }
}
