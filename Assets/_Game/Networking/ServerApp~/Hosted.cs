using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KryticalNova.ServerApp
{
    /// <summary>
    /// One program the app runs hidden inside itself (the game server, the save service): started with no window, its
    /// output kept as lines for its tab, its input open for commands. Raises <see cref="Exited"/> with the exit code.
    /// </summary>
    public sealed class Hosted
    {
        public const int MaxLines = 2000;

        private readonly string exe;
        private readonly string arguments;
        private readonly string folder;
        private readonly object gate = new object();
        private readonly List<string> lines = new List<string>();
        private Process process;
        private long added;

        public Hosted(string name, string exe, string arguments, string folder)
        {
            Name = name;
            this.exe = exe;
            this.arguments = arguments;
            this.folder = folder;
        }

        public string Name { get; }

        /// <summary>Raised on a background thread when the program ends, with its exit code.</summary>
        public event Action<int> Exited;

        public bool Running
        {
            get
            {
                try { return process != null && !process.HasExited; }
                catch (InvalidOperationException) { return false; }
            }
        }

        public bool Exists => File.Exists(exe);

        public DateTime StartedAt { get; private set; }

        /// <summary>How many lines have come in so far (the tab adds what is new).</summary>
        public long Added { get { lock (gate) return added; } }

        /// <summary>The lines after the first <paramref name="seen"/> of all lines that came in.</summary>
        public List<string> LinesSince(long seen, out long now)
        {
            lock (gate)
            {
                now = added;
                long first = added - lines.Count;
                var result = new List<string>();
                for (long i = Math.Max(seen, first); i < added; i++) result.Add(lines[(int)(i - first)]);
                return result;
            }
        }

        public void Note(string line)
        {
            lock (gate)
            {
                lines.Add(line);
                added++;
                if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
            }
        }

        public bool Start(out string problem)
        {
            problem = null;
            if (Running) return true;
            if (!Exists) { problem = "not found: " + exe; return false; }
            var start = new ProcessStartInfo(exe, arguments)
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            try
            {
                process = new Process { StartInfo = start, EnableRaisingEvents = true };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) Note(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) Note(e.Data); };
                Process mine = process;
                process.Exited += (s, e) =>
                {
                    int code;
                    try { code = mine.ExitCode; } catch (InvalidOperationException) { code = -1; }
                    Note($"--- {Name} stopped (exit code {code}) ---");
                    Exited?.Invoke(code);
                };
                process.Start();
                Job.Add(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                StartedAt = DateTime.Now;
                Note($"--- {Name} started {StartedAt:HH:mm:ss} ---");
                return true;
            }
            catch (Exception e)
            {
                problem = e.Message;
                return false;
            }
        }

        /// <summary>Types a line into the program's console (the game server reads its commands there).</summary>
        public bool Send(string line)
        {
            if (!Running) return false;
            try
            {
                process.StandardInput.WriteLine(line);
                process.StandardInput.Flush();
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Waits up to <paramref name="milliseconds"/> for it to end by itself, then ends it.</summary>
        public void StopWithin(int milliseconds)
        {
            if (!Running) return;
            try
            {
                if (!process.WaitForExit(milliseconds)) process.Kill();
            }
            catch (Exception) { /* already gone */ }
        }

        public void Kill()
        {
            try { if (Running) process.Kill(); } catch (Exception) { /* already gone */ }
        }

        /// <summary>
        /// Everything the app starts ends with it, even when the app is ended from Task Manager or crashes: a hidden
        /// server with no window left to stop it from is worse than one that stops (the save service goes too, so it
        /// could not save anyway). A normal close stops the server safely first.
        /// </summary>
        private static class Job
        {
            private static IntPtr handle;

            public static void Add(Process process)
            {
                try
                {
                    if (handle == IntPtr.Zero)
                    {
                        handle = CreateJobObject(IntPtr.Zero, null);
                        var info = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
                        int size = Marshal.SizeOf(typeof(ExtendedLimits));
                        IntPtr buffer = Marshal.AllocHGlobal(size);
                        Marshal.StructureToPtr(info, buffer, false);
                        SetInformationJobObject(handle, ExtendedLimitInformation, buffer, (uint)size);
                        Marshal.FreeHGlobal(buffer);
                    }
                    AssignProcessToJobObject(handle, process.Handle);
                }
                catch (Exception) { /* the program still runs; it just outlives a crash of the app */ }
            }

            private const uint KillOnJobClose = 0x2000;
            private const int ExtendedLimitInformation = 9;

            [StructLayout(LayoutKind.Sequential)]
            private struct BasicLimits
            {
                public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass, SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Counters { public ulong Reads, Writes, Others, ReadBytes, WriteBytes, OtherBytes; }

            [StructLayout(LayoutKind.Sequential)]
            private struct ExtendedLimits
            {
                public BasicLimits Basic;
                public Counters Io;
                public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

            [DllImport("kernel32.dll")]
            private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

            [DllImport("kernel32.dll")]
            private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        }
    }
}
