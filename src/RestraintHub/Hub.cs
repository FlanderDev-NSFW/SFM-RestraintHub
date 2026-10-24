using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;

namespace RestraintHub
{
    /// <summary>
    /// One restraint mod's registration with the hub (<see cref="Hub.Register"/>). The mod fills in the description and the
    /// callbacks before registering; the hub sets <see cref="Selected"/> / <see cref="StruggleHeld"/> and hands out + / - steps.
    /// Everything runs on Unity's main thread.
    /// </summary>
    public sealed class Restraint
    {
        // --- filled in by the mod -----------------------------------------------------------------------------------------

        /// <summary>Stable id ("SpreaderBar"): orders the ring entries, names it in the log.</summary>
        public string Id;
        /// <summary>RingItemType value of its ring entry, unique among mods (9000..9998).</summary>
        public int RingValue;
        /// <summary>Name in the ring and the list (read every time, so a config change shows at once).</summary>
        public Func<string> Label;
        /// <summary>Save key that holds the number shortcuts with this entry (kept from before the hub, so old saves keep them).</summary>
        public string ShortcutSaveKey;
        /// <summary>Optional: the save has no shortcut key; return a mask to use (SpreaderBar repairs saves from 0.1.0 here).</summary>
        public Func<int> OnShortcutsMissing;
        public BodyParts Holds;
        public Func<bool> IsWorn;
        /// <summary>What it takes away while worn (may change, e.g. with a config mode).</summary>
        public Func<Blocks> Blocks;
        /// <summary>The player chose the entry and nothing worn conflicts with it: put it on (or say why not).</summary>
        public Action OnChosen;
        /// <summary>Progress of the current struggle attempt, 0..1, for the game's "Unlocking..." gauge.</summary>
        public Func<float> Attempt;
        /// <summary>Optional: called with the frame's input after the hub's blocking while worn (SpreaderBar's pose mode).</summary>
        public Action<InputData> OnInput;
        /// <summary>Optional short text for the list ("+/- width").</summary>
        public string Controls;

        // --- set by the hub ------------------------------------------------------------------------------------------------

        /// <summary>The selected one of the worn restraints: F and + / - act on it.</summary>
        public bool Selected { get; internal set; }
        /// <summary>F is held for this restraint (it is selected, no menu or ring is open, she is not in an interaction area).</summary>
        public bool StruggleHeld { get; internal set; }

        internal int PendingStep;
        internal int Shortcuts;      // bit k: number key k+1 holds the entry
        internal int RingIndex = -1; // index in the current ring array

        /// <summary>A + (1) or - (-1) press for this restraint since the last call, else 0. Only the selected one gets them.</summary>
        public int TakeStep()
        {
            int s = PendingStep;
            PendingStep = 0;
            return s;
        }

        internal string Name => Label?.Invoke() ?? Id;
        internal bool Worn => IsWorn != null && IsWorn();
    }

    /// <summary>
    /// The shared home of the restraint mods: their ring entries (on pages after the game's), the number shortcuts, one list
    /// of what she wears with a selected item, the keys F and + / - for the selected item, the input blocking, the unlock
    /// gauge and the conflicts between restraints. A restraint mod depends on it (<c>[BepInDependency(Hub.Guid)]</c>) and
    /// calls <see cref="Register"/> from its <c>Load</c>.
    /// </summary>
    public static class Hub
    {
        public const string Guid = Plugin.PluginGuid;

        internal static readonly List<Restraint> All = new List<Restraint>();

        public static void Register(Restraint r)
        {
            if (r == null || string.IsNullOrEmpty(r.Id)) throw new ArgumentException("restraint needs an Id");
            if (r.RingValue < RingPages.ModValueStart || r.RingValue >= RingPages.PaddingValue) throw new ArgumentOutOfRangeException(nameof(r.RingValue));
            if (r.IsWorn == null || r.OnChosen == null) throw new ArgumentException("restraint " + r.Id + " needs IsWorn and OnChosen");
            foreach (var other in All)
                if (other.Id == r.Id || other.RingValue == r.RingValue) throw new ArgumentException("restraint " + r.Id + " registered twice (id or ring value)");
            All.Add(r);
            All.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // stable ring order whatever the load order
            Plugin.Logger.LogInfo("[SFM-ACCEPT] register: " + r.Id + " (ring value " + r.RingValue + ", holds " + r.Holds + ")");
        }

        /// <summary>Show a line in the game's message bar.</summary>
        public static void Message(string text) => SfmKit.Game.GameUi.Message(text);
    }
}
