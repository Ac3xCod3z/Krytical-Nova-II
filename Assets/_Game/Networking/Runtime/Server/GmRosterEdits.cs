using System;
using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The control page's GM buttons (add, bind, unbind, remove), apart from any page or netcode so both servers use the same rules and words
    /// (NGO's <c>ServerControlPage</c>, and the entity netcode's page, M7.4, 2 Nov 2026). <c>save</c> writes the list into the settings file and
    /// returns null, or the reason it could not; the roster changes only after the file did.
    /// </summary>
    public static class GmRosterEdits
    {
        public static string Add(string name, Func<GmEntry[], string> save)
        {
            var list = new List<GmEntry>(GmRoster.All);
            string problem = GmRoster.ValidateNew(list, name);
            if (problem != null) return problem;
            name = name.Trim();
            list.Add(new GmEntry { name = name });
            string error = save(list.ToArray());
            if (error != null) return error;
            GmRoster.SetAll(list);
            Debug.Log($"[GM] {name} was made a GM on the control page.");
            return $"{name} is a GM. Bind them: pick the player beside their name while they are in the game.";
        }

        /// <summary>
        /// Binds a GM to the account of the player on <paramref name="client"/> (in the game now; <paramref name="player"/> is their character's
        /// name for the answer). They are GM at once and on every later join.
        /// </summary>
        public static string Bind(string name, ulong client, string player, Func<GmEntry[], string> save)
        {
            GmEntry entry = GmRoster.Find((name ?? string.Empty).Trim());
            if (entry == null) return $"There is no GM called {name}.";
            if (!ServerAccounts.TryGet(NetPlayerKeys.Find(client), out ServerAccounts.Entry proven)) return "That player's account is not proven (they joined without Unity sign-in).";
            GmEntry already = GmRoster.FindByAccount(proven.Account);
            if (already != null && already != entry) return $"That player is already GM {already.name}.";
            var list = new List<GmEntry>(GmRoster.All);
            foreach (GmEntry each in list)
                if (each == entry) each.account = proven.Account;
            string error = save(list.ToArray());
            if (error != null) return error;
            GmRoster.SetAll(list);
            GmRoster.SignInBound(client, out _);
            Debug.Log($"[GM] {entry.name} was bound to {player}'s account on the control page.");
            return $"{entry.name} is now {player}'s account: GM at once, and every time they join.";
        }

        public static string Unbind(string name, Func<GmEntry[], string> save)
        {
            GmEntry entry = GmRoster.Find((name ?? string.Empty).Trim());
            if (entry == null) return $"There is no GM called {name}.";
            if (!entry.IsBound) return $"{entry.name} is not bound to anyone.";
            var list = new List<GmEntry>(GmRoster.All);
            foreach (GmEntry each in list)
                if (each == entry) each.account = string.Empty;
            string error = save(list.ToArray());
            if (error != null) return error;
            GmRoster.SetAll(list);
            Debug.Log($"[GM] {entry.name} was unbound on the control page.");
            return $"{entry.name} is bound to no one now; their GM rights ended.";
        }

        /// <summary>Removes a Game Master; if they are signed in, their GM rights end at once.</summary>
        public static string Remove(string name, Func<GmEntry[], string> save)
        {
            GmEntry entry = GmRoster.Find((name ?? string.Empty).Trim());
            if (entry == null) return $"There is no GM called {name}.";
            bool online = GmRoster.IsSignedIn(entry.name);
            var list = new List<GmEntry>(GmRoster.All);
            list.Remove(entry);
            string error = save(list.ToArray());
            if (error != null) return error;
            GmRoster.SetAll(list);
            Debug.Log($"[GM] {entry.name} is no longer a GM (control page).");
            return $"{entry.name} is no longer a GM." + (online ? " Their GM rights ended at once." : string.Empty);
        }
    }
}
