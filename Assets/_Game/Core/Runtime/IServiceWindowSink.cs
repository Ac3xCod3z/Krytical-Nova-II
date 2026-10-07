namespace KryticalNova.Core
{
    /// <summary>Names of the services a player opens a window for. Stable: NPC data and commands refer to them.</summary>
    public static class ServiceIds
    {
        public const string Vault = "vault";
        public const string DeepVault = "deepvault";
        public const string CrewRegistry = "crew";
        public const string Kiosk = "kiosk";
        public const string Forge = "forge";
        public const string Herald = "herald";
    }

    /// <summary>
    /// The windows services open for the local player (Vault, Deep Vault, Crew registry, Kiosk). Implemented by the
    /// UI assembly, so gameplay and tools can ask for a window without knowing it.
    /// </summary>
    public interface IServiceWindowSink
    {
        /// <summary>Opens the window of a service (see <see cref="ServiceIds"/>). False when nothing handles it.</summary>
        bool OpenService(string serviceId);
    }
}
