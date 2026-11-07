using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;

namespace RestraintHub
{
    /// <summary>
    /// What every item registered with the hub has: an id, a name, values missions may change, and hooks into the game's
    /// save. The two kinds are <see cref="Wearable"/> (on her body) and <see cref="Placeable"/> (an object put in the world).
    /// Fill in the fields before registering and keep the object.
    /// <para>
    /// <b>Threading and timing.</b> Everything happens on Unity's main thread. The hub's own <c>Update</c> runs every frame in
    /// game - in no fixed order relative to the mod's, so a value the hub sets can be one frame old when the mod reads it.
    /// Callbacks must be cheap (some run every frame) and must not throw: the hub catches and logs exceptions.
    /// </para>
    /// </summary>
    public abstract class HubItem
    {
        /// <summary>Required. Stable, unique id among all items, e.g. <c>"SpreaderBar"</c>: the panel orders items by it, it names
        /// the item in the log and is how missions address it (<c>RtHub_Apply("SpreaderBar")</c>). Never change it once released.</summary>
        public string Id;

        /// <summary>Recommended. The name shown in the panel, the worn list and messages. Called whenever it is shown, so a config
        /// change shows at once; keep it cheap. Null: <see cref="Id"/> is shown.</summary>
        public Func<string> Label;

        /// <summary>Optional. Values missions may read and change (<c>RtHub_Get</c> / <c>RtHub_Set</c>), usually what + / -
        /// change. A mission's change is not stored in the player's config.</summary>
        public IReadOnlyList<RestraintSetting> Settings;

        /// <summary>Optional. Called right after the game wrote a save slot: write your state with <see cref="SaveSlot.Save(string, bool)"/>
        /// etc. under keys in your mod's namespace (<c>"MyMod.Locked"</c>; never rename them once released). The system file is skipped.</summary>
        public Action<SaveSlot> OnSave;

        /// <summary>Optional. Called right after the game loaded a save slot, before the stage starts: read your keys back (with
        /// defaults for saves from before your mod).</summary>
        public Action<SaveSlot> OnLoad;

        /// <summary>Optional. The game's data was replaced without a load (a new game): take the item off / away without ceremony.</summary>
        public Action OnNewGame;

        internal string Name
        {
            get
            {
                try { return Label?.Invoke() ?? Id; }
                catch (Exception) { return Id; }
            }
        }
    }

    /// <summary>
    /// Something on her body (a spreader bar, a yoke, binders ...), handed to <see cref="Hub.Register(Wearable)"/> once from the
    /// mod's <c>Load</c>. The player puts it on in the hub's panel (<c>U</c>, "Wearables"); while it is worn the hub shows it in the
    /// worn list, routes F (struggle) and + / - to it when it is the selected one, refuses what it <see cref="Blocks"/>, and keeps
    /// out wearables that need a body part it <see cref="Holds"/>.
    /// <code>
    /// Hub.Register(new Wearable
    /// {
    ///     Id = "Blindfold", Label = () => "Blindfold", Holds = BodyParts.Neck,
    ///     IsWorn = () => machine.Worn, Blocks = () => Blocks.Running,
    ///     PutOn = () => machine.RequestLock() ? null : "it is already on", TakeOff = () => machine.Reset(),
    ///     Attempt = () => machine.Attempt,
    /// });
    /// </code>
    /// </summary>
    public sealed class Wearable : HubItem
    {
        // --- set by the mod -----------------------------------------------------------------------------------------------

        /// <summary>The body parts it holds. While <c>General.BlockEachOther</c> is on (the default; the player toggles it with
        /// <c>V</c>) the hub refuses a wearable that needs a part a worn one holds - the game's handcuffs hold
        /// <see cref="BodyParts.Arms"/>. With blocking off, two may pose the same bones: whichever writes them last in
        /// <c>LateUpdate</c> wins.</summary>
        public BodyParts Holds;

        /// <summary>Required. Whether it is on - from the moment it starts closing until it is fully off (SfmKit's
        /// <c>LockMachine.Worn</c>). Polled every frame; one that turns worn by itself (a loaded save) appears in the list.</summary>
        public Func<bool> IsWorn;

        /// <summary>Optional. What the player may not do while it is worn; called every input frame while worn. The hub applies the
        /// union over all worn wearables. Null: nothing.</summary>
        public Func<Blocks> Blocks;

        /// <summary>Required. Put it on - chosen in the panel, by a mission (<c>RtHub_Apply</c>) or through a placeable's
        /// <see cref="Placeable.InUse"/>; only called while it is off and nothing worn holds a part it needs. Return null when it
        /// started closing, else why not in a few plain words ("no wall in front of her"): the hub shows it to the player.</summary>
        public Func<string> PutOn;

        /// <summary>Optional. Whether <see cref="PutOn"/> would work right here, without putting it on: null = yes, else why not.
        /// Missions ask it through <c>RtHub_CanApply</c>. Leave null when it can only fail for rare reasons.</summary>
        public Func<string> CanPutOn;

        /// <summary>Required. Take it off now, without a struggle - a mission removes it, its timer ran out, or the player holds the
        /// escape key (<c>Keys.ToggleBlocking</c>, held). Must leave the body as without it.</summary>
        public Action TakeOff;

        /// <summary>Optional. Progress of the current struggle attempt, 0..1, shown in the game's "Unlocking..." gauge while it is
        /// selected and above 0. Null: no gauge.</summary>
        public Func<float> Attempt;

        /// <summary>Optional. Called once per input frame while worn, after the hub cleared what <see cref="Blocks"/> forbids -
        /// change the <see cref="InputData"/> for anything the flags do not cover.</summary>
        public Action<InputData> OnInput;

        /// <summary>Optional. A few words for the worn list next to "F" on the selected row, usually what + / - do: <c>"+/- width"</c>.</summary>
        public string Controls;

        /// <summary>Optional. A short tap of F (released within 0.3 s) while selected - a second use of the key. With it set,
        /// <see cref="StruggleHeld"/> turns true only once F is held longer than a tap.</summary>
        public Action OnTap;

        // --- set by the hub ------------------------------------------------------------------------------------------------

        /// <summary>Set by the hub. Worn and selected in the list: F and + / - act on it.</summary>
        public bool Selected { get; internal set; }

        /// <summary>Set by the hub. The player holds F for it: selected, no menu open, not in an interaction area. Feed it to your
        /// lock logic as "unlock key held". False while a mission forbids struggling.</summary>
        public bool StruggleHeld { get; internal set; }

        /// <summary>Set by the hub. + (1) or - (-1) held right now for it, else 0 (only the selected one, never in a menu).</summary>
        public int HeldStep { get; internal set; }

        internal int PendingStep;
        internal readonly Logic.TapOrHold Tap = new Logic.TapOrHold();
        /// <summary>False for a placeable's <see cref="Placeable.InUse"/>: not listed in the panel's Wearables.</summary>
        internal bool InPanel = true;

        /// <summary>A + (1) or - (-1) step since the last call, else 0 - call it once per frame and act on the result. Only the
        /// selected wearable gets steps; holding a key repeats (first after 0.4 s, then every 0.08 s).</summary>
        public int TakeStep()
        {
            int s = PendingStep;
            PendingStep = 0;
            return s;
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
    /// An object put into the world that she can be put into or use (a hogtie frame, a wall fitting ...), handed to
    /// <see cref="Hub.Register(Placeable)"/> once from the mod's <c>Load</c>. The hub keeps the placed ones: the player places one
    /// from the panel (<c>U</c>, "Placeables") where <see cref="FindSpot"/> says, the hub remembers its map, pose and
    /// <see cref="Placed.Data"/>, saves them with the game, and shows them whenever their map is loaded: it makes a root object at
    /// the pose, lets the mod build its model under it (<see cref="Show"/>) and adds one of the game's interaction areas, so
    /// "F &lt;name&gt;" appears when she stands at it. F puts on <see cref="InUse"/> there (<see cref="UsedAt"/> = that object), or
    /// calls <see cref="Use"/>. The panel's "Pick up" takes the nearest one on this map.
    /// <code>
    /// Hub.Register(new Placeable
    /// {
    ///     Id = "Stocks", Label = () => "Stocks",
    ///     FindSpot = () => PlaceSpot.InFront(1f),
    ///     Show = placed => Model.Build(placed.Root),
    ///     InUse = stocksWearable, // its PutOn reads placeable.UsedAt
    /// });
    /// </code>
    /// </summary>
    public sealed class Placeable : HubItem
    {
        // --- set by the mod -----------------------------------------------------------------------------------------------

        /// <summary>Optional. Where one would go now - called when the player places it: a spot, or <see cref="PlaceSpot.No"/> with
        /// why not in a few plain words ("no room here"). Null: on the floor 1 m in front of her, facing her
        /// (<see cref="PlaceSpot.InFront"/>).</summary>
        public Func<PlaceSpot> FindSpot;

        /// <summary>Required. Build the model of one placed object under <see cref="Placed.Root"/> (local coordinates: the root has
        /// the placed pose). Called when it is placed and every time its map is loaded. The hub destroys the root when the map
        /// unloads or the object is picked up.</summary>
        public Action<Placed> Show;

        /// <summary>Optional. Called just before the hub destroys a shown object's root (map unloaded, picked up, new game): drop
        /// references to it.</summary>
        public Action<Placed> Hide;

        /// <summary>Optional. The text after "F" at a placed object. Null: <see cref="HubItem.Label"/>.</summary>
        public Func<Placed, string> UseText;

        /// <summary>Optional. The wearable that holds her while she is in it - registered together with the placeable (its id must
        /// be unique too). F at a placed object puts it on (the hub's conflict check, then its <see cref="Wearable.PutOn"/> with
        /// <see cref="UsedAt"/> set; a refusal is shown). Listed in the worn list with struggle and blocks as usual, not in the
        /// panel's Wearables. Missions may put it on too (<see cref="UsedAt"/> null then).</summary>
        public Wearable InUse;

        /// <summary>Optional. What F at a placed object does instead of putting on <see cref="InUse"/>: null when done, else why
        /// not (shown).</summary>
        public Func<Placed, string> Use;

        /// <summary>How many may stand on one map (default 1). Placing one more is refused.</summary>
        public int MaxPerMap = 1;

        /// <summary>The interaction area's centre in the placed object's local coordinates (default: the pose itself), and its
        /// radius in metres (default 0.5; the game's own areas use 0.4).</summary>
        public Vector3 AreaCenter = Vector3.zero;
        public float AreaRadius = 0.5f;

        // --- set by the hub ------------------------------------------------------------------------------------------------

        /// <summary>Set by the hub. The placed object whose F put on <see cref="InUse"/>, while it is worn; null when it was put on
        /// another way (a mission). Read it in <see cref="InUse"/>'s <c>PutOn</c> to put her in the right place.</summary>
        public Placed UsedAt { get; internal set; }

        /// <summary>Set by the hub. The placed ones on the current map (shown), in placing order.</summary>
        public IReadOnlyList<Placed> Here => _here;

        internal readonly List<Placed> _here = new List<Placed>();

        internal string TextFor(Placed p)
        {
            try { return UseText?.Invoke(p) ?? Name; }
            catch (Exception e) { HubController.Fail(Id + ".UseText", e); return Name; }
        }
    }

    /// <summary>Where a placeable would go (<see cref="Placeable.FindSpot"/>): a pose in the world, the mod's own data, or why it
    /// can't go here.</summary>
    public sealed class PlaceSpot
    {
        public Vector3 Position;
        /// <summary>Its rotation; the model's forward is its local +Z.</summary>
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>The mod's own data for this object (saved with it, <see cref="Placed.Data"/>).</summary>
        public string Data = "";
        /// <summary>Why it can't go here, else null.</summary>
        public string Refused;

        /// <summary>A refusal: <c>PlaceSpot.No("no room here")</c>.</summary>
        public static PlaceSpot No(string why) => new PlaceSpot { Refused = why ?? "not here" };

        /// <summary><paramref name="metres"/> in front of her at her feet's height, turned to face her (its +Z towards her).
        /// <see cref="No"/> outside a stage.</summary>
        public static PlaceSpot InFront(float metres)
        {
            var player = ExposureUnnoticed2.Object3D.Player.Scripts.PlayerController.Instance;
            if (player == null) return No("not in a stage");
            var t = player.transform;
            Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            return new PlaceSpot { Position = t.position + forward * metres, Rotation = Quaternion.LookRotation(-forward, Vector3.up) };
        }
    }

    /// <summary>One placed object of a <see cref="Placeable"/> (on any map; shown while its map is loaded).</summary>
    public sealed class Placed
    {
        internal Placed(Placeable item, Logic.PlacedRecord record) { Item = item; Record = record; }

        internal readonly Logic.PlacedRecord Record;
        internal GameObject Area;

        /// <summary>The placeable it belongs to.</summary>
        public Placeable Item { get; }
        /// <summary>Its map (a <c>StageType</c> name, e.g. <c>"Apart"</c>).</summary>
        public string Stage => Record.Stage;
        public Vector3 Position => new Vector3(Record.Position[0], Record.Position[1], Record.Position[2]);
        public Quaternion Rotation => new Quaternion(Record.Rotation[0], Record.Rotation[1], Record.Rotation[2], Record.Rotation[3]);
        /// <summary>The mod's own data for it, from <see cref="PlaceSpot.Data"/>; may be changed, saved with the game.</summary>
        public string Data { get => Record.Data; set => Record.Data = value ?? ""; }
        /// <summary>The root object at its pose while it is shown, else null. Build the model under it in <see cref="Placeable.Show"/>.</summary>
        public Transform Root { get; internal set; }
    }

    /// <summary>
    /// The shared home of the restraint mods: one panel (<c>U</c>) to put on wearables and place placeables, the on-screen list of
    /// what she wears with a selected item, the keys F and + / - for it, one input hook for what worn items block, the unlock
    /// gauge, conflicts between items, an escape key, and functions for Custom Missions v2 scripts. A mod declares a hard
    /// dependency and registers once:
    /// <code>
    /// [BepInPlugin("you.SFM.Blindfold", "Blindfold", "1.0.0")]
    /// [BepInDependency(Hub.Guid, "^2.0.0")]
    /// public sealed class Plugin : BasePlugin
    /// {
    ///     public override void Load() { ...; Hub.Register(wearable); }
    /// }
    /// </code>
    /// What stays with the mod: its model and pose, its lock logic and its own save data (placed objects are kept by the hub).
    /// </summary>
    public static class Hub
    {
        /// <summary>The hub's BepInEx GUID, for <c>[BepInDependency(Hub.Guid, "^2.0.0")]</c>.</summary>
        public const string Guid = Plugin.PluginGuid;

        /// <summary>The hub's version. Require the API you build against with a version <i>range</i>: <c>"^2.0.0"</c> (2.0.0 up to,
        /// not including, 3.0.0). A bare <c>"2.0.0"</c> means exactly 2.0.0 to BepInEx 6: the mod would not load with any later hub.</summary>
        public const string Version = Plugin.PluginVersion;

        internal static readonly List<Wearable> All = new List<Wearable>();
        internal static readonly List<Placeable> Places = new List<Placeable>();

        /// <summary>Register a wearable (once, from the plugin's <c>Load</c>). Logs <c>[SFM-ACCEPT] register: &lt;Id&gt;</c>.</summary>
        /// <exception cref="ArgumentException">no <see cref="HubItem.Id"/>, <see cref="Wearable.IsWorn"/>, <see cref="Wearable.PutOn"/>
        /// or <see cref="Wearable.TakeOff"/>; or the id is already registered.</exception>
        public static void Register(Wearable w)
        {
            Check(w);
            if (w.IsWorn == null || w.PutOn == null || w.TakeOff == null) throw new ArgumentException("wearable " + w.Id + " needs IsWorn, PutOn and TakeOff");
            All.Add(w);
            All.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // stable panel order whatever the load order
            Plugin.Logger.LogInfo("[SFM-ACCEPT] register: " + w.Id + " (wearable, holds " + w.Holds + (w.InPanel ? "" : ", in use of a placeable") + ")");
        }

        /// <summary>Register a placeable, and its <see cref="Placeable.InUse"/> wearable if it has one. Logs
        /// <c>[SFM-ACCEPT] register: &lt;Id&gt;</c>. Objects of it in a save stay there even while the mod is not installed.</summary>
        /// <exception cref="ArgumentException">no <see cref="HubItem.Id"/> or <see cref="Placeable.Show"/>; or an id is already
        /// registered.</exception>
        public static void Register(Placeable p)
        {
            Check(p);
            if (p.Show == null) throw new ArgumentException("placeable " + p.Id + " needs Show");
            if (p.InUse != null)
            {
                p.InUse.InPanel = false;
                Register(p.InUse);
            }
            Places.Add(p);
            Places.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Plugin.Logger.LogInfo("[SFM-ACCEPT] register: " + p.Id + " (placeable" + (p.InUse == null ? "" : ", in use: " + p.InUse.Id) + ")");
        }

        private static void Check(HubItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) throw new ArgumentException("an item needs an Id");
            foreach (var other in All) if (other.Id == item.Id) throw new ArgumentException("item id " + item.Id + " is already registered");
            foreach (var other in Places) if (other.Id == item.Id) throw new ArgumentException("item id " + item.Id + " is already registered");
        }

        /// <summary>The registered wearables (also placeables' in-use ones), ordered by id.</summary>
        public static IReadOnlyList<Wearable> Wearables => All;

        /// <summary>The registered placeables, ordered by id.</summary>
        public static IReadOnlyList<Placeable> Placeables => Places;

        /// <summary>A game menu or panel is open (inventory, shop, closet, PC, pause, the hub's panel ...) or the ring is: keys belong to
        /// it. Updated by the hub once per frame; false outside a stage.</summary>
        public static bool MenuOpen => HubController.MenuOpenNow;

        /// <summary>She stands in an interaction area (closet, PC, machines, shops ...): the game shows "F &lt;name&gt;" and F is the
        /// area's. Updated by the hub once per frame; false outside a stage.</summary>
        public static bool InInteractArea => HubController.InInteractAreaNow;

        /// <summary>No menu, no ring, not in an interaction area: free keys for an item's own controls.</summary>
        public static bool InputFree => !MenuOpen && !InInteractArea;

        /// <summary>Show a line in the game's message bar (top of the screen, the game's warning style). Safe to call any time;
        /// does nothing outside a stage.</summary>
        public static void Message(string text) => SfmKit.Game.GameUi.Message(text);
    }

    /// <summary>
    /// One value of an item that missions may read and change (<see cref="HubItem.Settings"/>), e.g. the spreader bar's width in cm.
    /// </summary>
    public sealed class RestraintSetting
    {
        /// <summary>Name used by scripts, lower case: <c>"width"</c>.</summary>
        public string Name;
        /// <summary>The unit, for documentation and the log: <c>"cm"</c>.</summary>
        public string Unit;
        /// <summary>The current value; called only while the item is worn.</summary>
        public Func<float> Get;
        /// <summary>Change it (only while worn): return null when done - clamped to what fits, read back with <see cref="Get"/> - or
        /// why not. Must not write the player's config.</summary>
        public Func<float, string> Set;
    }

    /// <summary>
    /// The save slot the game is writing or has just read, for <see cref="HubItem.OnSave"/> / <see cref="HubItem.OnLoad"/>:
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
