# OpenIGI

**An open-source engine for _Project I.G.I.: I'm Going In_ (2000) and _I.G.I. 2: Covert Strike_ (2003).**

OpenIGI is a clean-room reimplementation of Innerloop Studios' engine, written in C# on .NET 10. It runs the original
games from your own retail installation. The original missions, scripts and AI play as they did in 2000, and a
modern renderer, launcher and modding platform are built around them.

[openigi.com](https://openigi.com) · [Releases](https://github.com/OpenIGI/OpenIGI/releases) ·
[Modding guide](sdk/README.md) · [Contributing](CONTRIBUTING.md)

> **You need your own copy of the game.** OpenIGI contains no game assets and none of the original code. Every
> texture, model, sound and script comes from the installation you point it at.

## Highlights

- **Faithful to the original.** The game's own QVM scripts run on a reimplemented interpreter, over a deterministic
  30 Hz simulation that reproduces the original's timing, AI perception, weapon tables and mission flow. A
  `vanilla` mode reproduces the original picture and is held pixel for pixel as a regression gate.
- **A modern renderer.** Deferred HDR rendering with cascaded shadows, real-time global illumination, volumetric
  fog, screen-space reflections, temporal upscaling and hardware ray tracing on capable GPUs. NVIDIA DLSS (Super
  Resolution, DLAA, Ray Reconstruction and Frame Generation) and Reflex are offered as an optional download.
- **Direct3D 12, Vulkan and WebGPU.** One renderer runs on all three. It runs natively on Windows, Linux and macOS,
  and in the browser.
- **A launcher.** It finds your games, installs and updates OpenIGI on the stable or beta channel, and manages
  content packs and mods.
- **Moddable.** You can replace art by dropping files in a folder, install content packs with a load order, or write
  code mods in C# against a versioned API, published through a community index and built from source.

## Status

| | Project I.G.I. | I.G.I. 2 |
|---|---|---|
| Campaign | All 14 missions | All 19 missions load and play; in development |
| Released builds | Yes | Not yet, run from source |
| Browser | First 6 missions, original graphics | No |
| Multiplayer | No | No |

| Platform | Graphics API | Hardware ray tracing |
|---|---|---|
| Windows 10/11 x64 | Direct3D 12 (default), Vulkan | Yes, on DXR 1.1 / RTX- and RDNA2-class GPUs |
| Linux x64 | Vulkan | Yes, on RTX- and RDNA2-class GPUs |
| macOS 13+ (Apple silicon) | Vulkan through MoltenVK, included | No: shadows, reflections and GI use their screen-space and shadow-map forms |
| Browser | WebGPU | No |

## Playing

**Desktop.** Install the launcher from [openigi.com](https://openigi.com). On Windows that's a download. On macOS and
Linux run:

```sh
curl -fsSL https://openigi.com/install.sh | bash
```

The launcher looks for your game installations, or you can point it at them in Settings. It installs OpenIGI and
keeps it up to date. Switch to the **beta** channel in Settings for builds from the development branch.

**Browser.** Open [openigi.com](https://openigi.com) in a browser with WebGPU and choose your Project I.G.I. folder.
The files are read on your machine and never uploaded.

## Building from source

You need the **.NET 10 SDK** (the version is pinned in [`global.json`](global.json)).

```sh
dotnet build -c Release OpenIGI.slnx
dotnet test  -c Release OpenIGI.slnx
```

Tests that need retail data read `IGI_GAME_PATH` and `IGI2_GAME_PATH`, and skip themselves when those are unset. A
checkout with no game data still builds and tests clean.

Some optional native components are fetched into `third-party/` rather than committed:

| Script | Needed for |
|---|---|
| `tools/fetch-wgpu-native.ps1` | The desktop WebGPU backend |
| `tools/fetch-streamline.ps1` | DLSS and Reflex in development builds |
| `tools/fetch-dxc.ps1` | Regenerating the committed DXIL shaders |

### Running

Copy the build into the game folder and run it there, or tell it where the game is:

```sh
export IGI_GAME_PATH="/path/to/Project IGI"      # PowerShell: $env:IGI_GAME_PATH = "C:\Games\Project IGI"
export IGI2_GAME_PATH="/path/to/IGI 2"
dotnet run -c Release --project apps/OpenIGI.Desktop -- -title igi1
```

| Option | Effect |
|---|---|
| `-title igi1\|igi2` | Which game to run |
| `-game <dir>` | The installation to read, ahead of the environment variables |
| `-mission <n>` | Start in a mission, skipping the menus |
| `-window`, `-size WxH`, `-fullscreen` | Window mode. Borderless is the default |
| `-difficulty easy\|normal\|hard` | Difficulty |
| `-vanilla` | The original menus, picture, pacing and audio |
| `-content <dir>` | Load a content pack folder |
| `+<command>` | Run a console command at startup, for example `+gfx_preset ultra` |

`-help` lists every option, including the capture and camera tools used in development.

## Graphics

The graphics presets are `vanilla`, `enhanced`, `high` and `ultra`. Each one sets the renderer's variables together;
every setting can also be changed on its own in the settings pages or the console (`` ` ``).

The graphics API is chosen automatically: Direct3D 12 on Windows (when the GPU supports shader model 6.6), Vulkan
elsewhere. Override it with `r_renderer`, or press **F10** to switch APIs while the game runs.

DLSS and Reflex need an NVIDIA RTX GPU on Windows. Install them from the launcher's Content page (**NVIDIA DLSS**).
Without them the engine's own temporal upscaler runs.

Settings are saved to `settings.cfg` beside the executable if one exists there, otherwise to your user profile:
`%APPDATA%\OpenIGI`, `~/.config/OpenIGI` or `~/Library/Application Support/OpenIGI`.

## Modding

- **Replace a file.** Put it under `<game>/overrides/` at the path it has in the game, for example
  `overrides/common/textures/000_04_1.tex`. A `.res` archive there works too. The `overrides` console command reloads
  the folder, and **F8** toggles it.
- **Content packs.** These are folders of replacement art with a load order, installed and ordered from the
  launcher's Content page. The tools to build one from your own installation, such as an AI-upscaled texture pack,
  are `tools/texture-forge` and `tools/OpenIGI.Tools.ResExtract`.
- **Mods.** A mod can ship files that stand in for the game's, whole new missions and campaigns, C# code, and shader
  passes over the picture, before the tone map or after it. Its code says what it changes with attributes
  (`[AddsLevelSystem]`, `[OnGameEvent]`, `[ReplacesTask]` and the rest), and listens to the games' events, adds settings,
  HUD overlays and task types. A save records the mods it was made with, and a mod's tests are built with the same SDK
  against a harness that configures it as the game does. Start one with the template (`--kind code`, `data` or
  `shader`):

  ```sh
  dotnet new install OpenIGI.Modding.Templates
  dotnet new openigi-mod -n MyMod --modId my-mod
  ```

  Mods are published through a community index and built by its CI from a pinned commit of their public source, never
  from an author's binaries. The launcher's Mods page installs them. See the [modding guide](sdk/README.md).

## Repository layout

```
apps/       OpenIGI.Desktop (the game), OpenIGI.Launcher, OpenIGI.Web (the browser build)
src/
  Core/       primitives, settings, retail file formats, the QVM interpreter, release manifests
  Engine/     the game host and frame loop, virtual file system, mod API and loader
  Game/       gameplay shared by both games, each game's own gameplay and presentation, the menus
  Rendering/  the renderer, its hardware interface, the D3D12/Vulkan/WebGPU backends, shaders, DLSS
  Audio/      OpenAL and Web Audio
  Platform/   windowing and input for desktop and browser
  UI/         the UI framework used by the menus and the launcher
services/   the update and content service behind api.openigi.com
sdk/        the modding SDK, the project template and the community index's rules
tools/      asset extraction, QVM disassembly, shader build, release tooling
tests/      xUnit test projects
```

Nothing under `src/` may reference a concrete backend; the applications wire backends in. The architecture tests in
`tests/OpenIGI.Core.Tests/Architecture` enforce this layering.

## Contributing

Contributions are welcome. [`CONTRIBUTING.md`](CONTRIBUTING.md) covers the determinism rules, the clean-room policy,
which retail build is the reference, and how changes are tested. In short: open a pull request against `develop`, keep
the Release build free of warnings, and keep the test suite passing. `tools/install-hooks.sh` installs the same
checks as git hooks. [`RELEASING.md`](RELEASING.md) describes how releases reach the launcher and the website.

## Legal

OpenIGI is licensed under the [GNU General Public License v3.0](LICENSE). Third-party components are listed in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

OpenIGI is a clean-room reimplementation for use with game data you already own. It is not affiliated with or endorsed
by the owners of _Project I.G.I._ or _I.G.I. 2_. All trademarks belong to their respective owners. The repository
contains no game assets, original source code or decompiler output, and contributions that include them are not
accepted.
