namespace KryticalNova.Social
{
    /// <summary>What became of a chat message once the rules were applied.</summary>
    public enum ChatRoute : byte
    {
        Delivered = 0,
        NotInGuild,
        NoSuchPlayer,
        /// <summary>Several online players share the whispered name; the candidates are returned instead.</summary>
        AmbiguousPlayer,
        WhisperToSelf,
        /// <summary>The GM channel is for the host only.</summary>
        NotGameMaster,
        /// <summary>System and Combat lines are written by the game, never sent by a player.</summary>
        NotAPlayerChannel,
    }
}
