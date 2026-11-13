using System;

namespace RestraintHub.Logic
{
    /// <summary>
    /// One-off rises of the game's ecstasy gauge (<c>Hub.Arouse</c>), collected until the next frame, and the per-frame step.
    /// Pure: the hub applies the result (Arousal.cs) the way the game's toys do - add, and reset the drain timer.
    /// </summary>
    public sealed class ArousalSources
    {
        private float _pulses;

        /// <summary>A one-off rise, applied with the next frame's (several in one frame add up).</summary>
        public void Pulse(float amount)
        {
            if (amount > 0f && !float.IsNaN(amount) && !float.IsInfinity(amount)) _pulses += amount;
        }

        /// <summary>The one-off rises since the last call.</summary>
        public float TakePulses()
        {
            float p = _pulses;
            _pulses = 0f;
            return p;
        }

        public void Clear() => _pulses = 0f;

        /// <summary>The gauge after this frame's rise: <paramref name="current"/> + (rate x dt + pulses) x scale, within 0..1.</summary>
        public static float Next(float current, float ratePerSecond, float dt, float pulses, float scale)
        {
            if (float.IsNaN(current)) current = 0f;
            float add = (Math.Max(0f, ratePerSecond) * Math.Max(0f, dt) + Math.Max(0f, pulses)) * Math.Max(0f, scale);
            return Math.Min(1f, Math.Max(0f, current + add));
        }
    }
}
