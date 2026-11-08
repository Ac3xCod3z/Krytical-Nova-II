using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Test lane only (owner, 29 Oct 2026, plan 43.1): the headless bots of the 2-client gate join with dev-bot tokens, whose
    /// accounts are real ids with no character on the server's roster, and the entity netcode cannot create one yet. On a test-lane
    /// server (<c>KN_TEST_LANE</c>, with development bots on) a bot's character is found by its name in its account, or made through
    /// the game's own roster create (<see cref="ServerRoster.Handle"/>: name and look rules, a free slot, one save), so the bot is then
    /// loaded and saved like any player, in the test database. The same for a signed-in player that sends no character id (the entity
    /// test client, which has no character select yet). Compiled out of every other build: there <see cref="Ensure"/> hands
    /// back the id it was given and touches nothing.
    /// </summary>
    public static class TestLaneBotCharacters
    {
        /// <summary>True only in a test-lane build.</summary>
        public const bool Compiled =
#if KN_TEST_LANE
            true;
#else
            false;
#endif

        /// <summary>Whether an account joined as a development bot (the setup sets it from the join check's list); null = none is.</summary>
        public static Func<string, bool> IsBot;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => IsBot = null;

        /// <summary>
        /// The character to load for <paramref name="account"/>: <paramref name="characterId"/> as sent, except for a bot on a test-lane
        /// server, which gets its character named <paramref name="name"/> (made on its first entry). Null with
        /// <paramref name="refusal"/> when the roster would not make it.
        /// </summary>
        public static string Ensure(string account, string characterId, string name, CharacterRecipe recipe, out string refusal)
        {
            refusal = null;
#if KN_TEST_LANE
            bool bot = IsBot != null && IsBot(account);
            // The entity test client (KnJoinHere -knsignin, DOTS builds 8 Nov 2026) signs in for real but has no character select yet:
            // a proven account that sends no character id is treated like a bot here (its character found by name, or made once), on a
            // test-lane server only, in the test database. Anyone who sends a character id keeps it.
            bool signedInWithoutCharacter = !bot && SaveKeys.IsId(account) && string.IsNullOrEmpty(characterId);
            if (!bot && !signedInWithoutCharacter) return characterId;
            AccountSave saved = ServerSave.ReadAccountFrom(ServerSave.Store, account);
            if (saved?.characters != null)
                foreach (CharacterSave c in saved.characters)
                    if (c != null && string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) return c.characterId;
            RosterReply made = ServerRoster.Handle(account, null, new RosterRequest { op = RosterRequest.Create, name = name, recipe = recipe });
            if (made == null || !made.ok || string.IsNullOrEmpty(made.characterId))
            {
                refusal = "The test server could not make this " + (bot ? "bot's" : "test player's") + " character: " + (made?.message ?? "no answer");
                return null;
            }
            return made.characterId;
#else
            return characterId;
#endif
        }
    }
}
