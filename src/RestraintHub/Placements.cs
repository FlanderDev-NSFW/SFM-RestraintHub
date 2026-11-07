using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.InteractArea;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using Il2CppInterop.Runtime;
using RestraintHub.Logic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RestraintHub
{
    /// <summary>
    /// The placed objects of all placeables: kept in a <see cref="PlacementStore"/> (saved in the slot under
    /// <see cref="SaveKey"/>), shown while their map is loaded - a root object per placed one, the mod's model under it, and one of
    /// the game's interaction areas (a copy of the dropped coat's, type DroppedClothes: F calls our callback and nothing else
    /// [observed 2026-11-07, docs/knowledge/api-notes/ui-and-input.md]).
    /// </summary>
    internal static class Placements
    {
        internal const string SaveKey = "RestraintHub.Placed";

        internal static readonly PlacementStore Store = new PlacementStore();
        private static IntPtr _stageController;
        private static string _stage;
        private static bool _dirty; // shown objects must be rebuilt (load, new game)
        private static readonly Dictionary<Placeable, bool> WasWorn = new Dictionary<Placeable, bool>();

        /// <summary>The current map's StageType name, null between maps.</summary>
        internal static string Stage => _stage;

        /// <summary>Once per frame in game: follow map changes, keep shown objects alive, free a placeable when its in-use wearable
        /// came off.</summary>
        internal static void Tick()
        {
            var ctrl = InGameManager.Instance == null ? null : InGameManager.Instance.CurrentStageController;
            IntPtr ptr = ctrl == null ? IntPtr.Zero : ctrl.Pointer;
            if (ptr != _stageController || _dirty || RootsLost())
            {
                HideAll();
                _stageController = ptr;
                _stage = ctrl == null ? null : ctrl.StageType.ToString();
                _dirty = false;
                if (_stage != null) ShowStage(ctrl);
            }
            foreach (var p in Hub.Places)
            {
                if (p.InUse == null) continue;
                bool worn = p.InUse.Worn;
                WasWorn.TryGetValue(p, out bool was);
                WasWorn[p] = worn;
                if (was && !worn && p.UsedAt != null)
                {
                    var at = p.UsedAt;
                    p.UsedAt = null;
                    SetAreaActive(at, true);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] free: " + p.Id + " (" + p.InUse.Id + " came off)");
                }
            }
        }

        private static bool RootsLost()
        {
            foreach (var p in Hub.Places)
                foreach (var placed in p._here)
                    if (placed.Root == null) return true; // destroyed with its scene (a load into the same map)
            return false;
        }

        // --- the panel: place, pick up ---------------------------------------------------------------------------------------

        /// <summary>Place one where its FindSpot says: null when placed, else why not.</summary>
        internal static string Place(Placeable p)
        {
            if (_stage == null || PlayerController.Instance == null) return "not in a stage";
            if (p.UsedAt != null || (p.InUse != null && p.InUse.Worn)) return "she is in it";
            if (p._here.Count >= Math.Max(1, p.MaxPerMap))
                return p.MaxPerMap <= 1 ? "there is one on this map already - pick it up first" : "there are " + p._here.Count + " on this map already";
            PlaceSpot spot;
            try { spot = p.FindSpot != null ? p.FindSpot() : PlaceSpot.InFront(1f); }
            catch (Exception e) { HubController.Fail(p.Id + ".FindSpot", e); return "it failed (see the log)"; }
            if (spot == null) return "not here";
            if (spot.Refused != null) return spot.Refused;
            var q = spot.Rotation.normalized;
            var record = Store.Add(p.Id, _stage, new[] { spot.Position.x, spot.Position.y, spot.Position.z }, new[] { q.x, q.y, q.z, q.w }, spot.Data);
            var placed = ShowOne(p, record, Scene());
            if (placed == null)
            {
                Store.Remove(record);
                return "it could not be shown (see the log)";
            }
            Plugin.Logger.LogInfo("[SFM-ACCEPT] placed: " + p.Id + " on " + _stage + " at " + Fmt(spot.Position));
            return null;
        }

        /// <summary>Pick up the nearest one on this map: null when done, else why not.</summary>
        internal static string PickUp(Placeable p)
        {
            Placed nearest = null;
            float best = float.MaxValue;
            Vector3 her = PlayerController.Instance == null ? Vector3.zero : PlayerController.Instance.transform.position;
            foreach (var placed in p._here)
            {
                float d = (placed.Position - her).sqrMagnitude;
                if (d < best) { best = d; nearest = placed; }
            }
            if (nearest == null) return "there is none on this map";
            if (p.UsedAt == nearest) return "she is in it";
            HideOne(nearest);
            p._here.Remove(nearest);
            Store.Remove(nearest.Record);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] picked up: " + p.Id + " on " + _stage + " at " + Fmt(nearest.Position));
            return null;
        }

        // --- F at a placed object --------------------------------------------------------------------------------------------

        private static void Interact(Placed placed)
        {
            try
            {
                var p = placed.Item;
                if (Hub.MenuOpen) return;
                if (p.Use != null)
                {
                    string why = p.Use(placed);
                    if (why != null) Refuse(p, why);
                    else Plugin.Logger.LogInfo("[SFM-ACCEPT] used: " + p.Id);
                    return;
                }
                if (p.InUse == null) return;
                if (p.InUse.Worn) { Hub.Message("She is already in the " + p.Name + "."); return; }
                p.UsedAt = placed;
                if (HubController.Instance == null || !HubController.Instance.Choose(p.InUse))
                {
                    p.UsedAt = null; // Choose showed why
                    return;
                }
                WasWorn[p] = true;
                SetAreaActive(placed, false); // F is the struggle now, not this area's
                Plugin.Logger.LogInfo("[SFM-ACCEPT] used: " + p.Id + " -> " + p.InUse.Id);
            }
            catch (Exception e) { HubController.Fail("using " + placed.Item.Id, e); }
        }

        private static void Refuse(Placeable p, string why)
        {
            Hub.Message("Can't use the " + p.Name + ": " + why);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] refused: use " + p.Id + ": " + why);
        }

        // --- save / load / new game ------------------------------------------------------------------------------------------

        internal static string Serialize() => Store.Serialize();

        internal static void OnLoad(string text)
        {
            foreach (var p in Hub.Places) p.UsedAt = null;
            int n = Store.Restore(text);
            _dirty = true;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] load: " + n + " placed objects");
        }

        internal static void OnNewGame()
        {
            foreach (var p in Hub.Places) p.UsedAt = null;
            Store.Clear();
            _dirty = true;
        }

        // --- showing ---------------------------------------------------------------------------------------------------------

        private static Scene Scene()
        {
            var ctrl = InGameManager.Instance == null ? null : InGameManager.Instance.CurrentStageController;
            return ctrl != null ? ctrl.gameObject.scene : SceneManager.GetActiveScene();
        }

        private static void ShowStage(StageController ctrl)
        {
            var scene = ctrl.gameObject.scene;
            int shown = 0, unknown = 0;
            foreach (var record in Store.On(_stage))
            {
                var p = Find(record.Kind);
                if (p == null) { unknown++; continue; } // its mod is not installed: kept in the save, not shown
                if (ShowOne(p, record, scene) != null) shown++;
            }
            if (shown + unknown > 0)
                Plugin.Logger.LogInfo("[SFM-ACCEPT] stage " + _stage + ": " + shown + " placed objects shown" + (unknown > 0 ? ", " + unknown + " of mods not installed" : ""));
        }

        private static Placed ShowOne(Placeable p, PlacedRecord record, Scene scene)
        {
            var placed = new Placed(p, record);
            GameObject root = null;
            try
            {
                root = new GameObject("RestraintHub " + p.Id);
                if (scene.IsValid()) SceneManager.MoveGameObjectToScene(root, scene); // goes away with the map
                root.transform.SetPositionAndRotation(placed.Position, placed.Rotation);
                placed.Root = root.transform;
                p.Show(placed);
            }
            catch (Exception e)
            {
                HubController.Fail(p.Id + ".Show", e);
                if (root != null) UnityEngine.Object.Destroy(root);
                return null;
            }
            try { placed.Area = MakeArea(placed); }
            catch (Exception e) { HubController.Fail("interaction area for " + p.Id, e); }
            p._here.Add(placed);
            if (p.UsedAt != null && p.UsedAt.Record == record) { p.UsedAt = placed; SetAreaActive(placed, false); }
            return placed;
        }

        private static void HideAll()
        {
            foreach (var p in Hub.Places)
            {
                foreach (var placed in p._here) HideOne(placed);
                p._here.Clear();
            }
        }

        private static void HideOne(Placed placed)
        {
            try { placed.Item.Hide?.Invoke(placed); }
            catch (Exception e) { HubController.Fail(placed.Item.Id + ".Hide", e); }
            try
            {
                if (placed.Root != null) UnityEngine.Object.Destroy(placed.Root.gameObject);
            }
            catch (Exception) { /* already gone with its scene */ }
            placed.Root = null;
            placed.Area = null;
        }

        private static GameObject _areaTemplate;

        /// <summary>A copy of the game's dropped-coat interaction area under the placed root: "F &lt;text&gt;" and F calls us.</summary>
        private static GameObject MakeArea(Placed placed)
        {
            if (_areaTemplate == null)
            {
                var refs = AssetReferencer.Instance;
                var prefabs = refs == null ? null : refs.DroppedPrefabs;
                if (prefabs == null || prefabs.Length == 0 || prefabs[0] == null) throw new InvalidOperationException("no dropped-item prefab");
                var area = prefabs[0].transform.Find("InteractArea");
                if (area == null) throw new InvalidOperationException(prefabs[0].name + " has no InteractArea");
                _areaTemplate = area.gameObject;
            }
            var go = UnityEngine.Object.Instantiate(_areaTemplate, placed.Root, false);
            go.name = "InteractArea";
            go.transform.localPosition = placed.Item.AreaCenter;
            go.transform.localRotation = Quaternion.identity;
            var col = go.GetComponent<SphereCollider>();
            if (col != null)
            {
                col.radius = placed.Item.AreaRadius;
                col.center = new Vector3(0f, 0.45f, 0f);
            }
            var ctrl = go.GetComponent<InteractAreaController>();
            ctrl.type = InteractType.DroppedClothes; // F calls onInteract only [observed 2026-11-07]
            Action call = () => Interact(placed);
            ctrl.Initialize(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(call), placed.Item.TextFor(placed));
            return go;
        }

        private static void SetAreaActive(Placed placed, bool on)
        {
            try
            {
                if (placed?.Area != null) placed.Area.SetActive(on);
            }
            catch (Exception e) { HubController.Fail("area of " + placed.Item.Id, e); }
        }

        private static Placeable Find(string id)
        {
            foreach (var p in Hub.Places) if (p.Id == id) return p;
            return null;
        }

        /// <summary>Diagnostics: "Apart: Hogtie (1.2, 0.0, -2.4); ..." of the current map, and the total.</summary>
        internal static string Describe()
        {
            var parts = new List<string>();
            foreach (var p in Hub.Places)
                foreach (var placed in p._here)
                    parts.Add(p.Id + (p.UsedAt == placed ? "*" : "") + " " + Fmt(placed.Position));
            return (_stage ?? "no stage") + ": " + (parts.Count == 0 ? "none" : string.Join("; ", parts)) + " (" + Store.All.Count + " in all)";
        }

        private static string Fmt(Vector3 v) => "(" + v.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", "
            + v.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", " + v.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
}
