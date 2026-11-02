using System;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.ObjectUI.InGame.TempUnlockPanel;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;
using RestraintHub.Logic;
using UnityEngine;

namespace RestraintHub
{
    /// <summary>Harmony hooks into the game, for all restraint mods. Every hook fails open: on an exception the game behaves
    /// as without the mods.</summary>
    internal static class Patches
    {
        public static void Apply(Harmony harmony)
        {
            var me = typeof(Patches);
            void Hook(System.Reflection.MethodBase target, string postfix) => harmony.Patch(target, postfix: new HarmonyMethod(me, postfix));
            Hook(AccessTools.Method(typeof(InputResolver), nameof(InputResolver.Resolve)), nameof(ResolvePostfix));
            // No hook on RingMenuParentView.OnInvokeCommandCommon: with it patched, saving crashed the game [observed 2026-10-11].
            Hook(AccessTools.Method(typeof(Localizer), nameof(Localizer.Trans), new[] { typeof(string) }), nameof(TransPostfix));
            Hook(AccessTools.Method(typeof(TempUnlockPanelView), nameof(TempUnlockPanelView.OnLateUpdate)), nameof(UnlockPanelPostfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Load)), nameof(LoadPostfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save)), nameof(SavePostfix));
        }

        // --- input: F for the selected restraint; what the worn restraints take away ----------------------------------------

        private static float _refusedAt = -10f;

        private static void ResolvePostfix(InputData __result)
        {
            try
            {
                var c = HubController.Instance;
                if (c == null || __result == null) return;
                c.InteractHeld = __result.Interact;
                Blocks blocks = Blocks.None;
                Restraint first = null;
                foreach (var r in Hub.All)
                {
                    if (!r.Worn) continue;
                    var b = Rules.Effective(r.Blocks?.Invoke() ?? Blocks.None, Plugin.BlockEachOther.Value);
                    if (first == null && (b & (Blocks.HandActions | Blocks.PoseSkills)) != 0) first = r;
                    blocks |= b;
                }
                // Only the selected item may be struggled with: while a mod restraint is selected, F does not start the game's
                // handcuff struggle (outside interaction areas only - there F is the area's).
                if (GameStateFacade.IsAttachSomeHandcuffs() && !c.HandcuffsSelected && c.List.Selected != null && !c.InInteractAreaCached)
                    __result.Interact = false;
                if (blocks != Blocks.None && (Plugin.DebugDisable.Value & 1) == 0) Apply(__result, blocks, first);
                foreach (var r in Hub.All)
                    if (r.OnInput != null && r.Worn) r.OnInput(__result);
            }
            catch (Exception e) { HubController.Fail("input hook", e); }
        }

        private static void Apply(InputData i, Blocks b, Restraint handsBy)
        {
            var player = PlayerController.Instance;
            var action = player == null || player.Pca == null || player.Pca.PlayerState == null ? null : player.Pca.PlayerState.CurrentAction;
            bool inPose = action != null && MAction.IsPoseSkill(action.Type);
            if ((b & (Blocks.Running | Blocks.Moving)) != 0)
            {
                // Shift + move ends a pose skill (as in vanilla): it stays while one runs, unless she may not move at all.
                if (!inPose || (b & Blocks.Moving) != 0) i.Dash = false;
                i.JustAutoRun = i.JustAutoChase = false;
            }
            if ((b & Blocks.Moving) != 0)
            {
                i.Move = Vector3.zero;
                i.JustMove = i.Jump = i.MoveForwardOneHand = i.JustSlowSkill = false;
            }
            if ((b & Blocks.Crouching) != 0) i.JustCrouch = false;
            if ((b & Blocks.Pee) != 0) i.JustPee = false;
            bool refused = false;
            if ((b & Blocks.HandActions) != 0)
            {
                refused = i.JustOnaniNormal || i.JustRingDildo || i.JustRingHandcuff || i.JustRingEyeMask || i.JustEyeMask || i.JustDrinkWater
                          || i.JustDrop || i.JustDropHandcuffKey || i.JustDropVibeRemocon || i.JustBra || i.JustPants || i.JustOpenClothesA
                          || i.JustOpenClothesB || i.JustCloseClothesA || i.JustCloseClothesB;
                i.JustOnaniNormal = i.JustRingDildo = i.JustRingHandcuff = i.JustRingEyeMask = i.JustEyeMask = i.JustDrinkWater = false;
                i.JustDrop = i.JustDropHandcuffKey = i.JustDropVibeRemocon = i.JustBra = i.JustPants = false;
                i.JustOpenClothesA = i.JustOpenClothesB = i.JustCloseClothesA = i.JustCloseClothesB = false;
                i.MoveForwardOneHand = false;
            }
            if ((b & Blocks.PoseSkills) != 0)
            {
                refused |= i.JustPoseSkill != RingMenuParentView.RingItemType.None;
                i.JustPoseSkill = RingMenuParentView.RingItemType.None;
            }
            if (!refused || handsBy == null || Time.unscaledTime - _refusedAt < 1f) return;
            _refusedAt = Time.unscaledTime;
            Hub.Message("Can't do that with the " + handsBy.Name + " on.");
            Plugin.Logger.LogInfo("[SFM-ACCEPT] refused: an action blocked by " + handsBy.Id);
        }

        // --- ring labels ---------------------------------------------------------------------------------------------------

        private static void TransPostfix(string key, ref string __result)
        {
            if (key == null || !key.StartsWith("restrainthub_", StringComparison.Ordinal)) return;
            if (key == RingPages.PaddingKey) { __result = ""; return; }
            foreach (var r in Hub.All)
                if (key == RingPages.LabelKey(r)) { __result = r.Name; return; }
        }

        // --- the game's "Unlocking..." gauge shows the selected restraint's attempt ------------------------------------------

        private static void UnlockPanelPostfix(TempUnlockPanelView __instance)
        {
            try
            {
                if (__instance == null) return;
                float attempt = 0f;
                foreach (var r in Hub.All)
                    if (r.Selected && r.Attempt != null) attempt = Math.Max(attempt, r.Attempt());
                if (attempt <= 0f) return;
                if (__instance.contents != null && !__instance.contents.activeSelf) __instance.contents.SetActive(true);
                if (__instance.circleGauge != null) __instance.circleGauge.fillAmount = attempt;
            }
            catch (Exception e) { HubController.Fail("unlock gauge hook", e); }
        }

        // --- save / load: each restraint's number shortcuts (its own pre-hub key), then its own data (OnSave / OnLoad) ---------
        // Written right after SaveManager.Save into the slot it just wrote [observed 2026-10-11, SpreaderBar].

        private static void SavePostfix()
        {
            try
            {
                if (HubController.Instance == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                var s = "";
                foreach (var r in Hub.All)
                {
                    if (string.IsNullOrEmpty(r.ShortcutSaveKey)) continue;
                    SaveRapper.Save(r.ShortcutSaveKey, r.Shortcuts);
                    s += " " + r.Id + "=" + r.Shortcuts;
                }
                SaveRapper.Save(Missions.SaveKey, Missions.Locks.Serialize());
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: shortcuts" + s + (Missions.Locks.Count == 0 ? "" : ", " + Missions.Locks.Count + " mission locks") + " path=" + SaveRapper.currentDataPath);
                var slot = new SaveSlot(SaveRapper.currentDataPath);
                foreach (var r in Hub.All)
                {
                    if (r.OnSave == null) continue;
                    try { r.OnSave(slot); }
                    catch (Exception e) { HubController.Fail(r.Id + ".OnSave", e); }
                }
            }
            catch (Exception e) { HubController.Fail("save hook", e); }
        }

        private static void LoadPostfix()
        {
            try
            {
                var c = HubController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                var s = "";
                foreach (var r in Hub.All)
                {
                    int mask = string.IsNullOrEmpty(r.ShortcutSaveKey) ? -1 : SaveRapper.Load(r.ShortcutSaveKey, -1);
                    if (mask < 0) mask = r.OnShortcutsMissing?.Invoke() ?? 0;
                    r.Shortcuts = mask & 0xFF;
                    s += " " + r.Id + "=" + r.Shortcuts;
                }
                c.OnSaveLoaded();
                Missions.OnLoad(SaveRapper.Load(Missions.SaveKey, ""));
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: shortcuts" + s + " path=" + SaveRapper.currentDataPath);
                var slot = new SaveSlot(SaveRapper.currentDataPath);
                foreach (var r in Hub.All)
                {
                    if (r.OnLoad == null) continue;
                    try { r.OnLoad(slot); }
                    catch (Exception e) { HubController.Fail(r.Id + ".OnLoad", e); }
                }
            }
            catch (Exception e) { HubController.Fail("load hook", e); }
        }
    }
}
