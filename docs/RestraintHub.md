# RestraintHub

> Developer notes (written in [sfm-modkit](https://github.com/FlanderDev-NSFW), where the mod is developed with
> an in-game test bridge; paths like `./tools/dev.ps1` and `docs/knowledge/` refer to that kit): how it works, settings in detail, diagnostics and the test history. The player readme (shipped in the
> release zip) is [`README.md`](../README.md).
>
> **1.0.0 (2026-10-27): first release** (F3M). Plugin GUID `FlanderDev.SFM.RestraintHub` (was `com.local.restrainthub`), so the
> config file is `BepInEx/config/FlanderDev.SFM.RestraintHub.cfg`. The release zips no longer include RestraintHub (it is its own download).

The shared home of the restraint mods for SecretFlasherManaka (BepInEx 6 IL2CPP). **SpreaderBar**, **WallRestraint**,
**Yoke** and **Binders** depend on it (a separate download they list as a dependency), and so can yours: the hub gives every restraint a ring entry, a place in an
on-screen list, the keys F and + / -, input blocking and conflict checks, so a restraint mod only has to do its own lock and
its visible part. Single-player/offline only.

**Status 0.3.0:** L3 (see Verification). Contents: [For players](#for-players) · [For modders](#for-modders) ·
[Config](#config-bepinexconfigflanderdevsfmrestrainthubcfg) · [Verification](#verification)

## For players

- **Ring pages:** all restraint entries are on pages of their own after the game's (empty "-" slots fill the game's last page),
  in a fixed order. With the game's 31 entries: page 5 holds Spreader Bar, Wall Restraint, Yoke; more restraints add pages.
  Every ring key (1-8) reaches them. Number shortcuts work as usual (release a ring key over an entry; a short tap runs it)
  and are kept in the save.
- **What she wears:** a small list directly above the head preview (bottom right), growing upwards, in the game's own style:
  one row per worn restraint (the game's handcuffs included), the selected one marked `>` with its keys, and a first row naming
  the select key when more than one is worn. A newly worn restraint becomes the selected one.
- **`B`** selects the next worn restraint. **Only the selected one reacts:** holding **F** struggles with it (with the game's
  handcuffs selected, F goes to them), **+ / -** adjust it. Menus, the ring and interaction areas (closet, PC ...) keep their keys.
- **Conflicts:** two restraints that hold the same body part (neck, arms, legs, her place) cannot be worn together - the second is
  refused ("Can't use the Wall Restraint: the Yoke holds her neck and arms."). **`V`** switches this off and on (kept in the
  config); off, restraints may overlap and the one that poses a body part last wins it, and they no longer refuse her animations
  (hand actions, pose skills, peeing - running, walking and crouching stay blocked); the list's first row then reads
  "Next restraint (overlap on)". Choosing a worn restraint in the ring selects it.
- **Blocking:** while restraints are on, what they take away (running, walking, crouching, peeing, actions that need her hands,
  pose skills) is refused - hand actions and poses with "Can't do that with the <restraint> on.". Shift + move still ends a pose.
  With blocking off (`V`) hand actions, pose skills and peeing are allowed.
  The game's "Unlocking..." gauge shows the selected restraint's struggle.
- **Escape (1.1.0):** holding **`V`** for `Keys.EscapeHoldSeconds` (3 s) takes off every restraint that has `TakeOff`, also ones
  a mission locked ("Keep holding V to take off all restraints." after 0.3 s). A tap still toggles blocking (on release).
- **Missions (1.1.0):** with Custom Missions v2 installed (soft dependency, bound by reflection - `Cm2.cs`), mission scripts get
  `RtHub_*` functions and events (`Missions.cs`; the author's reference is [Missions.md](Missions.md)). A mission's lock
  (`Logic/MissionLocks.cs`) can forbid struggling ("The Yoke is locked - you can't struggle out.", the list shows
  `[locked 1:23]` and no F), run a timer and end with the mission's thread / the mission; it is saved (`RestraintHub.MissionLocks`,
  one line per lock) and owned by the mission's name after a load. A Custom Missions version other than 2.4.x, or members that
  are missing, show a warning once in game (`General.WarnMissions`) and always in the log.

## For modders

### What the hub does, what your mod does

| The hub (you get it by registering) | Your mod |
|---|---|
| a ring entry on the restraint pages, its label (localisation), click and number-key detection | what happens when it is chosen (`OnChosen`) |
| number shortcuts, saved under your key; never lets the game save an index it cannot load | - |
| the list over the head preview, selection (`B`), which restraint F and + / - go to | read `StruggleHeld`, call `TakeStep()` |
| refusing restraints that hold the same body part (unless the player turned that off) | say which parts it holds (`Holds`) |
| clearing the input your restraint forbids, the refusal message | say what it forbids (`Blocks`), or adjust input in `OnInput` |
| the "Unlocking..." gauge for the selected restraint | report the attempt's progress (`Attempt`) |
| a short tap of F as a second key, separate from the struggle (0.3.0) | act on it (`OnTap`) |
| + / - held, for smooth adjustments (0.3.0) | read `HeldStep` |
| the save hooks: right after the game writes / reads a slot, a `SaveSlot` to write / read your keys (0.3.0) | `OnSave`, `OnLoad` |
| noticing a new game / the title replacing the game data without a load (0.3.0) | take it off (`OnNewGame`) |
| whether a menu or the ring is open, whether she stands in an interaction area (0.3.0) | read `Hub.MenuOpen`, `Hub.InInteractArea`, `Hub.InputFree` |
| - | the lock / struggle logic (SfmKit's `LockMachine` recommended), the model, the pose, what it saves |

### Quick start
Copy **`mods/RestraintTemplate`** - a complete restraint with no model, compiled with the repo and checked in game. Its notes (`docs/mods/RestraintTemplate.md`)
lists the renames (namespace, GUID, `Id`, `RingValue`, save keys) and how to build, deploy, test and package. The three real
restraints are larger examples: `mods/Yoke` (body-worn, arms posed, + / - width), `mods/SpreaderBar` (legs, own input handling
in `OnInput`), `mods/WallRestraint` (placed on a wall, the whole body posed, + / - height).

The registration is the whole API (full XML docs in `Hub.cs`, shipped as `RestraintHub.xml` next to the DLL):

```csharp
[BepInPlugin("com.you.blindfold", "Blindfold", "1.0.0")]
[BepInDependency(Hub.Guid, "^1.1.0")] // loads after the hub; refused without it, with an older one or with 2.x
public sealed class Plugin : BasePlugin
{
    internal static Restraint Entry;

    public override void Load()
    {
        ClassInjector.RegisterTypeInIl2Cpp<BlindfoldController>();
        AddComponent<BlindfoldController>();
        Entry = new Restraint
        {
            Id = "Blindfold", RingValue = 9400, Label = () => "Blindfold", ShortcutSaveKey = "Blindfold.Shortcuts",
            Holds = BodyParts.None, Blocks = () => Blocks.Running,
            IsWorn = () => BlindfoldController.Machine.Worn,
            OnChosen = () => BlindfoldController.Machine.RequestLock(),
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
//   if (Hub.InputFree) { ... your own keys ... }              // no menu, ring or interaction area
```

### Project setup
- **In this repo:** `<ProjectReference Include="..\RestraintHub\RestraintHub.csproj" Private="false" />` (compile against the
  hub, do not copy its DLL). `./tools/dev.ps1 deploy|cycle|package <YourMod>` bring the hub along (its own plugins folder, with
  `RestraintHub.xml`). SfmKit's types are `internal`, so your copy and the hub's do not clash.
- **Outside this repo:** `<Reference Include="RestraintHub"><HintPath>...\BepInEx\plugins\RestraintHub\RestraintHub.dll</HintPath>
  <Private>false</Private></Reference>`; ship your DLL in `BepInEx/plugins/<YourMod>/` and the hub's folder with it (or require it).
- **Versions:** `Hub.Version`; require a newer hub with a range: `[BepInDependency(Hub.Guid, "^1.1.0")]`. **A bare version is an
  exact match** in BepInEx 6: the 1.0 mods (`"1.0.0"`) were refused by hub 1.1.0 ("Could not load [RestraintTemplate 1.0.0]
  because it has missing dependencies: FlanderDev.SFM.RestraintHub (1.0.0)") [observed 2026-11-02]. The API is `Hub`, `Restraint`,
  `RestraintSetting`, `SaveSlot`, `BodyParts`, `Blocks`; `Plugin`, `HubController` and `Missions` are public only for the dev
  bridge and may change.

### The `Restraint` fields

| Field | Required | Called / read | Notes |
|---|---|---|---|
| `Id` | yes | at registration | stable and unique; ring order (ordinal); localisation key `restrainthub_<Id>` |
| `RingValue` | yes | at registration | 9000-9998, unique - see the registry below |
| `Label` | recommended | whenever shown (every frame while the list is visible) | cheap; null shows `Id` |
| `ShortcutSaveKey` | recommended | on save / load | never rename after a release (players lose their number keys) |
| `OnShortcutsMissing` | no | on load, when the save lacks the key | return a bit mask (bit k = key k+1) |
| `Holds` | no (`None`) | when anything is chosen | the game's handcuffs hold `Arms` |
| `IsWorn` | yes | every frame | true from the start of closing until fully off (`LockMachine.Worn`); an exception counts as not worn |
| `Blocks` | no | every input frame while worn | union over all worn restraints; may change |
| `OnChosen` | yes | the entry chosen while off and nothing conflicts | put it on, or `Hub.Message` why not; chosen while worn = selected instead |
| `Attempt` | no | every frame while selected | 0..1 for the "Unlocking..." gauge |
| `OnInput` | no | every input frame while worn, after blocking | edit the game's `InputData` for anything `Blocks` does not cover |
| `Controls` | no | with the list | a few words for + / -; label + controls under ~30 characters (longer rows are shrunk) |
| `OnTap` | no (0.3.0) | F tapped (released within 0.3 s) while selected, outside menus and interaction areas | a second use of F; with it set, `StruggleHeld` starts only after the tap time |
| `OnSave` | no (0.3.0) | right after the game wrote a slot (not the system file) | `slot.Save(key, bool / int / double / string)` under your mod's keys |
| `OnLoad` | no (0.3.0) | right after the game read a slot, before the stage starts | `slot.Load(key, fallback)`; the fallback is what older saves get |
| `OnNewGame` | no (0.3.0) | the game data was replaced without a load (new game) | take it off; the title screen keeps the old data, so this comes with New Game |
| `PutOn` | no (1.1.0) | a mission puts it on (`RtHub_Apply`), while off and nothing conflicts | put it on without a player message; return null when it is closing, else why not in a few words. Null: `OnChosen` is called and a refusal reported when not worn right after |
| `CanPutOn` | no (1.1.0) | `RtHub_CanApply` | would `PutOn` work here, without side effects (null = yes)? For place-dependent restraints (WallRestraint and Hogtie run their placement with a `check` flag) |
| `TakeOff` | recommended (1.1.0) | `RtHub_Remove`, a mission's timer / end, the escape key | take it off at once; leave the body as without it. Null: missions can't remove it and the escape key leaves it on |
| `Settings` | no (1.1.0) | `RtHub_Get` / `RtHub_Set` while worn | `RestraintSetting { Name, Unit, Get, Set }`; `Set` returns null or why not, clamps, and must not write the player's config |
| `Selected` (hub) | - | set every frame | this restraint is the selected worn item |
| `StruggleHeld` (hub) | - | set every frame | F held for it: selected, no menu / ring open, not in an interaction area |
| `TakeStep()` (hub) | - | call once per frame | +1 / -1 / 0; only the selected restraint gets steps; held keys repeat |
| `HeldStep` (hub, 0.3.0) | - | set every frame | +1 / -1 while + / - is held for the selected restraint, else 0 (smooth changes: `value += HeldStep * speed * dt`) |
| `Hub.MenuOpen`, `Hub.InInteractArea`, `Hub.InputFree` (0.3.0) | - | set every frame | a game panel or the ring is open / she is in an interaction area (F is the area's) / neither |

### Rules of thumb
- **Timing:** everything is on Unity's main thread. The hub's `Update` and yours run in no fixed order, so `StruggleHeld` and
  steps can be a frame late - harmless. Register once from `Load`; registering later misses the ring until the game rebuilds it.
- **Callbacks must not throw** (they run in the hub's frame and input hook). The hub catches and logs, but don't rely on it.
- **Overlap is possible.** The player can switch blocking off (`V`): your restraint may be worn with one that poses the same
  bones. Pose in `LateUpdate`, place your model from the drawn bones in `Camera.onPreCull`, and expect the other pose to win
  sometimes - never assume you own a bone. Blocking off also lifts `Blocks.HandActions`, `PoseSkills` and `Pee` (`Rules.Animations`),
  so a pose skill or a hand animation may start while yours is worn - if yours poses those bones, it should keep posing them.
- **Pose skills:** `Blocks.PoseSkills` stops new ones; end a running one yourself when yours goes on if you pose the same bones
  (`pca.PlayerAction.ChangeAction(ActionType.None)`, as Yoke does).
- **Your own save data** goes in your own keys through `OnSave` / `OnLoad` (see the template's `Patches.cs`); since 0.3.0 no
  restraint mod hooks `SaveManager` itself. Write the player's config only on the player's action
  (SpreaderBar once wrote a clamped value back on every launch and the setting crept).
- **Keys:** the hub owns `B`, `V`, F and + / - for restraints. For your own keys, check the game's bindings first:
  `game call RestraintHub.HubController KeysInUse 'H P U'` (free today: B H P U V Y Backquote).

### Ring value registry

| Value | Mod |
|---|---|
| 9100 | SpreaderBar |
| 9200 | WallRestraint |
| 9300 | Yoke |
| 9400 | Binders |
| 9500 | NippleTether |
| 9600 | Hogtie |
| 9900 | RestraintTemplate (example - replace it in your copy) |
| 9999 | the hub's empty filler (reserved) |

Pick an unused value in 9000-9998 and add it here (or to your mod's README if you publish elsewhere).

### Testing with the bridge
`game get RestraintHub.HubController::Status` ("selected X; list A; B;"), `game call RestraintHub.HubController SelectNext`,
`key B --via input --expect "select:"`. To choose an entry without touching the player's shortcuts: hold the ring key, select
the slot (`openRingList[page].currentAngle`), `game click --at 960 540`, then release (releasing over an entry assigns it).
Log markers: `register:`, `ring:`, `list:`, `select:`, `refused:`, `conflicts:`, `shortcut:`, `save:` / `load:`, `tap:`,
`new game:`, `escape:`, `missions:` (connected), `mission '<name>':` (calls, refusals, locks ending), `event:`, `warning`.
Missions: `game get RestraintHub.Missions::Status` (locks and the loaded missions), `game call RestraintHub.Missions
SetMissionActive <name> false` (as the phone does; `--allow-invoke`), and the test mission `tests/missions/RtHubTest` (copy it to
`CustomMissions2/RtHubTestA`; create `cmd/<n> <command>.txt` files - see its header - and read `[RtHubTest:A]` lines in the
log). "Found by someone" can be triggered with `MSYS_NO_PATHCONV=1 ./tools/dev.ps1 game call
SFM_custom_mission_v2.Objects.PatchPlayerController OnFoundStrangerEvent "=/Level/.../NpcLocation/Npc(Clone)"`.

## Config (`BepInEx/config/FlanderDev.SFM.RestraintHub.cfg`)
| Key | Default | Meaning |
|---|---|---|
| Keys.SelectNext | B | Input System key name that selects the next worn restraint |
| Keys.ToggleBlocking | V | tapped: switches General.BlockEachOther (K is the game's Costume key; J, L, M, I, O are taken too); held: the escape |
| Keys.EscapeHoldSeconds | 3 | how long to hold Keys.ToggleBlocking to take off all restraints (1-10) |
| General.BlockEachOther | true | restraints that hold the same body part block each other, and restraints refuse hand actions, pose skills and peeing |
| General.WarnMissions | true | show the Custom Missions version / connection warning in game (the log always has it) |
| Debug.Disable | 0 | bit 1 = block no input (negative control) |

## Verification
- L1: `./tools/dev.ps1 test RestraintHub` (55: page padding, conflicts and their wording, the worn list and its selection,
  tap vs hold, the escape hold time, mission locks: owners, option updates, timers and pauses, thread / mission / keep ends,
  never-worn and came-off locks, the save format, when number keys are restored / given up);
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

## Known limits / next
- Missions: events sent while a save loads come before the missions start; the game's own handcuffs can be read but not locked.
- A tap of a number key right after a stage change can be missed (the second tap worked).
- The list's unselected rows are dimmed only a little (the game's text shader).
- Long rows (label + controls over ~30 characters) are shrunk by the game's text box, not wrapped.
