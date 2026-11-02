using System;

namespace RestraintHub.Logic
{
    /// <summary>
    /// One key for two things: a short tap (released within <see cref="TapSeconds"/>) is a tap; held longer it counts as held
    /// from then on (the struggle). Pure: the key state and time come from the caller. (From NippleTether 0.1.0, where a tap of F
    /// hands the point over and holding F struggles.)
    /// </summary>
    public sealed class TapOrHold
    {
        public float TapSeconds = 0.3f;
        private float _down = -1f;

        /// <summary>The key has been held longer than a tap.</summary>
        public bool Holding { get; private set; }

        /// <summary>How long the key has been held down (0 when up).</summary>
        public float HeldFor => _down < 0f ? 0f : _down;

        /// <summary>True on the update the key was released after a tap.</summary>
        public bool Update(bool pressed, float dt)
        {
            if (pressed)
            {
                _down = _down < 0f ? 0f : _down + Math.Max(dt, 0f);
                Holding = _down >= TapSeconds;
                return false;
            }
            bool tap = _down >= 0f && _down < TapSeconds;
            _down = -1f;
            Holding = false;
            return tap;
        }

        /// <summary>Forget a press in progress (the key went to something else: a menu opened, the selection changed).</summary>
        public void Cancel()
        {
            _down = -1f;
            Holding = false;
        }
    }
}
