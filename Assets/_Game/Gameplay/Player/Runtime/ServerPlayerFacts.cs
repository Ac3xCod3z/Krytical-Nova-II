using KryticalNova.Factions;
using KryticalNova.Networking;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// Tells the server's own code who a player is (<see cref="ServerHooks.DescribePlayer"/>): the character's name, its
    /// account, faction and level, for the security flags, the records, bans and the control page. Networking sits
    /// below the player and cannot read <see cref="PlayerIdentity"/> itself.
    /// </summary>
    internal static class ServerPlayerFacts
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]   // EditMode tests of the server's code see it too
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => ServerHooks.DescribePlayer = Describe;

        private static ServerHooks.PlayerFacts Describe(GameObject player)
        {
            var facts = new ServerHooks.PlayerFacts { Faction = "-" };
            if (player.TryGetComponent(out PlayerIdentity identity))
            {
                facts.Found = true;
                facts.Character = identity.CharacterName;
                facts.Account = identity.AccountId;
                if (identity.Faction != FactionId.None) facts.Faction = identity.Faction.ToString();
            }
            if (player.TryGetComponent(out PlayerCharacterProgression progression)) facts.Level = progression.CurrentLevel;
            return facts;
        }
    }
}
