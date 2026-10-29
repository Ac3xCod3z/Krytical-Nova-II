using System.Collections.Generic;
using System.Text;

namespace KryticalNova.Networking.Entities
{
    /// <summary>Plain C# splitting and joining of the join payload (no ECS), so it is easy to test.</summary>
    public static class KnApprovalChunks
    {
        /// <summary>Bytes per chunk: well under a FixedString512Bytes (509) and under one packet.</summary>
        public const int MaxChunkBytes = 480;

        /// <summary>True when the text is ASCII and fits <paramref name="maxChunks"/> chunks; chunks stay empty otherwise.</summary>
        public static bool TrySplit(string payload, int maxChunks, List<string> chunks)
        {
            chunks.Clear();
            if (string.IsNullOrEmpty(payload)) return false;
            for (int i = 0; i < payload.Length; i++)
                if (payload[i] > 127) return false; // UTF-8 bytes would not equal chars; tokens are ASCII
            int count = (payload.Length + MaxChunkBytes - 1) / MaxChunkBytes;
            if (count > maxChunks) return false;
            for (int i = 0; i < count; i++)
                chunks.Add(payload.Substring(i * MaxChunkBytes, System.Math.Min(MaxChunkBytes, payload.Length - i * MaxChunkBytes)));
            return true;
        }

        public static string Join(string[] parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++) sb.Append(parts[i]);
            return sb.ToString();
        }
    }
}
