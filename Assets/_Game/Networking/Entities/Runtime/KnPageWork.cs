#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Threading;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The page's work for the game's thread (a command, a GM button; M7.4): the page's thread queues it and waits up to 10 s for the answer, as
    /// NGO's control page does (<c>OnMain</c>); <see cref="KnPageWorkSystem"/> runs it on the main thread.
    /// </summary>
    public static class KnPageWork
    {
        public const int WaitMs = 10000;

        private static readonly ConcurrentQueue<Action> s_Queue = new ConcurrentQueue<Action>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            while (s_Queue.TryDequeue(out _)) { }
        }

        public static int Pending => s_Queue.Count;

        /// <summary>Any thread but the main one: runs the reply's work there and returns the reply with its answer.</summary>
        public static KnPageRules.Reply Run(KnPageRules.Reply reply, int waitMs = WaitMs)
        {
            string answer = null;
            bool failed = false;
            using (var done = new ManualResetEventSlim(false))
            {
                s_Queue.Enqueue(() =>
                {
                    try { answer = reply.Work(); }
                    catch (Exception e) { answer = "Failed: " + e.Message; failed = true; }
                    finally { done.Set(); }
                });
                if (!done.Wait(waitMs)) return KnPageRules.Reply504();
            }
            return reply.WorkIsJson && !failed
                ? new KnPageRules.Reply(200, "application/json", answer ?? "[]", false)
                : KnPageRules.Answer(answer);
        }

        /// <summary>Main thread: runs everything queued.</summary>
        public static int RunQueued()
        {
            int ran = 0;
            while (s_Queue.TryDequeue(out Action work)) { work(); ran++; }
            return ran;
        }
    }

    /// <summary>Server, main thread: runs the page's queued work each frame.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnPageWorkSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (KnPageWork.Pending > 0) KnPageWork.RunQueued();
        }
    }
}
#endif
