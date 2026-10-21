# Modding OpenIGI

A mod is a folder holding a `mod.json`, and usually an assembly built against the modding API. The game's mod loader
plays the mods the player switched on for a game, in the order they chose. Players install mods from the launcher's
**Mods** page. Authors publish them through the [mods index](mods-index/README.md).

## Making a mod

```sh
dotnet new install OpenIGI.Modding.Templates
dotnet new openigi-mod -n MyMod --modId my-mod --modAuthor "Your name"
cd MyMod && dotnet build -c Release
```

The project references `OpenIGI.Modding.Sdk`, one package that brings:

- the modding API;
- both games' gameplay and presentation assemblies to build against;
- the build that writes `bin/Release/net10.0/mod/`, the folder as players install it.

The SDK's version is the game build the mod is built against. A mod built against version V works with game builds
from V on that still offer its `apiVersion`.

Nothing of the game's goes into the mod's folder. At run time a mod is given the game's own assemblies, and any library
the game ships, so its types are the game's. The folder carries:

- its `mod.json`;
- its assembly, symbols and dependency manifest;
- the files the project copies to its output (its own data);
- the assemblies of the packages the project references itself.

### The code

```csharp
[assembly: OpenIgiMod(typeof(MyMod.Mod))]

public sealed class Mod : IOpenIgiMod
{
    public void Configure(IModBuilder builder)
    {
        builder.AddModule(new MyModule());                        // task types, natives, services
        builder.ReplaceTask("HumanSoldier", () => new MySoldier());
        builder.MountTitleData(GameTitle.Igi2, "IGI2");           // read another game's files as IGI2:path
        builder.EditLevelSystems<MissionLevelBuild>(systems =>    // run a step of its own as each level comes up
            systems.InsertAfter(MissionSystemNames.Player, new LevelSystem<MissionLevelBuild>("MyMod.Step", build => { })));
    }
}
```

`Configure` runs once for each game the mod is for, before that game loads anything. `builder.Directory` is the mod's
own folder, for data it ships. A mod that also draws implements `IOpenIgiPresentationMod` (`OpenIGI.Modding.Presentation`).
[`mods/OpenIGI.Mods.Igi2Soldiers`](../mods/OpenIGI.Mods.Igi2Soldiers) is a full example: Project I.G.I.'s missions with I.G.I. 2's soldiers.

### `mod.json`

```jsonc
{
  "id": "my-mod",                       // lowercase letters, digits and dashes; unique among published mods
  "name": "My mod",
  "version": "1.0.0",                   // major.minor.patch
  "apiVersion": 1,                      // the modding API it was written against
  "summary": "What it does, in a line.",
  "authors": ["Your name"],
  "homepage": "https://github.com/you/my-mod",
  "license": "MIT",
  "titles": ["igi1"],                   // the games it is for; empty for every game
  "requiresTitleData": ["igi2"],        // other games whose installed data it reads
  "dependencies": { "other-mod": ">=1.2" },   // mods it needs played with it, each with the oldest version that will do
  "conflicts": ["rival-mod"],           // mods it cannot be played with
  "minGameVersion": "0.9.0",            // the oldest game build it works with; the launcher checks it
  "assembly": "MyMod.dll",              // its code; leave out for a mod of content alone
  "affectsSimulation": true             // whether it changes what happens in play, not only how it looks or sounds
}
```

## What the loader does

- **Which mods:** the ones the launcher's profile (`OPENIGI_MOD_PROFILE`) switches on for the game being played, in
  its order. A later mod's changes are made over an earlier one's.
- **Without a profile:** every folder holding a `mod.json` under `mods/` beside the game, or under the folder named in
  `OPENIGI_MODS`, in the order of their names. `OPENIGI_MODS` naming a folder wins over the profile, so an author can
  try a build from the launcher. `OPENIGI_MODS=off` plays the games as they ship.
- **Left out:** a mod is left out, with the reason in the log (`OPENIGI_LOG=Mods=information`), when:
  - it cannot be read or loaded;
  - it was written for another `apiVersion`;
  - it reads a game that is not installed;
  - it conflicts with a mod before it;
  - it needs a mod that is not played or is too old.

  Any mod that needed a mod left out is left out too.
- **Isolation:** each mod with code loads into its own assembly load context. It shares the game's assemblies and
  libraries, and keeps its other dependencies to itself.

Mods with code run with the game's own rights. Nothing sandboxes them. That is why published mods are built from
public source, and why the launcher asks before installing one.

## Trying a mod

Copy `bin/Release/net10.0/mod/` into the launcher's library under `mods/local/` (the Mods page's
**OPEN LOCAL MODS FOLDER**), then press **READ IT AGAIN**. The mod appears in the game's play order tagged LOCAL;
switch it on and press Play. Without the launcher, run the game with `OPENIGI_MODS=<a folder of mod folders>`.

## Publishing

See the mods index's [CONTRIBUTING.md](mods-index/CONTRIBUTING.md). In short:

1. Put the source in a public repository.
2. Open a pull request adding `mods/<id>/index.json` (its owners and repository) and `mods/<id>/<version>.json`
   (the commit).
3. CI checks it and builds it from that commit, and a maintainer reviews the source and merges it.
4. The merge publishes it to the download store, and every launcher lists it within a minute.

For maintainers, the release tool does each step by hand:

```sh
dotnet run --project tools/OpenIGI.Tools.Release -- mods-index check <index> [--base <ref> --author <login>]
dotnet run --project tools/OpenIGI.Tools.Release -- mods-index build <index> <id> <version> --out <dir>
dotnet run --project tools/OpenIGI.Tools.Release -- mods-index publish <index> <id> <version> --package <dir> [--ssh <host> | --to-dir <dir>]
```

`content publish <mod folder> --kind mod --source <repository> --commit <sha>` publishes a package that was built
outside the index.

The SDK packages are made by `tools/release/pack-modding-sdk.sh <version>`. A game release pushes them to nuget.org
once the repository has a `NUGET_API_KEY` secret.
