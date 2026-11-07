# RestraintHub 2.0.0

The shared base for restraint mods in **Secret Flasher Manaka**. On its own it adds nothing you can see; the restraint mods
(Spreader Bar, Wall Restraint, Hogtie, Yoke, Arm & Leg Binders, Nipple Tether) need it.

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/RestraintHub/`.
Then install the restraint mods you want the same way.

## What it does in game
- **Panel (`U`):** all restraints in one place, in two lists - **Wearables** (on her body: spreader bar, yoke, binders ...) and
  **Placeables** (objects you put somewhere and use: the hogtie, the wall restraint). `Q` / `E` switch between them; the mouse
  wheel scrolls; `U` or `Esc` closes it. Click a wearable to put it on, a placeable to place it in front of her. The game keeps
  running while it is open.
- **Placed objects** stay where they are on their map (also in the save) until you pick them up again in the panel
  ("Pick up: ...", the nearest one on this map). Walk to one and press **`F`** to get into it.
- **Worn list:** above the face preview (bottom right) a small list shows what she wears. The selected one is marked `>`.
- **Keys** (they act on the selected restraint):

| Key | Does |
|---|---|
| `U` | open / close the panel |
| `B` | select the next worn restraint |
| hold `F` | struggle free (the game's "Unlocking..." bar shows the progress) |
| `+` / `-` | adjust it (width, height, length ... depending on the restraint) |
| `V` | allow / forbid restraints that hold the same body part at the same time |
| hold `V` (3 s) | take off **all** restraints - also ones a mission locked |

In menus and interaction areas (closet, PC ...) the keys keep their normal job: step out of the area first.

- **Blocking:** while a restraint is on, what it takes away (running, using her hands, pose skills ...) is refused with a
  message. With `V` switched off, restraints may overlap and fewer actions are refused.
- **Missions** (optional, with [Custom Missions v2](https://f95zone.to/threads/263276/) 2.4): missions can put restraints on her
  and lock them - for a while, or until the mission ends. A locked restraint shows `[locked]` (and the time left) in the list,
  and F does not free her from it; holding `V` for 3 seconds always takes everything off. If the installed Custom Missions
  is a version the hub was not made for, a message says so (can be switched off).

**2.0 needs restraint mods made for 2.0.** The ring menu and number keys are no longer used: restraints are only in the panel.

## Settings
`BepInEx/config/FlanderDev.SFM.RestraintHub.cfg` (created on the first start): the keys for `U`, `B` and `V`, how long `V` must be
held to take everything off, whether restraints block each other, and whether to warn about the Custom Missions version.

## For modders
The API is documented in `RestraintHub.xml` next to the DLL; the guide is [docs/RestraintHub.md](docs/RestraintHub.md), and
[SFM-RestraintTemplate](https://github.com/FlanderDev-NSFW/SFM-RestraintTemplate) is a template restraint to start from.
Mission authors: the `RtHub_*` script functions and events are in [docs/Missions.md](docs/Missions.md), with an example
mission in [examples/missions/CaughtRestrained](examples/missions/CaughtRestrained).

Single-player / offline only. Code: MIT licence (`LICENSE.txt`).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/RestraintHub/RestraintHub.csproj -c Release`.
- The source of the mod is `src/RestraintHub` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit)
  library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
