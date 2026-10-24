using System;
using System.Collections.Generic;

namespace RestraintHub
{
    /// <summary>
    /// What a restraint holds (<see cref="Restraint.Holds"/>). While <c>General.BlockEachOther</c> is on, two restraints that
    /// share a part cannot be worn together: the one chosen second is refused with "Can't use the X: the Y holds her ...".
    /// </summary>
    [Flags]
    public enum BodyParts
    {
        /// <summary>Holds nothing the hub checks (never conflicts).</summary>
        None = 0,
        /// <summary>Neck (collars, yokes).</summary>
        Neck = 1,
        /// <summary>Arms and hands. The game's own handcuffs count as holding this.</summary>
        Arms = 2,
        /// <summary>Legs and feet (spreader bars, ankle cuffs).</summary>
        Legs = 4,
        /// <summary>She is held at a place in the world (a wall, a post): nothing else that moves her can be added.</summary>
        Spot = 8,
    }

    /// <summary>
    /// What a worn restraint takes away from the player (<see cref="Restraint.Blocks"/>). The hub clears these fields of the
    /// game's resolved input every frame (postfix on <c>InputResolver.Resolve</c>), for the union of all worn restraints.
    /// Refused hand actions and pose skills show "Can't do that with the X on." (at most once a second).
    /// </summary>
    [Flags]
    public enum Blocks
    {
        /// <summary>Takes nothing away.</summary>
        None = 0,
        /// <summary>No running: <c>Dash</c>, <c>JustAutoRun</c>, <c>JustAutoChase</c>. Shift stays usable while a pose skill
        /// runs, because Shift + move is how the player ends one.</summary>
        Running = 1,
        /// <summary>No moving at all: <c>Move</c> zeroed, <c>JustMove</c>, <c>Jump</c>, <c>MoveForwardOneHand</c>,
        /// <c>JustSlowSkill</c>, and running (as <see cref="Running"/>, Shift included).</summary>
        Moving = 2,
        /// <summary>No crouching / crawling: <c>JustCrouch</c>.</summary>
        Crouching = 4,
        /// <summary>Nothing that needs her hands: masturbating, the dildo, the game's handcuffs, the eye mask, drinking,
        /// dropping items (and the handcuff key / vibrator remote), taking off bra or pants, opening or closing the coat
        /// (<c>JustOnaniNormal</c>, <c>JustRingDildo</c>, <c>JustRingHandcuff</c>, <c>JustRingEyeMask</c>, <c>JustEyeMask</c>,
        /// <c>JustDrinkWater</c>, <c>JustDrop*</c>, <c>JustBra</c>, <c>JustPants</c>, <c>JustOpen/CloseClothesA/B</c>,
        /// <c>MoveForwardOneHand</c>). Shows the refusal message.</summary>
        HandActions = 8,
        /// <summary>No pose skills from the ring or number keys (<c>JustPoseSkill</c>). Shows the refusal message. A pose that
        /// already runs is not ended - end it yourself when the restraint goes on if it needs the bones the pose uses
        /// (Yoke: <c>pca.PlayerAction.ChangeAction(ActionType.None)</c>).</summary>
        PoseSkills = 16,
        /// <summary>No peeing: <c>JustPee</c>.</summary>
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
