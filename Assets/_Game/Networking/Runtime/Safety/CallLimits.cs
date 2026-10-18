using System;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>The kinds of request a game sends its server, each with its own speed limit (<see cref="CallLimits"/>).</summary>
    public enum CallKind : byte
    {
        /// <summary>Click to move, jump, travel, follow.</summary>
        Move,
        /// <summary>Skills, attacks, hit reports.</summary>
        Combat,
        /// <summary>Moving items in the Pack, gear and the Vault.</summary>
        Items,
        /// <summary>Trade, Kiosk, shop, forge, anything that spends or earns.</summary>
        Money,
        /// <summary>Invites, Crew, Contacts, Squad, Sponsor.</summary>
        Social,
        /// <summary>Chat messages.</summary>
        Chat,
        /// <summary>Emotes, pet tricks, looks.</summary>
        Cosmetic,
        /// <summary>Sign-in, identity and save requests, GM lines.</summary>
        Account,
    }

    /// <summary>One speed limit: a burst that can go at once, refilled steadily.</summary>
    [Serializable]
    public struct CallLimit
    {
        [Tooltip("How many may come at once.")]
        public float burst;
        [Tooltip("How many more are allowed each second.")]
        public float perSecond;

        public CallLimit(float burst, float perSecond)
        {
            this.burst = burst;
            this.perSecond = perSecond;
        }
    }

    /// <summary>
    /// The speed limits of every kind of request, how many ignored requests disconnect a game, and the longest text taken
    /// (online phase 2, milestone 3). On a server they come from <c>server-settings.json</c> ("callLimits"), so the owner
    /// tunes them without a new build; these are the defaults, loose enough that fast real play never meets them.
    /// </summary>
    [Serializable]
    public sealed class CallLimits
    {
        public CallLimit move = new CallLimit(30f, 20f);
        public CallLimit combat = new CallLimit(25f, 15f);
        public CallLimit items = new CallLimit(40f, 15f);
        public CallLimit money = new CallLimit(10f, 5f);
        public CallLimit social = new CallLimit(8f, 3f);
        public CallLimit chat = new CallLimit(5f, 1f);
        public CallLimit cosmetic = new CallLimit(8f, 4f);
        public CallLimit account = new CallLimit(5f, 2f);
        [Tooltip("Ignored requests from one game within floodSeconds that disconnect it.")]
        public int floodDrops = 50;
        public float floodSeconds = 10f;
        [Tooltip("The longest text a request may carry (a name, a title, a chat line); longer is refused and flagged.")]
        public int longestText = 500;

        public CallLimit For(CallKind kind)
        {
            switch (kind)
            {
                case CallKind.Move: return move;
                case CallKind.Combat: return combat;
                case CallKind.Items: return items;
                case CallKind.Money: return money;
                case CallKind.Social: return social;
                case CallKind.Chat: return chat;
                case CallKind.Cosmetic: return cosmetic;
                default: return account;
            }
        }
    }
}
