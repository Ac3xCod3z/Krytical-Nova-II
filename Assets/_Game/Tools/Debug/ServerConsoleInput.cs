#if UNITY_SERVER
using System;
using System.Collections.Concurrent;
using System.Threading;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// A dedicated server's console window takes commands: a line typed there and Enter runs it like the GM console
    /// does (gm.say, gm.players, gm.kick, gm.shutdown, gm.backup, ...), and the answer is printed below it. The line is
    /// read on a thread of its own and run on the game's. The control page's GM command box runs its lines here too
    /// (<see cref="Run"/>). Only in a server build.
    /// </summary>
    public sealed class ServerConsoleInput : MonoBehaviour
    {
        private static readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var host = new GameObject("Server Console Input");
            DontDestroyOnLoad(host);
            host.AddComponent<ServerConsoleInput>();
            KryticalNova.Networking.ServerControlPage.CommandRunner = Run;
            var reader = new Thread(Read) { IsBackground = true, Name = "Server console input" };
            reader.Start();
        }

        private static void Read()
        {
            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                    if (!string.IsNullOrWhiteSpace(line)) Lines.Enqueue(line.Trim());
            }
            catch (ThreadAbortException)
            {
                // the server is quitting
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Server] The console window takes no commands: " + e.Message);
            }
        }

        private void Update()
        {
            if (Lines.IsEmpty) return;
            if (!QuantumConsoleProcessor.TableGenerated)
            {
                if (!QuantumConsoleProcessor.TableIsGenerating) QuantumConsoleProcessor.GenerateCommandTable();
                if (!QuantumConsoleProcessor.TableGenerated) return; // the commands are still being found: next frame
            }
            while (Lines.TryDequeue(out string line)) Run(line);
        }

        /// <summary>Runs one command line on the game's thread, logs it, and returns the answer.</summary>
        public static string Run(string line)
        {
            string answer = GmCommandRelay.RunLine(line);
            Debug.Log("> " + line + "\n" + answer);
            return answer;
        }
    }
}
#endif
