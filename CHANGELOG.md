# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]
### Added
- `MVSLibraryData` asset (Create > Meiji Vinyl System > Library): albums, artists, years, genres with stable ids, covers, single- or multi-track records.
- `MVSLibrary` (UdonSharp): read-only baked arrays + lookup helpers (`FindAlbum`, `GetTrackUrl`, `Matches`, ...).
- `MVSLibrarySource` (editor-only): links a library asset to an `MVSLibrary`; auto-bakes before Play Mode and VRChat builds.
- Library Editor window (`Tools > Meiji Vinyl System > Library Editor`): search, genre filter, problems filter, track list, cover preview, genre management, Check tab (missing data, duplicate URLs/names).
- `MVSPlayer` (UdonSharp, Manual sync): record on the platter, track, loop, insert/eject cooldown, needle-drop start delay; drives a dedicated VizVid `Core` (play/pause/seek/late-joiner sync stay in VizVid). Multi-track records: Next/Previous, auto-advance, whole-record loop. Owner-left recovery while the needle is dropping. Listener events `_MVSRecordChanged`, `_MVSTrackChanged`, `_MVSStateChanged`, `_MVSLoopChanged`, `_MVSPlaybackChanged`.
- `MVSDebugPanel` (UdonSharp): developer test panel for `MVSPlayer`.
- VizVid (`JLChnToZ.VVMW`) is now a required dependency of the runtime assembly.
- Assembly definitions: `MeijiVinylSystem.Runtime` (+ U# Assembly Definition) and `MeijiVinylSystem.Editor`.

## [0.1.0] - 2026-11-08
### Added
- Project scaffold: README, LICENSE (MIT), CHANGELOG.
- Editor menu `Tools > Meiji Vinyl System > Export .unitypackage`.
