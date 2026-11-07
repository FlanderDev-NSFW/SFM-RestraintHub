# RestraintHub

> Developer notes (written in [sfm-modkit](https://github.com/FlanderDev-NSFW), where the mod is developed with
> an in-game test bridge; paths like `./tools/dev.ps1` and `docs/knowledge/` refer to that kit): how it works, settings in detail, diagnostics and the test history. The player readme (shipped in the
> release zip) is [`README.md`](../README.md).
>
> **1.0.0 (2026-10-27): first release** (F3M). Plugin GUID `FlanderDev.SFM.RestraintHub` (was `com.local.restrainthub`), so the
> config file is `BepInEx/config/FlanderDev.SFM.RestraintHub.cfg`. The release zips no longer include RestraintHub (it is its own download).

The shared home of the restraint mods for SecretFlasherManaka (BepInEx 6 IL2CPP). Restraint mods depend on it (a separate
download they list as a dependency), and so can yours: the hub gives every wearable and placeable a place in its panel (`U`),
the on-screen list, the keys F and + / -, input blocking, conflict checks and Custom Missions functions, so a restraint mod only
has to do its own lock and its visible part. Single-player/offline only.

**Status 2.0.0:** the panel, the new API and placed objects at L3 (a test plugin, then the ported restraint mods; see
Verification, Known limits). Contents: [For players](#for-players) · [For modders](#for-modders-20) ·
[Config](#config-bepinexconfigflanderdevsfmrestrainthubcfg) · [Verification](#verification)

## For players

- **The panel (`Keys.OpenPanel`, U)** lists the installed items in two tabs: **Wearables** (on her body) and **Placeables**
  (objects put into the world). `Q` / `E` or the last row switch tabs, the mouse wheel scrolls, `U` or `Esc` closes. Clicking a
  wearable puts it on (or selects it, when it is already on); clicking a placeable places one where the mod says (in front of
  her, on the wall she faces ...); with one on this map it shows `[1 here]` and a "Pick up" row (the nearest one).
  The panel closes after something went on, was placed or picked up; a refusal is shown in the message bar and the panel stays.
- **Placed objects** stay on their map and in the save. At one, the game's own prompt shows "F <name>": F puts her into it
  (the placeable's in-use wearable - worn list, struggle, escape as usual). While she is in it, its prompt is off, so F is the
  struggle.
  It is the game's own dildo choice panel, rebuilt (see "How the panel works"); the game keeps running while it is open.
- **What she wears:** a small list directly above the head preview (bottom right), in the game's own style: one row per worn
  item (the game's handcuffs included), the selected one marked `>` with its keys, and a first row naming the select key when more
  than one is worn. A newly worn item becomes the selected one.
- **`B`** selects the next worn item. **Only the selected one reacts:** holding **F** struggles with it (with the game's handcuffs
  selected, F goes to them), **+ / -** adjust it. Menus and interaction areas (closet, PC ...) keep their keys.
- **Conflicts:** two items that hold the same body part (neck, arms, legs, her place) cannot be worn together - the second is
  refused ("Can't use the Wall Restraint: the Yoke holds her neck and arms."). **`V`** switches this off and on (kept in the
  config); off, items may overlap and the one that poses a body part last wins it, and they no longer refuse her animations
  (hand actions, pose skills, peeing - running, walking and crouching stay blocked).
- **Blocking:** while items are worn, what they take away is refused - hand actions and poses with "Can't do that with the
  <item> on.". Shift + move still ends a pose. The game's "Unlocking..." gauge shows the selected item's struggle.
- **Escape:** holding **`V`** for `Keys.EscapeHoldSeconds` (3 s) takes off every worn item, also ones a mission locked.
- **Missions:** with Custom Missions v2 installed, mission scripts get `RtHub_*` functions and events ([Missions.md](Missions.md)).

## For modders (2.0)

### What the hub does, what your mod does

| The hub (you get it by registering) | Your mod |
|---|---|
| a row in the panel, putting on / placing / picking up from there, the refusal message | `PutOn` (wearables), `FindSpot` (placeables) - return why not |
| placed objects: kept per map, saved with the game, shown when their map loads, an interaction area "F <name>" each, picking up | its model under the hub's root (`Show` / `Hide`), what F does (`InUse`, or `Use`) |
| the list over the head preview, selection (`B`), which wearable F and + / - go to | read `StruggleHeld`, call `TakeStep()` / read `HeldStep` |
| refusing wearables that hold the same body part (unless the player turned that off) | say which parts it holds (`Holds`) |
| clearing the input a worn item forbids, the refusal message | say what it forbids (`Blocks`), or adjust input in `OnInput` |
| the "Unlocking..." gauge for the selected wearable | report the attempt's progress (`Attempt`) |
| a short tap of F as a second key, separate from the struggle | act on it (`OnTap`) |
| the escape key and mission removal | take it off at once (`TakeOff`) |
| the save hooks: right after the game writes / reads a slot, a `SaveSlot` for your keys | `OnSave`, `OnLoad` |
| noticing a new game replacing the game data without a load | take it off / away (`OnNewGame`) |
| whether a menu (the panel too) or the ring is open, whether she stands in an interaction area | read `Hub.MenuOpen`, `Hub.InInteractArea`, `Hub.InputFree` |
| Custom Missions v2 functions and events for wearables | `CanPutOn`, `Settings` (optional) |
| - | the lock / struggle logic (SfmKit's `LockMachine` recommended), the model, the pose, what it saves |

### Quick start

```csharp
[BepInPlugin("You.SFM.Blindfold", "Blindfold", "1.0.0")]
[BepInDependency(Hub.Guid, "^2.0.0")] // a range: a bare "2.0.0" would mean exactly 2.0.0
public sealed class Plugin : BasePlugin
{
    internal static Wearable Entry;

    public override void Load()
    {
        ClassInjector.RegisterTypeInIl2Cpp<BlindfoldController>();
        AddComponent<BlindfoldController>();
        Entry = new Wearable
        {
            Id = "Blindfold", Label = () => "Blindfold", Holds = BodyParts.None, Blocks = () => Blocks.Running,
            IsWorn = () => BlindfoldController.Machine.Worn,
            PutOn = () => BlindfoldController.Machine.RequestLock() ? null : "it is already on",
            TakeOff = () => BlindfoldController.Machine.Reset(),
            Attempt = () => BlindfoldController.Machine.Attempt,
            Controls = "+/- tightness",
            OnSave = slot => slot.Save("Blindfold.Locked", BlindfoldController.Machine.Worn),
            OnLoad = slot => BlindfoldController.Machine.Restore(slot.Load("Blindfold.Locked", false), 1f),
            OnNewGame = () => BlindfoldController.Machine.Reset(),
        };
        Hub.Register(Entry);
    }
}

// In the controller's Update:
//   int step = Plugin.Entry.TakeStep();                       // +1 / -1 / 0, only while selected
//   machine.Tick(Time.deltaTime, Plugin.Entry.StruggleHeld && machine.State == LockState.Locked, Random.value);
//   if (Hub.InputFree) { ... your own keys ... }              // no menu, panel or interaction area
```

A placeable registers the same way. The hub keeps its placed objects (map, pose, your data string), saves them, and shows them
whenever their map is loaded: a root object at the pose, your model built under it by `Show`, and one of the game's interaction
areas. F there puts on `InUse` - the wearable that holds her while she is in it (registered with it, shown in the worn list,
not in the Wearables tab) - with `UsedAt` set to that object:

```csharp
Placeable stocks = null;
stocks = new Placeable
{
    Id = "StocksFrame", Label = () => "Stocks",
    FindSpot = () => PlaceSpot.InFront(1f),       // or your own check: PlaceSpot.No("no room here")
    Show = placed => StocksModel.Build(placed.Root),   // local coordinates: the root has the placed pose
    Hide = placed => { },                          // optional: drop references before the root is destroyed
    AreaCenter = new Vector3(0f, 0f, 0.4f), AreaRadius = 0.5f, MaxPerMap = 1,
    InUse = new Wearable
    {
        Id = "Stocks", IsWorn = ..., TakeOff = ..., Holds = ..., Blocks = ...,
        // stocks.UsedAt: the placed one whose F was pressed; null when a mission puts it on (then: where she stands)
        PutOn = () => stocks.UsedAt != null ? machine.LockAt(stocks.UsedAt.Position, stocks.UsedAt.Rotation) : machine.LockHere(),
    },
};
Hub.Register(stocks);
```

`PlaceSpot.Data` / `Placed.Data` carry what you need to rebuild it (WallRestraint keeps the knee heights the wall allows there);
`Placeable.Here` lists the placed ones on the current map. The hub frees `UsedAt` when `InUse` comes off.

### Project setup
- **In this repo:** a mod repository checked out in `mods/<Mod>` sets `<SfmUsesRestraintHub>true</SfmUsesRestraintHub>`
  (`build/SfmMod.targets` compiles against `../RestraintHub`, `lib/RestraintHub` or the installed hub). `deploy|cycle|package`
  bring the hub along. SfmKit's types are `internal`, so your copy and the hub's do not clash.
- **Outside this repo:** reference `BepInEx\plugins\RestraintHub\RestraintHub.dll` with `Private=false` and require the hub.
- **API:** `Hub`, `HubItem`, `Wearable`, `Placeable`, `RestraintSetting`, `SaveSlot`, `BodyParts`, `Blocks`. `Plugin`,
  `HubController` and `Missions` are public only for the dev bridge and may change. Full XML docs in `Hub.cs`
  (`RestraintHub.xml` next to the DLL).

### Changes from 1.x (a clean break)
- `Restraint` is now `Wearable`; `RingValue`, `ShortcutSaveKey`, `OnShortcutsMissing` and `OnChosen` are gone - the ring menu and
  number keys are not used any more (they conflicted with other ring mods). `PutOn` and `TakeOff` are required; `PutOn`
  returns null or why not, and the hub shows the reason.
- New: `Placeable` (`FindSpot`, `Show`, `Hide`, `UseText`, `InUse`, `Use`, `MaxPerMap`, `AreaCenter` / `AreaRadius`;
  hub-set `UsedAt`, `Here`), `PlaceSpot`, `Placed`, `Hub.Register(Placeable)`, `Hub.Wearables` / `Hub.Placeables` (was
  `Hub.Registered`). Hogtie and WallRestraint are placeables now; their wearables keep the ids `Hogtie` / `WallRestraint`.
- Save data under the old shortcut keys is simply ignored.

### Rules of thumb
- **Timing:** everything is on Unity's main thread. The hub's `Update` and yours run in no fixed order, so `StruggleHeld` and
  steps can be a frame late - harmless. Register once from `Load`.
- **Callbacks must not throw** (they run in the hub's frame and input hook). The hub catches and logs, but don't rely on it.
- **Overlap is possible.** The player can switch blocking off (`V`): your wearable may be worn with one that poses the same
  bones. Pose in `LateUpdate`, place your model from the drawn bones in `Camera.onPreCull`, and expect the other pose to win
  sometimes. Blocking off also lifts `Blocks.HandActions`, `PoseSkills` and `Pee` (`Rules.Animations`).
- **Pose skills:** `Blocks.PoseSkills` stops new ones; end a running one yourself when yours goes on if you pose the same bones
  (`pca.PlayerAction.ChangeAction(ActionType.None)`, as Yoke does).
- **Your own save data** goes in your own keys through `OnSave` / `OnLoad`. Write the player's config only on the player's action.
- **Keys:** the hub owns `U`, `B`, `V`, F and + / - for restraints. For your own keys, check the game's bindings first:
  `game call RestraintHub.HubController KeysInUse 'H P Y'`.

### How the panel works (`HubPanel.cs`)
- `InGameUiManager.Instance.Open(AssetReferencer.Instance.ChooseDildoPanel, false)` opens the game's dildo choice panel under
  `InGameCanvas/MiddleCloneLayer`; the game then shows the cursor, holds the player's input, closes it on Escape and keeps
  running (`Time.timeScale` 1) [observed 2026-11-07].
- Rebuilt on open: the title's and buttons' `CommonTextView` removed (it would put the localised text back in `Start`), the
  button list (`ButtonGroupManager` + `VerticalLayoutGroup`) moved into a `ScrollRect` with a `RectMask2D` between the title and
  Close, the prefab's first button kept as the row template (40 high instead of 55, font 20 instead of 25), every row with a new
  `Button.onClick`. The box keeps the prefab's size: its open animation sets it (a bigger size was overwritten).
- Rows are refreshed every 0.25 s (state notes like `[on]`, `[locked 1:23]`, `[1 here]`).
- It opens in interaction areas too (she may stand at a placed object to pick it up), not over another menu or the ring.

### How placed objects work (`Placements.cs`, `Logic/PlacementStore.cs`)
- The store holds every placed object of every map: placeable id, map (`StageType` name), position, rotation, data. Saved in the
  slot under `RestraintHub.Placed` (one line each, tab separated, the data URL-escaped); objects of a mod that is not installed
  stay in the save and are not shown.
- The current map is `InGameManager.Instance.CurrentStageController`; when it changes (or a load / new game marks the shown ones
  stale, or a root was destroyed with its scene) all shown objects are hidden and the map's ones shown again. Roots are moved
  into the stage's scene, so they go away with it.
- The interaction area is a copy of the game's dropped-coat area (`AssetReferencer.DroppedPrefabs[0]`, child `InteractArea`),
  `type` set to `DroppedClothes` and `Initialize`d with our callback and text: with that type F calls the callback and nothing
  else. A plain `AddComponent<InteractAreaController>` throws in `Initialize` (it needs the ring effect). Details:
  `docs/knowledge/api-notes/ui-and-input.md` "Interact areas" [observed 2026-11-07].
- F: the hub's conflict check and `InUse.PutOn` (the same path as the panel's put-on, with `UsedAt` set); on success the area
  is switched off (deactivated: the game unregisters it, so `Hub.InInteractArea` is false and F is the struggle). When `InUse`
  is no longer worn, `UsedAt` is cleared and the area switched on again.

### Testing with the bridge
`game get RestraintHub.HubController::PanelStatus` ("open, tab Wearables, rows: ..."), `::Status` (the worn list),
`::PlacedStatus` ("Residence: HogtieDevice (-25.73, -0.11, -117.02) (2 in all)", `*` = she is in it),
`key U`, `key E`, `game click "<row label>"`, the scroll position:
`game set '/InGameManager/InGameCanvas/MiddleCloneLayer/RestraintHubPanel/Contents/RestraintHubScroll::[UnityEngine.UI.ScrollRect].verticalNormalizedPosition' 0`.
Log markers: `register:`, `panel:`, `put on:`, `placed:`, `picked up:`, `used:`, `free:`, `stage <Map>:`, `refused:`, `list:`, `select:`, `conflicts:`, `tap:`,
`save:` / `load:`, `new game:`, `escape:`, `missions:`, `mission '<name>':`, `event:`, `warning`.

## Config (`BepInEx/config/FlanderDev.SFM.RestraintHub.cfg`)
| Key | Default | Meaning |
|---|---|---|
| Keys.OpenPanel | U | Input System key name that opens and closes the panel |
| Keys.SelectNext | B | Input System key name that selects the next worn restraint |
| Keys.ToggleBlocking | V | tapped: switches General.BlockEachOther (K is the game's Costume key; J, L, M, I, O are taken too); held: the escape |
| Keys.EscapeHoldSeconds | 3 | how long to hold Keys.ToggleBlocking to take off all restraints (1-10) |
| General.BlockEachOther | true | restraints that hold the same body part block each other, and restraints refuse hand actions, pose skills and peeing |
| General.WarnMissions | true | show the Custom Missions version / connection warning in game (the log always has it) |
| Debug.Disable | 0 | bit 1 = block no input (negative control) |

## Verification
- L1: `./tools/dev.ps1 test RestraintHub` (50: blocking, conflicts and their wording, the worn list and its selection, tap vs
  hold, the escape hold time, mission locks: owners, option updates, timers and pauses, thread / mission / keep ends, never-worn
  and came-off locks, the save format; placed objects: per-map counts, save text round trip with tabs / newlines / exact
  floats, broken lines skipped);
  `test SfmDev` (deploy / package dependencies, incl. the real projects).
- L3 0.1.0 (2026-10-24, dev copy, first save; SpreaderBar 0.7.0, WallRestraint 1.2.0, Yoke 0.2.0):
  - load: "3 restraint entries from index 32 (page 5; 31 game entries, 1 empty)", shortcuts restored from the pre-hub save keys
    (key 1 Wall Restraint, key 4 Spreader Bar); ring page 5 shows Spreader Bar / Wall Restraint / Yoke and "-" slots (screenshot).
  - key 4 -> bar on, list "Spreader Bar"; Yoke chosen in the ring (select + click) -> list "Spreader Bar; > Yoke", key 1 kept.
  - `-` with the yoke selected: yoke 1 -> 0.98, bar unchanged; `B` -> bar selected; `-`: bar 21.36 -> 20.96 cm, yoke unchanged.
  - F held with the bar selected: only the bar came off, the yoke had no attempt; the list moved on to the yoke.
  - key 1 with the yoke on: "Can't use the Wall Restraint: the Yoke holds her neck and arms." (screenshot).
  - Yoke's blocking through the hub: Dildo refused with the message; Armpit Squat refused, with `Debug.Disable = 1` it starts
    (negative control); Shift + W then ended the pose and `IsDash` stayed false.
  - Game handcuffs ("Behind the body") + bar: list "Handcuffs; > Spreader Bar"; F with the bar selected: bar attempt 0.37,
    handcuff progress 0; after `B`: handcuff progress 0.16, bar 0. Struggled out of both.
  - Residence house wall: key 1 -> on the wall; key 4 refused (legs); `-` / `+` changed her height ("height: knees at 0.576 m");
    F freed her.
  - Save at home: "save: shortcuts SpreaderBar=8 WallRestraint=1 Yoke=0"; restart + load: restored.
  - Key check: "select key B is not bound by the game".
  - Overlap toggle: keys checked ("overlap toggle key V is not bound by the game"; the first choice K was the game's Costume key,
    caught by the check). Game handcuffs on, blocking on: yoke chosen in the ring -> "refused: Yoke conflicts with handcuffs (Arms)";
    `V` -> "conflicts: off", message shown; yoke chosen again -> locked, list "Handcuffs; > Yoke", header "(overlap on)"
    (screenshot); `V` -> on again; struggled out of both.
  - Animations follow `V` (0.2.0, 2026-10-25, user request): binders on, blocking on -> Armpit Squat (key 5) "refused: an action
    blocked by binders"; yoke on, blocking off -> no refusal, the squat runs with her arms still in the yoke (`CurrentAction`
    WakimiseCrouch, screenshot). While crawling the game itself does not start the squat.
  - List over the head preview (user request): bar on -> one row just above the preview, centred on it; yoke on too -> three
    rows, the bottom row in the same place, the others above it (screenshots).
  - Docs / template (2026-10-24): RestraintTemplate built against the hub, deployed with it (hub folder with `RestraintHub.xml`,
    92 documented members), checked in game and removed again - see `docs/mods/RestraintTemplate.md`.
  - Ring keys 2-8 (user report: the restraint page only with key 1): `maxRingIndexSkill` was 3, key 2 showed 4 pages and X wrapped
    to page 1; set to 4 by hand it showed page 5. With the fix ("number-key rings page up to 5"): key 2 and key 3 reached page 5
    (Spreader Bar, Yoke there), released over their own entries (shortcuts unchanged); the limit stayed after two map changes. `selftest --game` green, dev copy state unchanged.

- L3 0.3.0 (2026-10-25, Apartment, first save; Binders 0.1.1, NippleTether 0.2.0, SpreaderBar 0.8.2, WallRestraint 1.3.1,
  Yoke 0.3.1 - their save hooks, new-game tracking and SpreaderBar's own menu / area checks moved here):
  - all six registered, load report clean; load: "load: locked=False" from Binders, NippleTether, SpreaderBar, Yoke via `OnLoad`.
  - `OnTap`: NippleTether, F tapped twice -> "tap: NippleTether", point mode on, off; held 3.6 s -> struggled free (no tap).
  - `HeldStep`: + held -> the tether's length ran 119.6 -> 317 cm smoothly at its 0.4 m/s; config written once on release.
  - Save at home (closet) with the yoke and the tether on: "save: locked=True" from both, False from the others; restart + load
    -> "load: locked=True" for both, list "Nipple Tether; Yoke". Taken off and saved again afterwards.
  - `OnNewGame`: not triggered in game - "Return to Title" keeps the same game data object (seen), so it comes with New Game;
    the same pointer check the mods did themselves before.

- L3 1.1.0 (2026-11-02, first save, Custom Missions v2 2.4.0; Binders 1.1.0, Hogtie 0.2.0, NippleTether 1.1.0, SpreaderBar
  1.1.0, WallRestraint 1.5.0, Yoke 1.1.0; two copies of the test mission, A and B):
  - load: "connected to Custom Missions v2 2.4.0, RtHub_* functions registered (API 1)"; the missions see
    `FunctionExists("RtHub_Apply")` true, `RtHub_List()` = all six, lists / strings / numbers / booleans come through.
  - Refusals with reasons: unknown id, no id, bad `onEnd`, `CanApply` Hogtie "something is in the way" (indoors) / WallRestraint
    "no wall in front of her" (street), SpreaderBar while the binders were on "the Arm & Leg Binders hold her legs". Hogtie
    `CanApply` true on the street and WallRestraint true at the house wall - both then went on with `RtHub_Apply`.
  - Owner: A locked the yoke; B's `Remove`, `Set`, `Apply` refused ("locked by the mission 'RtHubTestA'"); A's `Set width 120`
    worked (115.2 -> 120 %). Timer 20 s: "taken off (timer)", `RtHub_Removed by=timer` in both missions.
  - No struggling: the yoke locked by A, F held 8 s on the street: "The Yoke is locked - you can't struggle out.", list
    "Yoke [locked] (+/- width)" (screenshots), still on. Negative control: `RtHub_Apply("Yoke", struggle=true)` and the same F
    hold -> struggled free, `RtHub_Removed by=struggle` + `RtHub_Escaped` (F in the closet area opened the closet - that first
    try did not count).
  - Thread end: `hold Binders` (a new thread) -> on; `drop` (thread unreferenced) -> "taken off (thread ended)", `by=threadEnd`.
    A lock taken from the polling listener stayed on for 400 frames (listeners do not end per call).
  - Mission switched off (`SetMissionActive`): A's `onEnd=thread` spreader bar taken off (`by=missionEnd`); B's `keep` tether
    stayed on, only let go.
  - Escape: tapping V toggled blocking off / on and removed nothing; holding V: hint after 0.3 s (screenshot), at 3 s the bar and
    the tether came off with `by=escapeKey` while A and B held them; blocking unchanged.
  - Player put-on (key 4): `RtHub_Applied by=player`, owner null; A then took hold of it (`holds SpreaderBar`).
  - Settings: SpreaderBar width 40 cm; NippleTether length 39.7 -> 50 -> 9999 clamped to 400 cm; WallRestraint height 75.6 ->
    60 -> 500 clamped to 128.6 cm (the wall's top); the configs kept the player's values (NippleTether LengthCm 39.7,
    WallRestraint KneeHeight 1.75). A mission's width ended with the bar (after the load it was the player's 21 cm again).
  - Save / load: A's spreader bar lock (no struggling, 120 s) saved at home ("1 mission locks"), game restarted, loaded:
    "load: mission locks SpreaderBar,RtHubTestA,0,110.341,mission", `RtHub_Applied by=load`, still locked with the timer
    running; after the load B was refused and A (a new session of the same mission) removed it.
  - Without Custom Missions (its DLL moved away for one start): "not connected", all mods load, key 4 + F work, the save's lock
    was dropped as "mission ended" (the bar came off); the DLL put back, connected again.
  - Warning: a test text was shown in game; with `General.WarnMissions` off a second one was not ("warning not shown").
  - Example mission CaughtRestrained: switched on, "found" triggered through Custom Missions' own hook -> `CanApply` refused
    Hogtie / WallRestraint there, picked the yoke: "caught: Yoke for 60 s", locked, list "Yoke [locked 0:44]", taken off by its
    timer; switched off, "found" again -> nothing.
  - Log texts shortened afterwards (quoted here as logged then): now "missions: Custom Missions v2 2.4.0, API 1",
    "warning (hidden): ...", "ring: 6 entries from index 32 (page 5)".
  - Not observed: a Custom Missions version other than 2.4.0 (the warning path was tested with a test text).
- L3 1.1.0 number keys vs another mod (2026-11-02; user report: with a leash collar mod "it tries to clear the hotkeys and this
  tries to restore them", over and over, and a key could not be given something else). The hub restored its keys from the masks
  every time the ring array was replaced - before looking at what the player had done. Not reproduced with the CollarAsset 1.1.6
  installed here (it touches neither the ring nor the keys), so a stand-in test plugin (RingChurn, scratch only) rebuilt the ring
  without the restraint entries and pointed keys holding a restraint back at their saved game entry, every 60 frames or every
  frame:
  - before the fix: the hub rebuilt and restored on every one of them ("ring: ..." + three "restored" lines each time; 378
    rebuilds logged at one per frame).
  - fixed (`Logic/ShortcutGuard.cs`): a key is restored after a rebuild only while its saved index is still the hub's (else the
    player gave it something else), at most 3 times in 10 s, then "Key 4 keeps being changed by another mod; Spreader Bar no longer restored." once per key, and one warning for a ring that keeps being replaced. One rebuild
    per second: 16 rebuilds, 3-4 restores per key, the warnings, then quiet. One per frame: key 1 given a pose and key 4 the
    dildo through the ring - both kept. Without the stand-in: load restores keys 1 / 4 / 8, a map change keeps them, key 4 still
    puts the bar on, the game's own `RingMenuParentView.Initialize` (keeps the entries, resets the keys) -> restored once; a
    restraint put on a key again by the player after the hub gave up stays there across a map change.

- L3 2.0.0 panel (2026-11-07, test plugin with 10 wearables and 2 placeables - one refusing, one with an in-use wearable;
  the 1.x mods refused by BepInEx next to 2.0 as expected): `U` -> "panel: open (Wearables)", all ten rows, "Placeables >"
  (`PanelStatus`, screenshots); the list clipped inside the box, scrolled to the end by its `ScrollRect` (last rows and
  "Placeables >" shown); "Test Cuffs" clicked -> "put on: Test1", panel closed, worn list "> Test Cuffs (F, test)"; reopened ->
  "Test Cuffs [on]"; `E` -> Placeables; "Test post" -> "refused: place TestPost: no room here", panel stays; "Test frame" ->
  "placed", reopened "[1 placed]" + "Pick up: Test frame" -> "picked up"; Escape closed it; holding V took the cuffs off through
  `TakeOff`; no exceptions from the hub. "keys: panel key U is not bound by the game". Not tested: the mouse wheel (no bridge
  command; `ScrollRect` handles it), gamepad navigation, real restraint mods (step 4: porting them).
- L3 2.0.0 placed objects (2026-11-07). Proof first (scratch plugin): a `new GameObject` + `InteractAreaController` threw in
  `Initialize`; a copy of the dropped coat's area showed "F Test clone"; with `type` None F did nothing, with `DroppedClothes` F
  called the callback (twice, `isInteracting` false, the real dropped coat untouched). Then the hub with the test plugin (a cube
  as the model): "placed: TestFrame on Apart", "refused: place TestPost: no room here (test refusal)"; at it "F Get into the
  test frame" (screenshot); F -> "put on: TestFrameInUse", "used: TestFrame -> TestFrameInUse", the plugin saw `UsedAt`, the
  area inactive and the game's interact list back to its placeholder, `Hub.InInteractArea` false; V held -> "free: TestFrame",
  area active. Travel to Residence -> hidden ("hide"), back -> "stage Apart: 1 placed objects shown"; closet save "1 placed
  objects", restart + load -> "load: 1 placed objects", shown again; the panel opened in the closet's area, "Pick up: Test
  frame" -> "picked up", saved again with 0. No exceptions.
- L3 2.0.0 with the ported mods (2026-11-07; Hogtie 2.0.0, WallRestraint 2.0.0): Hogtie refused in the apartment ("something
  is in the way (bed01)", then flowers on the street), placed on the street (device on the floor with the game's circle,
  screenshot); F -> "used: HogtieDevice -> Hogtie", hogtied in the device (the empty copy hidden, worn list "Hogtie (F)",
  screenshot); F held 5.5 s -> "attempt: success", "free: HogtieDevice", the device and its area back. WallRestraint placed on a
  street fence ("place: ok on 'Fence_Concrete_4m_01_LOD0'", screenshot of the empty restraint on the fence); F -> locked to the
  wall there; V held -> "free: WallRestraintFitting". Both saved at home ("2 placed objects"), restart + load + travel ->
  "stage Residence: 2 placed objects shown"; the wall used again after the load (knees within the saved heights); both picked
  up from the panel, saved clean. Configs: only descriptions changed. Not tested in 2.0: missions putting Hogtie /
  WallRestraint on (`RtHub_Apply`, where she stands - the 1.x path, unchanged), two of a kind on one map (`MaxPerMap` is 1 for
  both).

## Known limits / next
- Placed objects: missions can't place or pick them up (they put the in-use wearable on where she stands); "Pick up" takes the
  nearest one on the map, wherever she is; a placed model is sized for her body when it is shown.
- Missions: events sent while a save loads come before the missions start; the game's own handcuffs can be read but not locked.
- The list's unselected rows are dimmed only a little (the game's text shader).
- Long rows (label + controls over ~30 characters) are shrunk by the game's text box, not wrapped.
