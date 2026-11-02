using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Unity.IL2CPP;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using RestraintHub.Logic;
using UnityEngine;

namespace RestraintHub
{
    /// <summary>
    /// The restraints for Custom Missions v2 scripts: functions <c>RtHub_*</c> (put on, take off, lock, read and change settings)
    /// and events <c>RtHub_Applied</c> / <c>RtHub_Removed</c> / <c>RtHub_Escaped</c>. Only when Custom Missions v2 is installed
    /// (soft dependency). The functions are documented for mission authors in <c>docs/Missions.md</c>.
    /// Bridge diagnostics: <c>game get RestraintHub.Missions::Status</c>.
    /// </summary>
    public static class Missions
    {
        internal const string CmGuid = "SFM_custom_mission_v2";
        /// <summary>The script API's version (<c>RtHub_ApiVersion()</c>); raised when functions are added or change.</summary>
        internal const int ApiVersion = 1;
        /// <summary>The Custom Missions v2 version the functions were made and tested with.</summary>
        internal const string TestedVersion = "2.4";
        internal const string SaveKey = "RestraintHub.MissionLocks";
        internal const string HandcuffsName = "Handcuffs";

        internal static Cm2 Cm;
        internal static readonly MissionLocks Locks = new MissionLocks();
        /// <summary>A warning for the player, shown once in game unless General.WarnMissions is off (each new text once); null if
        /// none. Public for the bridge (diagnostics).</summary>
        public static string Warning;

        /// <summary>"connected (2.4.0); locks: ...; missions: ..." for diagnostics.</summary>
        public static string Status = "not connected";

        private static readonly Dictionary<string, string> _lastError = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _applyBy = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _removeBy = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _owners = new Dictionary<string, string>(StringComparer.Ordinal); // last frame's
        private static readonly Dictionary<string, float> _struggledAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly HashSet<string> _worn = new HashSet<string>(StringComparer.Ordinal);
        private static bool _loading = true; // until the first frame in game after a load / new game
        private static float _loadedAt = -100f;
        private static string _warned;
        private static float _lockedSaidAt = -10f;

        /// <summary>Just after a load: worn changes are the save's, and missions get time to start before their locks are checked.</summary>
        private const float LoadSeconds = 3f;

        // --- start ---------------------------------------------------------------------------------------------------------------

        /// <summary>From the plugin's Load: connect to Custom Missions v2 if it is installed and register the functions.</summary>
        internal static void Init()
        {
            if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(CmGuid, out var info))
            {
                Status = "Custom Missions v2 not installed";
                return;
            }
            string version = info.Metadata.Version.ToString();
            if (info.Instance == null)
            {
                Status = "Custom Missions v2 " + version + " did not load";
                Plugin.Logger.LogWarning(Status + "; no mission functions.");
                return;
            }
            Cm = Cm2.Bind(info.Instance.GetType().Assembly, out string problem);
            if (Cm == null)
            {
                Status = "not connected to Custom Missions v2 " + version + ": " + problem;
                Warning = "RestraintHub: Custom Missions " + version + " not supported.";
                Plugin.Logger.LogWarning(Status);
                return;
            }
            try { RegisterFunctions(); }
            catch (Exception e)
            {
                Cm = null;
                Status = "registering functions with Custom Missions v2 " + version + " failed: " + e.Message;
                Warning = "RestraintHub: Custom Missions " + version + " not supported.";
                Plugin.Logger.LogWarning(Status + Environment.NewLine + e);
                return;
            }
            var v = info.Metadata.Version;
            if (v.Major + "." + v.Minor != TestedVersion)
            {
                Warning = "RestraintHub: Custom Missions " + version + " untested (made for " + TestedVersion + ").";
                Plugin.Logger.LogWarning(Warning);
            }
            Status = "connected (" + version + ")";
            Plugin.Logger.LogInfo("[SFM-ACCEPT] missions: Custom Missions v2 " + version + ", API " + ApiVersion);
        }

        // --- the functions --------------------------------------------------------------------------------------------------------

        private static void RegisterFunctions()
        {
            Reg("RtHub_ApiVersion", (a, t) => Cm.Of(ApiVersion));
            Reg("RtHub_List", (a, t) =>
            {
                var ids = new List<object>();
                foreach (var r in Hub.All) ids.Add(Cm.Of(r.Id));
                return Cm.List(ids);
            });
            Reg("RtHub_Worn", (a, t) =>
            {
                var ids = new List<object>();
                foreach (var r in Hub.All) if (r.Worn) ids.Add(Cm.Of(r.Id));
                if (InGame() && GameStateFacade.IsAttachSomeHandcuffs()) ids.Add(Cm.Of(HandcuffsName));
                return Cm.List(ids);
            });
            Reg("RtHub_IsOn", (a, t) =>
            {
                string id = Cm.Text(Cm.Arg(a, "id", 0));
                if (id == HandcuffsName) return Cm.Of(InGame() && GameStateFacade.IsAttachSomeHandcuffs());
                var r = HubController.Find(id);
                return Cm.Of(r != null && r.Worn);
            });
            Reg("RtHub_CanApply", (a, t) => Answer(t, "RtHub_CanApply", Find(a, out var r) ?? WhyNot(r, Program(t), true)));
            Reg("RtHub_Apply", (a, t) => Answer(t, "RtHub_Apply", Apply(a, t)));
            Reg("RtHub_Remove", (a, t) => Answer(t, "RtHub_Remove", Remove(a, t)));
            Reg("RtHub_Owner", (a, t) => Find(a, out var r) != null ? Cm.Null : Cm.Of(Locks.Get(r.Id)?.Owner));
            Reg("RtHub_Remaining", (a, t) => Cm.Of(Find(a, out var r) != null ? 0.0 : Math.Round(Locks.Get(r.Id)?.Remaining ?? 0f, 2)));
            Reg("RtHub_Settings", (a, t) =>
            {
                var names = new List<object>();
                if (Find(a, out var r) == null && r.Settings != null)
                    foreach (var s in r.Settings) names.Add(Cm.Of(s.Name));
                return Cm.List(names);
            });
            Reg("RtHub_Get", (a, t) =>
            {
                RestraintSetting s = null;
                string error = Find(a, out var r) ?? Setting(r, a, out s) ?? (r.Worn ? null : r.Name + " is not on");
                if (error != null) { Refuse(t, "RtHub_Get", error); return Cm.Null; }
                SetError(t, "");
                return Cm.Of(Math.Round(s.Get(), 3));
            });
            Reg("RtHub_Set", (a, t) => Answer(t, "RtHub_Set", Set(a, t)));
            Reg("RtHub_LastError", (a, t) => Cm.Of(_lastError.TryGetValue(Program(t) ?? "", out var e) ? e : ""));
        }

        private static void Reg(string name, Func<object, object, object> fn)
        {
            Cm.Register(name, (a, t) =>
            {
                try { return fn(a, t); }
                catch (Exception e)
                {
                    HubController.Fail(name, e);
                    SetError(t, "internal error: " + e.Message);
                    return Cm.Null;
                }
            });
        }

        /// <summary>true when <paramref name="error"/> is null, else false with the error kept for <c>RtHub_LastError</c>.</summary>
        private static object Answer(object thread, string fn, string error)
        {
            if (error == null) { SetError(thread, ""); return Cm.Of(true); }
            Refuse(thread, fn, error);
            return Cm.Of(false);
        }

        private static void Refuse(object thread, string fn, string error)
        {
            SetError(thread, error);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] mission '" + Program(thread) + "': " + fn + " refused: " + error);
        }

        private static void SetError(object thread, string error) => _lastError[Program(thread) ?? ""] = error;

        private static string Program(object thread) => Cm?.ProgramOf(thread) ?? "(unknown mission)";

        private static bool InGame() => PlayerController.Instance != null && GameState.GameStateData != null;

        /// <summary>The restraint named by the first argument (<c>id</c>); null if found, else the error.</summary>
        private static string Find(object args, out Restraint r)
        {
            string id = Cm.Text(Cm.Arg(args, "id", 0));
            r = HubController.Find(id);
            if (r != null) return null;
            return id == null ? "no restraint id given" : id == HandcuffsName ? "the game's handcuffs can't be used this way" : "no restraint '" + id + "' installed";
        }

        private static string Setting(Restraint r, object args, out RestraintSetting setting)
        {
            setting = null;
            string name = Cm.Text(Cm.Arg(args, "name", 1));
            if (name == null) return "no setting name given";
            if (r.Settings != null)
                foreach (var s in r.Settings)
                    if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) { setting = s; return null; }
            return r.Name + " has no setting '" + name + "'";
        }

        /// <summary>Why <paramref name="r"/> can't go on now; null if it can. <paramref name="probe"/>: also ask the mod (CanPutOn).</summary>
        private static string WhyNot(Restraint r, string program, bool probe)
        {
            if (!InGame()) return "not in a stage";
            if (r.Worn) return r.Name + " is already on";
            if (Plugin.BlockEachOther.Value)
            {
                var busy = Rules.Conflict(r.Holds, HubController.WornParts());
                if (busy != null)
                {
                    string other = busy.Value.Label;
                    return "the " + other + (other.EndsWith("s") ? " hold " : " holds ") + Rules.Describe(busy.Value.Shared);
                }
            }
            if (!probe || r.CanPutOn == null) return null;
            try { return r.CanPutOn(); }
            catch (Exception e) { HubController.Fail(r.Id + ".CanPutOn", e); return "internal error"; }
        }

        /// <summary>RtHub_Apply(id[, struggle][, seconds][, onEnd]).</summary>
        private static string Apply(object args, object thread)
        {
            string error = Find(args, out var r);
            if (error != null) return error;
            string program = Program(thread);
            object vStruggle = Cm.Arg(args, "struggle", 1), vSeconds = Cm.Arg(args, "seconds", 2), vEnd = Cm.Arg(args, "onEnd", 3);
            bool? struggle = vStruggle == null ? (bool?)null : Cm.Truth(vStruggle);
            float? seconds = vSeconds == null ? (float?)null : (float)Cm.Number(vSeconds);
            LockEnd? onEnd = null;
            if (vEnd != null)
            {
                if (!MissionLock.TryParseEnd(Cm.Text(vEnd), out var end)) return "onEnd must be \"thread\", \"mission\" or \"keep\", not '" + Cm.Text(vEnd) + "'";
                onEnd = end;
            }
            if (seconds.HasValue && (float.IsNaN(seconds.Value) || seconds.Value < 0f)) return "seconds must be 0 or more";
            if (r.Worn)
            {
                // Already on (put on by the player, or by this mission): take hold of it / change the hold.
                error = Locks.Refuses(r.Id, program);
                if (error != null) return r.Name + " is " + error;
                var held = Locks.Take(r.Id, program, thread, struggle, seconds, onEnd, true);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] mission '" + program + "': holds " + r.Id + " (" + Describe(held) + ")");
                return null;
            }
            error = WhyNot(r, program, false);
            if (error != null) return error;
            _applyBy[r.Id] = "mission";
            try
            {
                if (r.PutOn != null) error = r.PutOn();
                else
                {
                    r.OnChosen();
                    if (!r.Worn) error = r.Name + " could not be put on here";
                }
            }
            catch (Exception e)
            {
                HubController.Fail(r.Id + ".PutOn", e);
                error = "internal error";
            }
            if (error != null)
            {
                _applyBy.Remove(r.Id);
                return error;
            }
            var l = Locks.Take(r.Id, program, thread, struggle, seconds, onEnd, r.Worn);
            HubController.Instance?.List.Select(r.Id);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] mission '" + program + "': put on " + r.Id + " (" + Describe(l) + ")");
            return null;
        }

        /// <summary>RtHub_Remove(id).</summary>
        private static string Remove(object args, object thread)
        {
            string error = Find(args, out var r);
            if (error != null) return error;
            if (!r.Worn) return r.Name + " is not on";
            string program = Program(thread);
            error = Locks.Refuses(r.Id, program);
            if (error != null) return r.Name + " is " + error;
            if (r.TakeOff == null) return r.Name + " can't be taken off by missions";
            TakeOff(r, "mission");
            Locks.Drop(r.Id);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] mission '" + program + "': took off " + r.Id);
            return null;
        }

        /// <summary>RtHub_Set(id, name, value).</summary>
        private static string Set(object args, object thread)
        {
            RestraintSetting s = null;
            string error = Find(args, out var r) ?? Setting(r, args, out s);
            if (error != null) return error;
            object v = Cm.Arg(args, "value", 2);
            if (v == null) return "no value given";
            double value = Cm.Number(v);
            if (double.IsNaN(value) || double.IsInfinity(value)) return "value must be a number";
            if (!r.Worn) return r.Name + " is not on";
            error = Locks.Refuses(r.Id, Program(thread));
            if (error != null) return r.Name + " is " + error;
            error = s.Set((float)value);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] mission '" + Program(thread) + "': " + r.Id + "." + s.Name + " = " + value.ToString("0.###", CultureInfo.InvariantCulture)
                                  + (error == null ? " -> " + s.Get().ToString("0.###", CultureInfo.InvariantCulture) + " " + s.Unit : " refused: " + error));
            return error;
        }

        private static string Describe(MissionLock l) =>
            (l.Struggle ? "struggle allowed" : "no struggling") + (l.Remaining > 0f ? ", " + l.Remaining.ToString("0.#", CultureInfo.InvariantCulture) + " s" : "")
            + ", on end: " + MissionLock.Name(l.OnEnd);

        // --- taking off -------------------------------------------------------------------------------------------------------------

        /// <summary>Take <paramref name="r"/> off now; <paramref name="by"/> goes into the RtHub_Removed event.</summary>
        internal static void TakeOff(Restraint r, string by)
        {
            if (r.TakeOff == null) return;
            _removeBy[r.Id] = by;
            try { r.TakeOff(); }
            catch (Exception e) { HubController.Fail(r.Id + ".TakeOff", e); }
        }

        /// <summary>The escape key: everything off that can be taken off, all missions' holds dropped.</summary>
        internal static void EscapeAll()
        {
            var stuck = new List<string>();
            int n = 0;
            foreach (var r in Hub.All)
            {
                if (!r.Worn) continue;
                if (r.TakeOff == null) { stuck.Add(r.Name); continue; }
                TakeOff(r, "escapeKey");
                n++;
            }
            Locks.Clear();
            Hub.Message(n == 0 && stuck.Count == 0 ? "No restraints to take off."
                        : stuck.Count == 0 ? "All restraints taken off." : "Taken off what could be (not: " + string.Join(", ", stuck) + ").");
            Plugin.Logger.LogInfo("[SFM-ACCEPT] escape: took off " + n + (stuck.Count == 0 ? "" : ", left " + string.Join(", ", stuck)));
        }

        // --- struggling and the list ---------------------------------------------------------------------------------------------

        /// <summary>A mission forbids struggling out of <paramref name="r"/>.</summary>
        internal static bool StruggleLocked(Restraint r)
        {
            var l = Locks.Get(r.Id);
            return l != null && !l.Struggle;
        }

        /// <summary>The player tried to struggle with a locked restraint: say why not (at most every 3 s).</summary>
        internal static void SayLocked(Restraint r)
        {
            if (Time.unscaledTime - _lockedSaidAt < 3f) return;
            _lockedSaidAt = Time.unscaledTime;
            var l = Locks.Get(r.Id);
            Hub.Message("The " + r.Name + " is locked" + (l != null && l.Remaining > 0f ? " for " + Clock(l.Remaining) : "") + " - you can't struggle out.");
        }

        /// <summary>A note for the list on screen: "locked 1:23", "1:23", "locked"; "" without a hold.</summary>
        internal static string Note(string id)
        {
            var l = Locks.Get(id);
            if (l == null) return "";
            string time = l.Remaining > 0f ? Clock(l.Remaining) : "";
            return l.Struggle ? (time.Length == 0 ? "" : "  [" + time + "]") : "  [locked" + (time.Length == 0 ? "" : " " + time) + "]";
        }

        private static string Clock(float seconds)
        {
            int s = (int)Math.Ceiling(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // --- every frame in game ------------------------------------------------------------------------------------------------

        /// <summary>From the hub's Update while in game, after the list was brought up to date.</summary>
        internal static void Tick(bool menus)
        {
            float now = Time.unscaledTime;
            if (_loading) { _loading = false; _loadedAt = now; }
            bool afterLoad = now - _loadedAt < LoadSeconds;
            if (Warning != null && Warning != _warned && !afterLoad)
            {
                _warned = Warning;
                if (Plugin.WarnMissions.Value) Hub.Message(Warning);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] warning" + (Plugin.WarnMissions.Value ? "" : " (hidden)") + ": " + Warning);
            }
            Watch(now, afterLoad);
            var actions = Locks.Tick(Time.deltaTime, !menus && Time.timeScale > 0f,
                id => HubController.Find(id)?.Worn == true,
                l => Cm != null && Cm.Ended(l.Thread),
                program => afterLoad || (Cm != null && Cm.Runs(program)));
            foreach (var (l, action, why) in actions)
            {
                var r = HubController.Find(l.Id);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] mission '" + l.Owner + "': " + l.Id + " " + (action == LockAction.TakeOff ? "taken off" : "let go") + " (" + why + ")");
                if (why == "never went on") _applyBy.Remove(l.Id);
                if (action != LockAction.TakeOff || r == null) continue;
                if (r.TakeOff == null) { Hub.Message("The " + r.Name + " is no longer locked."); continue; }
                TakeOff(r, why == "timer" ? "timer" : why == "thread ended" ? "threadEnd" : "missionEnd");
                if (why == "timer") Hub.Message("The " + r.Name + " comes off.");
            }
            Status = (Cm == null ? "not connected" : "connected") + "; locks: " + (Locks.Count == 0 ? "none" : string.Join(", ", Locks.All.Select(l => l.Id + " by '" + l.Owner + "' (" + Describe(l) + ")")))
                     + "; missions: " + (Cm == null ? "-" : Cm.Programs());
        }

        /// <summary>Events for restraints that went on or came off since the last frame.</summary>
        private static void Watch(float now, bool afterLoad)
        {
            foreach (var r in Hub.All)
            {
                bool worn = r.Worn, was = _worn.Contains(r.Id);
                if (r.StruggleHeld) _struggledAt[r.Id] = now;
                if (worn && !was)
                {
                    _worn.Add(r.Id);
                    string by = _applyBy.TryGetValue(r.Id, out var b) ? b : afterLoad ? "load" : "player";
                    _applyBy.Remove(r.Id);
                    _removeBy.Remove(r.Id);
                    Event("RtHub_Applied", r.Id, by, Locks.Get(r.Id)?.Owner);
                }
                else if (!worn && was)
                {
                    _worn.Remove(r.Id);
                    string by = _removeBy.TryGetValue(r.Id, out var b) ? b
                              : _struggledAt.TryGetValue(r.Id, out float at) && now - at < 2f ? "struggle"
                              : afterLoad ? "load" : "other";
                    _removeBy.Remove(r.Id);
                    _owners.TryGetValue(r.Id, out string owner);
                    Event("RtHub_Removed", r.Id, by, owner);
                    if (by == "struggle") Event("RtHub_Escaped", r.Id, by, owner);
                }
                string o = Locks.Get(r.Id)?.Owner;
                if (o == null) _owners.Remove(r.Id); else _owners[r.Id] = o;
            }
        }

        private static void Event(string name, string id, string by, string owner)
        {
            Plugin.Logger.LogInfo("[SFM-ACCEPT] event: " + name + " " + id + " by " + by + (owner == null ? "" : " (owner '" + owner + "')"));
            if (Cm == null) return;
            try
            {
                Cm.SetEvent(name, Cm.Named(new[]
                {
                    new KeyValuePair<string, object>("id", Cm.Of(id)),
                    new KeyValuePair<string, object>("by", Cm.Of(by)),
                    new KeyValuePair<string, object>("owner", owner == null ? Cm.Null : Cm.Of(owner)),
                }));
            }
            catch (Exception e) { HubController.Fail("event " + name, e); }
        }

        /// <summary>Diagnostics (bridge, --allow-invoke): switch a mission on or off as the phone does.</summary>
        public static string SetMissionActive(string program, bool on) =>
            Cm == null ? "not connected" : Cm.SetActive(program, on) ? program + (on ? " on" : " off") : "no mission '" + program + "'";

        // --- save identity ------------------------------------------------------------------------------------------------------

        /// <summary>A save was loaded (before the restraints read theirs): the missions' holds come from the save.</summary>
        internal static void OnLoad(string saved)
        {
            Locks.Restore(saved);
            _applyBy.Clear();
            _removeBy.Clear();
            _loading = true;
            if (Locks.Count > 0) Plugin.Logger.LogInfo("[SFM-ACCEPT] load: mission locks " + Locks.Serialize().Replace('\n', ';').Replace('\t', ','));
        }

        /// <summary>A new game or the title screen: no holds.</summary>
        internal static void OnNewGame()
        {
            Locks.Clear();
            _applyBy.Clear();
            _removeBy.Clear();
            _loading = true;
        }
    }
}
