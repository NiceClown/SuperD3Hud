# SuperD3Hud

**English** | [Русский](README.ru.md)

A standalone local launcher, plugin host, Diablo III-styled map overlay, built-in HUD, and smart town macro engine for **Diablo III** (`2.8.0.99920`). Combines game launching, TurboHUD-compatible `.cs` plugins, 60 FPS overlay rendering, and configurable town/rift automation in a single portable application.

> **Important Notice.** SuperD3Hud is an independent project. It does not decrypt protected plugins, does not bypass subscription or license checks, and never contacts any third-party servers. Everything runs locally on your machine and manages open `.cs` plugins only. Commercial third-party plugins still require a valid license from their respective authors.
>
> *Note on UI localization: the desktop launcher interface and default macro/HUD labels are currently in Russian, while all source code, CLI diagnostics, and TurboHUD plugin APIs are in standard English / C#.*

## What's New in 1.8.0

- **Diablo III Visual Theme & Smooth 60 FPS Animations**
  - **Seamless parchment-and-gold map silhouette:** replaced cold blue approximation rectangles and internal navmesh tile seams with a baked world-space rasterized silhouette (`LockBits` morphological contour pass) featuring warm smoky parchment floors, crisp dark obsidian halos, antique-gold walls/pillars, and a soft circular minimap edge vignette.
  - **Critically-damped 60 FPS smoothing:** player, monster, and loot tracking use high-resolution `Stopwatch` exponential smoothing (with instant snapping on teleports/zone transitions > 35 yards) instead of overshoot-prone linear extrapolation.
  - **Zero-allocation gothic HUD overlay:** `HudOverlay` now reuses a persistent DIB-backed layered window surface across frames (eliminating per-frame full-screen bitmap allocations), with smooth Rift/Greater Rift progress bar damping + sheen sweep, 100% Rift Guardian glow pulse, and skill ready-flash animations.
  - **Diablo III launcher theme & TurboHUD UI calibration:** updated launcher controls (`Ui`, `DashboardPage`, `MapPage`) to an obsidian and hammered bronze-gold palette; added accurate resolution-scaled UI rectangles for Health/Resource globes, latency meter, portrait-bottom, and clock elements, and fixed bold Cyrillic text width measurement in `GroundLabelDecorator` boxes.
- **Smart Memory-Aware Town Automation & Item Protection**
  - **Aspect-ratio-aware macro anchoring:** added `MOVEL` (left-anchored dialogs: Blacksmith, Obelisk, Kadala, Urshi, Cube), `MOVER` (right-anchored inventory), and `MOVEC` (centered popups/drop point) so scripts scale cleanly across 16:9, 16:10, and 21:9 aspect ratios.
  - **Smart macro DSL commands:** added memory-aware auto-salvage (`SMARTSALVAGE`), smart drop (`SMARTDROP`), Kadala slot-threshold gambling (`SMARTGAMBLE`), normal/Empowered portal opening & acceptance (`OPENRIFT`, `OPENGR`, `ACCEPTGR`), gem upgrades (`GEMUPS`), and Kanai's Cube rare upgrades (`CUBEUPGRADE`), protecting Ancient, Primal, and locked left/right backpack columns.
  - **Emergency abort:** pressing `Esc` or moving the mouse more than `180 px` away from the macro's cursor target cancels running macro loops immediately.
- **Portable Installer (`SuperD3Hud-Setup.exe`)**
  - Single-file self-extracting installer bundling the launcher, `Turbo.Plugins.dll`, `Turbo.Plugins.Default.dll`, the Roslyn compiler (for local `.cs` plugin compilation), demo plugins, and default settings. Everything stays inside the chosen folder with no external registry or system paths required.
- **Accurate Tab Large Map & Full Level Reveal**
  - The full-screen (`Tab`) overlay centers on the hero and scales by window height to match the native Diablo III map overlay across resolutions, and combines active scenes with reveal-cache geometry for visited areas.
  - Filtered interactive markers strictly to level gizmos (pylons, shrines, doors) and confirmed rift exits, preventing ground loot from cluttering the map.

<details>
<summary>Earlier Changelog (1.7.0 – 1.5.0)</summary>

### 1.7.0
- Added a TurboHUD-style **Group Damage Meter** (total damage, DPS, and per-player damage share estimated from monster HP drops) and rift exit / pylon pulsing markers.
- Recalibrated world-to-ground ellipse compression so circles align directly under monsters' feet.
- Disabled noisy debug overlays (FPS/memory/experience zeros and overlapping centered rift notifications) by default.

### 1.6.0
- Added **Simple Mode** (5 core tabs: Dashboard, Map, Screen HUD, Plugins, Macros) with **Advanced Mode** toggle for Profiles, `hud.ini`, XML/Pickit files, and About.
- Redesigned the Plugins search/toggle workflow and the split Hotkey + Script Macros editor.
- Added 60 Hz layered direct-memory surface rendering and player/marker smoothing.

### 1.5.0 – 1.5.2
- Built an independent, open-interface **TurboHUD plugin API host** (`Turbo.Plugins.dll` + `Turbo.Plugins.Default.dll`) supporting `IAfterCollectHandler`, `IBeforeRenderHandler`, `IInGameTopPainter`, and `IInGameWorldPainter` without `TurboHUD.exe`.
- Fixed 64-bit double-indirection actor container reading, human-readable item/power names, internal buff filtering, `--turbohud-live`, `--hud-preview`, and `--map-preview`.

</details>

## Features

- **Dashboard** — Auto-detects Diablo III and any optional external HUD engine, launches either or both, and displays live process, build, and map reader status.
- **Map Overlay** — Standalone read-only memory reader supporting native minimap overlay (click-through), movable panel mode, and `Tab` full-screen map mode (or manual `F9` toggle) without code injection or external engines.
- **Built-in HUD Overlay** — Independent HUD showing active buffs (with stacks and Convention of Elements rotation), skill cooldowns, health, backpack slot count, damage meter, rift progress bar, elite/boss circles, affix badges, and legendary loot indicators.
- **Plugins Manager** — Separate searchable lists with checkboxes, descriptions, notes, and per-plugin JSON settings for:
  - **SuperD3Hud local plugins** (`plugins-local\*.cs`, inheriting `HudPlugin`)
  - **TurboHUD plugins** (`plugins-turbohud\*.cs` + built-in default plugins compiled via Roslyn)
  - **External HUD engine plugins** (move between `plugins` and `plugins.disabled`, import user `.cs` files)
- **Macros & Smart Town Automation** — Global hotkeys (`RegisterHotKey`), aspect-ratio-aware script commands, humanized 1–5 ms click jitter, memory-aware item protection (Ancient/Primal + locked backpack columns), and instant Esc/mouse-move abort.
- **HUD Config & Files** — Optional editors for `hud.ini` (with backups and `app.config` Roslyn path repair), `config.xml`, `hotkeys.xml`, and `pickit_sc_70.ini` / `pickit_hc_70.ini`.
- **Profiles & Tray** — Local ZIP profile import/export with zero cloud dependencies, plus minimize-to-tray support while keeping global hotkeys active.

The only optional network request is checking public GitHub Releases on your configured repository. There is no telemetry, no analytics, and no account login. All settings stay in `config/` next to `SuperD3Hud.exe`.

## Requirements

- Windows 10 / Windows 11 (x64)
- .NET Framework 4.8 (built into Windows)
- Diablo III x64 client (build `2.8.0.99920` for memory features; game path can be auto-detected or browsed manually)
- Optional: a separate external HUD engine executable if you want to manage it through the launcher

## Installation (Portable)

1. Copy `SuperD3Hud-Setup.exe` into any folder (e.g. `C:\SuperD3Hud`).
2. Run it to unpack the launcher, `Turbo.Plugins.dll`, `Turbo.Plugins.Default.dll`, SharpDX libraries, bundled Roslyn compiler, examples, and default configuration in place.
3. Launch Diablo III and play — the launcher finds the game automatically and compiles local `.cs` plugins using the bundled compiler.

To build the self-extracting installer from source:

```powershell
git clone https://github.com/NiceClown/SuperD3Hud.git
cd SuperD3Hud
.\make-installer.ps1
```

## Building from Source

```powershell
git clone https://github.com/NiceClown/SuperD3Hud.git
cd SuperD3Hud
.\build.ps1
```

Output binary: `bin\SuperD3Hud.exe`.

### CLI Diagnostics & Headless Verification

All diagnostic checks can run from the command line without opening the GUI:

```powershell
.\bin\SuperD3Hud.exe --selftest             # Verify config, plugins, macros, math, and map rendering
.\bin\SuperD3Hud.exe --functional           # Sandboxed settings & macro serialization test
.\bin\SuperD3Hud.exe --map-check            # Inspect live Diablo III scenes, navcells, and player position
.\bin\SuperD3Hud.exe --map-preview out_map  # Render synthetic minimap/panel/large-map PNGs
.\bin\SuperD3Hud.exe --hud-preview out_hud  # Render synthetic built-in HUD PNG
.\bin\SuperD3Hud.exe --plugins              # Compile and execute local SuperD3Hud plugins
.\bin\SuperD3Hud.exe --turbohud             # Compile TurboHUD plugins and render a test frame
.\bin\SuperD3Hud.exe --turbohud-live        # Verify TurboHUD API data against running Diablo III
.\bin\SuperD3Hud.exe --describe             # Dump resolved plugin descriptions
.\bin\SuperD3Hud.exe --cooldowns            # Dump player actor cooldown attributes
.\bin\SuperD3Hud.exe --game-check           # Dump live monsters, elites, buffs, health, and backpack
.\bin\SuperD3Hud.exe --render out_ui        # Render all launcher tabs to PNGs
```

`--selftest` writes its detailed report to `bin\config\selftest.txt`.

## Quick Start

No setup is required out of the box:

1. Open **SuperD3Hud** (using `bin\SuperD3Hud.exe` or the desktop shortcut `SuperD3Hud.lnk`).
2. Click **Запустить игру** (Launch Game) or start Diablo III normally.
3. Play — the minimap overlay, `Tab` map, TurboHUD plugins, and skill cooldowns connect automatically.

By default, Simple Mode displays the five main sections (**Dashboard**, **Map**, **Screen HUD**, **Plugins**, **Macros**). Enable **Расширенные настройки** (Advanced Settings) at the bottom of the sidebar to unlock Profiles, `hud.ini`, HUD Files, and About.

## Map Overlay

SuperD3Hud reads scene geometry directly from Diablo III memory:

- **Strictly read-only:** opens the process with `PROCESS_VM_READ` only — never writes memory, injects DLLs, or suspends threads.
- **Build verification:** checks the executable SHA-256 hash and scene iterator signature (`2.8.0.99920`) before reading pointers, failing closed on mismatched builds.
- **Three display modes:** native minimap click-through overlay, standalone movable panel, and full-screen `Tab` map (also toggleable via `F9` and automatically hidden when opening the `M` waypoint/bounty map).
- **60 FPS smoothness:** smooth `Stopwatch`-damped player and marker motion paired with baked seam-free parchment/gold wall silhouettes.

## Built-in HUD & Cooldowns

| Feature | Data Source |
|---|---|
| Buff list, stack counts, Convention of Elements phase | Game memory (verified attribute keys) |
| Health & Rift / Greater Rift progress | Game memory (hero attributes & actor/area tracking) |
| Backpack slot counter (`Used / 60`) | Game memory (`InventoryModule`) |
| Elites, bosses, affixes, and Rift Guardians | Game memory (rarity, SNO, and power-to-affix mapping) |
| Legendary / Set / Ancient / Primal ground loot | Game memory (quality, rank, and game balance table names) |
| Skill cooldowns (`Q W E R` / mouse slots) | Direct memory ticks (`0x272` / `0x252` + equipped SNOs) with keypress fallback |

### Cooldown Detection

Diablo III stores per-skill cooldown end and start ticks using `(powerSno << 12) | descriptor` attributes (`0x272` and `0x252` on 64-bit `2.8.0.99920`, synchronized with the client's internal 60-tick game clock at `root + 0x20`). When exact attributes are not found or a custom client schema differs, the HUD automatically falls back to descriptor auto-discovery and keypress estimation.

### Current Memory Reader Scope

- **Maximum health and secondary resources** (Fury, Hatred, Discipline, Mana, Spirit, etc.) remain unset (`-1`) unless verified on the target build rather than estimating fake numbers.
- **Item names** come from the in-memory item balance table and fall back cleanly to item quality/rank labels if unavailable.

## Default Macro Hotkeys

| Hotkey | Action | Description |
|---|---|---|
| `F1` | `Salvage` | Smart blacksmith salvage (bulk Common/Magic/Rare + unlocked non-protected Legendaries) |
| `Shift+F1` | `CubeUpgrade` | Kanai's Cube rare-to-legendary upgrade loop (respects lock columns & item protection) |
| `F2` | `InvDrop` | Smart inventory drop to ground (`SMARTDROP`), skipping protected items |
| `F3` | `Gamble` | Rapid Kadala right-click gambling (`SMARTGAMBLE`), stops at free-slot threshold |
| `F4` | `LeftClick` | Single left-click spam trigger at current cursor position |
| `F5` | `OpenGR` | Open Greater Rift at the Obelisk (honors `EmpoweredGR` toggle) |
| `Shift+F5` | `OpenRift` | Open standard Nephalem Rift at the Obelisk |
| `Ctrl+F5` | `AcceptGR` | Accept Greater Rift entry popup |
| `F6` | `GemUps` | Click Urshi's Legendary Gem upgrade button (`1–5` times, configurable) |
| `F7` | `Pause` | Pause / resume macro execution |
| `F8` | `LeaveGame` | `Esc` -> Leave Game |

All hotkeys, smart protection toggles (`SmartMode`, `SalvageBulkButtons`, `SalvageLegendaries`, `ProtectAncient`, `ProtectPrimal`, `LockedLeftColumns`, `LockedRightColumns`, `EmpoweredGR`, `GemUpgradeCount`, `GambleStopFreeSlots`, `AbortOnMouseMove`), and custom scripts are editable on the **Macros** tab.

## Macro Script DSL

Scripts are plain text, one command per line (`#` starts a comment). Reference coordinates use a **2560×1440** client area and automatically scale across arbitrary resolutions and aspect ratios (`16:9`, `16:10`, `21:9`). Legacy `1920×1080` scripts are migrated automatically on load.

| Command | Description |
|---|---|
| `FOCUS` | Bring the Diablo III window into focus |
| `MOVE x y` | Move cursor using automatic 2560×1440 region mapping |
| `MOVEL x y` / `MOVER x y` / `MOVEC x y` | Move cursor anchored to left edge / right edge / screen center |
| `CLICK [right]` / `CLICKR` | Left-click (or right-click) at current cursor position |
| `KEY vk` | Press virtual key code or name (`ENTER`, `ESC`, `F1`, `I`, etc.) |
| `SLEEP ms` | Sleep with ±8% humanized jitter |
| `GRID x y dx dy cols rows [key]` | Click a grid of cells, optionally double-pressing `key` after each |
| `GRIDDROP x y dx dy cols rows dropX dropY` | Pick up each cell in a grid and drop at `(dropX, dropY)` |
| `SMARTSALVAGE` | Smart blacksmith salvage using live inventory memory and lock/rarity protection |
| `OPENGR [empowered]` | Open Greater Rift at Obelisk (uses `EmpoweredGR` or optional argument) |
| `OPENRIFT` | Open Nephalem Rift at Obelisk |
| `ACCEPTGR` | Click the centered Greater Rift Accept button |
| `GEMUPS [count]` | Perform `count` (1–5) Urshi gem upgrades |
| `SMARTGAMBLE [maxClicks]` | Right-click at cursor until `FreeSlots <= GambleStopFreeSlots` |
| `SMARTDROP` | Drop only unlocked, non-protected items from backpack |
| `CUBEUPGRADE [maxRuns]` | Automate Kanai's Cube rare upgrades with item protection |

### Humanized Timing & Safety

- Default inter-step delay is **1–5 ms** randomized around the configured average (`timeBeginPeriod(1)` is enabled during active macros so Windows honors sub-15 ms sleeps; mouse and key hold durations vary naturally between ~9–21 ms).
- Press `Esc` or move the cursor > `180 px` from the macro position to abort any running loop immediately.

## TurboHUD Plugin API Compatibility

SuperD3Hud embeds an independent compatibility layer (`Turbo.Plugins.dll` built from the open `compat/interfaces` definitions + `Turbo.Plugins.Default.dll` built from open `BasePlugin`/default helpers):

- Compiles user `.cs` plugins placed in `plugins-turbohud\` locally using Roslyn at startup.
- Supports `IAfterCollectHandler`, `IBeforeRenderHandler`, `IInGameTopPainter`, and `IInGameWorldPainter`.
- Provides `IController` implementations (`Game`, `Render`, `Sno`, `Inventory`, `Input`, `Queue`, `SceneReveal`, `Avoidance`, `Interaction`, `Target`, `ActorQuery`, `Window`, `Time`, `Sound`, `Tracker`, `TextLog`) backed by SuperD3Hud's read-only snapshots.
- Sample API plugins (`Stage1Demo.cs` – `Stage4Demo.cs`, `SuperD3HudStatusPlugin.cs`) ship in `examples/turbohud/`.

## Project Structure

```text
SuperD3Hud/
  build.ps1                Compiles SuperD3Hud.exe via .NET Framework 4.x csc.exe
  make-installer.ps1       Builds portable SuperD3Hud-Setup.exe
  installer/
    Setup.cs               Self-extracting portable installer source
  src/
    Program.cs             Entry point, CLI diagnostics, --selftest & --render
    AppPaths.cs            Path resolution and persistent settings.ini
    HudEngine.cs           External HUD engine discovery & status checks
    IniFile.cs             Comment-preserving INI reader/writer
    Models.cs              Macro settings, hotkeys, and smart automation defaults
    JsonStore.cs           Safe JSON load/save helpers
    InputSender.cs         Win32 input synthesis, cursor/window geometry helpers
    MacroEngine.cs         Macro DSL parser, aspect-aware scaling, smart execution
    HotkeyManager.cs       Global RegisterHotKey management
    PluginManager.cs       Plugin scanning and enable/disable management
    ProcessRunner.cs       Process launch & app.config Roslyn repair
    ProfileManager.cs      Local ZIP settings profiles
    GameMemory.cs          Read-only PROCESS_VM_READ process wrapper
    MapReader.cs           Scene, navcell asset, and reveal-cache reader
    MapGeometry.cs         Quaternion transforms, isometric projection, player reader
    MapService.cs          Background map capture service with persistent asset cache
    MapOverlay.cs          Layered 60 FPS parchment/gold silhouette & marker overlay
    MapTests.cs            Geometry, visual style, and projection tests
    LargeMapTracker.cs     Low-level keyboard hook & Tab/M large map tracker
    Ui.cs                  Obsidian & bronze-gold Diablo theme and shared controls
    MainForm.cs            Main launcher window, navigation, tray icon, 60 Hz tick
    DashboardPage.cs       Dashboard tab
    MapPage.cs             Map configuration tab
    HudPage.cs             hud.ini editor tab
    HudFilesPage.cs        config.xml, hotkeys.xml, pickit editor tab
    PluginsPage.cs         Plugin manager tab
    MacrosPage.cs          Hotkeys, smart automation settings, and script editor tab
    ProfilesPage.cs        Profile import/export tab
    AboutPage.cs           About & GitHub release updater tab
    game/                  Actor, Buff, Cooldown, Damage, Inventory, Skill & HudOverlay
    plugins/               Local HudPlugin compiler & runtime
    turbohost/             TurboHUD public API controller implementations
    turbohud/              TurboHUD plugin compiler & frame runtime
  compat/
    interfaces/            Public Turbo.Plugins interface definitions
    baseplugin/            BasePlugin & ValueFormat sources
    sharpdx/               Bundled SharpDX assemblies (MIT)
    build-api.ps1          Builds Turbo.Plugins.dll
    build-default.ps1      Builds Turbo.Plugins.Default.dll
  examples/turbohud/       Reference plugin examples
  tools/                   Catalogue/stub generators and verification scripts
```

## Security & Disclaimers

- SuperD3Hud contains **no** license bypasses, memory writes, DLL injection, or binary patches.
- Protected third-party HUD engines refuse direct startup without their own loader; SuperD3Hud detects this and does not attempt to bypass it.
- Macros send synthetic keyboard/mouse input to the active window. Use them responsibly and at your own risk.
- Some antivirus tools flag unsigned utilities that synthesize input or use `ReadProcessMemory`; all source code is open in `src/` so you can inspect and compile everything yourself.

## Updates & Shortcut

- **GitHub Updates:** Go to **О программе** (About), enter `NiceClown/SuperD3Hud` in **Репозиторий обновлений**, and click **Проверить сейчас**, or run `.\bin\SuperD3Hud.exe --check-updates owner/repo`.
- **Desktop / Folder Shortcut:** `SuperD3Hud.lnk` with the embedded icon is created automatically next to the program folder on startup and can be recreated from the About tab.

## License

Source code of SuperD3Hud is licensed under the **MIT License** (see [LICENSE](LICENSE)). Third-party tools and game clients (Diablo III, external HUD engines) belong to their respective owners and are not included in this repository.
