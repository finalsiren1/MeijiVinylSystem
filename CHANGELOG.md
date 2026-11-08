# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]
### Changed
- Records are now **album sleeves**: the pickup is a sleeve (front + back cover); the vinyl only appears on the platter. While playing, the empty sleeve waits at the eject point (not pickupable); on eject it can be taken again. `MVS Sleeve` prefab + `MeijiVinylSystem/Record Sleeve` shader + sleeve mesh. Display sleeves can keep their own material (`applyLibraryArt` off).
- Library albums take an optional 2x2 **sleeve texture** (front / back / label / vinyl). Atlases are built in three sets (front, back, label; sizes per set). The platter vinyl shows the real label art.
- Spawning a new record puts the previous one away if nobody took it from the spawn point yet; a smaller pop-out distance.
- Album browser: drag (pull) the album grid sideways to change page (`pageScroller`, `_OnPageDrag`).

### Added
- **Side A / Side B** for multi-track records: `Has Side B` + `Side B starts at track` in the Library Editor; the side B label comes from the bottom-right quarter of the sleeve texture (4th atlas set `LabelB`).
- Side flip animation on the turntable (needle up, arm back, record lifted and turned over, needle down; `flipTime`). New player state `STATE_FLIPPING` (no eject while flipping), `FlipSide()`; a track change that crosses sides flips automatically. Now Playing shows the side and a Flip button on two-sided records.
- `Templates/MVS_SleeveTemplate.png`: 2048 sleeve texture template (front / back / label A / label B).
- `MVSUIProximity`: world UI pops in near the player and fades out further away (canvas + collider off while hidden).

### Fixed
- Multi-track records no longer skip a track when the VizVid object is disabled (VizVid 1.8.3+ reports a video end on disable).

### Added
- `MVSLibraryData` asset (Create > Meiji Vinyl System > Library): albums, artists, years, genres with stable ids, covers, single- or multi-track records.
- `MVSLibrary` (UdonSharp): read-only baked arrays + lookup helpers (`FindAlbum`, `GetTrackUrl`, `Matches`, ...).
- `MVSLibrarySource` (editor-only): links a library asset to an `MVSLibrary`; auto-bakes before Play Mode and VRChat builds.
- Library Editor window (`Tools > Meiji Vinyl System > Library Editor`): search, genre filter, problems filter, track list, cover preview, genre management, Check tab (missing data, duplicate URLs/names).
- `MVSPlayer` (UdonSharp, Manual sync): record on the platter, track, loop, insert/eject cooldown, needle-drop start delay; drives a dedicated VizVid `Core` (play/pause/seek/late-joiner sync stay in VizVid). Multi-track records: Next/Previous, auto-advance, whole-record loop. Owner-left recovery while the needle is dropping. Listener events `_MVSRecordChanged`, `_MVSTrackChanged`, `_MVSStateChanged`, `_MVSLoopChanged`, `_MVSPlaybackChanged`.
- `MVSDisc` (UdonSharp, no sync): a pickup record (VRCPickup + VRCObjectSync); pool records or display records with a fixed album; cover via MaterialPropertyBlock; teleport / return home / lock helpers.
- `MVSDiscManager` (UdonSharp, Manual sync): record pool (synced album per slot), spawn at a spawn point, recycle oldest idle record above a soft limit, hard limit warning, idle auto-return (pool → put away, display → shelf spot), Reset All (pool records only) and a separate ResetDisplayRecords, "in use" lookup across record players. Event `_MVSDiscsChanged`.
- Package art (no third-party assets): procedural 12" record mesh, `MeijiVinylSystem/Vinyl Record` shader (grooves + cover on the label), `MVS Record` prefab. `Tools > Meiji Vinyl System > Dev > Rebuild Record Assets`.
- Disc manager inspector: Create Pool Records, Collect Display Records.
- Spawned records glide out of the spawn point (`MVSDisc.PopOut`, `MVSDiscManager.spawnPopDistance/Time`).
- Local listening booth: `MVSBooth` (Manual sync: wearer id + cooldown) with `MVSHeadphones` (click to put on; follows the wearer's head, hidden for the wearer). For the wearer only: world music sources fade out and mute (AudioSource level, VizVid volume/persistence untouched), booth audio fades in; voice modes Voices On / Focus / Muffle / Silent via VRCPlayerApi voice settings (also applied to players who join). Taking off or leaving ejects the booth record. `MVSBoothPanel` UI.
- `MVSPlayer` booth mode (`Booth` field): only the headphone wearer can operate it (pair with an unsynced VizVid Core), `ForceEject`, listener left → booth freed, `IsBooth` / `CanControl` / `GetListenerName`.
- Album browser and Now Playing show "<name> is listening" for booth records; Now Playing hides controls for people who can't operate the player.
- UI logic components (style-neutral, TextMeshPro): `MVSAlbumBrowser` (genre tabs cloned per library genre + All + Unsorted, search, grid/list views with pages, click to take a record out, Playing/Out status, records-out count, limit notice, Reset All), `MVSAlbumCell`, `MVSGenreTab`, `MVSNowPlaying` (cover, title, artist, year/genre, track, status, time + seek bar, play/pause, previous/next for multi-track, loop, eject with cooldown), `MVSUIAnim` (press bounce + appear fade via VRCTween).
- `MVSTurntable` (UdonSharp, local presentation): insert by touching the deck's trigger with a held record; record copy drops onto the platter, platter spins up (33⅓ rpm), tonearm swings in and lowers with a needle-drop sound, needle follows song progress; eject reverses it and the real record pops out at an eject point (then returns home after the idle time). Model-agnostic (assign platter/tonearm transforms, axes, angles). Optional Animator bools `HasRecord` / `Spinning`. Late joiners snap to the current pose.
- `MVSDisc.SetParked`: hides a record (renderer, collider, pickup) while a turntable shows its copy.
- Cover atlas: covers are packed into 4096-wide PNG pages (default 256 px cells, ASTC 6x6 on Android/iOS) next to the library asset and rebuilt automatically before Play/Build when covers change. `MVSLibrary.GetCoverPage` / `GetCoverRect` / `HasCover` replace raw cover textures; records and UI sample the atlas (`_CoverRect`, `RawImage.uvRect`). Library Editor Check tab: cover size + rebuild.
- `MVSDebugPanel` (UdonSharp): developer test panel for `MVSPlayer` and `MVSDiscManager`.
- VizVid (`JLChnToZ.VVMW`) is now a required dependency of the runtime assembly.
- Assembly definitions: `MeijiVinylSystem.Runtime` (+ U# Assembly Definition) and `MeijiVinylSystem.Editor`.

## [0.1.0] - 2026-11-08
### Added
- Project scaffold: README, LICENSE (MIT), CHANGELOG.
- Editor menu `Tools > Meiji Vinyl System > Export .unitypackage`.
