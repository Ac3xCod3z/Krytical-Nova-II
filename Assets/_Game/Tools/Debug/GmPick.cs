using System.Collections.Generic;
using System.Text;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Lets a GM command take a list number instead of a long name (owner, 22 Oct 2026: "simplify the commands where we
    /// can"). The number is the entry's place, from 1, in the list that the command's listing command prints, and that
    /// list prints its numbers with <see cref="Numbered"/>, so what the GM reads is what they type.
    /// </summary>
    public static class GmPick
    {
        /// <summary>True when <paramref name="text"/> is a whole number from 1 to <paramref name="count"/>; <paramref name="index"/> is then 0-based.</summary>
        public static bool TryNumber(string text, int count, out int index)
        {
            index = -1;
            if (!int.TryParse((text ?? string.Empty).Trim(), out int number) || number < 1 || number > count) return false;
            index = number - 1;
            return true;
        }

        /// <summary>The lines with their numbers in front, "1  first", "2  second" ..., one a line.</summary>
        public static string Numbered(IEnumerable<string> lines)
        {
            var text = new StringBuilder();
            int number = 0;
            foreach (string line in lines)
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(++number).Append(number < 10 ? "   " : "  ").Append(line);
            }
            return text.ToString();
        }
    }
}
