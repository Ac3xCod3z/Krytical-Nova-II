using KryticalNova.Core;

namespace KryticalNova.Social
{
    /// <summary>A typed chat line after <see cref="ChatCommands.Parse"/> has read it.</summary>
    public struct ChatInput
    {
        public ChatInputKind Kind;
        public ChatChannel Channel;
        /// <summary>The player a whisper goes to.</summary>
        public string Target;
        public string Text;
        public string Notice;
    }
}
