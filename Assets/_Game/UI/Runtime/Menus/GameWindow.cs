using System;
using System.Runtime.InteropServices;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Brings the game's window back in front of the browser once the player has signed in on Unity's page (owner, 20 Oct
    /// 2026): the page's own tab cannot be closed by the game (browsers only let a page close a tab it opened itself).
    /// Windows only; elsewhere it does nothing.
    /// </summary>
    public static class GameWindow
    {
        public static void BringToFront()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                IntPtr window = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (window == IntPtr.Zero) return;
                if (IsIconic(window)) ShowWindow(window, RestoreWindow);
                // Windows lets only the app in front hand the front over: the game borrows its input for a moment.
                IntPtr front = GetForegroundWindow();
                uint frontThread = GetWindowThreadProcessId(front, IntPtr.Zero);
                uint ownThread = GetCurrentThreadId();
                bool attached = frontThread != ownThread && AttachThreadInput(frontThread, ownThread, true);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                if (attached) AttachThreadInput(frontThread, ownThread, false);
            }
            catch (Exception) { /* the window stays where it is; the player clicks it */ }
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int RestoreWindow = 9;
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
#endif
    }
}
