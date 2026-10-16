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
    /// read on a thread of its own and run on the game's. Only in a server build.
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
            while (Lines.TryDequeue(out string line))
            {
                try
                {
                    object result = QuantumConsoleProcessor.InvokeCommand(line);
                    Debug.Log("> " + line + (result != null ? "\n" + result : string.Empty));
                }
                catch (Exception e)
                {
                    Debug.Log("> " + line + "\n" + (e.InnerException ?? e).Message);
                }
            }
        }
    }
}
#endif
