using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using RestraintHub.Logic;
using UnityEngine;

namespace RestraintHub
{
    /// <summary>
    /// Raises the game's ecstasy gauge (the pink bar on the left) for worn items (<see cref="Wearable.Stimulation"/>), one-off
    /// rises (<see cref="Hub.Arouse"/>) and missions (<c>RtHub_Stimulate</c> / <c>RtHub_Arouse</c>) - the way the game's toys
    /// do: add to <c>GameStateData.PlayerEcstasy</c> and set <c>PlayerEcstasyController.lastAddEcstasyTime</c> to now, so the
    /// game's drain (-0.05/s x min(1, (s/10)^2), s = seconds since that time) stays off while something raises it. Nothing is
    /// added during the climax, its aftermath, or the game's handcuff cool-down; at 1 the game runs its own climax and reset
    /// [observed 2026-11-13, docs/knowledge/api-notes/player-stats.md].
    /// </summary>
    internal static class Arousal
    {
        internal static readonly ArousalSources Sources = new ArousalSources();
        private static bool _wasClimax;
        private static float _lastRiseAt = -100f;
        private static string _lastBy = "game", _lastId, _lastOwner;

        /// <summary>"0.42, worn 0.020/s, missions 0/s" for diagnostics.</summary>
        internal static string Status = "-";

        /// <summary>The gauge 0..1; NaN outside a stage.</summary>
        internal static float Value => GameState.GameStateData == null ? float.NaN : GameState.GameStateData.PlayerEcstasy;

        /// <summary>The climax runs (or its short aftermath), or the game's handcuff cool-down holds the gauge: nothing rises.</summary>
        internal static bool Blocked(out string why)
        {
            why = null;
            var pca = PlayerController.Instance?.Pca;
            var st = pca?.PlayerState;
            if (st == null || GameState.GameStateData == null) { why = "not in a stage"; return true; }
            if (st.IsEcstasyMotion || st.EcstasyRemainAftermathTime > 0f) { why = "she is climaxing"; return true; }
            try
            {
                if (pca.PlayerEcstasyController != null && pca.PlayerEcstasyController.IsStopEcstasyByHandcuffEcstasyCoolTime())
                { why = "the handcuffs' cool-down"; return true; }
            }
            catch (Exception) { /* the game's check failed: no reason to block */ }
            return false;
        }

        /// <summary>Once per frame in game, after the worn list was updated.</summary>
        internal static void Tick()
        {
            var data = GameState.GameStateData;
            var pc = PlayerController.Instance;
            if (data == null || pc == null || pc.Pca == null) return;
            var st = pc.Pca.PlayerState;
            float dt = Time.deltaTime;

            foreach (var (program, why) in Sources.Tick(dt, p => Missions.MissionRuns(p)))
                Plugin.Logger.LogInfo("[SFM-ACCEPT] arousal: mission '" + program + "' stimulation ended (" + why + ")");

            // The worn items' rises, per second.
            float worn = 0f;
            string firstId = null;
            foreach (var w in Hub.All)
            {
                if (w.Stimulation == null || !w.Worn) continue;
                float r;
                try { r = w.Stimulation(); }
                catch (Exception e) { HubController.Fail(w.Id + ".Stimulation", e); continue; }
                if (!(r > 0f) || float.IsInfinity(r)) continue;
                worn += r;
                firstId ??= w.Id;
            }
            float missions = Sources.MissionsRate();
            float pulses = Sources.TakePulses();
            float scale = Plugin.ArousalScale.Value;
            bool climax = st != null && st.IsEcstasyMotion;

            if ((worn > 0f || missions > 0f || pulses > 0f) && scale > 0f && !Blocked(out _))
            {
                float before = data.PlayerEcstasy;
                data.PlayerEcstasy = ArousalSources.Next(before, worn + missions, dt, pulses, scale);
                pc.Pca.PlayerEcstasyController.lastAddEcstasyTime = Time.time; // as the game's toys: no drain while it rises
                _lastRiseAt = Time.time;
                _lastBy = worn > 0f ? "restraint" : "mission";
                _lastId = firstId;
                _lastOwner = FirstMission();
            }

            if (climax && !_wasClimax)
            {
                // Ours when the hub raised the gauge within the last 2 s (else the game's own toys or actions).
                bool ours = Time.time - _lastRiseAt < 2f;
                Missions.Climax(ours ? _lastId : null, ours ? _lastBy : "game", ours ? _lastOwner : null);
            }
            _wasClimax = climax;
            Status = data.PlayerEcstasy.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ", worn " + F(worn) + "/s, missions "
                     + F(missions) + "/s, scale " + F(scale) + (Blocked(out string b) ? ", blocked: " + b : "");
        }

        private static string FirstMission()
        {
            foreach (var m in Sources.Missions) return m.Program;
            return null;
        }

        /// <summary>A one-off rise (Hub.Arouse, RtHub_Arouse): null when it will be added with the next frame, else why not.</summary>
        internal static string Arouse(float amount)
        {
            if (float.IsNaN(amount) || amount <= 0f) return "the amount must be more than 0";
            if (Blocked(out string why)) return why;
            if (Plugin.ArousalScale.Value <= 0f) return "switched off in the hub's config (Arousal.Scale)";
            Sources.Pulse(Math.Min(1f, amount));
            return null;
        }

        internal static void OnNewGame()
        {
            Sources.Clear();
            _wasClimax = false;
        }

        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
