using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace KryticalNova.ServerApp
{
    public static class Program
    {
        // One app per server folder: a second start brings nothing up (the first is already running it).
        [STAThread]
        public static void Main(string[] args)
        {
            string root = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            string name = "KryticalNovaServerApp_" + root.Replace('\\', '_').Replace(':', '_');
            using (var single = new Mutex(true, name, out bool first))
            {
                if (!first)
                {
                    MessageBox.Show("Krytical Nova Server is already open for " + root + ". Look for it on the taskbar.", "Krytical Nova Server",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int test = Array.IndexOf(args, "--selftest");
                Application.Run(new MainWindow(root, test >= 0 && test + 1 < args.Length ? args[test + 1] : null));
            }
        }
    }
}
