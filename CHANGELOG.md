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
- Assembly definitions: `MeijiVinylSystem.Runtime` (+ U# Assembly Definition) and `MeijiVinylSystem.Editor`.

## [0.1.0] - 2026-11-08
### Added
- Project scaffold: README, LICENSE (MIT), CHANGELOG.
- Editor menu `Tools > Meiji Vinyl System > Export .unitypackage`.
