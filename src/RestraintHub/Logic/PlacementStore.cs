using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RestraintHub.Logic
{
    /// <summary>One placed object: which placeable, on which map, where, and the mod's own data.</summary>
    public sealed class PlacedRecord
    {
        public string Kind;   // the placeable's id
        public string Stage;  // StageType name
        public readonly float[] Position = new float[3];
        public readonly float[] Rotation = { 0f, 0f, 0f, 1f }; // quaternion x, y, z, w
        public string Data = "";
    }

    /// <summary>
    /// The placed objects of all placeables on all maps, and their line in the save file. Pure: the hub shows / hides them
    /// (Placements.cs) and decides when.
    /// </summary>
    public sealed class PlacementStore
    {
        private readonly List<PlacedRecord> _all = new List<PlacedRecord>();

        public IReadOnlyList<PlacedRecord> All => _all;

        public PlacedRecord Add(string kind, string stage, float[] position, float[] rotation, string data)
        {
            var r = new PlacedRecord { Kind = kind, Stage = stage, Data = data ?? "" };
            Array.Copy(position, r.Position, 3);
            Array.Copy(rotation, r.Rotation, 4);
            _all.Add(r);
            return r;
        }

        public bool Remove(PlacedRecord r) => _all.Remove(r);

        public void Clear() => _all.Clear();

        public List<PlacedRecord> On(string stage) => _all.FindAll(r => r.Stage == stage);

        public int Count(string kind, string stage) => _all.FindAll(r => r.Kind == kind && r.Stage == stage).Count;

        // --- the save: one line per object, tab separated: kind, stage, x y z, qx qy qz qw, data (URL-escaped) ---------------

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var r in _all)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Clean(r.Kind)).Append('\t').Append(Clean(r.Stage)).Append('\t').Append(Floats(r.Position)).Append('\t')
                  .Append(Floats(r.Rotation)).Append('\t').Append(Uri.EscapeDataString(r.Data ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>Replace the objects with the ones in <paramref name="text"/> (bad lines skipped). Returns how many were read.</summary>
        public int Restore(string text)
        {
            _all.Clear();
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (string line in text.Split('\n'))
            {
                var f = line.Split('\t');
                if (f.Length < 5 || f[0].Length == 0 || f[1].Length == 0) continue;
                if (!TryFloats(f[2], 3, out var pos) || !TryFloats(f[3], 4, out var rot)) continue;
                string data;
                try { data = Uri.UnescapeDataString(f[4]); }
                catch (UriFormatException) { continue; }
                Add(f[0], f[1], pos, rot, data);
            }
            return _all.Count;
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');

        private static string Floats(float[] v)
        {
            var parts = new string[v.Length];
            for (int i = 0; i < v.Length; i++) parts[i] = v[i].ToString("R", CultureInfo.InvariantCulture);
            return string.Join(" ", parts);
        }

        private static bool TryFloats(string s, int n, out float[] v)
        {
            v = new float[n];
            var parts = s.Split(' ');
            if (parts.Length != n) return false;
            for (int i = 0; i < n; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || float.IsNaN(v[i]) || float.IsInfinity(v[i]))
                    return false;
            return true;
        }
    }
}
