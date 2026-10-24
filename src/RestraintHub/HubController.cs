using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using Il2CppInterop.Runtime.Attributes;
using RestraintHub.Logic;
using SfmKit.Game;
using UnityEngine;
using Key = UnityEngine.InputSystem.Key;
using Keyboard = UnityEngine.InputSystem.Keyboard;

namespace RestraintHub
{
    /// <summary>
    /// Runs the hub every frame: ring pages, the list of worn restraints and its selection, the select key, F and + / - for
    /// the selected restraint, the list on screen. Bridge diagnostics: <c>game get RestraintHub.HubController::Status</c>,
    /// <c>game call RestraintHub.HubController SelectNext</c> (session with --allow-invoke).
    /// </summary>
    public sealed class HubController : MonoBehaviour
    {
        internal static HubController Instance;
        public HubController(IntPtr pointer) : base(pointer) { }

        /// <summary>The game's own handcuffs in the list (id; not a registered restraint): F goes to the game when selected.</summary>
        internal const string HandcuffsId = "(handcuffs)";

        /// <summary>"selected X; list A, B; ..." for diagnostics.</summary>
        public static string Status = "";

        internal readonly WornList List = new WornList();
        internal RingPages Ring;
        internal bool InteractHeld;         // F this frame (Patches.ResolvePostfix)
        internal bool HandcuffsSelected;    // the game's handcuffs are selected: F is theirs
        internal bool InInteractAreaCached; // she stands in an interaction area (F is the area's), this frame
        private readonly GameUi _ui = new GameUi();
        private readonly PlusMinusKeys _plusMinus = new PlusMinusKeys();
        private HudList _hud;
        private IntPtr _gameData;
        private bool _bindingsChecked;
        private static int _failures;

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake()
        {
            Instance = this;
            Ring = new RingPages(OnChosen);
            _hud = new HudList();
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            bool panel = _ui.PanelOpen();
            Ring.Tick(panel);
            TrackSaveIdentity();
            bool inGame = PlayerController.Instance != null && GameState.GameStateData != null;
            if (!inGame)
            {
                _hud.Hide();
                return;
            }
            if (!_bindingsChecked) CheckBindings();

            // What is worn now, in registration order (the list keeps the order things were put on).
            var worn = new List<string>();
            foreach (var r in Hub.All) if (r.Worn) worn.Add(r.Id);
            if (GameStateFacade.IsAttachSomeHandcuffs()) worn.Add(HandcuffsId);
            if (List.Update(worn)) Plugin.Logger.LogInfo("[SFM-ACCEPT] list: " + Describe());

            bool menus = panel || GameUi.RingOpen();
            if (!menus) WatchConflictToggle();
            if (!menus && SelectPressed() && List.Items.Count > 1)
            {
                List.Next();
                Plugin.Logger.LogInfo("[SFM-ACCEPT] select: " + Describe());
            }

            int step = menus ? 0 : _plusMinus.Poll();
            InInteractAreaCached = _ui.InInteractArea();
            bool struggle = InteractHeld && !menus && !InInteractAreaCached;
            HandcuffsSelected = List.Selected == HandcuffsId;
            foreach (var r in Hub.All)
            {
                r.Selected = r.Id == List.Selected;
                r.StruggleHeld = r.Selected && struggle;
                if (r.Selected && step != 0) r.PendingStep = step;
                if (!r.Selected) r.PendingStep = 0;
            }
            Status = Describe();
            _hud.Show(this);
        }

        [HideFromIl2Cpp]
        internal string Describe()
        {
            var s = "selected " + (List.Selected == null ? "none" : NameOf(List.Selected)) + "; list";
            foreach (string id in List.Items) s += " " + NameOf(id) + ";";
            return s;
        }

        [HideFromIl2Cpp]
        internal static string NameOf(string id)
        {
            if (id == HandcuffsId) return "Handcuffs";
            foreach (var r in Hub.All) if (r.Id == id) return r.Name;
            return id;
        }

        [HideFromIl2Cpp]
        internal static Restraint Find(string id)
        {
            foreach (var r in Hub.All) if (r.Id == id) return r;
            return null;
        }

        /// <summary>The worn restraints' labels and parts, with the game's handcuffs (they hold her arms).</summary>
        [HideFromIl2Cpp]
        internal static List<(string, BodyParts)> WornParts()
        {
            var worn = new List<(string, BodyParts)>();
            foreach (var r in Hub.All) if (r.Worn) worn.Add((r.Name, r.Holds));
            if (GameState.GameStateData != null && (GameStateFacade.IsAttachSomeHandcuffs() || GameState.GameStateData.PlayerIsAttachMapHandcuffs))
                worn.Add(("handcuffs", BodyParts.Arms));
            return worn;
        }

        /// <summary>A restraint's ring entry or number key: select it if it is worn; else put it on unless something worn
        /// holds the same part.</summary>
        [HideFromIl2Cpp]
        private void OnChosen(Restraint r)
        {
            try
            {
                if (r.Worn)
                {
                    List.Select(r.Id);
                    Hub.Message(r.Name + " is on - hold F to struggle free.");
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] select: " + Describe());
                    return;
                }
                // Off (toggle key): restraints may overlap - whichever poses a body part last wins it.
                var busy = Plugin.BlockEachOther.Value ? Rules.Conflict(r.Holds, WornParts()) : null;
                if (busy != null)
                {
                    string other = busy.Value.Label;
                    Hub.Message("Can't use the " + r.Name + ": the " + other + (other.EndsWith("s") ? " hold " : " holds ") + Rules.Describe(busy.Value.Shared) + ".");
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] refused: " + r.Id + " conflicts with " + other + " (" + busy.Value.Shared + ")");
                    return;
                }
                r.OnChosen();
            }
            catch (Exception e) { Fail("choosing " + r.Id, e); }
        }

        /// <summary>Diagnostics: select the next worn restraint, as the select key does.</summary>
        public static string SelectNext()
        {
            var c = Instance;
            if (c == null) return "no hub";
            c.List.Next();
            return c.Describe();
        }

        // --- the hub's keys: select the next restraint, let restraints overlap -----------------------------------------------

        private bool _selectDown, _toggleDown;

        [HideFromIl2Cpp]
        private static bool Pressed(string keyName, ref bool wasDown)
        {
            var kb = Keyboard.current;
            if (kb == null || !Enum.TryParse(keyName, true, out Key key) || key == Key.None) return false;
            bool down = kb[key].isPressed;
            bool pressed = down && !wasDown;
            wasDown = down;
            return pressed;
        }

        [HideFromIl2Cpp]
        private bool SelectPressed() => Pressed(Plugin.SelectKey.Value, ref _selectDown);

        /// <summary>The toggle key flips whether restraints that hold the same body parts block each other (saved in the config).</summary>
        [HideFromIl2Cpp]
        private void WatchConflictToggle()
        {
            if (!Pressed(Plugin.ToggleConflictsKey.Value, ref _toggleDown)) return;
            bool on = !Plugin.BlockEachOther.Value;
            Plugin.BlockEachOther.Value = on;
            Hub.Message(on ? "Restraints block each other again." : "Restraints no longer block each other (they may overlap).");
            Plugin.Logger.LogInfo("[SFM-ACCEPT] conflicts: " + (on ? "on" : "off"));
        }

        /// <summary>Warn once for each of the hub's keys that is one of the game's own bindings (the game binds keys by action).</summary>
        [HideFromIl2Cpp]
        private void CheckBindings()
        {
            if (InputManager.Instance == null) return;
            _bindingsChecked = true;
            CheckBinding(Plugin.SelectKey, "select key");
            CheckBinding(Plugin.ToggleConflictsKey, "overlap toggle key");
        }

        [HideFromIl2Cpp]
        private static void CheckBinding(BepInEx.Configuration.ConfigEntry<string> entry, string what)
        {
            try
            {
                string name = entry.Definition.Section + "." + entry.Definition.Key;
                var clashes = GameUses(entry.Value, out Key key);
                if (clashes == null)
                {
                    Plugin.Logger.LogWarning(name + " '" + entry.Value + "' is not an Input System key name; the key does nothing.");
                    return;
                }
                if (clashes.Count == 0) Plugin.Logger.LogInfo("[SFM-ACCEPT] keys: " + what + " " + key + " is not bound by the game");
                else Plugin.Logger.LogWarning(name + " " + key + " is also the game's " + string.Join(", ", clashes) + " - pick another key.");
            }
            catch (Exception e) { Fail("checking the game's key bindings", e); }
        }

        /// <summary>The game's actions bound to a key (Input System key name); null if it is not a key.</summary>
        [HideFromIl2Cpp]
        private static List<string> GameUses(string keyName, out Key key)
        {
            var kb = Keyboard.current;
            if (kb == null || !Enum.TryParse(keyName, true, out key) || key == Key.None) { key = Key.None; return null; }
            string want = "/" + kb[key].name;
            var clashes = new List<string>();
            foreach (InputManager.InputType type in Enum.GetValues(typeof(InputManager.InputType)))
            {
                UnityEngine.InputSystem.InputAction action;
                try { action = InputManager.GetInputAction(type); }
                catch (Exception) { continue; } // types without an action ("None") throw KeyNotFoundException [observed 2026-10-24]
                if (action == null) continue;
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    string path = bindings[i].effectivePath;
                    if (path != null && path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) && path.EndsWith(want, StringComparison.OrdinalIgnoreCase))
                        clashes.Add(type.ToString());
                }
            }
            return clashes;
        }

        /// <summary>Diagnostics: which game actions use each of the given keys ("J K L" -> "J: free; K: Costume; ...").</summary>
        public static string KeysInUse(string keyNames)
        {
            var parts = new List<string>();
            foreach (string k in keyNames.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var uses = GameUses(k, out _);
                parts.Add(k + ": " + (uses == null ? "not a key" : uses.Count == 0 ? "free" : string.Join(",", uses)));
            }
            return string.Join("; ", parts);
        }

        // --- save identity -------------------------------------------------------------------------------------------------

        /// <summary>A new game or the title screen replaces the game data without a load: forget the keys.</summary>
        [HideFromIl2Cpp]
        private void TrackSaveIdentity()
        {
            var data = GameState.GameStateData;
            IntPtr ptr = data == null ? IntPtr.Zero : data.Pointer;
            if (ptr == _gameData) return;
            _gameData = ptr;
            Ring.Reset();
        }

        internal void OnSaveLoaded()
        {
            _gameData = GameState.GameStateData == null ? IntPtr.Zero : GameState.GameStateData.Pointer;
            Ring.OnShortcutsLoaded();
        }

        private void OnDestroy()
        {
            _hud?.Destroy();
            if (Instance == this) Instance = null;
        }
    }
}
