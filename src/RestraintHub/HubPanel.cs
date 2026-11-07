using System;
using System.Collections.Generic;
using Common.CommonUI;
using Common.Scripts.UI;
using ExposureUnnoticed2.Object3D.IngameManager;
using HadakaCoat.ObjectsUi.Common.Button;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Key = UnityEngine.InputSystem.Key;
using Keyboard = UnityEngine.InputSystem.Keyboard;

namespace RestraintHub
{
    /// <summary>
    /// The hub's panel (<c>Keys.OpenPanel</c>, U): two tabs, Wearables and Placeables, as a scroll list of buttons. Built from the
    /// game's own dildo choice panel (<c>AssetReferencer.ChooseDildoPanel</c>, opened with <c>InGameUiManager.Open</c>), so it looks
    /// and behaves like the game's panels: cursor, input held, Escape closes, the game keeps running [observed 2026-11-07,
    /// docs/knowledge/api-notes/ui-and-input.md]. Its own texts replace the localised ones (their <c>CommonTextView</c> removed),
    /// its rows replace the prefab's buttons (a new <c>Button.onClick</c> each).
    /// </summary>
    internal sealed class HubPanel
    {
        // Layout in the panel's units (the prefab: box 1057 x 385, rows 309.5 x 54.9, font 25).
        private const float RowHeight = 40f, RowSpacing = 3f, FontSize = 20f, ListWidth = 330f;

        private readonly HubController _hub;
        private BasePanelView _view;
        private Transform _list;
        private GameObject _template;
        private TextMeshProUGUI _title;
        private readonly List<(GameObject Row, TextMeshProUGUI Text, TextMeshProUGUI Mask)> _rows = new List<(GameObject, TextMeshProUGUI, TextMeshProUGUI)>();
        private readonly List<string> _shownLabels = new List<string>();
        private int _tab; // 0 Wearables, 1 Placeables
        private float _nextRefresh;
        private bool _keyDown, _qDown, _eDown;

        /// <summary>"open, tab Wearables, rows: ..." for diagnostics (<c>game get RestraintHub.HubController::PanelStatus</c>).</summary>
        internal string Status = "closed";

        public HubPanel(HubController hub) { _hub = hub; }

        internal bool IsOpen => _view != null && !_view.isAlreadyClose;

        /// <summary>Once per frame in game. <paramref name="otherMenu"/>: a game panel (or ours) or the ring is open.</summary>
        internal void Tick(bool otherMenu)
        {
            bool key = Pressed(Plugin.OpenPanelKey.Value, ref _keyDown);
            if (IsOpen)
            {
                if (key) { Close(); return; }
                if (Pressed("Q", ref _qDown) || Pressed("E", ref _eDown)) SwitchTab(1 - _tab);
                if (Time.unscaledTime >= _nextRefresh) Refresh();
                return;
            }
            if (_view != null) { _view = null; Status = "closed"; }
            if (key && !otherMenu) Open(); // also in an interaction area: she may stand at a placed object to pick it up
        }

        private static bool Pressed(string keyName, ref bool wasDown)
        {
            var kb = Keyboard.current;
            if (kb == null || !Enum.TryParse(keyName, true, out Key k) || k == Key.None) return false;
            bool down = kb[k].isPressed, pressed = down && !wasDown;
            wasDown = down;
            return pressed;
        }

        private void Open()
        {
            try
            {
                var ui = InGameUiManager.Instance;
                var refs = AssetReferencer.Instance;
                if (ui == null || refs == null || refs.ChooseDildoPanel == null) return;
                _view = ui.Open(refs.ChooseDildoPanel, false);
                if (_view == null) return;
                _view.gameObject.name = "RestraintHubPanel";
                Build();
                _shownLabels.Clear();
                Refresh();
                Plugin.Logger.LogInfo("[SFM-ACCEPT] panel: open (" + TabName + ")");
            }
            catch (Exception e)
            {
                HubController.Fail("opening the panel", e);
                Close();
            }
        }

        internal void Close()
        {
            try { if (IsOpen) _view.Close(); }
            catch (Exception e) { HubController.Fail("closing the panel", e); }
            _view = null;
            Status = "closed";
        }

        private string TabName => _tab == 0 ? "Wearables" : "Placeables";

        private void SwitchTab(int tab)
        {
            _tab = tab;
            _shownLabels.Clear();
            Refresh();
            Plugin.Logger.LogInfo("[SFM-ACCEPT] panel: tab " + TabName);
        }

        // --- building: a scroll view between the title and Close, the prefab's buttons as the row template ---------------------
        // (The box keeps the prefab's size: its open animation sets it, a bigger size was overwritten [observed 2026-11-07].)

        private void Build()
        {
            var t = _view.transform;
            var contents = t.Find("Contents");
            var titleRt = contents.Find("Title").GetComponent<RectTransform>();
            _title = titleRt.GetComponent<TextMeshProUGUI>();
            Unlocalise(_title);
            var cancel = contents.Find("CancelButton");
            var cancelRt = cancel.GetComponent<RectTransform>();
            Wire(cancel.gameObject, "Close", Close);

            // The list area: from below the title to above Close (Contents is 100 x 100, anchored at the box's centre).
            float top = titleRt.anchoredPosition.y + 50f - titleRt.sizeDelta.y / 2f - 12f;
            float bottom = cancelRt.anchoredPosition.y - 50f + cancelRt.sizeDelta.y / 2f + 12f;
            var scrollGo = new GameObject("RestraintHubScroll");
            var scroll = scrollGo.AddComponent<RectTransform>();
            scroll.SetParent(contents, false);
            scroll.anchorMin = scroll.anchorMax = new Vector2(0.5f, 0.5f);
            scroll.pivot = new Vector2(0.5f, 1f);
            scroll.anchoredPosition = new Vector2(0f, top);
            scroll.sizeDelta = new Vector2(ListWidth, top - bottom);
            var catcher = scrollGo.AddComponent<Image>(); // invisible: the mouse wheel scrolls between rows too
            catcher.color = new Color(0f, 0f, 0f, 0f);
            scrollGo.AddComponent<RectMask2D>();

            var group = contents.GetComponentInChildren<ButtonGroupManager>(true);
            _list = group.transform;
            var list = _list.GetComponent<RectTransform>();
            list.SetParent(scroll, false);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.anchoredPosition = Vector2.zero;
            list.sizeDelta = new Vector2(0f, 0f);
            var layout = _list.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = RowSpacing;
            var fitter = _list.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rect = scrollGo.AddComponent<ScrollRect>();
            rect.content = list;
            rect.viewport = scroll;
            rect.horizontal = false;
            rect.vertical = true;
            rect.movementType = ScrollRect.MovementType.Clamped;
            rect.scrollSensitivity = RowHeight;

            // The first button is the template for every row; the prefab's others go.
            _template = null;
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                var child = _list.GetChild(i).gameObject;
                if (child.GetComponent<ButtonView>() == null) continue;
                if (_template != null) UnityEngine.Object.Destroy(_template);
                _template = child;
            }
            _template.SetActive(false);
            _rows.Clear();
        }

        /// <summary>Own text for a localised label: its <c>CommonTextView</c> would put the game's text back in <c>Start</c>.</summary>
        private static void Unlocalise(TextMeshProUGUI text)
        {
            var loc = text.GetComponent<CommonTextView>();
            if (loc != null) UnityEngine.Object.DestroyImmediate(loc);
        }

        private static void Wire(GameObject button, string label, Action click)
        {
            foreach (var text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                Unlocalise(text);
                text.text = label;
            }
            var view = button.GetComponent<ButtonView>();
            view.button.onClick = new Button.ButtonClickedEvent();
            if (click != null) view.button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() =>
            {
                try { click(); }
                catch (Exception e) { HubController.Fail("panel button " + label, e); }
            })));
            view.onClick = null;
        }

        // --- content ------------------------------------------------------------------------------------------------------

        private readonly struct Entry
        {
            public readonly string Label;
            public readonly Action Click;
            public Entry(string label, Action click) { Label = label; Click = click; }
        }

        private List<Entry> Entries()
        {
            var list = new List<Entry>();
            if (_tab == 0)
            {
                foreach (var w in Hub.All)
                {
                    if (!w.InPanel) continue;
                    var item = w;
                    string note = Missions.Note(w.Id).Trim();
                    string state = note.Length > 0 ? "  " + note : w.Worn ? "  [on]" : "";
                    list.Add(new Entry(w.Name + state, () => { if (_hub.Choose(item)) Close(); }));
                }
                if (list.Count == 0) list.Add(new Entry("(no wearables installed)", null));
                if (Hub.Places.Count > 0) list.Add(new Entry("Placeables  >", () => SwitchTab(1)));
            }
            else
            {
                foreach (var p in Hub.Places)
                {
                    var item = p;
                    int placed = p.Here.Count;
                    list.Add(new Entry(p.Name + (placed > 0 ? "  [" + placed + " here]" : ""), () => { if (_hub.Place(item)) Close(); }));
                    if (placed > 0) list.Add(new Entry("Pick up: " + p.Name, () => { if (_hub.PickUp(item)) Close(); }));
                }
                if (Hub.Places.Count == 0) list.Add(new Entry("(no placeables installed)", null));
                list.Add(new Entry("<  Wearables", () => SwitchTab(0)));
            }
            return list;
        }

        /// <summary>Rows for the current entries: texts updated in place, the rows rebuilt when the number changes.</summary>
        private void Refresh()
        {
            _nextRefresh = Time.unscaledTime + 0.25f;
            if (!IsOpen || _list == null) return;
            var entries = Entries();
            _title.text = "Restraints - " + TabName + "   (Q / E)";
            bool same = entries.Count == _shownLabels.Count;
            for (int i = 0; same && i < entries.Count; i++) same = entries[i].Label == _shownLabels[i];
            if (same) return;
            if (entries.Count != _rows.Count) Rebuild(entries.Count);
            _shownLabels.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                Wire(_rows[i].Row, entries[i].Label, entries[i].Click);
                _rows[i].Row.GetComponent<Button>().interactable = entries[i].Click != null;
                _shownLabels.Add(entries[i].Label);
            }
            Status = "open, tab " + TabName + ", rows: " + string.Join(" | ", _shownLabels);
        }

        private void Rebuild(int count)
        {
            foreach (var r in _rows) UnityEngine.Object.Destroy(r.Row);
            _rows.Clear();
            for (int i = 0; i < count; i++)
            {
                var row = UnityEngine.Object.Instantiate(_template, _list);
                row.name = "Row" + i;
                row.SetActive(true);
                var rt = row.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, RowHeight);
                TextMeshProUGUI text = null, mask = null;
                foreach (var tmp in row.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    tmp.enableAutoSizing = false;
                    tmp.fontSize = FontSize;
                    if (text == null) text = tmp; else mask = tmp;
                }
                _rows.Add((row, text, mask));
            }
            var group = _list.GetComponent<ButtonGroupManager>();
            if (group != null) group.ReSearchButton(true, true);
        }
    }
}
