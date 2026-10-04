namespace KryticalNova.Social
{
    /// <summary>What a line typed into the chat box turned out to be.</summary>
    public enum ChatInputKind : byte
    {
        Nothing = 0,
        /// <summary>A message to send on <see cref="ChatInput.Channel"/>.</summary>
        Send,
        /// <summary>No message: only make <see cref="ChatInput.Channel"/> the channel the player talks on.</summary>
        SwitchChannel,
        /// <summary>A command ran, or the line was wrong: show <see cref="ChatInput.Notice"/> to the player.</summary>
        Notice,
    }
}
