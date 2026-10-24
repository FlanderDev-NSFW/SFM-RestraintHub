using System;
using System.Collections.Generic;

namespace RestraintHub
{
    /// <summary>What a restraint holds; two restraints that hold the same part cannot be worn together.</summary>
    [Flags]
    public enum BodyParts
    {
        None = 0,
        Neck = 1,
        Arms = 2,      // arms / hands (the game's handcuffs count here)
        Legs = 4,
        Spot = 8,      // she is held at a place (a wall)
    }

    /// <summary>What a worn restraint takes away from the player's input (the hub applies the union of all worn ones).</summary>
    [Flags]
    public enum Blocks
    {
        None = 0,
        Running = 1,      // Shift, auto run / chase (Shift still ends a pose skill, as in vanilla)
        Moving = 2,       // walking, jumping, the slow skill
        Crouching = 4,
        HandActions = 8,  // masturbating, toys, the game's handcuffs, eye mask, drinking, dropping items, clothes
        PoseSkills = 16,
        Pee = 32,
    }
}

namespace RestraintHub.Logic
{
    public static class Rules
    {
        public const int PageSize = 8;

        /// <summary>Empty slots after the game's entries so the mods' entries start on a page of their own.</summary>
        public static int Padding(int vanillaCount) => (PageSize - vanillaCount % PageSize) % PageSize;

        /// <summary>The first worn restraint that holds a part <paramref name="want"/> needs, and the parts they share; null if none.</summary>
        public static (string Label, BodyParts Shared)? Conflict(BodyParts want, IEnumerable<(string Label, BodyParts Holds)> worn)
        {
            foreach (var (label, holds) in worn)
                if ((holds & want) != 0) return (label, holds & want);
            return null;
        }

        /// <summary>Plain words for the parts in <paramref name="parts"/> ("her arms and legs").</summary>
        public static string Describe(BodyParts parts)
        {
            var words = new List<string>();
            if ((parts & BodyParts.Neck) != 0) words.Add("neck");
            if ((parts & BodyParts.Arms) != 0) words.Add("arms");
            if ((parts & BodyParts.Legs) != 0) words.Add("legs");
            string s = words.Count == 0 ? "" : "her " + string.Join(words.Count == 2 ? " and " : ", ", words);
            if ((parts & BodyParts.Spot) != 0) s += (s.Length == 0 ? "" : " and ") + "her in place";
            return s;
        }
    }

    /// <summary>
    /// The restraints she wears, in the order they were put on, and which one is selected (the one F and + / - act on).
    /// A newly worn restraint becomes the selected one; when the selected one comes off, the next one in the list (or the last)
    /// is selected.
    /// </summary>
    public sealed class WornList
    {
        private readonly List<string> _items = new List<string>();

        public IReadOnlyList<string> Items => _items;
        public string Selected { get; private set; }

        /// <summary>Bring the list up to date with what is worn now (ids, any order). Returns true when anything changed.</summary>
        public bool Update(IEnumerable<string> wornNow)
        {
            var order = new List<string>(wornNow);
            var now = new HashSet<string>(order);
            bool changed = false;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (now.Contains(_items[i])) continue;
                if (_items[i] == Selected)
                    Selected = _items.Count == 1 ? null : _items[i + 1 < _items.Count ? i + 1 : i - 1];
                _items.RemoveAt(i);
                changed = true;
            }
            foreach (string id in order)
            {
                if (_items.Contains(id)) continue;
                _items.Add(id);
                Selected = id; // newly put on: selected
                changed = true;
            }
            if (Selected == null && _items.Count > 0) { Selected = _items[0]; changed = true; }
            return changed;
        }

        /// <summary>Select the next one (wraps). Returns the selected id.</summary>
        public string Next()
        {
            if (_items.Count == 0) return Selected = null;
            int i = Selected == null ? -1 : _items.IndexOf(Selected);
            Selected = _items[(i + 1) % _items.Count];
            return Selected;
        }

        /// <summary>Select <paramref name="id"/> if it is in the list.</summary>
        public bool Select(string id)
        {
            if (!_items.Contains(id)) return false;
            Selected = id;
            return true;
        }
    }
}
