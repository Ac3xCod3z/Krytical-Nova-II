using System;
using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Reads what the player typed into the chat box: plain text goes to the current channel, "/s /f /p /g /w /r /gm"
    /// pick a channel, and any other "/name" runs a command another system registered (duels, for one). Runs on the
    /// machine that typed; the server still applies the chat rules to whatever is sent.
    /// </summary>
    public static class ChatCommands
    {
        /// <summary>Gets the text after the command name; returns a line to show the player, or null.</summary>
        public delegate string Handler(string arguments);

        private static readonly Dictionary<string, Handler> Handlers = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase);

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Handlers.Clear();

        /// <summary>Adds "/name" to the chat box. Register again to replace it.</summary>
        public static void Register(string name, Handler handler)
        {
            if (string.IsNullOrWhiteSpace(name) || handler == null) return;
            Handlers[name.Trim().TrimStart('/')] = handler;
        }

        public static void Unregister(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) Handlers.Remove(name.Trim().TrimStart('/'));
        }

        /// <param name="current">The channel the player is talking on.</param>
        /// <param name="currentTarget">The player whispers go to while <paramref name="current"/> is Whisper.</param>
        /// <param name="replyTo">Who whispered this player last, for "/r".</param>
        public static ChatInput Parse(string raw, ChatChannel current, string currentTarget, string replyTo)
        {
            string text = ChatRouting.Clean(raw);
            if (text.Length == 0) return default;

            if (text[0] != '/')
            {
                if (current == ChatChannel.Whisper && string.IsNullOrEmpty(currentTarget))
                    return Notice("Whisper whom? Use /w <name> <message>.");
                return new ChatInput { Kind = ChatInputKind.Send, Channel = current, Target = currentTarget, Text = text };
            }

            Split(text.Substring(1), out string command, out string rest);
            switch (command.ToLowerInvariant())
            {
                case "s":
                case "say":
                    return OnChannel(ChatChannel.General, rest);
                case "f":
                case "faction":
                    return OnChannel(ChatChannel.Faction, rest);
                case "p":
                case "party":
                case "squad":
                    return OnChannel(ChatChannel.Party, rest);
                case "g":
                case "guild":
                case "crew":
                    return OnChannel(ChatChannel.Guild, rest);
                case "gm":
                    return OnChannel(ChatChannel.Gm, rest);
                case "w":
                case "whisper":
                case "t":
                case "tell":
                    Split(rest, out string name, out string message);
                    if (name.Length == 0) return Notice("Use /w <name> <message>.");
                    return new ChatInput
                    {
                        Kind = message.Length > 0 ? ChatInputKind.Send : ChatInputKind.SwitchChannel,
                        Channel = ChatChannel.Whisper, Target = name, Text = message
                    };
                case "r":
                case "reply":
                    if (string.IsNullOrEmpty(replyTo)) return Notice("Nobody has whispered you yet.");
                    return new ChatInput
                    {
                        Kind = rest.Length > 0 ? ChatInputKind.Send : ChatInputKind.SwitchChannel,
                        Channel = ChatChannel.Whisper, Target = replyTo, Text = rest
                    };
            }

            if (Handlers.TryGetValue(command, out Handler handler))
            {
                string reply = handler(rest);
                return string.IsNullOrEmpty(reply) ? default : Notice(reply);
            }
            return Notice($"Unknown command '/{command}'.");
        }

        private static ChatInput OnChannel(ChatChannel channel, string message) => new ChatInput
        {
            Kind = message.Length > 0 ? ChatInputKind.Send : ChatInputKind.SwitchChannel,
            Channel = channel, Text = message
        };

        private static ChatInput Notice(string notice) => new ChatInput { Kind = ChatInputKind.Notice, Notice = notice };

        private static void Split(string text, out string first, out string rest)
        {
            text = text.Trim();
            int space = text.IndexOf(' ');
            first = space < 0 ? text : text.Substring(0, space);
            rest = space < 0 ? string.Empty : text.Substring(space + 1).Trim();
        }
    }
}
