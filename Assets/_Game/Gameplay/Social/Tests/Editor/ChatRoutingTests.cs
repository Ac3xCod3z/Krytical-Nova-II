using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Social;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Who hears a chat message on each channel, and who may speak on the GM channel.</summary>
    public sealed class ChatRoutingTests
    {
        private const float Shout = 40f;

        // 0: the host (Hegemony, at the origin).  1: Hegemony, 10 m away.  2: Null, 10 m away.
        // 3: Hegemony, across the map.  4: Covenant, across the map.
        private static List<ChatParticipant> World() => new List<ChatParticipant>
        {
            new ChatParticipant { ClientId = 0, Name = "Host", Faction = FactionId.Hegemony, Guild = "Wolves", Position = Vector3.zero, IsGameMaster = true },
            new ChatParticipant { ClientId = 1, Name = "Ally", Faction = FactionId.Hegemony, Guild = "", Position = new Vector3(10f, 0f, 0f) },
            new ChatParticipant { ClientId = 2, Name = "Foe", Faction = FactionId.Null, Guild = "wolves", Position = new Vector3(0f, 0f, 10f) },
            new ChatParticipant { ClientId = 3, Name = "Scout", Faction = FactionId.Hegemony, Guild = "", Position = new Vector3(500f, 0f, 0f) },
            new ChatParticipant { ClientId = 4, Name = "Scout", Faction = FactionId.Covenant, Guild = "", Position = new Vector3(-500f, 0f, 0f) },
        };

        private static ChatRoute Route(ChatChannel channel, int sender, out List<int> recipients, string whisper = null)
        {
            var world = World();
            recipients = new List<int>();
            return ChatRouting.Recipients(channel, world[sender], world, whisper, Shout, recipients);
        }

        [Test]
        public void GeneralIsHeardOnlyNearby()
        {
            Assert.AreEqual(ChatRoute.Delivered, Route(ChatChannel.General, 0, out var heard));
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, heard);
        }

        [Test]
        public void FactionReachesTheWholeFactionAnywhereAndNobodyElse()
        {
            Assert.AreEqual(ChatRoute.Delivered, Route(ChatChannel.Faction, 1, out var heard));
            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, heard);
        }

        [Test]
        public void GuildReachesItsMembersAcrossFactions()
        {
            Assert.AreEqual(ChatRoute.Delivered, Route(ChatChannel.Guild, 0, out var heard));
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, heard);
        }

        [Test]
        public void GuildChatNeedsAGuild()
        {
            Assert.AreEqual(ChatRoute.NotInGuild, Route(ChatChannel.Guild, 1, out var heard));
            Assert.IsEmpty(heard);
        }

        [Test]
        public void WhisperReachesOnePlayerWhateverTheLetterCase()
        {
            Assert.AreEqual(ChatRoute.Delivered, Route(ChatChannel.Whisper, 0, out var heard, "aLLy"));
            CollectionAssert.AreEqual(new[] { 1 }, heard);
        }

        [Test]
        public void WhisperToASharedNameAsksWhichOne()
        {
            Assert.AreEqual(ChatRoute.AmbiguousPlayer, Route(ChatChannel.Whisper, 0, out var candidates, "Scout"));
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, candidates);

            Assert.AreEqual(ChatRoute.Delivered, Route(ChatChannel.Whisper, 0, out var heard, "Scout#4"));
            CollectionAssert.AreEqual(new[] { 4 }, heard);
        }

        [Test]
        public void WhisperToNobodyOrToYourselfIsRefused()
        {
            Assert.AreEqual(ChatRoute.NoSuchPlayer, Route(ChatChannel.Whisper, 0, out _, "Ghost"));
            Assert.AreEqual(ChatRoute.NoSuchPlayer, Route(ChatChannel.Whisper, 0, out _, ""));
            Assert.AreEqual(ChatRoute.WhisperToSelf, Route(ChatChannel.Whisper, 0, out var heard, "Host"));
            Assert.IsEmpty(heard);
        }

        [Test]
        public void TheHostReachesEveryFactionEverywhereOnTheGmChannel()
        {
            Assert.AreEqual(ChatRoute.Delivered, Route(ChatChannel.Gm, 0, out var heard));
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, heard);
        }

        [Test]
        public void NobodyButTheHostCanSpeakOnTheGmChannel()
        {
            for (int client = 1; client <= 4; client++)
            {
                Assert.AreEqual(ChatRoute.NotGameMaster, Route(ChatChannel.Gm, client, out var heard));
                Assert.IsEmpty(heard);
            }
        }

        [Test]
        public void PlayersCannotWriteSystemOrCombatLines()
        {
            Assert.AreEqual(ChatRoute.NotAPlayerChannel, Route(ChatChannel.System, 0, out _));
            Assert.AreEqual(ChatRoute.NotAPlayerChannel, Route(ChatChannel.Combat, 0, out _));
        }

        [Test]
        public void MessagesAreTrimmedCutAndStrippedOfControlCharacters()
        {
            Assert.AreEqual(string.Empty, ChatRouting.Clean("   \t "));
            Assert.AreEqual("hello there", ChatRouting.Clean("  hello\nthere  "));
            Assert.AreEqual(ChatRouting.MaxLength, ChatRouting.Clean(new string('x', 500)).Length);
        }

        [Test]
        public void TypedLinesPickTheirChannel()
        {
            ChatInput plain = ChatCommands.Parse("hello", ChatChannel.Faction, null, null);
            Assert.AreEqual(ChatInputKind.Send, plain.Kind);
            Assert.AreEqual(ChatChannel.Faction, plain.Channel);

            ChatInput whisper = ChatCommands.Parse("/w Ally meet me at the plaza", ChatChannel.General, null, null);
            Assert.AreEqual(ChatInputKind.Send, whisper.Kind);
            Assert.AreEqual(ChatChannel.Whisper, whisper.Channel);
            Assert.AreEqual("Ally", whisper.Target);
            Assert.AreEqual("meet me at the plaza", whisper.Text);

            Assert.AreEqual(ChatInputKind.SwitchChannel, ChatCommands.Parse("/g", ChatChannel.General, null, null).Kind);
            Assert.AreEqual(ChatInputKind.Notice, ChatCommands.Parse("/r hi", ChatChannel.General, null, null).Kind);
            Assert.AreEqual("Foe", ChatCommands.Parse("/r hi", ChatChannel.General, null, "Foe").Target);
            Assert.AreEqual(ChatInputKind.Notice, ChatCommands.Parse("/nonsense", ChatChannel.General, null, null).Kind);
            Assert.AreEqual(ChatInputKind.Nothing, ChatCommands.Parse("   ", ChatChannel.General, null, null).Kind);
        }
    }
}
