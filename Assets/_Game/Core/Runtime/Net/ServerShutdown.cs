using System;

namespace KryticalNova.Core
{
    /// <summary>
    /// What a server must do before its process ends: save every player and the world. The stop path of a dedicated server
    /// (a SIGTERM, the console closing, <c>POST /stop</c>) calls <see cref="ServerShutdown.SaveAll"/>; Persistence fills
    /// <see cref="ServerShutdown.Handler"/> with the same flush the control page's Stop button uses. Main thread only.
    /// </summary>
    public interface IServerShutdown
    {
        /// <summary>Writes every player and world document out and returns when they are safe. May be called twice.</summary>
        void SaveAll();
    }

    public static class ServerShutdown
    {
        /// <summary>The slot Persistence fills. Null in a build or test without saves.</summary>
        public static IServerShutdown Handler;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Handler = null;

        /// <summary>Saves everything through the handler; false when there is no handler (nothing was saved).</summary>
        public static bool SaveAll()
        {
            IServerShutdown handler = Handler;
            if (handler == null) return false;
            handler.SaveAll();
            return true;
        }

        /// <summary>A handler from a plain action (tests, small hosts).</summary>
        public static IServerShutdown FromAction(Action saveAll) => new ActionShutdown(saveAll);

        private sealed class ActionShutdown : IServerShutdown
        {
            private readonly Action m_Save;
            public ActionShutdown(Action save) { m_Save = save; }
            public void SaveAll() => m_Save?.Invoke();
        }
    }
}
