using System;
using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// What the server's own parts (security flags, the records, bans, the control page, the safe stop, the start) need
    /// from the game: who a player is, how to say something to everyone, and the saves. Networking sits below the game's
    /// systems and cannot name a player, a chat line or a save itself, so those systems fill these slots when the game
    /// loads: <c>Player</c> (who a player is), <c>Social</c> (announcing) and <c>Persistence</c> (the saves). An empty
    /// slot means that system is not loaded (an EditMode test of Networking alone); every caller copes with it.
    /// </summary>
    public static class ServerHooks
    {
        /// <summary>Who a player is, as the server's records and its control page show it.</summary>
        public struct PlayerFacts
        {
            /// <summary>True when the object is a player the game knows (it has a character identity).</summary>
            public bool Found;
            /// <summary>The character's name; empty before a character is chosen.</summary>
            public string Character;
            /// <summary>The account the character is saved under; empty when unknown.</summary>
            public string Account;
            /// <summary>The faction's name, or "-" without one.</summary>
            public string Faction;
            public int Level;
        }

        /// <summary>Who the player on this object is. Filled by the Player system.</summary>
        public static Func<GameObject, PlayerFacts> DescribePlayer;

        /// <summary>Says a line to every player online as the server (the GM channel); returns how many heard it. Filled by Social.</summary>
        public static Func<string, string, int> Announce;

        /// <summary>The saves. Filled by Persistence.</summary>
        public static IServerSaves Saves;

        /// <summary>The facts of the player on <paramref name="player"/>, or empty facts when there is none or nothing fills the slot.</summary>
        public static PlayerFacts Describe(GameObject player) =>
            player != null && DescribePlayer != null ? DescribePlayer(player) : new PlayerFacts { Faction = "-" };
    }

    /// <summary>The parts of the saves the server's own code calls (bans, the start, the safe stop, the control page).</summary>
    public interface IServerSaves
    {
        /// <summary>True on the server (and offline): only it may write the saves.</summary>
        bool HasAuthority { get; }

        /// <summary>A world document by name (the bans), read once and kept.</summary>
        T World<T>(string name) where T : class, new();

        /// <summary>Writes a world document now.</summary>
        void SaveWorld(string name);

        /// <summary>Every account that has a character of this name.</summary>
        List<string> AccountsWithCharacter(string characterName);

        /// <summary>Saves every player and world document now.</summary>
        void FlushAll();

        /// <summary>Whether old saves from before sign-in are carried over to the proven account, once.</summary>
        bool CarryOverEnabled { set; }

        /// <summary>False when the saves go through the save service and it does not answer; <paramref name="problem"/> says why.</summary>
        bool ServiceReachable(out string problem);

        /// <summary>Where the saves go, for the control page ("saves: files", or the service and database with whether both answer).</summary>
        string Status(out bool ok);

        /// <summary>The kept ledger, when the store keeps one.</summary>
        ILedgerStore Ledger { get; }

        /// <summary>Saves everyone and backs the saves up into <paramref name="backupFolder"/>, keeping <paramref name="kept"/>. Returns the log line.</summary>
        string Backup(string backupFolder, int kept, string reason);
    }
}
