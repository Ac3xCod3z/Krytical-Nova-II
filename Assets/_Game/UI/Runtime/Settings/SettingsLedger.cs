using System;
using System.Collections.Generic;

namespace KryticalNova.UI
{
    /// <summary>
    /// What Apply and Revert work on. A setting takes effect the moment its control is moved, so the player sees
    /// what it does; the ledger remembers every value as it was when Settings was opened (or last applied), which is
    /// what REVERT puts back and what APPLY forgets. Every row of the Settings pages writes itself into the ledger
    /// that is recording while its page is built.
    /// </summary>
    public sealed class SettingsLedger
    {
        private sealed class Entry
        {
            public Func<object> Get;
            public Action<object> Set;
            public Action Show;
            public object Marked;
        }

        private readonly List<Entry> entries = new List<Entry>();

        /// <summary>The ledger rows add themselves to while a page is built; null outside of that.</summary>
        public static SettingsLedger Recording { get; set; }

        public int Count => entries.Count;

        public void Clear() => entries.Clear();

        /// <summary>One setting: how to read it, how to write it, and how its row shows the stored value again.</summary>
        public void Add<T>(Func<T> get, Action<T> set, Action show)
        {
            entries.Add(new Entry { Get = () => get(), Set = value => set((T)value), Show = show, Marked = get() });
        }

        /// <summary>Remembers every value as it is now: Settings was opened, or the changes were applied.</summary>
        public void Mark()
        {
            foreach (Entry entry in entries) entry.Marked = entry.Get();
        }

        /// <summary>True when a value is no longer what it was at the last mark.</summary>
        public bool HasChanges
        {
            get
            {
                foreach (Entry entry in entries)
                    if (!Equals(entry.Get(), entry.Marked)) return true;
                return false;
            }
        }

        /// <summary>Puts every value back to what it was at the last mark, and shows it.</summary>
        public void Revert()
        {
            // Twice: one setting can move another (a quality preset writes the details under it), and the second
            // pass sets right whatever the first one's order disturbed.
            for (int pass = 0; pass < 2; pass++)
                foreach (Entry entry in entries)
                    if (!Equals(entry.Get(), entry.Marked)) entry.Set(entry.Marked);
            ShowAll();
        }

        /// <summary>Every row shows its stored value again (a reset, a revert, or the other Settings window changed it).</summary>
        public void ShowAll()
        {
            foreach (Entry entry in entries) entry.Show?.Invoke();
        }
    }
}
