using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace RestraintHub
{
    /// <summary>
    /// The shared home of the restraint mods: see <see cref="Hub"/>. Loads before them
    /// (they declare a hard dependency on <see cref="PluginGuid"/>).
    /// <para><b>Not API:</b> the config entries are public for the dev bridge (live tuning). Restraint mods use
    /// <see cref="Hub"/> (<see cref="Hub.Guid"/>, <see cref="Hub.Version"/>) <see cref="Wearable"/> and <see cref="Placeable"/>.</para>
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Missions.CmGuid, BepInDependency.DependencyFlags.SoftDependency)] // optional: missions use the restraints
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "FlanderDev.SFM.RestraintHub";
        public const string PluginName = "RestraintHub";
        public const string PluginVersion = "2.0.0";

        internal static ManualLogSource Logger;
        public static ConfigEntry<string> SelectKey, ToggleConflictsKey, OpenPanelKey;
        public static ConfigEntry<bool> BlockEachOther, WarnMissions;
        public static ConfigEntry<float> EscapeSeconds;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            OpenPanelKey = Config.Bind("Keys", "OpenPanel", "U",
                "Key that opens and closes the restraint panel (wearables and placeables). An Input System key name; the log warns if the " +
                "game uses it too.");
            SelectKey = Config.Bind("Keys", "SelectNext", "B",
                "Key that selects the next worn restraint (F and + / - act on the selected one). An Input System key name (B, V, " +
                "Backquote ...); the log warns if the game uses it too.");
            ToggleConflictsKey = Config.Bind("Keys", "ToggleBlocking", "V",
                "Key that switches General.BlockEachOther on / off with a tap; held for Keys.EscapeHoldSeconds it takes off every " +
                "restraint, also ones a mission locked (an Input System key name; the log warns if the game uses it too).");
            EscapeSeconds = Config.Bind("Keys", "EscapeHoldSeconds", 3f, new ConfigDescription(
                "How long to hold Keys.ToggleBlocking to take off all restraints (the way out of anything a mission locked).",
                new AcceptableValueRange<float>(1f, 10f)));
            BlockEachOther = Config.Bind("General", "BlockEachOther", true,
                "Restraints that hold the same body part block each other (the yoke and the wall restraint both hold her arms), and " +
                "restraints refuse the animations they forbid (hand actions, pose skills, peeing). Off: they may be worn together - the " +
                "one that poses a body part last wins it - and those animations are allowed (running, moving, crouching stay blocked). " +
                "Toggled in game with Keys.ToggleBlocking.");
            WarnMissions = Config.Bind("General", "WarnMissions", true,
                "Show a warning in game when the installed Custom Missions v2 is a version the hub's mission functions were not made " +
                "for, or the hub can't connect to it (the log always has it).");
            DebugDisable = Config.Bind("Debug", "Disable", 0, "Bit mask for tests: 1 = block no input.");
            Patches.Apply(new HarmonyLib.Harmony(PluginGuid));
            ClassInjector.RegisterTypeInIl2Cpp<HubController>();
            AddComponent<HubController>();
            try { Missions.Init(); }
            catch (System.Exception e) { Logger.LogWarning("connecting to Custom Missions v2 failed: " + e); }
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready");
        }
    }
}
