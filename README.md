# OpenIGI

An open-source reimplementation of **Project I.G.I. — I'm Going In** (2000, Innerloop Studios), built
to run against the original retail game data. Written in C# on **.NET 10** with **Silk.NET** and an
OpenGL renderer.

> **You must own an original copy of the game.** OpenIGI ships **no** game assets and no original
> code. It reads the data files from your own installation, and every texture, model, sound and script
> it uses comes from there. Nothing in this repository is derived from Innerloop's art or binaries.

This is a clean-room reimplementation. Behaviour is established by reverse-engineering the original
executable and reproducing it in modern C# — a redesigned engine that behaves the same, not a
function-by-function transliteration.

## What runs today

A mission is playable end to end.

**Engine.** A fixed 30 Hz schedule built on absolute per-tick deadlines rather than a delta
accumulator, with the original's catch-up cap, one-render-per-tick latch and excluded-time stopwatch.
The simulation is a function of the tick index alone: the same run against clocks of 1, 5, 16 and
100 ms granularity draws different numbers of frames and computes identical state. On top of it sit
the QVM bytecode interpreter that runs the game's own scripts, the task tree the whole game is built
from, and the ILFF archive readers for every `.res`, `.mef` and `.fnt` a retail install contains.

**Gameplay.** The player walks, crouches, climbs ladders, rides wires, works doors and switches, and
fires the retail weapon set with its own tuning data. Guards see through the original's two view cones,
hear gunshots and impacts at the ranges the weapon tables declare, run their authored QVM behaviour
scripts, and raise the alarm chain. Missions carry their objectives, complete or fail through
`LevelFlow`, and unlock the next mission the way the original's campaign config does.

**Presentation.** A deferred HDR renderer with cascaded shadow maps, volumetric fog, TAA and temporal
upscaling, screen-space ambient occlusion, screen-space GI, screen-space reflections, bloom, colour
grading, depth of field and motion blur. A cinematic front end — main menu, campaign, settings,
profiles — drawn over a live mission backdrop, which is promoted into the mission itself when you press
Play rather than being thrown away and reloaded. The C-key map computer is there in both forms: the
retail handheld panel, and a full-screen tactical interface.

**Audio.** The desktop host plays mission sounds and music through the OpenAL backend.

**Not yet.** Only the desktop platforms are wired up; the mobile and web backends are planned
but not started.

## Quick start

Requires the **.NET 10 SDK**.

```sh
dotnet build OpenIGI.slnx
dotnet test  OpenIGI.slnx
```

**Put the build in your game folder** — the one holding `IGI.exe`, `common/` and `missions/` — and it
will find the data on its own, with nothing to configure. That is the whole installation procedure.

To run it from anywhere else, point it at the installation with the environment variable or the
`-game` switch:

```sh
# Windows (PowerShell)
$env:IGI_GAME_PATH = "C:\Games\Project IGI"
# Linux / macOS
export IGI_GAME_PATH="/path/to/Project IGI"

dotnet run --project apps/OpenIGI.Desktop
```

Tests that need real assets read the same variable and **skip** cleanly when it is unset, so a
checkout with no game data still builds green.

### Useful switches

| Switch | What it does |
|---|---|
| `-game <dir>` | Installation to read data from, ahead of `IGI_GAME_PATH` and of what is found beside the executable. |
| `-mission <n>` | Load straight into a mission, skipping the front end. |
| `-window`, `-size WxH` | Run windowed at a given client size. Borderless is the default. |
| `-fullscreen` | Take over the monitor and switch its display mode. |
| `-vanilla` | The whole remaster track off: retail menus and the original's render path. |
| `-difficulty <d>` | `easy`, `normal` or `hard`. |
| `-screenshot <f>` | Write a BMP of the rendered frame and quit. |

Passing an option the parser does not recognise prints the full list, which includes the capture,
camera-flight and menu-navigation options used for development.

### Graphics presets

Four presets — `vanilla`, `enhanced`, `high` and `ultra` — set the sixty-odd `r_*` variables together.
`vanilla` reproduces the original's picture and is the regression gate; the others add modern
rendering on top. Everything is reachable individually from the in-game console and from the settings
pages. Settings are written to a `settings.cfg` beside the executable if one exists, and to your user
profile otherwise, so a copied folder stays self-contained and an installed one stays writable.

### Derived surface detail

I.G.I. ships one diffuse texture per surface and nothing else, so the normal and roughness maps a
physically-based shading model needs are derived from those textures. This happens automatically: a
texture with no map derives one on a background thread the first time it is seen, and the result is
cached under your user profile, so the first run fills the cache and later runs read it. Nothing needs
to be downloaded and nothing is committed here — the maps come from your own installation's art.

`ResExtract maps` pre-bakes them in one pass if you would rather not pay for it as you play, and
`r_material_pack` points at the result.

## Repository layout

```
src/     Core Engine Formats Scripting Game Modding UI Frontend
         + Rendering/Audio/Input/Platform abstractions and their backends
apps/    OpenIGI.Desktop            composition root and executable
tools/   ResExtract, QvmDisasm      archive/texture extraction and QVM disassembly
tests/   xUnit projects, one per source project
```

The layering rule is that `OpenIGI.Engine` and `OpenIGI.Game` may reference only the abstraction
projects, never a concrete backend; backends are wired together in the desktop app.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). In short: preserve the determinism contract, keep the game
core free of backend dependencies, document every public member, and add golden tests for format
readers.

## Legal

Licensed under **GPL-3.0** — see [`LICENSE`](LICENSE).

This is a clean-room reimplementation created for interoperability with data you already own. "Project
I.G.I.", "I'm Going In" and related trademarks belong to their respective owners; this project is not
affiliated with or endorsed by them. No game assets, decompiler output or original source are included
in this repository, and contributions containing them will not be accepted.
