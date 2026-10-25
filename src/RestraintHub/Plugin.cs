using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace RestraintHub
{
    /// <summary>
    /// The shared home of the restraint mods (SpreaderBar, WallRestraint, Yoke): see <see cref="Hub"/>. Loads before them
    /// (they declare a hard dependency on <see cref="PluginGuid"/>).
    /// <para><b>Not API:</b> the config entries are public for the dev bridge (live tuning). Restraint mods use
    /// <see cref="Hub"/> (<see cref="Hub.Guid"/>, <see cref="Hub.Version"/>) and <see cref="Restraint"/>.</para>
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.local.restrainthub";
        public const string PluginName = "RestraintHub";
        public const string PluginVersion = "0.3.0";

        internal static ManualLogSource Logger;
        public static ConfigEntry<string> SelectKey, ToggleConflictsKey;
        public static ConfigEntry<bool> BlockEachOther;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            SelectKey = Config.Bind("Keys", "SelectNext", "B",
                "Key that selects the next worn restraint (F and + / - act on the selected one). An Input System key name (B, V, " +
                "Backquote ...); the log warns if the game uses it too.");
            ToggleConflictsKey = Config.Bind("Keys", "ToggleBlocking", "V",
                "Key that switches General.BlockEachOther on / off (an Input System key name; the log warns if the game uses it too).");
            BlockEachOther = Config.Bind("General", "BlockEachOther", true,
                "Restraints that hold the same body part block each other (the yoke and the wall restraint both hold her arms), and " +
                "restraints refuse the animations they forbid (hand actions, pose skills, peeing). Off: they may be worn together - the " +
                "one that poses a body part last wins it - and those animations are allowed (running, moving, crouching stay blocked). " +
                "Toggled in game with Keys.ToggleBlocking.");
            DebugDisable = Config.Bind("Debug", "Disable", 0, "Bit mask for tests: 1 = block no input.");
            Patches.Apply(new HarmonyLib.Harmony(PluginGuid));
            ClassInjector.RegisterTypeInIl2Cpp<HubController>();
            AddComponent<HubController>();
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready");
        }
    }
}
