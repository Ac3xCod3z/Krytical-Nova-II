namespace KryticalNova.Items
{
    /// <summary>The emote a character is showing (NGO's <c>EmoteNetState</c>, here as plain fields): an empty key = none; the serial goes up on every change.</summary>
    public struct BodyEmoteState
    {
        public string key;
        public byte serial;
        public bool stance;
    }

    /// <summary>
    /// A player body's emotes, for the entity netcode (M8 8.4.6): the server body decides its owner's emote by NGO's PlayerEmotes rules (alive,
    /// standing still: its ghost, since the server body has no animation motor) and every view plays what the server shows. Here, below Social.
    /// </summary>
    public interface IBodyEmotes
    {
        /// <summary>What this body shows now (the server's own, or what a view was told).</summary>
        BodyEmoteState State { get; }
        /// <summary>The server body: its owner's play or stop (NGO's PlayRpc / StopRpc body, after the Cosmetic limit).</summary>
        void RunEmote(bool stop, string key);
        /// <summary>Any view: the emote the server shows for this player (NGO's netState, read by everyone).</summary>
        void MirrorState(in BodyEmoteState state);
    }
}
