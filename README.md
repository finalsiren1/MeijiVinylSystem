# Meiji Vinyl System (MVS)

A vinyl record player system for VRChat worlds. Pick a record from a menu or a shelf, drop it on
the turntable, and everyone in the instance hears the same song in sync (late joiners included).

> **Status: early development.** Available now: the album library (data asset, Library Editor window,
> Udon-readable `MVSLibrary`). Everything else below is planned.

## Planned features

- Global playback through [VizVid](https://github.com/JLChnToZ/VVMW), synced for late joiners
- Records with one or many tracks (Next / Previous for multi-track records)
- Turntable animation: record drops, tonearm lowers, needle-drop sound, platter spins (reverse on eject)
- Record pool with auto-cleanup and limit warning, idle auto-return, Reset All
- Album covers printed on each record
- Library editor window: albums, artists, years, genres (add your own), track lists, covers
- Album browser UI: cover grid, list, genre tabs, search
- Local listening booth with headphones, Focus and Silent modes
- AudioLink support (optional)
- Quest / iOS compatible

## Requirements

| Dependency | Notes |
|---|---|
| Unity 2022.3 | VRChat-supported version |
| VRChat Worlds SDK 3.x | via VCC |
| UdonSharp | bundled with the Worlds SDK |
| VizVid (`idv.jlchntoz.vvmw`) | via VCC; stock version, no patches required |
| AudioLink | optional |

## Installation

1. Install the dependencies above through VCC.
2. Import `MeijiVinylSystem_vX.Y.Z.unitypackage` from the [Releases](../../releases) page.
3. The package installs to `Assets/MeijiVinylSystem/`.

## Quick start: library

1. `Create > Meiji Vinyl System > Library` to make a library asset.
2. `Tools > Meiji Vinyl System > Library Editor` (or double-click the asset) to add genres and albums.
   Each album has one or more tracks (title + URL); records with 2+ tracks get Next / Previous.
3. Click **Add Library to Scene**. This creates an `MVS Library` object (UdonSharp `MVSLibrary` +
   editor-only `MVSLibrarySource`). The data is baked into the scene automatically before Play Mode
   and before every VRChat build, or manually with **Bake Now**.
4. Other MVS components read albums through `MVSLibrary` by index or by stable album id.

## Building the package (maintainers)

`Tools > Meiji Vinyl System > Export .unitypackage` writes
`<ProjectRoot>/Exports/MeijiVinylSystem_v<version>.unitypackage`.
The version lives in `Editor/MVSInfo.cs`.

## Content policy

This package contains code, shaders, UI and editor tools only. It ships **no** third-party models,
album artwork or song lists. Bring your own models and make sure you have the rights to any
music links and cover images you add.

## License

[MIT](LICENSE) © 2026 Meiji_VRC

## Sleeve texture

One 2048x2048 image per album, split 2x2 (see `Templates/MVS_SleeveTemplate.png`):

| Quarter | Used for |
|---|---|
| top-left | front cover (sleeve front + album menu) |
| top-right | back cover |
| bottom-left | label, side A (round, centre of the vinyl) |
| bottom-right | label, side B (only for records with **Has Side B**; otherwise leave black) |

Atlases are rebuilt automatically before Play/Build when artwork changes.
