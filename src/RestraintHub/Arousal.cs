using System;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using RestraintHub.Logic;
using UnityEngine;

namespace RestraintHub
{
    /// <summary>
    /// Raises the game's ecstasy gauge (the pink bar on the left) for worn items (<see cref="Wearable.Stimulation"/>) and one-off
    /// rises (<see cref="Hub.Arouse"/>) - the way the game's toys do: add to <c>GameStateData.PlayerEcstasy</c> and set
    /// <c>PlayerEcstasyController.lastAddEcstasyTime</c> to now, so the game's drain (-0.05/s x min(1, (s/10)^2), s = seconds since
    /// that time) stays off while something raises it. Nothing is added during the climax, its aftermath, or the game's handcuff
    /// cool-down; at 1 the game runs its own climax and reset [observed 2026-11-13, docs/knowledge/api-notes/player-stats.md].
    /// Missions use Custom Missions v2's own functions for the gauge.
    /// </summary>
    internal static class Arousal
    {
        internal static readonly ArousalSources Sources = new ArousalSources();

        /// <summary>"0.420, worn 0.02/s, scale 1" for diagnostics.</summary>
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

            float worn = 0f;
            foreach (var w in Hub.All)
            {
                if (w.Stimulation == null || !w.Worn) continue;
                float r;
                try { r = w.Stimulation(); }
                catch (Exception e) { HubController.Fail(w.Id + ".Stimulation", e); continue; }
                if (r > 0f && !float.IsInfinity(r)) worn += r;
            }
            float pulses = Sources.TakePulses();
            float scale = Plugin.ArousalScale.Value;
            if ((worn > 0f || pulses > 0f) && scale > 0f && !Blocked(out _))
            {
                data.PlayerEcstasy = ArousalSources.Next(data.PlayerEcstasy, worn, Time.deltaTime, pulses, scale);
                pc.Pca.PlayerEcstasyController.lastAddEcstasyTime = Time.time; // as the game's toys: no drain while it rises
            }
            Status = data.PlayerEcstasy.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ", worn " + F(worn)
                     + "/s, scale " + F(scale) + (Blocked(out string b) ? ", blocked: " + b : "");
        }

        /// <summary>A one-off rise (Hub.Arouse): null when it will be added with the next frame, else why not.</summary>
        internal static string Arouse(float amount)
        {
            if (float.IsNaN(amount) || amount <= 0f) return "the amount must be more than 0";
            if (Blocked(out string why)) return why;
            if (Plugin.ArousalScale.Value <= 0f) return "switched off in the hub's config (Arousal.Scale)";
            Sources.Pulse(Math.Min(1f, amount));
            return null;
        }

        internal static void OnNewGame() => Sources.Clear();

        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
