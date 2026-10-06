namespace KryticalNova.Core
{
    public static class SaveKeys
    {
        public const int MaxLength = 96;

        /// <summary>Lower-case letters, digits, '-', '_' and '/' as a separator; no empty or dotted segments, so a key can never leave the save folder.</summary>
        public static bool IsValid(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxLength || key[0] == '/' || key[key.Length - 1] == '/') return false;
            char previous = '/';
            foreach (char c in key)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '/';
                if (!ok || (c == '/' && previous == '/')) return false;
                previous = c;
            }
            return true;
        }

        /// <summary>True for the ids the game hands out: 32 lower-case hex characters (a GUID without dashes).</summary>
        public static bool IsId(string id)
        {
            if (id == null || id.Length != 32) return false;
            foreach (char c in id)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
