using System;
using System.Collections.Generic;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using TMPro;
using UnityEngine;

namespace RestraintHub
{
    /// <summary>
    /// The list of worn restraints on screen, directly above the head preview (bottom right), growing upwards with each row. It
    /// is a copy of the game's own
    /// shortcut panel (<c>InGameCanvas/MiddleLayer/Right/ShortcutPanel</c>: rows with a key text and a name text, TextMeshPro)
    /// without the game's scripts, so it looks like the game's HUD. The first row names the select key; the selected
    /// restraint's row is bright and marked, the others dimmed.
    /// </summary>
    internal sealed class HudList
    {
        private const string PanelPath = "/InGameManager/InGameCanvas/MiddleLayer/Right/ShortcutPanel";
        // The head preview: a 300 x 300 RawImage anchored bottom right, its top edge at y 430 of 1080 [observed 2026-10-24].
        private const string FacePath = "/InGameManager/InGameCanvas/BackLayer/FaceCameraImage";
        private const float Gap = 6f;
        private RectTransform _face;
        private Vector2 _pivot, _size, _anchored; // the copy's own placement (above the shortcut panel), used when there is no preview
        private GameObject _panel;
        private readonly List<(GameObject Row, TextMeshProUGUI Key, TextMeshProUGUI Name)> _rows = new List<(GameObject, TextMeshProUGUI, TextMeshProUGUI)>();
        private string _shown;
        private float _retryAt;

        private bool Ensure()
        {
            if (_panel != null) return true;
            if (Time.unscaledTime < _retryAt) return false;
            _retryAt = Time.unscaledTime + 2f;
            _rows.Clear();
            _shown = null;
            var source = GameObject.Find(PanelPath);
            if (source == null) return false;
            _panel = UnityEngine.Object.Instantiate(source, source.transform.parent);
            _panel.name = "RestraintHubList";
            // Above the game's panel: moved up by its own height and a gap.
            var rect = _panel.GetComponent<RectTransform>();
            var srcRect = source.GetComponent<RectTransform>();
            rect.anchoredPosition = srcRect.anchoredPosition + new Vector2(0f, srcRect.rect.height + 12f);
            _pivot = rect.pivot;
            _size = rect.sizeDelta;
            _anchored = rect.anchoredPosition;
            foreach (var view in _panel.GetComponentsInChildren<ShortcutItemView>(true))
            {
                _rows.Add((view.gameObject, view.buttonText, view.name));
                var animator = view.GetComponent<Animator>();
                UnityEngine.Object.DestroyImmediate(view);
                if (animator != null) UnityEngine.Object.DestroyImmediate(animator);
            }
            if (_rows.Count == 0)
            {
                Plugin.Logger.LogWarning("restraint list: the game's shortcut panel has no rows; list not shown");
                UnityEngine.Object.Destroy(_panel);
                _panel = null;
                _retryAt = float.MaxValue;
                return false;
            }
            Plugin.Logger.LogInfo("[SFM-ACCEPT] hud: restraint list built from the shortcut panel (" + _rows.Count + " rows)");
            return true;
        }

        private void EnsureRows(int n)
        {
            while (_rows.Count < n)
            {
                var copy = UnityEngine.Object.Instantiate(_rows[0].Row, _panel.transform);
                var texts = copy.GetComponentsInChildren<TextMeshProUGUI>(true);
                // The copy's texts in the same order as the template's (key first, then name).
                TextMeshProUGUI key = null, name = null;
                var tKey = _rows[0].Key; var tName = _rows[0].Name;
                foreach (var t in texts)
                {
                    if (t.gameObject.name == (tKey == null ? null : tKey.gameObject.name) && key == null) key = t;
                    else if (t.gameObject.name == (tName == null ? null : tName.gameObject.name) && name == null) name = t;
                }
                _rows.Add((copy, key, name));
            }
        }

        public void Show(HubController c)
        {
            var items = c.List.Items;
            if (items.Count == 0) { Hide(); return; }
            if (!Ensure()) return;
            string key = Plugin.SelectKey.Value;
            // Header with the select key (only useful with more than one), then one row per worn restraint.
            var lines = new List<(string Key, string Name, bool Bright)>();
            if (items.Count > 1) lines.Add((key, Plugin.BlockEachOther.Value ? "Next restraint" : "Next restraint (overlap on)", false));
            foreach (string id in items)
            {
                bool selected = id == c.List.Selected;
                var r = HubController.Find(id);
                string name = HubController.NameOf(id) + Missions.Note(id);
                // F only when she may struggle (a mission can lock it).
                string keys = r != null && Missions.StruggleLocked(r) ? r.Controls : string.IsNullOrEmpty(r?.Controls) ? "F" : "F, " + r.Controls;
                if (selected && !string.IsNullOrEmpty(keys)) name += "  (" + keys + ")";
                lines.Add((selected ? ">" : "", name, selected));
            }
            string state = string.Join("|", lines);
            Place(lines.Count); // every frame: follows the preview if it is shown / hidden
            if (state == _shown && _panel.activeSelf) return;
            _shown = state;
            EnsureRows(lines.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool used = i < lines.Count;
                if (row.Row.activeSelf != used) row.Row.SetActive(used);
                if (!used) continue;
                float alpha = lines[i].Bright ? 1f : 0.6f;
                if (row.Key != null) { row.Key.text = lines[i].Key; row.Key.alpha = alpha; }
                if (row.Name != null) { row.Name.text = lines[i].Name; row.Name.alpha = alpha; }
            }
            if (!_panel.activeSelf) _panel.SetActive(true);
        }

        /// <summary>
        /// Over the head preview: the panel is sized to exactly its rows (the game's panel is a 100 x 100 box its 28 px rows
        /// overflow from the top left) and hangs from its bottom centre, placed just above the preview's top edge - so each row
        /// added grows the list upwards. Without the preview (hidden) the list goes back above the shortcut panel.
        /// </summary>
        private void Place(int rows)
        {
            var rect = _panel.GetComponent<RectTransform>();
            if (_face == null)
            {
                var go = GameObject.Find(FacePath);
                if (go != null) _face = go.GetComponent<RectTransform>();
            }
            if (_face == null || !_face.gameObject.activeInHierarchy)
            {
                rect.pivot = _pivot;
                rect.sizeDelta = _size;
                rect.anchoredPosition = _anchored;
                return;
            }
            var row = _rows[0].Row.GetComponent<RectTransform>().rect;
            var layout = _panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            float spacing = layout == null ? 0f : layout.spacing;
            float height = rows * row.height + Math.Max(0, rows - 1) * spacing;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, row.width);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            var face = _face.rect;
            Vector3 topCentre = _face.TransformPoint(new Vector3(face.center.x, face.yMax, 0f));
            rect.position = topCentre + new Vector3(0f, Gap * _face.lossyScale.y, 0f);
        }

        public void Hide()
        {
            if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
        }

        public void Destroy()
        {
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _panel = null;
        }
    }
}
