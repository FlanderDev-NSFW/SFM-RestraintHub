using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RestraintHub.Logic
{
    /// <summary>When a mission's hold on a restraint ends (<c>RtHub_Apply(..., onEnd=...)</c>).</summary>
    public enum LockEnd
    {
        /// <summary>Taken off when the script thread that put it on ends, or the mission stops (the default).</summary>
        Thread,
        /// <summary>Taken off when the mission stops (is switched off, or is gone after a load).</summary>
        Mission,
        /// <summary>Stays on when the mission stops; the mission just lets go (the player may struggle again).</summary>
        Keep,
    }

    /// <summary>A mission's hold on one worn restraint.</summary>
    public sealed class MissionLock
    {
        public string Id;
        /// <summary>The owning mission (Custom Missions v2 project name): only its scripts may change or remove the restraint.</summary>
        public string Owner;
        /// <summary>The player may struggle out (F).</summary>
        public bool Struggle = true;
        /// <summary>Seconds left until it comes off by itself; 0 = no timer.</summary>
        public float Remaining;
        public LockEnd OnEnd = LockEnd.Thread;
        /// <summary>The script thread that put it on (opaque; null after a load: then only the mission is watched).</summary>
        public object Thread;
        /// <summary>Seen worn since it was taken (a restraint may need some frames to go on).</summary>
        public bool Seen;
        /// <summary>Seconds waited for it to be seen worn.</summary>
        public float Waited;

        public static bool TryParseEnd(string s, out LockEnd end)
        {
            end = LockEnd.Thread;
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "thread": end = LockEnd.Thread; return true;
                case "mission": end = LockEnd.Mission; return true;
                case "keep": end = LockEnd.Keep; return true;
                default: return false;
            }
        }

        public static string Name(LockEnd end) => end == LockEnd.Thread ? "thread" : end == LockEnd.Mission ? "mission" : "keep";
    }

    /// <summary>What <see cref="MissionLocks.Tick"/> asks the hub to do.</summary>
    public enum LockAction
    {
        /// <summary>Take the restraint off (timer ran out, or the owner ended with <see cref="LockEnd.Thread"/> / <see cref="LockEnd.Mission"/>).</summary>
        TakeOff,
        /// <summary>The lock is gone, the restraint stays on (owner ended with <see cref="LockEnd.Keep"/>; or it never went on / came off).</summary>
        Forget,
    }

    /// <summary>
    /// The missions' holds on worn restraints (RestraintHub's Custom Missions v2 functions): who owns which restraint, whether
    /// the player may struggle, timers, and what happens when the owner ends. Pure: what is worn and whether a thread or mission
    /// still runs come in from the caller.
    /// </summary>
    public sealed class MissionLocks
    {
        /// <summary>How long a taken restraint may take to show as worn before the lock is dropped.</summary>
        public const float SeenTimeout = 5f;

        private readonly Dictionary<string, MissionLock> _locks = new Dictionary<string, MissionLock>(StringComparer.Ordinal);

        public IEnumerable<MissionLock> All => _locks.Values;
        public int Count => _locks.Count;

        public MissionLock Get(string id) => id != null && _locks.TryGetValue(id, out var l) ? l : null;

        /// <summary>Why <paramref name="program"/> may not change restraint <paramref name="id"/>; null if it may (unowned or its own).</summary>
        public string Refuses(string id, string program)
        {
            var l = Get(id);
            return l == null || string.Equals(l.Owner, program, StringComparison.Ordinal) ? null : "locked by the mission '" + l.Owner + "'";
        }

        /// <summary>Take (or update) <paramref name="program"/>'s hold on <paramref name="id"/>. Options left null keep the current
        /// value of an existing hold (defaults for a new one: struggle allowed, no timer, <see cref="LockEnd.Thread"/>).
        /// <paramref name="seconds"/> &lt;= 0 removes the timer.</summary>
        public MissionLock Take(string id, string program, object thread, bool? struggle, float? seconds, LockEnd? onEnd, bool alreadyWorn)
        {
            var l = Get(id);
            if (l == null || l.Owner != program)
            {
                l = new MissionLock { Id = id, Owner = program };
                _locks[id] = l;
            }
            l.Thread = thread ?? l.Thread;
            if (struggle.HasValue) l.Struggle = struggle.Value;
            if (seconds.HasValue) l.Remaining = seconds.Value > 0f && !float.IsInfinity(seconds.Value) ? seconds.Value : 0f;
            if (onEnd.HasValue) l.OnEnd = onEnd.Value;
            if (alreadyWorn) l.Seen = true;
            l.Waited = 0f;
            return l;
        }

        public bool Drop(string id) => id != null && _locks.Remove(id);

        public void Clear() => _locks.Clear();

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds. <paramref name="clockRuns"/>: timers count (false while the game is paused or
        /// a menu is open). <paramref name="worn"/>: the restraint is on now. <paramref name="threadEnded"/>: the lock's thread is
        /// known and has ended. <paramref name="missionRuns"/>: the owning mission is loaded and active. Returns what to do; locks
        /// that end are already removed (<see cref="LockAction.TakeOff"/>: after the hub took it off).
        /// </summary>
        public List<(MissionLock Lock, LockAction Action, string Why)> Tick(float dt, bool clockRuns, Func<string, bool> worn,
            Func<MissionLock, bool> threadEnded, Func<string, bool> missionRuns)
        {
            var result = new List<(MissionLock, LockAction, string)>();
            if (!(dt > 0f)) dt = 0f;
            foreach (var l in new List<MissionLock>(_locks.Values))
            {
                if (!worn(l.Id))
                {
                    if (l.Seen) { result.Add((l, LockAction.Forget, "came off")); _locks.Remove(l.Id); continue; }
                    l.Waited += dt;
                    if (l.Waited > SeenTimeout) { result.Add((l, LockAction.Forget, "never went on")); _locks.Remove(l.Id); }
                    continue;
                }
                l.Seen = true;
                bool missionGone = !missionRuns(l.Owner);
                bool threadGone = l.OnEnd == LockEnd.Thread && l.Thread != null && threadEnded(l);
                if (missionGone || threadGone)
                {
                    string why = missionGone ? "mission ended" : "thread ended";
                    result.Add((l, l.OnEnd == LockEnd.Keep ? LockAction.Forget : LockAction.TakeOff, why));
                    _locks.Remove(l.Id);
                    continue;
                }
                if (l.Remaining > 0f && clockRuns)
                {
                    l.Remaining = Math.Max(0f, l.Remaining - dt);
                    if (l.Remaining <= 0f) { result.Add((l, LockAction.TakeOff, "timer")); _locks.Remove(l.Id); }
                }
            }
            return result;
        }

        // --- in the save file: one line per lock, tab separated (id, owner, struggle, remaining seconds, onEnd) ----------------

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var l in _locks.Values)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Clean(l.Id)).Append('\t').Append(Clean(l.Owner)).Append('\t').Append(l.Struggle ? '1' : '0').Append('\t')
                  .Append(l.Remaining.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t').Append(MissionLock.Name(l.OnEnd));
            }
            return sb.ToString();
        }

        /// <summary>Replace the locks with the ones in <paramref name="text"/> (from <see cref="Serialize"/>; bad lines are
        /// skipped). They have no thread and are not yet seen worn.</summary>
        public void Restore(string text)
        {
            _locks.Clear();
            if (string.IsNullOrEmpty(text)) return;
            foreach (string line in text.Split('\n'))
            {
                var f = line.Split('\t');
                if (f.Length < 5 || f[0].Length == 0 || f[1].Length == 0) continue;
                if (!float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float left) || left < 0f || float.IsNaN(left)) continue;
                if (!MissionLock.TryParseEnd(f[4], out var end)) continue;
                _locks[f[0]] = new MissionLock { Id = f[0], Owner = f[1], Struggle = f[2] != "0", Remaining = left, OnEnd = end };
            }
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');
    }
}
