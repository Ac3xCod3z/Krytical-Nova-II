namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Perf round 4: command-line switches for a same-build before/after on the server. <c>-knmove-serial</c> runs the walk, the jump
    /// flight and the hit history on one worker thread instead of all of them (the math is the same either way).
    /// <c>-knseparation-serial</c> is read by <see cref="PlayerSeparationSystem"/>. Read once, when a system is made.
    /// </summary>
    public static class PlayerPerfSwitches
    {
        public static bool MoveSerial => Has("-knmove-serial");

        public static bool Has(string arg)
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
                if (a == arg) return true;
            return false;
        }
    }
}
