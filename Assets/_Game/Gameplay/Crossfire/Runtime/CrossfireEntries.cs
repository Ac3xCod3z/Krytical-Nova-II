using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Crossfire
{
    /// <summary>A place to send a player back to: millimetres and facing (centidegrees), as the player's pose holds them.</summary>
    public struct CrossfireSpot
    {
        public Vector3Int Mm;
        public int YawCdeg;
    }

    /// <summary>
    /// Where each player came from before they joined the war (owner's DAY list item 17: "ends it on time and sends everyone back"). The
    /// server looks at every player a few times a second: the last place seen outside the war area is kept, and when the player is in a
    /// Muster Yard while the yards take players in, that place becomes their entry. When the war is over, everyone with an entry who is
    /// still in the war area goes back there. Server only; pure (no world), so it is tested as it is.
    /// </summary>
    public sealed class CrossfireEntries<T>
    {
        readonly Dictionary<T, CrossfireSpot> m_Outside = new Dictionary<T, CrossfireSpot>();
        readonly Dictionary<T, CrossfireSpot> m_Entry = new Dictionary<T, CrossfireSpot>();
        readonly List<T> m_Keys = new List<T>();

        /// <summary>Players who joined this war.</summary>
        public int Count => m_Entry.Count;

        public bool TryEntry(T who, out CrossfireSpot spot) => m_Entry.TryGetValue(who, out spot);

        /// <summary>One look at a player: where they stand, whether that is in the war area and in a yard, and whether the yards take players in.</summary>
        public void Observe(T who, CrossfireSpot now, bool inArea, bool inYard, bool joinOpen)
        {
            if (!inArea)
            {
                m_Outside[who] = now;
                return;
            }
            if (inYard && joinOpen && !m_Entry.ContainsKey(who) && m_Outside.TryGetValue(who, out CrossfireSpot from)) m_Entry[who] = from;
        }

        /// <summary>A player who left the game.</summary>
        public void Forget(T who)
        {
            m_Outside.Remove(who);
            m_Entry.Remove(who);
        }

        /// <summary>Forgets every player <paramref name="gone"/> says has left.</summary>
        public void ForgetWhere(Func<T, bool> gone)
        {
            m_Keys.Clear();
            m_Keys.AddRange(m_Outside.Keys);
            foreach (T who in m_Keys) if (gone(who)) Forget(who);
            m_Keys.Clear();
            m_Keys.AddRange(m_Entry.Keys);
            foreach (T who in m_Keys) if (gone(who)) Forget(who);
        }

        /// <summary>The war is over: who goes back where (those still in the war area), and the entries are cleared for the next war.</summary>
        public List<(T who, CrossfireSpot to)> TakeSendBack(Func<T, bool> stillInArea)
        {
            var back = new List<(T, CrossfireSpot)>();
            foreach (KeyValuePair<T, CrossfireSpot> entry in m_Entry)
                if (stillInArea(entry.Key)) back.Add((entry.Key, entry.Value));
            m_Entry.Clear();
            return back;
        }
    }
}
