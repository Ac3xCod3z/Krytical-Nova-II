using System.IO;
using SQLite;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Read-only access to Sidekick's SQLite database for our editor bakers (part flags, colour cells, skin presets,
    /// body presets, the rig-movement table). Runtime code never touches the database: everything it needs is baked
    /// into our own assets.
    /// </summary>
    internal static class SidekickDatabase
    {
        public const string DatabasePath = "Assets/Synty/SidekickCharacters/Database/Side_Kick_Data.db";

        /// <summary>Returns null (after logging) when the database is missing. Dispose the connection when done.</summary>
        public static SQLiteConnection OpenReadOnly()
        {
            if (!File.Exists(DatabasePath))
            {
                Debug.LogError($"[Sidekick] Database not found at {DatabasePath}.");
                return null;
            }
            return new SQLiteConnection(DatabasePath, SQLiteOpenFlags.ReadOnly);
        }
    }
}
