# OpenIGI

An open-source, cross-platform reimplementation of **Project I.G.I. — I'm Going In** (2000,
Innerloop Studios), compatible with the original retail game data. Built on **.NET 10** and
**Silk.NET**.

> **You must own an original copy of the game.** OpenIGI ships **no** game assets or original code —
> it reads the data files from your own legally obtained installation.

## Status
Early. **M1 (foundations) is done**: shared math with the original's truncation semantics, an ILFF
archive reader that parses every `.res`, `.mef` and `.fnt` of a retail install, the virtual file system
with its alias/device model, the resource registry, the config-variable registry, and the `ResExtract`
CLI. Gameplay is not implemented yet — work is tracked milestone by milestone in
[`TODO.md`](TODO.md).

## Goals
- **1:1 game logic** with the original (timing, math, behaviour), rewritten as clean, modern C# —
  redesigned systems, not a function-by-function translation.
- **Renderer decoupled and swappable** behind abstraction interfaces.
- **Cross-platform**: Windows, macOS, Linux, Web (WASM/WebGL2), Android, iOS.
- **Moddable** via C# plugin assemblies and data-file overrides.
- **Enterprise-grade**: SOLID/DRY, XML docs on every public member, zero warnings.

## Platform matrix
| Platform | Backend | Status |
|---|---|---|
| Windows / macOS / Linux | OpenGL 3.3 core (Silk.NET) | primary target |
| Android | GLES 3.0 | planned (M14) |
| iOS | GLES 3.0 (NativeAOT) | planned (M14) |
| Web | WebGL2 (WASM) | planned (M14) |

## Repository layout
```
src/     OpenIGI.Core Engine Formats Scripting Game Modding
         + Rendering/Audio/Input/Platform abstractions and their backends
apps/    OpenIGI.Desktop        (composition root / executable)
tools/   ResExtract, QvmDisasm  (CLI utilities)
tests/   xUnit test projects
docs/    architecture.md, formats/ (file-format specs)
```
See [`docs/architecture.md`](docs/architecture.md) for the layering rules and
[`PLAN.md`](PLAN.md) for the full plan.

## Building
Requires the **.NET 10 SDK**.
```sh
dotnet build OpenIGI.slnx
dotnet test  OpenIGI.slnx
```

### Running against your game data
Tests and tools that need real assets read the `IGI_GAME_PATH` environment variable (the folder
containing `IGI.exe`, `common/`, `missions/`). Tests **skip** cleanly when it is unset.
```sh
# Windows (PowerShell)
$env:IGI_GAME_PATH = "C:\Games\Project IGI"
# Linux/macOS
export IGI_GAME_PATH="/path/to/Project IGI"
```

## Contributing
See [`CONTRIBUTING.md`](CONTRIBUTING.md). In short: match the determinism contract, document public
members, keep the game core free of backend dependencies, and add golden tests for format readers.

## Legal
Licensed under **GPL-3.0** ([`LICENSE`](LICENSE)). This is a clean-room reimplementation created for
interoperability with data you already own. "Project I.G.I." and related trademarks belong to their
respective owners; this project is not affiliated with or endorsed by them.
