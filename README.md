# RestraintHub

The shared home of the restraint mods for SecretFlasherManaka (BepInEx 6 IL2CPP): **SpreaderBar**, **WallRestraint** and **Yoke**
depend on it (hard dependency; their packages include it). Single-player/offline only.

**Status 0.1.0:** L3 (see Verification).

- **Ring pages:** all restraint entries go on pages of their own after the game's (empty "-" slots fill the game's last page), in a
  fixed order (by id). With the game's 31 entries: page 5 holds Spreader Bar, Wall Restraint, Yoke. More restraints add pages.
  Every ring key reaches that page (0.1.0 fix: keys 2-8 page only up to the game's own page limit, which the hub raises).
  Number shortcuts work as before (release a ring key over an entry; a short tap runs it) and are saved under each mod's own key,
  so saves from before the hub keep their shortcuts.
- **What she wears:** a small list bottom right, above the game's shortcuts, in the game's own style: one row per worn restraint
  (the game's handcuffs included), the selected one marked `>` with its keys, and a first row naming the select key when more
  than one is worn. A newly worn restraint becomes the selected one.
- **Select:** `B` (`Keys.SelectNext`) selects the next worn restraint. The hub warns in the log if the game binds that key too.
- **Only the selected restraint reacts:** holding **F** struggles only with the selected one (with the game's handcuffs selected, F
  goes to them; otherwise their struggle does not start); **+ / -** change only the selected one (width or height). Menus, the
  ring and interaction areas (closet, PC ...) keep F and + / - as before.
- **Conflicts:** each restraint says which parts it holds (Spreader Bar: legs; Yoke: neck and arms; Wall Restraint: neck, arms,
  legs and her place; the game's handcuffs: arms). Choosing one that needs a part already held is refused with a message
  ("Can't use the Wall Restraint: the Yoke holds her neck and arms."). Choosing a worn one selects it. **`V`**
  (`Keys.ToggleBlocking`) switches this off and on (`General.BlockEachOther`, kept in the config): off, restraints may overlap -
  the one that poses a body part last wins it (the yoke over the game's handcuffs: arms up in the yoke). The list's first row
  then reads "Next restraint (overlap on)".
- **Input:** one input hook applies what the worn restraints block (running, moving, crouching, peeing, actions that need her
  hands, pose skills) and shows "Can't do that with the <restraint> on." for refused hand actions and poses; Shift + move still
  ends a running pose skill. The game's "Unlocking..." gauge shows the selected restraint's attempt.

## For restraint mods

```csharp
[BepInDependency(Hub.Guid, BepInDependency.DependencyFlags.HardDependency)]
...
Hub.Register(new Restraint
{
    Id = "Yoke", RingValue = 9300, Label = () => RingLabel.Value, ShortcutSaveKey = "Yoke.Shortcuts",
    Holds = BodyParts.Neck | BodyParts.Arms, IsWorn = () => ..., Blocks = () => Blocks.Running | Blocks.HandActions,
    OnChosen = () => ..., Attempt = () => machine.Attempt, Controls = "+/- width",
});
// each frame: entry.StruggleHeld (F for this restraint), entry.TakeStep() (+1 / -1 / 0)
```

The project references `..\RestraintHub\RestraintHub.csproj` with `Private="false"` (the DLL is not copied into the mod);
`./tools/dev.ps1 deploy|cycle|package <Mod>` bring the hub along. SfmKit's types are `internal`, so the hub's copy of SfmKit and
the mod's own copy do not clash.

## Config (`BepInEx/config/com.local.restrainthub.cfg`)
| Key | Default | Meaning |
|---|---|---|
| Keys.SelectNext | B | Input System key name that selects the next worn restraint |
| Keys.ToggleBlocking | V | key that switches General.BlockEachOther (K is the game's Costume key; J, L, M, I, O are taken too) |
| General.BlockEachOther | true | restraints that hold the same body part block each other |
| Debug.Disable | 0 | bit 1 = block no input (negative control) |

## Diagnostics
`game get RestraintHub.HubController::Status` ("selected X; list A; B;"), `game call RestraintHub.HubController SelectNext`,
`game call RestraintHub.HubController KeysInUse 'B V K'` (which game actions use each key: "K: Costume").

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
  - Ring keys 2-8 (user report: the restraint page only with key 1): `maxRingIndexSkill` was 3, key 2 showed 4 pages and X wrapped
    to page 1; set to 4 by hand it showed page 5. With the fix ("number-key rings page up to 5"): key 2 and key 3 reached page 5
    (Spreader Bar, Yoke there), released over their own entries (shortcuts unchanged); the limit stayed after two map changes. `selftest --game` green, dev copy state unchanged.

## Known limits / next
- A tap of a number key right after a stage change can be missed (the second tap worked).
- The list's unselected rows are dimmed only a little (the game's text shader).
