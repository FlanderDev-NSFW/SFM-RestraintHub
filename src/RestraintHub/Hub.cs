using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;

namespace RestraintHub
{
    /// <summary>
    /// One restraint's registration with the hub - a description plus callbacks, handed to <see cref="Hub.Register"/> once from
    /// the mod's <c>Load</c>. Fill in the fields marked "set by the mod" before registering and keep the object: the hub writes
    /// <see cref="Selected"/> and <see cref="StruggleHeld"/> into it every frame and queues + / - presses for
    /// <see cref="TakeStep"/>.
    /// <para>
    /// <b>Threading and timing.</b> Everything happens on Unity's main thread. The hub's own <c>Update</c> runs every frame in
    /// game (ring, list, keys) - in no fixed order relative to the mod's <c>Update</c>, so a value the hub sets can be one frame
    /// old when the mod reads it; that is harmless for keys held over many frames. Callbacks must be cheap (some run every
    /// frame) and must not throw: the hub catches and logs exceptions, but a throwing <see cref="IsWorn"/> makes the restraint
    /// count as not worn.
    /// </para>
    /// <para>
    /// <b>Minimal registration</b> (see <c>mods/RestraintTemplate</c> for a complete mod):
    /// <code>
    /// Hub.Register(new Restraint
    /// {
    ///     Id = "Blindfold", RingValue = 9400, Label = () => "Blindfold", ShortcutSaveKey = "Blindfold.Shortcuts",
    ///     Holds = BodyParts.Neck, IsWorn = () => machine.Worn, Blocks = () => Blocks.Running,
    ///     OnChosen = () => machine.RequestLock(), Attempt = () => machine.Attempt,
    /// });
    /// </code>
    /// </para>
    /// </summary>
    public sealed class Restraint
    {
        // --- set by the mod -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Set by the mod (required). Stable, unique id, e.g. <c>"SpreaderBar"</c>: the ring entries are ordered by it (ordinal), it
        /// names the restraint in the log and builds its localisation key (<c>restrainthub_&lt;Id&gt;</c>). Never change it once
        /// released - players' ring positions follow it.
        /// </summary>
        public string Id;

        /// <summary>
        /// Set by the mod (required). The <c>RingItemType</c> value of the ring entry: 9000..9998, unique among all restraint mods
        /// (9999 is the hub's empty filler). The game ignores values it does not know, so this only has to differ from the other
        /// mods - see the registry in <c>docs/mods/RestraintHub.md</c> (taken: 9100 SpreaderBar, 9200 WallRestraint, 9300 Yoke).
        /// Never saved: the hub keeps the game's saved shortcut indices on game entries.
        /// </summary>
        public int RingValue;

        /// <summary>
        /// Set by the mod (recommended). The name shown in the ring, the list and messages ("Can't use the <i>Label</i>: ...").
        /// Called whenever it is shown (every frame while the list is visible), so a config change shows at once; keep it cheap.
        /// Null: <see cref="Id"/> is shown.
        /// </summary>
        public Func<string> Label;

        /// <summary>
        /// Set by the mod (recommended). The key in the game's save file under which the hub stores which number keys (1-8) hold
        /// this entry (an int bit mask: bit k = key k+1). Pick one in your mod's namespace (<c>"MyMod.Shortcuts"</c>) and never
        /// rename it, or players lose their shortcuts. Null: shortcuts are not saved.
        /// </summary>
        public string ShortcutSaveKey;

        /// <summary>
        /// Set by the mod (optional). Called on load when the save has no <see cref="ShortcutSaveKey"/> (a save from before the
        /// mod, or from before it used the hub); return the bit mask to use, 0 for none. Runs inside the game's load, before the
        /// stage starts. Null: 0.
        /// </summary>
        public Func<int> OnShortcutsMissing;

        /// <summary>
        /// Set by the mod. The body parts the restraint holds. While <c>General.BlockEachOther</c> is on (the default; the
        /// player toggles it with <c>V</c>) the hub refuses to put on a restraint that needs a part a worn one holds, with a
        /// message - the game's handcuffs count as holding <see cref="BodyParts.Arms"/>. When the player switches blocking off,
        /// your restraint can be worn together with one that poses the same bones: whichever writes the bones last in
        /// <c>LateUpdate</c> wins, so pose code must not assume it is alone.
        /// </summary>
        public BodyParts Holds;

        /// <summary>
        /// Set by the mod (required). Whether it is on - from the moment it starts closing until it is fully off (with SfmKit's
        /// <c>LockMachine</c>: <c>machine.Worn</c>). Polled every frame; the list, F, + / -, blocking and the conflicts follow it.
        /// A restraint that turns worn by itself (a loaded save) appears in the list and becomes the selected one.
        /// </summary>
        public Func<bool> IsWorn;

        /// <summary>
        /// Set by the mod (optional). What the player may not do while it is worn; called every input frame while worn, so it
        /// may change (e.g. with a config mode). The hub applies the union over all worn restraints. Null: nothing.
        /// </summary>
        public Func<Blocks> Blocks;

        /// <summary>
        /// Set by the mod (required). The player chose the entry (ring click or a number key) while it is <b>not</b> worn and
        /// nothing worn holds a part it needs (or blocking is off). Put it on - or tell the player why not with
        /// <see cref="Hub.Message"/> (no wall here, too narrow ...). Choosing it while worn never calls this: the hub selects it
        /// in the list instead and says "... is on - hold F to struggle free."
        /// </summary>
        public Action OnChosen;

        /// <summary>
        /// Set by the mod (optional). Progress of the current struggle attempt, 0..1, shown in the game's own "Unlocking..."
        /// gauge while the restraint is selected and above 0 (<c>machine.Attempt</c>). Null: no gauge.
        /// </summary>
        public Func<float> Attempt;

        /// <summary>
        /// Set by the mod (optional). Called once per input frame while worn, inside a postfix on the game's
        /// <c>InputResolver.Resolve</c>, after the hub cleared what <see cref="Blocks"/> forbids - change the
        /// <see cref="InputData"/> here for anything the flags do not cover (SpreaderBar lets its forced pose step aside for
        /// actions). <c>Interact</c> is already false when the game's handcuffs are worn but not selected.
        /// </summary>
        public Action<InputData> OnInput;

        /// <summary>
        /// Set by the mod (optional). A few words for the list next to "F" on the selected row, usually what + / - do:
        /// <c>"+/- width"</c>. Null: only "(F)".
        /// </summary>
        public string Controls;

        /// <summary>
        /// Set by the mod (optional, hub 0.3.0). A short tap of F (released within 0.3 s) while the restraint is selected - for a
        /// second use of the key (NippleTether hands the chains' point to the player). With it set,
        /// <see cref="StruggleHeld"/> turns true only once F has been held longer than a tap, so a tap never starts the struggle.
        /// Null: F is only the struggle.
        /// </summary>
        public Action OnTap;

        /// <summary>
        /// Set by the mod (optional, hub 0.3.0). Called right after the game wrote a save slot: write your restraint's state
        /// with <see cref="SaveSlot.Save(string, bool)"/> etc. under keys in your mod's namespace (<c>"MyMod.Locked"</c>; never
        /// rename them once released). The system file is skipped. Replaces a Harmony postfix on <c>SaveManager.Save</c>.
        /// </summary>
        public Action<SaveSlot> OnSave;

        /// <summary>
        /// Set by the mod (optional, hub 0.3.0). Called right after the game loaded a save slot, before the stage starts: read
        /// your keys back (with defaults for saves from before your mod). The hub already restored the shortcuts.
        /// </summary>
        public Action<SaveSlot> OnLoad;

        /// <summary>
        /// Set by the mod (optional, hub 0.3.0). The game's data was replaced without a load - a new game, or back to the title
        /// screen: take the restraint off without ceremony. (Replaces tracking <c>GameState.GameStateData</c> yourself.)
        /// </summary>
        public Action OnNewGame;

        /// <summary>
        /// Set by the mod (optional, hub 1.1.0). Put it on for a mission script or the hub (no player message): return null when it
        /// started closing, else why not, in a few plain words ("no wall in front of her"). Only called while it is off and the
        /// hub found no conflict. Null: the hub calls <see cref="OnChosen"/> instead (which shows its own messages) and reports a
        /// refusal when it is not worn right after.
        /// </summary>
        public Func<string> PutOn;

        /// <summary>
        /// Set by the mod (optional, hub 1.1.0). Whether <see cref="PutOn"/> would work right here, without putting it on: null =
        /// yes, else why not (needs a wall, room on the floor ...). Leave null when it can only fail for rare reasons. Missions
        /// call it through <c>RtHub_CanApply</c> before choosing a restraint.
        /// </summary>
        public Func<string> CanPutOn;

        /// <summary>
        /// Set by the mod (recommended, hub 1.1.0). Take it off now, without a struggle - a mission removes it, its timer ran out,
        /// or the player holds the escape key (<c>Keys.ToggleBlocking</c>, held). Must leave the body as without the restraint.
        /// Null: missions cannot remove it and the escape key leaves it on.
        /// </summary>
        public Action TakeOff;

        /// <summary>
        /// Set by the mod (optional, hub 1.1.0). Values missions may read and change while it is worn (<c>RtHub_Get</c> /
        /// <c>RtHub_Set</c>), usually what + / - change. A mission's change is not stored in the player's config.
        /// </summary>
        public IReadOnlyList<RestraintSetting> Settings;

        // --- set by the hub ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Set by the hub. This restraint is worn and selected in the list: F and + / - act on it. Exactly one worn item is
        /// selected (it may be the game's handcuffs, then no restraint is).
        /// </summary>
        public bool Selected { get; internal set; }

        /// <summary>
        /// Set by the hub. The player holds F for this restraint: it is selected, no game menu or the ring is open, and she is
        /// not in an interaction area (closet, PC ... - F belongs to the area there). Feed it to your lock logic as "unlock key
        /// held" (with SfmKit's <c>LockMachine</c>: <c>machine.Tick(dt, entry.StruggleHeld &amp;&amp; machine.State == LockState.Locked, roll)</c>).
        /// </summary>
        public bool StruggleHeld { get; internal set; }

        /// <summary>
        /// Set by the hub (0.3.0). The + (1) or - (-1) key held right now for this restraint, else 0: for adjustments that run
        /// smoothly while the key is held (<c>value += HeldStep * speed * Time.deltaTime</c>) instead of in
        /// <see cref="TakeStep"/>'s steps. Only for the selected restraint, never while a menu or the ring is open.
        /// </summary>
        public int HeldStep { get; internal set; }

        internal int PendingStep;
        internal readonly Logic.TapOrHold Tap = new Logic.TapOrHold();
        internal int Shortcuts;      // bit k: number key k+1 holds the entry
        internal int RingIndex = -1; // index in the current ring array

        /// <summary>
        /// A + (1) or - (-1) step for this restraint since the last call, else 0 - call it once per frame from your
        /// <c>Update</c> and act on the result. Only the selected restraint gets steps, never while a menu or the ring is open.
        /// The keys are the ones printed "+" / "-" on the player's keyboard, the numpad keys and "=" on US layouts; holding one
        /// repeats (first after 0.4 s, then every 0.08 s). A step that is not taken is dropped when the selection changes.
        /// </summary>
        public int TakeStep()
        {
            int s = PendingStep;
            PendingStep = 0;
            return s;
        }

        internal string Name
        {
            get
            {
                try { return Label?.Invoke() ?? Id; }
                catch (Exception) { return Id; }
            }
        }

        internal bool Worn
        {
            get
            {
                try { return IsWorn != null && IsWorn(); }
                catch (Exception e) { HubController.Fail(Id + ".IsWorn", e); return false; }
            }
        }
    }

    /// <summary>
    /// The shared home of the restraint mods (SpreaderBar, WallRestraint, Yoke and yours): it owns their ring entries (on pages
    /// after the game's), the number shortcuts, the on-screen list of what she wears with a selected item, the keys F and
    /// + / - for that item, one input hook for what worn restraints block, the unlock gauge and the conflicts between
    /// restraints. A restraint mod declares a hard dependency and registers once:
    /// <code>
    /// [BepInPlugin("com.you.blindfold", "Blindfold", "1.0.0")]
    /// [BepInDependency(Hub.Guid, BepInDependency.DependencyFlags.HardDependency)]
    /// public sealed class Plugin : BasePlugin
    /// {
    ///     public override void Load() { ...; Hub.Register(entry); }
    /// }
    /// </code>
    /// What stays with the mod: its model and pose, its lock logic and its own save data (lock state etc.).
    /// </summary>
    public static class Hub
    {
        /// <summary>The hub's BepInEx GUID, for <c>[BepInDependency(Hub.Guid)]</c>.</summary>
        public const string Guid = Plugin.PluginGuid;

        /// <summary>The hub's version. A mod that needs a newer hub API can require it with a version <i>range</i>:
        /// <c>[BepInDependency(Hub.Guid, "^1.1.0")]</c> (1.1.0 up to, not including, 2.0.0). A bare <c>"1.1.0"</c> means exactly
        /// 1.1.0 to BepInEx 6: the mod would not load with any later hub.</summary>
        public const string Version = Plugin.PluginVersion;

        internal static readonly List<Restraint> All = new List<Restraint>();

        /// <summary>
        /// Register a restraint (once, from the plugin's <c>Load</c>; the hub loads first because of the dependency). From the
        /// next time the game builds its ring it has an entry, shortcuts are restored from <see cref="Restraint.ShortcutSaveKey"/>
        /// on load, and the hub starts calling the callbacks. Logs <c>[SFM-ACCEPT] register: &lt;Id&gt;</c>.
        /// </summary>
        /// <exception cref="ArgumentException">no <see cref="Restraint.Id"/>, <see cref="Restraint.IsWorn"/> or
        /// <see cref="Restraint.OnChosen"/>; or the id or ring value is already registered.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><see cref="Restraint.RingValue"/> outside 9000..9998.</exception>
        public static void Register(Restraint r)
        {
            if (r == null || string.IsNullOrEmpty(r.Id)) throw new ArgumentException("restraint needs an Id");
            if (r.RingValue < RingPages.ModValueStart || r.RingValue >= RingPages.PaddingValue)
                throw new ArgumentOutOfRangeException(nameof(r.RingValue), r.RingValue, "restraint " + r.Id + ": RingValue must be 9000..9998");
            if (r.IsWorn == null || r.OnChosen == null) throw new ArgumentException("restraint " + r.Id + " needs IsWorn and OnChosen");
            foreach (var other in All)
                if (other.Id == r.Id || other.RingValue == r.RingValue)
                    throw new ArgumentException("restraint " + r.Id + " (ring value " + r.RingValue + ") clashes with " + other.Id + " (" + other.RingValue + ")");
            All.Add(r);
            All.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // stable ring order whatever the load order
            Plugin.Logger.LogInfo("[SFM-ACCEPT] register: " + r.Id + " (ring value " + r.RingValue + ", holds " + r.Holds + ")");
        }

        /// <summary>The registered restraints, ordered by id (read-only view, e.g. to show what else is installed).</summary>
        public static IReadOnlyList<Restraint> Registered => All;

        /// <summary>
        /// A game menu or panel is open (inventory, shop, closet, PC, pause ...) or the ring is: keys belong to it. Updated by
        /// the hub once per frame (hub 0.3.0); false outside a stage.
        /// </summary>
        public static bool MenuOpen => HubController.MenuOpenNow;

        /// <summary>
        /// She stands in an interaction area (closet, PC, machines, shops ...): the game shows "F &lt;name&gt;" and F is the
        /// area's. Updated by the hub once per frame from the areas' own flag (hub 0.3.0); false outside a stage.
        /// </summary>
        public static bool InInteractArea => HubController.InInteractAreaNow;

        /// <summary>No menu, no ring, not in an interaction area: free keys for a restraint's own controls (hub 0.3.0).</summary>
        public static bool InputFree => !MenuOpen && !InInteractArea;

        /// <summary>Show a line in the game's message bar (top of the screen, the game's warning style). Safe to call any time;
        /// does nothing outside a stage.</summary>
        public static void Message(string text) => SfmKit.Game.GameUi.Message(text);
    }

    /// <summary>
    /// One value of a restraint that missions may read and change (<see cref="Restraint.Settings"/>), e.g. the spreader bar's
    /// width in cm. Hub 1.1.0.
    /// </summary>
    public sealed class RestraintSetting
    {
        /// <summary>Name used by scripts, lower case: <c>"width"</c>.</summary>
        public string Name;
        /// <summary>The unit, for documentation and the log: <c>"cm"</c>.</summary>
        public string Unit;
        /// <summary>The current value; called only while the restraint is worn.</summary>
        public Func<float> Get;
        /// <summary>Change it (only while worn): return null when done - clamped to what fits, read back with <see cref="Get"/> - or
        /// why not. Must not write the player's config.</summary>
        public Func<float, string> Set;
    }

    /// <summary>
    /// The save slot the game is writing or has just read, for <see cref="Restraint.OnSave"/> / <see cref="Restraint.OnLoad"/>:
    /// extra keys in the game's own save file (Easy Save 3, through the game's <c>SaveRapper</c>), which the game ignores
    /// without your mod. Only valid during the call.
    /// </summary>
    public sealed class SaveSlot
    {
        internal SaveSlot(string path) { Path = path; }

        /// <summary>The slot's file, e.g. <c>SaveData/1.sd</c> (diagnostics).</summary>
        public string Path { get; }

        public void Save(string key, bool value) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Save(key, value);
        public void Save(string key, int value) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Save(key, value);
        public void Save(string key, double value) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Save(key, value);
        public void Save(string key, string value) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Save(key, value);

        /// <summary>The value under <paramref name="key"/>, or <paramref name="fallback"/> when the save has none (older save).</summary>
        public bool Load(string key, bool fallback) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Load(key, fallback);
        public int Load(string key, int fallback) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Load(key, fallback);
        public double Load(string key, double fallback) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Load(key, fallback);
        public string Load(string key, string fallback) => ExposureUnnoticed2.Scripts.Base.SaveRapper.Load(key, fallback);
    }
}
