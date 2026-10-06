using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The service windows of this HUD, by name. A window registers what it opens (the Vault, the Crew registry);
    /// the NPC dialogue shows a button for each one the NPC offers, and gameplay or a command can ask for one by
    /// id through <see cref="IServiceWindowSink"/>. A new service adds a registration and nothing else changes.
    /// </summary>
    public static class NpcServices
    {
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly NpcOffer Offer;
            public readonly string Label;
            public readonly Action Open;

            public Entry(string id, NpcOffer offer, string label, Action open)
            {
                Id = id;
                Offer = offer;
                Label = label;
                Open = open;
            }
        }

        private sealed class Sink : IServiceWindowSink
        {
            public bool OpenService(string serviceId) => NpcServices.Open(serviceId);
        }

        private static readonly List<Entry> Entries = new List<Entry>(4);
        private static readonly Sink Windows = new Sink();

        /// <summary>Every registered service, in the order the dialogue shows them.</summary>
        public static IReadOnlyList<Entry> All => Entries;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Entries.Clear();

        /// <param name="offer">The NPC offer that shows this service's button; None for a service no NPC opens.</param>
        public static void Register(string id, NpcOffer offer, string label, Action open)
        {
            if (string.IsNullOrEmpty(id) || open == null) return;
            Unregister(id);
            Entries.Add(new Entry(id, offer, label, open));
            ClientPresentationRegistry.RegisterServices(Windows);
        }

        public static void Unregister(string id)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
                if (Entries[i].Id == id) Entries.RemoveAt(i);
        }

        public static bool Open(string id)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Id != id) continue;
                Entries[i].Open();
                return true;
            }
            return false;
        }
    }
}
