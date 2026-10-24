using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RestraintHub
{
    /// <summary>
    /// The keys printed "+" and "-" on the player's keyboard (and the numpad keys), with hold-to-repeat. Read through the Input
    /// System like the game's controls, so they work unfocused and through the bridge (<c>key Equals --via input</c>).
    /// On a US layout "+" is Shift + "=", so "=" counts as "+" too (on a German layout "=" is not found and "+" has its own key)
    /// [observed 2026-10-12, SpreaderBar width keys; shared 2026-10-24 for WallRestraint; in RestraintHub since 0.1.0: only the selected restraint gets the steps].
    /// </summary>
    internal sealed class PlusMinusKeys
    {
        private float _repeatAt;
        public float FirstDelay = 0.4f, RepeatDelay = 0.08f;

        /// <summary>+1 / -1 when a step is due (on press, then repeating while held), else 0.</summary>
        public int Poll()
        {
            var kb = Keyboard.current;
            if (kb == null) return 0;
            int dir = Held(kb, "+", kb.numpadPlusKey) || Held(kb, "=", null) ? 1 : Held(kb, "-", kb.numpadMinusKey) ? -1 : 0;
            if (dir == 0) { _repeatAt = 0f; return 0; }
            bool first = _repeatAt == 0f;
            if (!first && Time.unscaledTime < _repeatAt) return 0;
            _repeatAt = Time.unscaledTime + (first ? FirstDelay : RepeatDelay);
            return dir;
        }

        private static bool Held(Keyboard kb, string character, KeyControl numpad)
        {
            if (numpad != null && numpad.isPressed) return true;
            var key = kb.FindKeyOnCurrentKeyboardLayout(character);
            return key != null && key.isPressed;
        }
    }
}
