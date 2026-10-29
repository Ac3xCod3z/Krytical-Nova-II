using System;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Gives Core's <see cref="BanLookup"/> slot the server's ban list, so the Netcode for Entities join check (which cannot see
    /// this assembly) turns banned accounts away with the same words <see cref="ServerBans.Refusal"/> writes.
    /// </summary>
    internal sealed class ServerBansLookup : IBanLookup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register() => BanLookup.Current = new ServerBansLookup();

        public bool TryGetBan(string account, DateTime utcNow, out string refusal)
        {
            refusal = null;
            if (ServerHooks.Saves == null) return false; // no saves loaded: no list to ask
            if (!ServerBans.IsBanned(account, utcNow, out BanEntry ban)) return false;
            refusal = ServerBans.Refusal(ban, utcNow);
            return true;
        }
    }
}
