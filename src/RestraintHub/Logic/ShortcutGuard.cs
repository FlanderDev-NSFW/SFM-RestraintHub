using System;
using System.Collections.Generic;

namespace RestraintHub.Logic
{
    /// <summary>
    /// When the hub may put a restraint back on a number key after the ring was rebuilt, without fighting the player or another mod.
    /// Background: the game keeps a key's saved index on a game entry (mod indices broke loading), so after every rebuild of the
    /// ring the key points at that game entry again and the hub restores the restraint. A mod that rebuilds the ring or resets the
    /// keys over and over made the hub restore over and over - and a reassignment by the player made in between was undone
    /// [reported 2026-11-02 with a leash collar mod]. Pure: time comes in from the caller.
    /// </summary>
    public sealed class ShortcutGuard
    {
        public enum Decision
        {
            /// <summary>The key still holds the restraint: nothing to do.</summary>
            Keep,
            /// <summary>Put the restraint back on the key.</summary>
            Restore,
            /// <summary>The player gave the key something else (its saved index changed): it is theirs now.</summary>
            PlayerChanged,
            /// <summary>Something keeps taking the key away: stop restoring it.</summary>
            Yield,
        }

        /// <summary>At most this many restores of one key within <see cref="WindowSeconds"/>; the next one yields.</summary>
        public int MaxRestores = 3;
        public float WindowSeconds = 10f;

        private readonly List<float>[] _restores = new List<float>[8];
        private readonly List<float> _rebuilds = new List<float>();

        public ShortcutGuard()
        {
            for (int k = 0; k < 8; k++) _restores[k] = new List<float>();
        }

        /// <summary>A load or a new game: start over.</summary>
        public void Reset()
        {
            foreach (var l in _restores) l.Clear();
            _rebuilds.Clear();
        }

        /// <summary>
        /// Key <paramref name="key"/> (0-7) belongs to a restraint by its saved mask. <paramref name="holdsIt"/>: the key's model
        /// is that restraint's entry. <paramref name="afterLoad"/>: right after a load (the save's index is the hub's own).
        /// <paramref name="savedIndex"/>: the game's saved index for the key now; <paramref name="hubIndex"/>: the index the hub
        /// kept there (the game entry under the restraint).
        /// </summary>
        public Decision Decide(int key, bool holdsIt, bool afterLoad, int savedIndex, int hubIndex, float now)
        {
            if (holdsIt) return Decision.Keep;
            if (!afterLoad && savedIndex != hubIndex) return Decision.PlayerChanged;
            var times = _restores[key];
            times.RemoveAll(t => now - t > WindowSeconds);
            if (!afterLoad && times.Count >= MaxRestores) return Decision.Yield;
            times.Add(now);
            return Decision.Restore;
        }

        /// <summary>The ring was rebuilt (by the hub, after someone replaced it). Returns how many rebuilds there were within
        /// <see cref="WindowSeconds"/> - more than a few means another mod keeps replacing it.</summary>
        public int Rebuilt(float now)
        {
            _rebuilds.RemoveAll(t => now - t > WindowSeconds);
            _rebuilds.Add(now);
            return _rebuilds.Count;
        }
    }
}
