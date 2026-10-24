# RestraintHub

The shared home of the restraint mods for SecretFlasherManaka (BepInEx 6 IL2CPP). **SpreaderBar**, **WallRestraint** and
**Yoke** depend on it (their packages include it), and so can yours: the hub gives every restraint a ring entry, a place in an
on-screen list, the keys F and + / -, input blocking and conflict checks, so a restraint mod only has to do its own lock and
its visible part. Single-player/offline only.

**Status 0.1.0:** L3 (see Verification). Contents: [For players](#for-players) · [For modders](#for-modders) ·
[Config](#config-bepinexconfigcomlocalrestrainthubcfg) · [Verification](#verification)

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
  config); off, restraints may overlap and the one that poses a body part last wins it; the list's first row then reads
  "Next restraint (overlap on)". Choosing a worn restraint in the ring selects it.
- **Blocking:** while restraints are on, what they take away (running, walking, crouching, peeing, actions that need her hands,
  pose skills) is refused - hand actions and poses with "Can't do that with the <restraint> on.". Shift + move still ends a pose.
  The game's "Unlocking..." gauge shows the selected restraint's struggle.

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
| - | the lock / struggle logic (SfmKit's `LockMachine` recommended), the model, the pose, its own save data |

### Quick start
Copy **`mods/RestraintTemplate`** - a complete restraint with no model, compiled with the repo and checked in game. Its README
lists the renames (namespace, GUID, `Id`, `RingValue`, save keys) and how to build, deploy, test and package. The three real
restraints are larger examples: `mods/Yoke` (body-worn, arms posed, + / - width), `mods/SpreaderBar` (legs, own input handling
in `OnInput`), `mods/WallRestraint` (placed on a wall, the whole body posed, + / - height).

The registration is the whole API (full XML docs in `Hub.cs`, shipped as `RestraintHub.xml` next to the DLL):

```csharp
[BepInPlugin("com.you.blindfold", "Blindfold", "1.0.0")]
[BepInDependency(Hub.Guid, BepInDependency.DependencyFlags.HardDependency)] // loads after the hub; refused without it
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
        };
        Hub.Register(Entry);
    }
}

// In the controller's Update:
//   int step = Plugin.Entry.TakeStep();                       // +1 / -1 / 0, only while selected
//   machine.Tick(Time.deltaTime, Plugin.Entry.StruggleHeld && machine.State == LockState.Locked, Random.value);
```

### Project setup
- **In this repo:** `<ProjectReference Include="..\RestraintHub\RestraintHub.csproj" Private="false" />` (compile against the
  hub, do not copy its DLL). `./tools/dev.ps1 deploy|cycle|package <YourMod>` bring the hub along (its own plugins folder, with
  `RestraintHub.xml`). SfmKit's types are `internal`, so your copy and the hub's do not clash.
- **Outside this repo:** `<Reference Include="RestraintHub"><HintPath>...\BepInEx\plugins\RestraintHub\RestraintHub.dll</HintPath>
  <Private>false</Private></Reference>`; ship your DLL in `BepInEx/plugins/<YourMod>/` and the hub's folder with it (or require it).
- **Versions:** `Hub.Version`; require a newer hub with `[BepInDependency(Hub.Guid, "0.2.0")]`. The API is `Hub`, `Restraint`,
  `BodyParts`, `Blocks`; `Plugin` and `HubController` are public only for the dev bridge and may change.

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
| `Selected` (hub) | - | set every frame | this restraint is the selected worn item |
| `StruggleHeld` (hub) | - | set every frame | F held for it: selected, no menu / ring open, not in an interaction area |
| `TakeStep()` (hub) | - | call once per frame | +1 / -1 / 0; only the selected restraint gets steps; held keys repeat |

### Rules of thumb
- **Timing:** everything is on Unity's main thread. The hub's `Update` and yours run in no fixed order, so `StruggleHeld` and
  steps can be a frame late - harmless. Register once from `Load`; registering later misses the ring until the game rebuilds it.
- **Callbacks must not throw** (they run in the hub's frame and input hook). The hub catches and logs, but don't rely on it.
- **Overlap is possible.** The player can switch blocking off (`V`): your restraint may be worn with one that poses the same
  bones. Pose in `LateUpdate`, place your model from the drawn bones in `Camera.onPreCull`, and expect the other pose to win
  sometimes - never assume you own a bone.
- **Pose skills:** `Blocks.PoseSkills` stops new ones; end a running one yourself when yours goes on if you pose the same bones
  (`pca.PlayerAction.ChangeAction(ActionType.None)`, as Yoke does).
- **Your own save data** goes in your own keys after `SaveManager.Save` / before the stage starts on `SaveManager.Load` (see the
  template's `Patches.cs`); the hub only saves the shortcuts. Write the player's config only on the player's action
  (SpreaderBar once wrote a clamped value back on every launch and the setting crept).
- **Keys:** the hub owns `B`, `V`, F and + / - for restraints. For your own keys, check the game's bindings first:
  `game call RestraintHub.HubController KeysInUse 'H P U'` (free today: B H P U V Y Backquote).

### Ring value registry

| Value | Mod |
|---|---|
| 9100 | SpreaderBar |
| 9200 | WallRestraint |
| 9300 | Yoke |
| 9900 | RestraintTemplate (example - replace it in your copy) |
| 9999 | the hub's empty filler (reserved) |

Pick an unused value in 9000-9998 and add it here (or to your mod's README if you publish elsewhere).

### Testing with the bridge
`game get RestraintHub.HubController::Status` ("selected X; list A; B;"), `game call RestraintHub.HubController SelectNext`,
`key B --via input --expect "select:"`. To choose an entry without touching the player's shortcuts: hold the ring key, select
the slot (`openRingList[page].currentAngle`), `game click --at 960 540`, then release (releasing over an entry assigns it).
Log markers: `register:`, `ring:`, `list:`, `select:`, `refused:`, `conflicts:`, `shortcut:`, `save:` / `load:`.

## Config (`BepInEx/config/com.local.restrainthub.cfg`)
| Key | Default | Meaning |
|---|---|---|
| Keys.SelectNext | B | Input System key name that selects the next worn restraint |
| Keys.ToggleBlocking | V | key that switches General.BlockEachOther (K is the game's Costume key; J, L, M, I, O are taken too) |
| General.BlockEachOther | true | restraints that hold the same body part block each other |
| Debug.Disable | 0 | bit 1 = block no input (negative control) |

## Verification
- L1: `./tools/dev.ps1 test RestraintHub` (19: page padding, conflicts and their wording, the worn list and its selection);
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
  - List over the head preview (user request): bar on -> one row just above the preview, centred on it; yoke on too -> three
    rows, the bottom row in the same place, the others above it (screenshots).
  - Docs / template (2026-10-24): RestraintTemplate built against the hub, deployed with it (hub folder with `RestraintHub.xml`,
    92 documented members), checked in game and removed again - see `mods/RestraintTemplate/README.md`.
  - Ring keys 2-8 (user report: the restraint page only with key 1): `maxRingIndexSkill` was 3, key 2 showed 4 pages and X wrapped
    to page 1; set to 4 by hand it showed page 5. With the fix ("number-key rings page up to 5"): key 2 and key 3 reached page 5
    (Spreader Bar, Yoke there), released over their own entries (shortcuts unchanged); the limit stayed after two map changes. `selftest --game` green, dev copy state unchanged.

## Known limits / next
- A tap of a number key right after a stage change can be missed (the second tap worked).
- The list's unselected rows are dimmed only a little (the game's text shader).
- Long rows (label + controls over ~30 characters) are shrunk by the game's text box, not wrapped.
