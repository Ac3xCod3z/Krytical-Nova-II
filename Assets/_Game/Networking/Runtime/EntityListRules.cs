namespace KryticalNova.Networking
{
    /// <summary>
    /// The rules of the entity netcode's side of the Servers window and the join (cutover, 12 Nov 2026). Pure, so tests can state them:
    /// the game that speaks the entity netcode lists only the servers marked <see cref="ServerListing.entities"/> (a listing that is not
    /// marked is an NGO server it could not join); the test server of this PC is only in a development build; the join is encrypted
    /// (DTLS) everywhere except to that test server, so the owner playing on the server's own PC meets the same encryption as a brother.
    /// </summary>
    public static class EntityListRules
    {
        /// <summary>True when the entity-netcode game shows this published listing (<c>entitiesClient</c>), else the game shows every valid one.</summary>
        public static bool Shows(ServerListing listing, bool entitiesClient) => listing != null && (!entitiesClient || listing.entities);

        /// <summary>The "Entity test server (this PC)" line: only where the link is filled and the build is a development one.</summary>
        public static bool ShowsTestEntry(bool linkAvailable, bool development) => linkAvailable && development;

        /// <summary>The test server of this PC: the one place that is joined with plain UDP (loopback has no certificate for the test lane).</summary>
        public static bool IsTestLaneListing(ServerListing listing) =>
            listing != null && listing.entities && listing.address == "127.0.0.1" && listing.port == EntityServerLink.TestPort;

        /// <summary>DTLS for every entity server but the test lane's.</summary>
        public static bool Encrypted(ServerListing listing) => !IsTestLaneListing(listing);

        /// <summary>The address the join dials: this PC's own server is reached on loopback (the router may not loop its public address back).</summary>
        public static string JoinAddress(ServerListing listing) => listing.OnThisMachine ? "127.0.0.1" : listing.address;
    }
}
