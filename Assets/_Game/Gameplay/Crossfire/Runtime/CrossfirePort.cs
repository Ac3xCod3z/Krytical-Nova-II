using KryticalNova.Factions;

namespace KryticalNova.Crossfire
{
    /// <summary>What the Transit Row port does with a player who steps into it (<see cref="CrossfirePort.Decide"/>).</summary>
    public enum CrossfirePortAnswer : byte
    {
        /// <summary>Not Crossfire's port: the portal does what it always does.</summary>
        NotThisPort = 0,
        /// <summary>No war is calling this player now (the Gap, the War, the End, or a level below the war's): the portal as always.</summary>
        NotCalled = 1,
        /// <summary>Called, but the player's bracket has no Crossfire zone yet: told so, not moved.</summary>
        NoZone = 2,
        /// <summary>Called for a bracket with a zone, but their faction has no Rally Point on this map: told so, not moved.</summary>
        NoRally = 3,
        /// <summary>Put on their own faction's Rally Point, on this server.</summary>
        Land = 4,
    }

    /// <summary>
    /// The Transit Row port in Crossfire (owner, 16 Nov 2026): while a war is called (the Call and the Rally), a player of a bracket that
    /// has a Crossfire zone (<see cref="CrossfireSettings.zoneBrackets"/>; for now only X, 100-112) who goes through the port
    /// (<see cref="CrossfireSettings.portPortalId"/>) is teleported onto their own faction's Rally Point (<see cref="CrossfireArea"/>, a
    /// Muster Yard with that faction) on the same server: no map-to-map travel. A player of another bracket is told their bracket has no
    /// zone yet and stays. A pure rule; the server's <c>CrossfireEntryServerSystem</c> moves the player.
    /// </summary>
    public static class CrossfirePort
    {
        /// <summary>The war calls players now: the Call and the Rally (the yards' gates still shut).</summary>
        public static bool Calling(in CrossfireState state) =>
            state.Known && (state.Now.Phase == CrossfirePhase.Call || state.Now.Phase == CrossfirePhase.Muster);

        /// <summary>What the port does with a player of <paramref name="level"/> and <paramref name="faction"/>; the yard to land on for <see cref="CrossfirePortAnswer.Land"/>.</summary>
        public static CrossfirePortAnswer Decide(CrossfireSettings settings, in CrossfireState state, string portalId, int level, FactionId faction,
                                                 out CrossfireArea yard)
        {
            yard = null;
            if (settings == null || string.IsNullOrEmpty(settings.portPortalId) || portalId != settings.portPortalId) return CrossfirePortAnswer.NotThisPort;
            int bracket = settings.BracketOf(level);
            if (!Calling(state) || bracket <= 0) return CrossfirePortAnswer.NotCalled;
            if (!settings.HasZone(bracket)) return CrossfirePortAnswer.NoZone;
            return CrossfireArea.TryRallyPoint(faction, out yard) ? CrossfirePortAnswer.Land : CrossfirePortAnswer.NoRally;
        }

        /// <summary>What the player is told for <paramref name="answer"/> (the words are in the settings); null for none.</summary>
        public static string Message(CrossfireSettings settings, CrossfirePortAnswer answer, int level)
        {
            if (settings == null) return null;
            switch (answer)
            {
                case CrossfirePortAnswer.NoZone: return CrossfireCaller.Fill(settings.portNoZoneText, settings, settings.BracketOf(level));
                case CrossfirePortAnswer.NoRally: return settings.portNoRallyText;
                case CrossfirePortAnswer.Land: return string.IsNullOrEmpty(settings.portArrivedText) ? null : settings.portArrivedText;
                default: return null;
            }
        }
    }
}
