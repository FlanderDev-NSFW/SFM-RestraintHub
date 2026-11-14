using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RestraintHub
{
    /// <summary>
    /// The keys of <c>Keys.Increase</c> / <c>Keys.Decrease</c> (default: the keys that type "+" / "-" on the player's layout, "="
    /// and the numpad's; <see cref="KeyBindings"/>), with hold-to-repeat. Read through the Input System like the game's controls,
    /// so they work unfocused and through the bridge (<c>key Equals --via input</c>). On a US layout "+" is Shift + "=", so "="
    /// is in the default [observed 2026-10-12, SpreaderBar width keys; in RestraintHub since 0.1.0; configurable since 2.1.0].
    /// </summary>
    internal sealed class PlusMinusKeys
    {
        private float _repeatAt;
        public float FirstDelay = 0.4f, RepeatDelay = 0.08f;

        /// <summary>The key held at the last <see cref="Poll"/>: 1 (+), -1 (-) or 0.</summary>
        public int Direction { get; private set; }

        /// <summary>+1 / -1 when a step is due (on press, then repeating while held), else 0.</summary>
        public int Poll()
        {
            var kb = Keyboard.current;
            int dir = kb == null ? 0 : KeyBindings.Held(Plugin.IncreaseKey.Value) ? 1 : KeyBindings.Held(Plugin.DecreaseKey.Value) ? -1 : 0;
            Direction = dir;
            if (dir == 0) { _repeatAt = 0f; return 0; }
            bool first = _repeatAt == 0f;
            if (!first && Time.unscaledTime < _repeatAt) return 0;
            _repeatAt = Time.unscaledTime + (first ? FirstDelay : RepeatDelay);
            return dir;
        }
    }
}
