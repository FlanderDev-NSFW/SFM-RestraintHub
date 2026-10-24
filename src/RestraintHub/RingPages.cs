using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RestraintHub.Logic;

namespace RestraintHub
{
    /// <summary>
    /// The restraints' entries in the common ring menu (hold 1), on pages after the game's (empty slots fill the game's last
    /// page), with the game's number shortcuts. Moved here from SfmKit's RingEntry (one entry per mod) so one place builds the
    /// ring for all of them.
    ///
    /// Game facts this relies on [observed 2026-10-11, SpreaderBar; 2026-10-24 WallRestraint / Yoke]:
    /// - entries are <c>RingMenuParentView.commonRingMenuModel</c>; the array is rebuilt on loads and stage changes, so the
    ///   entries are added again whenever the array changes. 8 slots per page; the ring adds pages as needed.
    ///   Mod entries use RingItemType values from <see cref="ModValueStart"/>, the game's are below.
    /// - there is no safe hook on the game's handler (patching <c>OnInvokeCommandCommon</c> broke saving), so a click is read
    ///   here: an entry selected on the shown page + a left click this frame.
    /// - releasing ring key N over an entry stores it as that key's shortcut (<c>lastInvokeModel*</c>) and its index in
    ///   <c>GameStateData.RingShortcutModelIndex*</c>, which is saved. On load the game reads that index before any mod entry
    ///   exists: a mod index threw IndexOutOfRange and the stage never started. So the stored index is always kept on a game
    ///   entry (the key's previous one), and the keys holding a mod entry are restored from each restraint's mask.
    /// - a short tap of N runs the key's model through the game's handler, which ignores mod entries: detected with
    ///   <c>GetShortUp</c>.
    /// </summary>
    internal sealed class RingPages
    {
        public const int ModValueStart = 9000, ModValueEnd = 9999;
        /// <summary>Empty filler slots (shown "-": their release condition is false).</summary>
        public const int PaddingValue = 9999;
        public const string PaddingKey = "restrainthub_empty";

        private readonly Action<Restraint> _onChosen;
        private IntPtr _ringArray;
        private bool _applyShortcuts;
        private int _vanilla = 31;
        private readonly int[] _fallbackIndex = new int[8];
        private static Il2CppSystem.Func<bool> _alwaysReleased, _neverReleased;

        public RingPages(Action<Restraint> onChosen) { _onChosen = onChosen; }

        /// <summary>The game's own entries in the current ring (a stored shortcut index must stay below it).</summary>
        public int VanillaCount => _vanilla;

        public void OnShortcutsLoaded() => _applyShortcuts = true;

        /// <summary>A new game / the title replaced the game data without a load: forget the keys.</summary>
        public void Reset()
        {
            foreach (var r in Hub.All) r.Shortcuts = 0;
        }

        private static bool IsMod(RingMenuItemModel m) => (int)m.RingItemType >= ModValueStart && (int)m.RingItemType <= ModValueEnd;

        /// <summary>Once per frame. <paramref name="uiOpen"/>: a game panel is open (shortcuts must not fire).</summary>
        public void Tick(bool uiOpen)
        {
            var parent = RingMenuParentView.Instance;
            if (parent == null || Hub.All.Count == 0) return;
            EnsureEntries(parent);
            EnsurePageLimit(parent);
            WatchClick(parent);
            SyncShortcuts(parent);
            if (!uiOpen) WatchShortcuts(parent);
        }

        private void EnsureEntries(RingMenuParentView view)
        {
            var models = view.commonRingMenuModel;
            if (models == null || models.Pointer == _ringArray) return;
            // The game's entries (anything a mod added before - e.g. an older mod version - is dropped and rebuilt here).
            var vanilla = new List<RingMenuItemModel>();
            for (int i = 0; i < models.Length; i++)
                if (models[i] != null && !IsMod(models[i])) vanilla.Add(models[i]);
            _vanilla = vanilla.Count;
            int padding = Rules.Padding(_vanilla);
            if (_alwaysReleased == null)
            {
                _alwaysReleased = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() => true));
                _neverReleased = DelegateSupport.ConvertDelegate<Il2CppSystem.Func<bool>>(new Func<bool>(() => false));
            }
            var grown = new Il2CppReferenceArray<RingMenuItemModel>(_vanilla + padding + Hub.All.Count);
            int n = 0;
            foreach (var m in vanilla) grown[n++] = m;
            for (int i = 0; i < padding; i++)
                grown[n] = new RingMenuItemModel { RingItemType = (RingMenuParentView.RingItemType)PaddingValue, NameKey = PaddingKey, Index = n++, ReleaseCondition = _neverReleased };
            foreach (var r in Hub.All)
            {
                r.RingIndex = n;
                grown[n] = new RingMenuItemModel { RingItemType = (RingMenuParentView.RingItemType)r.RingValue, NameKey = LabelKey(r), Index = n++, ReleaseCondition = _alwaysReleased };
            }
            view.commonRingMenuModel = grown;
            _ringArray = grown.Pointer;
            _applyShortcuts = true;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] ring: " + Hub.All.Count + " restraint entries from index " + (_vanilla + padding) + " (page "
                                  + ((_vanilla + padding) / Rules.PageSize + 1) + "; " + _vanilla + " game entries, " + padding + " empty)");
        }

        /// <summary>
        /// Ring keys 2-8 open "Skill" rings that page only up to <c>maxRingIndexSkill</c>, set from the game's 31 entries (3 =
        /// four pages); key 1's common ring shows all pages. So with key 2 the restraint page never appeared and X wrapped from
        /// page 4 to page 1; raising it to the array's last page shows it (5 pages, the restraints on page 5) [observed
        /// 2026-10-24]. Checked every frame: the game may set it again when it sets the ring up.
        /// </summary>
        private void EnsurePageLimit(RingMenuParentView view)
        {
            var models = view.commonRingMenuModel;
            if (models == null || models.Length == 0) return;
            int last = (models.Length - 1) / Rules.PageSize;
            if (view.maxRingIndexSkill >= last) return;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] ring: number-key rings page up to " + (last + 1) + " (was " + (view.maxRingIndexSkill + 1) + ")");
            view.maxRingIndexSkill = last;
        }

        /// <summary>The localisation key the ring shows for a restraint (translated in the hub's Localizer.Trans postfix).</summary>
        public static string LabelKey(Restraint r) => "restrainthub_" + r.Id;

        private void WatchClick(RingMenuParentView parent)
        {
            if (!parent.IsOpenRing) return;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var views = parent.openRingList;
            int page = parent.CurrentRingIndexCommon;
            if (views == null || page < 0 || page >= views.Count) return;
            var selected = views[page] == null ? null : views[page].currentSelectView;
            var r = selected == null || selected.Model == null ? null : Find((int)selected.Model.RingItemType);
            if (r == null) return;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] ring: " + r.Id + " chosen");
            _onChosen(r);
        }

        private static Restraint Find(int value)
        {
            foreach (var r in Hub.All) if (r.RingValue == value) return r;
            return null;
        }

        private void SyncShortcuts(RingMenuParentView parent)
        {
            var data = GameState.GameStateData;
            var models = parent.commonRingMenuModel;
            if (data == null || models == null || _ringArray != models.Pointer) return;
            if (_applyShortcuts)
            {
                _applyShortcuts = false;
                foreach (var r in Hub.All)
                {
                    for (int k = 0; k < 8; k++)
                        if ((r.Shortcuts & (1 << k)) != 0) SetShortcutModel(parent, k, models[r.RingIndex]);
                    if (r.Shortcuts != 0) Plugin.Logger.LogInfo("[SFM-ACCEPT] shortcut: " + r.Id + " restored on keys " + KeyList(r.Shortcuts));
                }
            }
            for (int k = 0; k < 8; k++)
            {
                var model = ShortcutModel(parent, k);
                int index = ShortcutIndex(data, k);
                var held = model == null ? null : Find((int)model.RingItemType);
                foreach (var r in Hub.All)
                {
                    if (r == held)
                    {
                        if ((r.Shortcuts & (1 << k)) == 0) Plugin.Logger.LogInfo("[SFM-ACCEPT] shortcut: " + r.Id + " assigned to key " + (k + 1));
                        r.Shortcuts |= 1 << k;
                    }
                    else r.Shortcuts &= ~(1 << k); // reassigned
                }
                if (model != null && IsMod(model))
                {
                    if (index >= _vanilla) SetShortcutIndex(data, k, _fallbackIndex[k]); // never save a mod index (also not a filler's)
                }
                else if (index >= 0 && index < _vanilla) _fallbackIndex[k] = index;
            }
        }

        private static readonly InputManager.InputType[] ShortcutKeys =
        {
            InputManager.InputType.CommonRingMenu, InputManager.InputType.SkillRingMenu, InputManager.InputType.SkillRingMenu3,
            InputManager.InputType.SkillRingMenu4, InputManager.InputType.SkillRingMenu5, InputManager.InputType.SkillRingMenu6,
            InputManager.InputType.SkillRingMenu7, InputManager.InputType.SkillRingMenu8,
        };

        private void WatchShortcuts(RingMenuParentView parent)
        {
            if (parent.IsOpenRing || PlayerController.Instance == null) return;
            for (int i = 0; i < ShortcutKeys.Length; i++)
            {
                var model = ShortcutModel(parent, i);
                var r = model == null ? null : Find((int)model.RingItemType);
                if (r == null || !parent.GetShortUp(ShortcutKeys[i])) continue;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] shortcut: key " + (i + 1) + " runs " + r.Id);
                _onChosen(r);
                return;
            }
        }

        private static string KeyList(int mask)
        {
            var s = "";
            for (int k = 0; k < 8; k++) if ((mask & (1 << k)) != 0) s += (s.Length == 0 ? "" : ",") + (k + 1);
            return s;
        }

        private static RingMenuItemModel ShortcutModel(RingMenuParentView p, int key) => key switch
        {
            0 => p.lastInvokeModelCommon, 1 => p.lastInvokeModelSkill, 2 => p.lastInvokeModelSkill3, 3 => p.lastInvokeModelSkill4,
            4 => p.lastInvokeModelSkill5, 5 => p.lastInvokeModelSkill6, 6 => p.lastInvokeModelSkill7, _ => p.lastInvokeModelSkill8,
        };

        private static void SetShortcutModel(RingMenuParentView p, int key, RingMenuItemModel model)
        {
            switch (key)
            {
                case 0: p.lastInvokeModelCommon = model; break;
                case 1: p.lastInvokeModelSkill = model; break;
                case 2: p.lastInvokeModelSkill3 = model; break;
                case 3: p.lastInvokeModelSkill4 = model; break;
                case 4: p.lastInvokeModelSkill5 = model; break;
                case 5: p.lastInvokeModelSkill6 = model; break;
                case 6: p.lastInvokeModelSkill7 = model; break;
                default: p.lastInvokeModelSkill8 = model; break;
            }
            var views = p.shortcutItemViews;
            if (views == null) return;
            foreach (var v in views)
                if (v != null && (int)v.type == key) v.SetName(model);
        }

        public static int ShortcutIndex(GameStateData d, int key) => key switch
        {
            0 => d.RingShortcutModelIndexCommon, 1 => d.RingShortcutModelIndexSkill, 2 => d.RingShortcutModelIndexSkill3,
            3 => d.RingShortcutModelIndexSkill4, 4 => d.RingShortcutModelIndexSkill5, 5 => d.RingShortcutModelIndexSkill6,
            6 => d.RingShortcutModelIndexSkill7, _ => d.RingShortcutModelIndexSkill8,
        };

        public static void SetShortcutIndex(GameStateData d, int key, int index)
        {
            switch (key)
            {
                case 0: d.RingShortcutModelIndexCommon = index; break;
                case 1: d.RingShortcutModelIndexSkill = index; break;
                case 2: d.RingShortcutModelIndexSkill3 = index; break;
                case 3: d.RingShortcutModelIndexSkill4 = index; break;
                case 4: d.RingShortcutModelIndexSkill5 = index; break;
                case 5: d.RingShortcutModelIndexSkill6 = index; break;
                case 6: d.RingShortcutModelIndexSkill7 = index; break;
                default: d.RingShortcutModelIndexSkill8 = index; break;
            }
        }
    }
}
