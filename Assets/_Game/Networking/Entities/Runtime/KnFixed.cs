using System.Text;
using Unity.Collections;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Text into fixed-size strings without ever failing: a longer text is cut on a character boundary, so a ban reason of any
    /// length cannot break the refusal that carries it. Plain C#, easy to test.
    /// </summary>
    public static class KnFixed
    {
        /// <summary>FixedString64Bytes holds 61 bytes, FixedString512Bytes 509 (length byte, terminator and a margin of one).</summary>
        public const int Max64 = 60;
        public const int Max512 = 500;

        /// <summary>The longest start of <paramref name="text"/> that is at most <paramref name="maxBytes"/> bytes in UTF-8.</summary>
        public static string CutUtf8(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
            int bytes = 0, end = 0;
            while (end < text.Length)
            {
                int step = char.IsHighSurrogate(text[end]) && end + 1 < text.Length ? 2 : 1;
                int size = Encoding.UTF8.GetByteCount(text.Substring(end, step));
                if (bytes + size > maxBytes) break;
                bytes += size;
                end += step;
            }
            return text.Substring(0, end);
        }

        public static FixedString64Bytes Text64(string text) => new FixedString64Bytes(CutUtf8(text, Max64));

        public static FixedString512Bytes Text512(string text) => new FixedString512Bytes(CutUtf8(text, Max512));
    }
}
