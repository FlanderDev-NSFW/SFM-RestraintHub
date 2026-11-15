using System.Collections.Generic;
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
        public const string PluginVersion = "2.3.2";

        internal static ManualLogSource Logger;

        private const string KeysHelp = "One character - the key that types it on your keyboard layout - or an Input System key name " +
            "(a fixed position: NumpadPlus, RightBracket, Backquote ...); several separated by commas. The log lists what each key " +
            "setting means on your layout and warns when the game uses a key too.";

        /// <summary>All of the hub's key settings (for the log and for learning typed characters).</summary>
        internal static IEnumerable<ConfigEntry<string>> KeySettings()
        {
            foreach (var e in new[] { OpenPanelKey, SelectKey, ToggleConflictsKey, IncreaseKey, DecreaseKey })
                if (e != null) yield return e;
        }
        public static ConfigEntry<string> SelectKey, ToggleConflictsKey, OpenPanelKey, IncreaseKey, DecreaseKey;
        public static ConfigEntry<bool> BlockEachOther, WarnMissions, ActAsHandcuffs;
        public static ConfigEntry<float> EscapeSeconds, ArousalScale;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            OpenPanelKey = Config.Bind("Keys", "OpenPanel", "U",
                "Key that opens and closes the restraint panel (wearables and placeables). " + KeysHelp);
            SelectKey = Config.Bind("Keys", "SelectNext", "B",
                "Key that selects the next worn restraint (F and + / - act on the selected one). " + KeysHelp);
            ToggleConflictsKey = Config.Bind("Keys", "ToggleBlocking", "V",
                "Key that switches General.BlockEachOther on / off with a tap; held for Keys.EscapeHoldSeconds it takes off every " +
                "restraint, also ones a mission locked. " + KeysHelp);
            IncreaseKey = Config.Bind("Keys", "Increase", "+, =, NumpadPlus",
                "Keys that raise the selected restraint's value (width, height, length ...; held: repeats). " + KeysHelp);
            DecreaseKey = Config.Bind("Keys", "Decrease", "-, NumpadMinus",
                "Keys that lower the selected restraint's value (held: repeats). " + KeysHelp);
            EscapeSeconds = Config.Bind("Keys", "EscapeHoldSeconds", 3f, new ConfigDescription(
                "How long to hold Keys.ToggleBlocking to take off all restraints (the way out of anything a mission locked).",
                new AcceptableValueRange<float>(1f, 10f)));
            BlockEachOther = Config.Bind("General", "BlockEachOther", true,
                "Restraints that hold the same body part block each other (the yoke and the wall restraint both hold her arms), and " +
                "restraints refuse the animations they forbid (hand actions, pose skills, peeing). Off: they may be worn together - the " +
                "one that poses a body part last wins it - and those animations are allowed (running, moving, crouching stay blocked). " +
                "Toggled in game with Keys.ToggleBlocking.");
            ActAsHandcuffs = Config.Bind("General", "ActAsHandcuffs", true,
                "Restraints that hold her arms (yoke, binders, hogtie, wall restraint ...) count as the game's handcuffs: the game refuses " +
                "what it refuses in handcuffs (picking things up, the coat, masturbating, poses ...), and other mods that ask the game see " +
                "handcuffs; ones that also hold her in place count as handcuffs on an object in front. Off while General.BlockEachOther " +
                "is off.");
            WarnMissions = Config.Bind("General", "WarnMissions", true,
                "Show a warning in game when the installed Custom Missions v2 is a version the hub's mission functions were not made " +
                "for, or the hub can't connect to it (the log always has it).");
            ArousalScale = Config.Bind("Arousal", "Scale", 1f, new ConfigDescription(
                "How much restraints raise her ecstasy (the pink bar on the left): 1 = as the restraint or mission says, " +
                "0 = never (the game's own toys and actions, and missions, are not affected).", new AcceptableValueRange<float>(0f, 3f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0, "Bit mask for tests: 1 = block no input; 2 = leave the game's particle effects on her bones (they start where the animation alone puts her).");
            Patches.Apply(new HarmonyLib.Harmony(PluginGuid));
            ClassInjector.RegisterTypeInIl2Cpp<HubController>();
            AddComponent<HubController>();
            try { Missions.Init(); }
            catch (System.Exception e) { Logger.LogWarning("connecting to Custom Missions v2 failed: " + e); }
            PhoneHook.Register(); // the "RestraintHub" app on Custom Missions v2's phone (only when it is installed)
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready");
        }
    }
}
