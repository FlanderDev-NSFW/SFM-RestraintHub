using System;
using System.Collections.Generic;

namespace RestraintHub.Logic
{
    /// <summary>
    /// A key setting from the config: a comma-separated list, each entry either one character - the key that types it on the
    /// player's keyboard layout (<c>+</c>, <c>-</c>, <c>U</c>) - or an Input System key name, a fixed position on the keyboard
    /// (<c>NumpadPlus</c>, <c>RightBracket</c>, <c>Backquote</c>). Pure: KeyBindings.cs resolves the entries to keys.
    /// </summary>
    public readonly struct KeyEntry
    {
        public readonly bool IsCharacter;
        public readonly string Text;
        public KeyEntry(bool isCharacter, string text) { IsCharacter = isCharacter; Text = text; }
        public override string ToString() => IsCharacter ? "'" + Text + "'" : Text;
    }

    public static class KeySpec
    {
        /// <summary>The entries of <paramref name="spec"/>: split at commas, trimmed, empty ones dropped; one character = a
        /// character entry, longer = a key name (the comma key itself: <c>Comma</c>).</summary>
        public static List<KeyEntry> Parse(string spec)
        {
            var list = new List<KeyEntry>();
            if (string.IsNullOrWhiteSpace(spec)) return list;
            foreach (string raw in spec.Split(','))
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                list.Add(new KeyEntry(t.Length == 1, t));
            }
            return list;
        }

        /// <summary>Whether two characters are the same key label (letters ignore case: the label "U" types "u").</summary>
        public static bool SameLabel(string a, string b) =>
            a != null && b != null && string.Equals(a.Trim(), b.Trim(), a.Trim().Length == 1 && char.IsLetter(a.Trim()[0]) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        /// <summary>A short text for messages: the first entry ("U", "+").</summary>
        public static string Short(string spec)
        {
            var e = Parse(spec);
            return e.Count == 0 ? "(none)" : e[0].Text;
        }
    }
}
