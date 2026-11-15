#if SFM_CMV2
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using RestraintHub.Logic;
using SFM_custom_mission_v2.Script;
using SFM_custom_mission_v2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static SFM_custom_mission_v2.UI.PluginGUI;

namespace RestraintHub
{
    /// <summary>
    /// "RestraintHub" on Custom Missions v2's phone (TAB menu): <b>Restraints</b> - the panel's wearables and placeables as a list
    /// (put on / select, place, pick up) - and <b>Config</b> - every installed mod's BepInEx settings, changed without a restart,
    /// split into the restraint mods (the hub and the mods that register items with it) and the others. A mod's settings open
    /// in Custom Missions' own settings screen (<c>ProgramSettingsWindow</c>) filled from its <c>.cfg</c>; changes are written
    /// back at once. Uses Custom Missions' documented app API (<c>PluginGUI.RegisterApp</c>, <c>App</c>, <c>Window</c>).
    /// Only compiled with Custom Missions' DLL present (SFM_CMV2) and only touched when it is installed (<see cref="PhoneHook"/>).
    /// </summary>
    internal sealed class HubPhoneApp : App
    {
        internal static readonly Color Background = new Color(0.95f, 0.95f, 0.95f);
        internal static readonly Color ButtonColor = new Color(0.78f, 0.78f, 0.80f);
        internal static readonly Color Accent = new Color(0.55f, 0.12f, 0.35f);

        public override Window InitializeWindow() => new HubMainWindow();

        public override GameObject CreateButton(GameObject parent, float size)
        {
            string icon = Path.Combine(Path.GetDirectoryName(typeof(HubPhoneApp).Assembly.Location) ?? "", "phone-icon.png");
            if (!File.Exists(icon)) icon = Path.Combine(GUIfolder, "gear.png");
            return DefaultCreateButton(parent, size, UISettings.Get("ui.restraintHubIconBackground", Accent),
                UISettings.Get("ui.restraintHubIcon", Color.white), icon);
        }
    }

    /// <summary>Helpers shared by the app's windows: a scroll list below the menu bar and rows in it.</summary>
    internal static class PhoneUi
    {
        internal const float RowWidth = 78f, RowHeight = 11f, Pad = 5f;

        /// <summary>A vertical scroll list filling the window below the menu bar (and below <paramref name="top"/> more units).</summary>
        internal static Transform List(Window w, float top = 0f)
        {
            float s = Window.scale;
            var go = new GameObject("RestraintHubList");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(w.gameObject.transform, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(0f, Pad * s);
            rt.offsetMax = new Vector2(0f, -(menubarheight + Pad + top) * s);
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // the wheel scrolls between rows too
            go.AddComponent<RectMask2D>();
            var content = new GameObject("Content");
            var crt = content.AddComponent<RectTransform>();
            crt.SetParent(rt, false);
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = layout.childControlWidth = false;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            layout.spacing = 2f * s;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = go.AddComponent<ScrollRect>();
            scroll.content = crt;
            scroll.viewport = rt;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RowHeight * s;
            return crt;
        }

        internal static GameObject Row(Transform list, string text, Action click, bool enabled = true, Color? color = null)
        {
            UnityAction action = (UnityAction)(() =>
            {
                try { click?.Invoke(); }
                catch (Exception e) { HubController.Fail("phone: " + text, e); }
            });
            var b = CreateButton(list.gameObject, "Row", new Vector2(RowWidth, RowHeight), 0.2f,
                enabled ? (color ?? HubPhoneApp.ButtonColor) : new Color(0.88f, 0.88f, 0.88f), enabled ? Color.black : new Color(0.45f, 0.45f, 0.45f), text, action, 0.25f);
            var button = b.GetComponent<Button>();
            if (button != null) button.interactable = enabled;
            return b;
        }

        internal static GameObject Note(Transform list, string text, float height = 14f)
        {
            var t = CreateText(text, list.gameObject, new Vector2(RowWidth, height), new Color(0.2f, 0.2f, 0.2f), 7f, TextAlignmentOptions.TopLeft);
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null) { tmp.enableWordWrapping = true; tmp.enableAutoSizing = false; }
            return t;
        }

        internal static void Clear(Transform list)
        {
            for (int i = list.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(list.GetChild(i).gameObject);
        }
    }

    internal sealed class HubMainWindow : Window
    {
        public HubMainWindow() : base("RestraintHub", HubPhoneApp.Background)
        {
            var list = PhoneUi.List(this);
            PhoneUi.Note(list, "Use Restraints (also with U).", 18f);
            PhoneUi.Row(list, "Restraints", () => ShowWindow(new HubRestraintsWindow()));

            PhoneUi.Note(list, "Change settings of mods, without a restart. `*` might requires restart. EXPERIMENTAL.", 18f);
            PhoneUi.Row(list, "Config (.cfg files)", () => ShowWindow(new HubConfigWindow()));
        }
    }

    /// <summary>The panel's items: wearables (put on / select) and placeables (place, pick up), refreshed while open.</summary>
    internal sealed class HubRestraintsWindow : Window
    {
        private readonly Transform _list;
        private string _shown = "";
        private string _status = "";
        private float _next;

        public HubRestraintsWindow() : base("Restraints", HubPhoneApp.Background)
        {
            _list = PhoneUi.List(this);
            Refresh(true);
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime < _next) return;
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            _next = Time.unscaledTime + 0.3f;
            var hub = HubController.Instance;
            var rows = new List<(string Text, Action Click, bool Header)>();
            rows.Add(("Wearables", null, true));
            foreach (var w in Hub.All)
            {
                if (!w.InPanel) continue;
                var item = w;
                string note = Missions.Note(w.Id).Trim();
                rows.Add((w.Name + (note.Length > 0 ? "  " + note : w.Worn ? "  [on]" : ""), () => Done(hub != null && hub.Choose(item), item.Name), false));
            }
            if (Hub.Places.Count > 0) rows.Add(("Placeables", null, true));
            foreach (var p in Hub.Places)
            {
                var item = p;
                int here = p.Here.Count;
                rows.Add((p.Name + (here > 0 ? "  [" + here + " here]" : ""), () => Done(hub != null && hub.Place(item), "placed: " + item.Name), false));
                if (here > 0) rows.Add(("Pick up: " + p.Name, () => Done(hub != null && hub.PickUp(item), "picked up: " + item.Name), false));
            }
            string key = string.Join("|", rows.ConvertAll(r => r.Text)) + "|" + _status;
            if (!force && key == _shown) return;
            _shown = key;
            PhoneUi.Clear(_list);
            if (_status.Length > 0) PhoneUi.Note(_list, _status, 9f);
            foreach (var r in rows)
            {
                if (r.Header) PhoneUi.Note(_list, r.Text, 8f);
                else PhoneUi.Row(_list, r.Text, r.Click);
            }
        }

        private void Done(bool ok, string what)
        {
            // A refusal is in the game's message bar too; here it says only whether it worked.
            _status = ok ? "Done: " + what : "Not possible here - see the message at the top of the screen.";
            Refresh(true);
        }
    }

    /// <summary>Config: the restraint mods or the others.</summary>
    internal sealed class HubConfigWindow : Window
    {
        public HubConfigWindow() : base("Config", HubPhoneApp.Background)
        {
            var list = PhoneUi.List(this);
            PhoneUi.Row(list, "Restraint mods", () => ShowWindow(new HubModListWindow(true)));
            PhoneUi.Row(list, "Other mods", () => ShowWindow(new HubModListWindow(false)));
            PhoneUi.Note(list, ConfigNotes.LaterNote, 20f);
        }
    }

    /// <summary>The installed mods with settings (restraint mods, or the others); a row opens the mod's settings.</summary>
    internal sealed class HubModListWindow : Window
    {
        public HubModListWindow(bool restraintMods) : base(restraintMods ? "Restraint mods" : "Other mods", HubPhoneApp.Background)
        {
            var list = PhoneUi.List(this);
            var restraint = RestraintAssemblies();
            var mods = new List<(string Name, ConfigFile Config)>();
            foreach (var info in IL2CPPChainloader.Instance.Plugins.Values)
            {
                if (!(info?.Instance is BepInEx.Unity.IL2CPP.BasePlugin plugin) || plugin.Config == null || plugin.Config.Count == 0) continue;
                bool isRestraint = restraint.Contains(plugin.GetType().Assembly);
                if (isRestraint != restraintMods) continue;
                mods.Add((info.Metadata.Name, plugin.Config));
            }
            mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var m in mods)
            {
                var mod = m;
                PhoneUi.Row(list, mod.Name, () => ConfigScreens.Open(mod.Name, mod.Config));
            }
            if (mods.Count == 0) PhoneUi.Note(list, "No mods with settings here.", 10f);
        }

        /// <summary>The hub and every mod that registered an item with it (by the assembly of its callbacks).</summary>
        private static HashSet<Assembly> RestraintAssemblies()
        {
            var set = new HashSet<Assembly> { typeof(Hub).Assembly };
            void Add(Delegate d) { if (d?.Method?.DeclaringType != null) set.Add(d.Method.DeclaringType.Assembly); }
            foreach (var w in Hub.All) { Add(w.IsWorn); Add(w.PutOn); Add(w.TakeOff); }
            foreach (var p in Hub.Places) { Add(p.Show); Add(p.FindSpot); }
            return set;
        }
    }

    /// <summary>A mod's BepInEx settings as a Custom Missions settings screen; changes go straight into the config (saved, and
    /// live for mods that read it when used).</summary>
    internal static class ConfigScreens
    {
        private sealed class Link
        {
            public ProgramSettings.Setting Setting;
            public ConfigEntryBase Entry;
            public string[] Options; // AcceptableValueList / enum names
            public bool Text;        // edited as BepInEx's text form
        }

        internal static void Open(string modName, ConfigFile config)
        {
            var ps = new ProgramSettings();
            ProgramSettings.allProgramSettings?.Remove(ps); // not one of the missions' settings
            ps.title = new LocalizedString(modName);
            ps.description = new LocalizedString(ConfigNotes.LaterNote);
            var links = new List<Link>();
            ps.Add(new ProgramSettings.Setting("_note", new LocalizedString(ConfigNotes.LaterNote), ProgramSettings.SettingType.Label));
            var entries = new List<ConfigEntryBase>();
            foreach (var key in config.Keys) entries.Add(config[key]);
            entries.Sort((a, b) => string.CompareOrdinal(a.Definition.Section + "." + a.Definition.Key, b.Definition.Section + "." + b.Definition.Key));
            int n = 0;
            string section = null;
            foreach (var e in entries)
            {
                try
                {
                    if (e.Definition.Section != section)
                    {
                        // The section as a heading; the rows then show only the key (long titles ran into the value).
                        section = e.Definition.Section;
                        ps.Add(new ProgramSettings.Setting("h" + n, new LocalizedString(section), ProgramSettings.SettingType.Label));
                    }
                    var link = Make(e, "s" + n++);
                    if (link == null) continue;
                    ps.Add(link.Setting);
                    links.Add(link);
                }
                catch (Exception ex) { HubController.Fail("phone config " + e.Definition, ex); }
            }
            ps.OnChange += () => Apply(modName, links);
            ShowWindow(new ProgramSettingsWindow(ps));
            Plugin.Logger.LogInfo("[SFM-ACCEPT] phone: config of " + modName + " (" + links.Count + " settings)");
        }

        private static Link Make(ConfigEntryBase e, string name)
        {
            var d = e.Definition;
            string desc = e.Description?.Description ?? "";
            var title = new LocalizedString(ConfigNotes.Title(null, d.Key, desc));
            Type t = e.SettingType;
            var acc = e.Description?.AcceptableValues;
            object v = e.BoxedValue;
            if (t == typeof(bool))
                return new Link { Entry = e, Setting = new ProgramSettings.Setting(name, title, (bool)v) };
            if (t.IsEnum && t.GetCustomAttribute<FlagsAttribute>() == null)
            {
                var names = Enum.GetNames(t);
                int index = Array.IndexOf(names, v.ToString());
                return new Link { Entry = e, Options = names, Setting = new ProgramSettings.Setting(name, title, Math.Max(0, index), Localized(names)) };
            }
            if (acc is AcceptableValueList<string> strings)
            {
                var names = strings.AcceptableValues;
                return new Link { Entry = e, Options = names, Setting = new ProgramSettings.Setting(name, title, Math.Max(0, Array.IndexOf(names, (string)v)), Localized(names)) };
            }
            if (acc is AcceptableValueRange<float> fr)
                return new Link { Entry = e, Setting = new ProgramSettings.Setting(name, title, (float)v, fr.MinValue, fr.MaxValue) };
            if (acc is AcceptableValueRange<int> ir)
                return new Link { Entry = e, Setting = new ProgramSettings.Setting(name, title, (int)v, ir.MinValue, ir.MaxValue) };
            if (acc is AcceptableValueRange<double> dr)
                return new Link { Entry = e, Setting = new ProgramSettings.Setting(name, title, (float)(double)v, (float)dr.MinValue, (float)dr.MaxValue) };
            // Everything else (strings such as key lists, numbers without a range, other types): BepInEx's text form.
            return new Link { Entry = e, Text = true, Setting = new ProgramSettings.Setting(name, title, e.GetSerializedValue()) };
        }

        private static LocalizedString[] Localized(string[] names)
        {
            var l = new LocalizedString[names.Length];
            for (int i = 0; i < names.Length; i++) l[i] = new LocalizedString(names[i]);
            return l;
        }

        /// <summary>The screen changed something: write every changed value into its config entry.</summary>
        private static void Apply(string modName, List<Link> links)
        {
            foreach (var l in links)
            {
                try
                {
                    var value = l.Setting.Value;
                    if (value == null || value.IsNull()) continue;
                    if (l.Text)
                    {
                        string text = value.ToString();
                        if (text == l.Entry.GetSerializedValue()) continue;
                        l.Entry.SetSerializedValue(text);
                        Log(modName, l, text);
                        continue;
                    }
                    Type t = l.Entry.SettingType;
                    object next;
                    if (t == typeof(bool)) next = value.ToBool();
                    else if (l.Options != null)
                    {
                        int i = (int)Math.Round(value.ToFloat());
                        if (i < 0 || i >= l.Options.Length) continue;
                        next = t.IsEnum ? Enum.Parse(t, l.Options[i]) : l.Options[i];
                    }
                    else next = Convert.ChangeType(t == typeof(int) ? Math.Round(value.ToFloat()) : value.ToFloat(), t, System.Globalization.CultureInfo.InvariantCulture);
                    if (Equals(next, l.Entry.BoxedValue)) continue;
                    l.Entry.BoxedValue = next; // clamps to its range, saves the file, raises SettingChanged
                    Log(modName, l, l.Entry.GetSerializedValue());
                }
                catch (Exception e) { HubController.Fail("phone config " + l.Entry.Definition, e); }
            }
        }

        private static void Log(string modName, Link l, string value) =>
            Plugin.Logger.LogInfo("[SFM-ACCEPT] phone: " + modName + " " + l.Entry.Definition.Section + "." + l.Entry.Definition.Key + " = " + value);
    }

    /// <summary>Registers the app; only called when Custom Missions v2 is installed.</summary>
    internal static class PhoneApp
    {
        internal static void Register()
        {
            PluginGUI.RegisterApp(new HubPhoneApp());
            Plugin.Logger.LogInfo("[SFM-ACCEPT] phone: RestraintHub app registered with Custom Missions v2");
        }
    }
}
#endif

namespace RestraintHub
{
    /// <summary>The phone app's entry point, safe without Custom Missions v2: the types that derive from its classes are only
    /// loaded inside <see cref="RegisterInner"/>, which runs only when it is installed.</summary>
    internal static class PhoneHook
    {
        internal static void Register()
        {
#if SFM_CMV2
            if (!BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.TryGetValue(Missions.CmGuid, out var info) || info.Instance == null) return;
            try { RegisterInner(); }
            catch (Exception e) { Plugin.Logger.LogWarning("phone app not added to Custom Missions v2: " + e.Message); }
#endif
        }

#if SFM_CMV2
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RegisterInner() => PhoneApp.Register();
#endif
    }
}
