using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Compiled into every build (release servers too: the bootstrap and roster call it); it does nothing unless <c>-knout</c> is on the command line or <see cref="Configure"/> was called.
    /// A plain-file trace for the probe players (and Editor/development runs): one line per call with the time and thread, written with
    /// <c>File.AppendAllText</c> before anything that could block. Switched on by <see cref="Configure"/> (the probe client does it) or by
    /// <c>-knout &lt;folder&gt;</c> on the command line; otherwise every call returns at once. Found necessary in the first real-socket
    /// run (M2c): a main thread that stops inside a world dispose leaves no other trace.
    /// </summary>
    public static class KnTrace
    {
        private static readonly object s_Lock = new object();
        private static readonly Stopwatch s_Clock = Stopwatch.StartNew();
        private static string s_File;
        private static bool s_Looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { s_File = null; s_Looked = false; }

        /// <summary>Writes into <paramref name="folder"/>/<paramref name="fileName"/>.</summary>
        public static void Configure(string folder, string fileName)
        {
            lock (s_Lock)
            {
                s_Looked = true;
                s_File = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, fileName);
            }
        }

        public static void Write(string line)
        {
            try
            {
                lock (s_Lock)
                {
                    if (!s_Looked)
                    {
                        s_Looked = true;
                        string[] args = Environment.GetCommandLineArgs();
                        for (int i = 0; i < args.Length - 1; i++)
                            if (args[i] == "-knout") { s_File = Path.Combine(args[i + 1], "kn-trace-" + Process.GetCurrentProcess().Id + ".txt"); break; }
                    }
                    if (s_File == null) return;
                    File.AppendAllText(s_File, $"{s_Clock.ElapsedMilliseconds,8} ms  thread {Thread.CurrentThread.ManagedThreadId,3}  {line}" + Environment.NewLine);
                }
            }
            catch (Exception) { /* tracing never breaks the game */ }
        }
    }
}
