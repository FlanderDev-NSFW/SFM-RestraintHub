# Restraints in Custom Missions v2 missions

With RestraintHub 1.1 (or newer) installed next to **Custom Missions v2** (made for and tested with 2.4.0), mission scripts
(`.code`) can put on, lock and take off the restraints of the installed restraint mods (Spreader Bar, Yoke, Wall Restraint,
Arm & Leg Binders, Nipple Tether, Hogtie ...) and react when they go on or come off. Without RestraintHub these functions do not
exist, so check first:

```
if FunctionExists("RtHub_Apply")
	...
```

A complete example is [`examples/missions/CaughtRestrained`](../examples/missions/CaughtRestrained): when someone finds her, a
restraint that fits where she stands is locked on for a while instead of a Game Over.

## Restraint ids

`RtHub_List()` gives the installed ones. Current ids: `SpreaderBar`, `Yoke`, `WallRestraint`, `Binders`, `NippleTether`,
`Hogtie`. `RtHub_Worn()` and `RtHub_IsOn()` also know the game's own handcuffs as `Handcuffs` (they can't be put on or locked
this way).

## Functions

Arguments can be passed by position or by name (`RtHub_Apply("Yoke", seconds=60)`). Functions that can fail return `false`
(or `null`) and keep the reason for `RtHub_LastError()` - in plain words, e.g. `no wall in front of her`,
`the Yoke holds her arms`, `Spreader Bar is locked by the mission 'Hunted'`.

| Function | Returns | Does |
|---|---|---|
| `RtHub_ApiVersion()` | Number | the version of these functions (1); raised when functions are added or changed |
| `RtHub_List()` | List | ids of the installed restraints |
| `RtHub_Worn()` | List | ids of what she wears now (with `Handcuffs`) |
| `RtHub_IsOn(id)` | Boolean | whether that restraint is on |
| `RtHub_CanApply(id)` | Boolean | whether it could go on right now: in a stage, not already on, nothing worn holds the same body part, and the restraint's own check (a wall in front of her, room on the floor for the hogtie ...) |
| `RtHub_Apply(id[, struggle][, seconds][, onEnd])` | Boolean | put it on and lock it for this mission; see below |
| `RtHub_Remove(id)` | Boolean | take it off now (only if it is not locked by another mission) |
| `RtHub_Owner(id)` | String / null | the mission that locked it (its folder name), null if none |
| `RtHub_Remaining(id)` | Number | seconds left on its timer, 0 without one |
| `RtHub_Settings(id)` | List | names of the values `RtHub_Get` / `RtHub_Set` know for it |
| `RtHub_Get(id, name)` | Number / null | a value while it is on |
| `RtHub_Set(id, name, value)` | Boolean | change a value while it is on (not if another mission locked it); clamped to what fits - read it back with `RtHub_Get` |
| `RtHub_LastError()` | String | why this mission's last failing call failed (`""` after a success) |

### RtHub_Apply

```
RtHub_Apply(id, struggle=true, seconds=0, onEnd="thread")
```

- **struggle** - `false`: she can't struggle out with F (the list shows `[locked]`, F shows "The X is locked - you can't
  struggle out.").
- **seconds** - more than 0: it comes off by itself after that many seconds of game time (stopped while the game is paused or
  a menu is open; the list shows the time left). 0: no timer.
- **onEnd** - when this mission's hold ends:
  - `"thread"` (default): it comes off when the script thread that called `RtHub_Apply` ends, or the mission is switched off.
    A thread ends when nothing references it any more (`holder = null`). Listeners count as threads: called from a listener,
    it stays as long as the listener exists.
  - `"mission"`: it comes off when the mission is switched off (or is gone after a load).
  - `"keep"`: it stays on when the mission ends; the mission only lets go (struggling is allowed again).

If the restraint is already on and not locked by another mission, `RtHub_Apply` takes hold of it (also one the player put on)
or changes the hold: options you leave out keep their current values. So `RtHub_Apply("Yoke", struggle=true)` later allows
struggling again, `RtHub_Apply("Yoke", seconds=30)` starts a new timer.

`true` means it is going on (some restraints need a few frames); `RtHub_Applied` follows when it is on.

### Settings

| Restraint | Name | Unit |
|---|---|---|
| SpreaderBar | `width` | cm between the ankles |
| Yoke | `width` | % of the model's width |
| WallRestraint | `height` | cm, her knees above the floor (within what the wall allowed when it went on) |
| NippleTether | `length` | cm of chain (the chains run there; not shorter than she can follow) |

A mission's value is not stored in the player's config; it ends when the restraint comes off (and is not in the save).

## Events

Listen with `CreateEventListener(label, name)`; the label runs at once, with `_eventdata`:

| Event | When | `_eventdata` |
|---|---|---|
| `RtHub_Applied` | a restraint went on | `id`, `by`, `owner` |
| `RtHub_Removed` | a restraint came off | `id`, `by`, `owner` (the mission that held it, else null) |
| `RtHub_Escaped` | she struggled out of one (also sends `RtHub_Removed`) | `id`, `by`, `owner` |

`by` is one of: `player` (ring menu / number key), `mission` (`RtHub_Apply` / `RtHub_Remove`), `load` (loading a save),
`struggle`, `timer`, `threadEnd`, `missionEnd`, `escapeKey`, `other`.

```
listener = CreateEventListener("on_escaped", "RtHub_Escaped")

on_escaped:
	if _eventdata.owner == _name
		Log("she got out of the " + _eventdata.id)
```

Events sent while a save is loading come before missions start; a mission that cares about what she wears at the start should
check `RtHub_Worn()`.

## How locks behave

- **Locked by one mission at a time.** Other missions can't remove, re-lock or change a restraint another mission locked;
  they can still read everything.
- **Saving:** a mission's lock is saved with the game (for restraints that stay on after a load - Spreader Bar, Yoke, Binders,
  Nipple Tether) and belongs to the mission again after the load, timer included. The thread is not known after a load, so an
  `onEnd="thread"` lock then behaves like `"mission"`.
- **The player always has a way out:** holding `V` (RestraintHub's `Keys.ToggleBlocking`) for 3 seconds takes off every
  restraint, locked or not (`by` = `escapeKey`). Design missions so that this is a "give up", not the normal way.
- Conflicts follow the player's setting: with `V` tapped off, restraints may overlap and `RtHub_CanApply` does not refuse them.

## Testing your mission

The BepInEx log shows every call that was refused (`mission '<name>': RtHub_Apply refused: ...`), every lock and every event
(`[SFM-ACCEPT] event: RtHub_Removed Yoke by timer (owner '<name>')`). RestraintHub's own test mission is
[`tests/missions/RtHubTest`](../tests/missions/RtHubTest) (commands from file names, results in the log).
