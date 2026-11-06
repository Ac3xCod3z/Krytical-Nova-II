using System;

namespace KryticalNova.Core
{
    /// <summary>
    /// Whether worlds of the entity netcode (a client or a server world) are live in this process, for the Netcode for GameObjects code
    /// that must stand aside while they run (M5.0 seam: NGO's Hostile zones). The entity netcode fills <see cref="LiveProbe"/> at start;
    /// unset (edit mode, an NGO-only build) nothing is live.
    /// </summary>
    public static class EntityNetcodeWorlds
    {
        public static Func<bool> LiveProbe { get; set; }

        public static bool Live => LiveProbe != null && LiveProbe();
    }
}
