using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;

namespace KryticalNova.Social
{
    /// <summary>
    /// The chat rules, free of networking so they can be tested: who hears a message on each channel.
    /// General is a local shout, Faction and Guild reach their members anywhere on the server, a whisper reaches one
    /// player, and the GM channel reaches everyone but only the host may speak on it.
    /// </summary>
    public static class ChatRouting
    {
        public const int MaxLength = 200;
        /// <summary>"Name#3" picks the player with client id 3 when several share a name.</summary>
        public const char IdSeparator = '#';

        /// <summary>Trims a typed message, drops control characters and cuts it to <see cref="MaxLength"/>.</summary>
        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var clean = new StringBuilder(Math.Min(raw.Length, MaxLength));
            foreach (char c in raw)
            {
                if (clean.Length == MaxLength) break;
                clean.Append(char.IsControl(c) ? ' ' : c);
            }
            return clean.ToString().Trim();
        }

        /// <summary>
        /// Fills <paramref name="recipients"/> with the indices (into <paramref name="everyone"/>) of the players who
        /// receive the message. When the result is <see cref="ChatRoute.AmbiguousPlayer"/> it holds the candidates.
        /// </summary>
        public static ChatRoute Recipients(ChatChannel channel, in ChatParticipant sender, IReadOnlyList<ChatParticipant> everyone,
            string whisperTarget, float shoutRadius, List<int> recipients)
        {
            recipients.Clear();
            switch (channel)
            {
                case ChatChannel.General:
                    float reach = shoutRadius * shoutRadius;
                    for (int i = 0; i < everyone.Count; i++)
                        if ((everyone[i].Position - sender.Position).sqrMagnitude <= reach) recipients.Add(i);
                    return ChatRoute.Delivered;

                case ChatChannel.Faction:
                    for (int i = 0; i < everyone.Count; i++)
                        if (everyone[i].Faction == sender.Faction) recipients.Add(i);
                    return ChatRoute.Delivered;

                case ChatChannel.Guild:
                    if (string.IsNullOrEmpty(sender.Guild)) return ChatRoute.NotInGuild;
                    for (int i = 0; i < everyone.Count; i++)
                        if (string.Equals(everyone[i].Guild, sender.Guild, StringComparison.OrdinalIgnoreCase)) recipients.Add(i);
                    return ChatRoute.Delivered;

                case ChatChannel.Whisper:
                    FindByName(whisperTarget, everyone, recipients);
                    if (recipients.Count == 0) return ChatRoute.NoSuchPlayer;
                    if (recipients.Count > 1) return ChatRoute.AmbiguousPlayer;
                    if (everyone[recipients[0]].ClientId == sender.ClientId)
                    {
                        recipients.Clear();
                        return ChatRoute.WhisperToSelf;
                    }
                    return ChatRoute.Delivered;

                case ChatChannel.Gm:
                    if (!sender.IsGameMaster) return ChatRoute.NotGameMaster;
                    for (int i = 0; i < everyone.Count; i++) recipients.Add(i);
                    return ChatRoute.Delivered;

                default:
                    return ChatRoute.NotAPlayerChannel;
            }
        }

        /// <summary>Players called <paramref name="query"/> (any letter case), or the one player picked by "Name#clientId".</summary>
        public static void FindByName(string query, IReadOnlyList<ChatParticipant> everyone, List<int> matches)
        {
            matches.Clear();
            if (string.IsNullOrWhiteSpace(query)) return;
            query = query.Trim();

            ulong id = 0;
            bool hasId = false;
            int at = query.LastIndexOf(IdSeparator);
            if (at > 0 && ulong.TryParse(query.Substring(at + 1), out id))
            {
                hasId = true;
                query = query.Substring(0, at);
            }

            for (int i = 0; i < everyone.Count; i++)
            {
                if (!string.Equals(everyone[i].Name, query, StringComparison.OrdinalIgnoreCase)) continue;
                if (hasId && everyone[i].ClientId != id) continue;
                matches.Add(i);
            }
        }
    }
}
