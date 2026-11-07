using KryticalNova.Social;
using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Owner's 2-player test, 6 Nov 2026, item 16b: the menu's Follow looks for the target in the local player's Squad. NGO matches a
    /// member in sight by network object id; the entity netcode has none (its own row is 0, the others are server keys a client body
    /// does not know), so it matches by name. That is safe: character names are unique across the whole game, capitals ignored
    /// (ServerRoster.cs:15, CharacterNameRules.cs:8; the server refuses a taken name), and a client view has no server key to prefer.
    /// </summary>
    public sealed class PlayerMenuSquadTests
    {
        static PartyMember Member(ulong id, string name) => new PartyMember { ObjectId = id, Name = name, Level = 60 };

        [Test]
        public void Ngo_MatchesByObjectIdInSight_AndByNameOutOfSight_NeverItself()
        {
            Assert.IsTrue(PlayerMenuController.IsOtherSquadMember(Member(7, "Vesper"), true, 3, 7, "someone"), "in sight: by id");
            Assert.IsFalse(PlayerMenuController.IsOtherSquadMember(Member(8, "Vesper"), true, 3, 7, "Vesper"), "in sight: another id is not them");
            Assert.IsTrue(PlayerMenuController.IsOtherSquadMember(Member(8, "Vesper"), true, 3, 0, "vesper"), "out of sight: by name");
            Assert.IsFalse(PlayerMenuController.IsOtherSquadMember(Member(3, "Me"), true, 3, 0, "Me"), "never the local player's own row");
        }

        [Test]
        public void EntityNetcode_MatchesByName_TheOwnRowIsZero()
        {
            Assert.IsTrue(PlayerMenuController.IsOtherSquadMember(Member(642, "Tester"), false, 0, 0, "Tester"), "a member in sight, by name");
            Assert.IsFalse(PlayerMenuController.IsOtherSquadMember(Member(642, "Tester"), false, 0, 0, "Player2"), "someone else");
            Assert.IsFalse(PlayerMenuController.IsOtherSquadMember(Member(0, "Player2"), false, 0, 0, "Player2"), "the own row (id 0) is never a target");
        }
    }
}
