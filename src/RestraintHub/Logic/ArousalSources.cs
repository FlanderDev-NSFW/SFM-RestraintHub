using System;
using System.Collections.Generic;

namespace RestraintHub.Logic
{
    /// <summary>One mission's continuous rise (<c>RtHub_Stimulate</c>).</summary>
    public sealed class MissionStimulation
    {
        public string Program;
        public float PerSecond;
        /// <summary>Seconds left; 0 = until stopped or the mission ends.</summary>
        public float Remaining;
        public bool Timed;
    }

    /// <summary>
    /// What raises the game's ecstasy gauge through the hub, besides worn items: one-off rises (<c>Hub.Arouse</c>,
    /// <c>RtHub_Arouse</c>) collected until the next frame, and missions' continuous rises. Pure: the hub applies the result
    /// (Arousal.cs) the way the game's toys do - add, and reset the drain timer.
    /// </summary>
    public sealed class ArousalSources
    {
        private readonly Dictionary<string, MissionStimulation> _missions = new Dictionary<string, MissionStimulation>(StringComparer.Ordinal);
        private float _pulses;

        public IEnumerable<MissionStimulation> Missions => _missions.Values;

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

        /// <summary>Set (or with <paramref name="perSecond"/> &lt;= 0 stop) a mission's continuous rise; <paramref name="seconds"/>
        /// &gt; 0 ends it after that long, 0 keeps it until stopped or the mission ends.</summary>
        public void SetMission(string program, float perSecond, float seconds)
        {
            if (program == null) return;
            if (!(perSecond > 0f) || float.IsInfinity(perSecond)) { _missions.Remove(program); return; }
            _missions[program] = new MissionStimulation { Program = program, PerSecond = perSecond, Remaining = seconds > 0f ? seconds : 0f, Timed = seconds > 0f };
        }

        public float MissionRate(string program) => program != null && _missions.TryGetValue(program, out var m) ? m.PerSecond : 0f;

        /// <summary>The missions' rises together, per second.</summary>
        public float MissionsRate()
        {
            float sum = 0f;
            foreach (var m in _missions.Values) sum += m.PerSecond;
            return sum;
        }

        /// <summary>Count down the timed ones and drop the expired and those whose mission no longer runs. Returns the dropped
        /// (program, why: "timer" / "missionEnd").</summary>
        public List<(string Program, string Why)> Tick(float dt, Func<string, bool> missionRuns)
        {
            var dropped = new List<(string, string)>();
            foreach (var m in new List<MissionStimulation>(_missions.Values))
            {
                if (missionRuns != null && !missionRuns(m.Program)) { _missions.Remove(m.Program); dropped.Add((m.Program, "missionEnd")); continue; }
                if (!m.Timed) continue;
                m.Remaining -= dt;
                if (m.Remaining > 0f) continue;
                _missions.Remove(m.Program);
                dropped.Add((m.Program, "timer"));
            }
            return dropped;
        }

        public void Clear()
        {
            _missions.Clear();
            _pulses = 0f;
        }

        /// <summary>The gauge after this frame's rise: <paramref name="current"/> + (rate x dt + pulses) x scale, within 0..1.</summary>
        public static float Next(float current, float ratePerSecond, float dt, float pulses, float scale)
        {
            if (float.IsNaN(current)) current = 0f;
            float add = (Math.Max(0f, ratePerSecond) * Math.Max(0f, dt) + Math.Max(0f, pulses)) * Math.Max(0f, scale);
            return Math.Min(1f, Math.Max(0f, current + add));
        }
    }
}
