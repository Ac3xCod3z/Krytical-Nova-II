using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// A rule on a player's body that must be told the player is leaving the world, while the body still exists: the save
    /// is written and the account let go (<c>PlayerSaveAgent</c>), an open trade or Kiosk is cancelled. The server calls
    /// it once, before it destroys the body. It must be safe to call twice (a second call finds nothing to do).
    /// </summary>
    public interface IServerLeaving
    {
        void OnServerLeaving();
    }

    public static class ServerLeaving
    {
        /// <summary>Tells every <see cref="IServerLeaving"/> on a body that its player is leaving.</summary>
        public static void Notify(GameObject body)
        {
            if (body == null) return;
            var rules = body.GetComponents<IServerLeaving>();
            for (int i = 0; i < rules.Length; i++) rules[i].OnServerLeaving();
        }
    }
}
