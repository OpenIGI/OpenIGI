# Modding OpenIGI

A mod is a folder holding a `mod.json`, and usually an assembly built against the modding API. The game's mod loader
plays the mods the player switched on for a game, in the order they chose. Players install mods from the launcher's
**Mods** page. Authors publish them through the [mods index](mods-index/README.md).

A mod can be any mix of three things:

- **Files** that stand in for the game's own: textures, sounds, models, scripts, whole new missions.
- **Code**: new behaviour, written as a C# class whose members say what they change.
- **Shaders**: passes over the picture before the tone map.

## Making a mod

```sh
dotnet new install OpenIGI.Modding.Templates
dotnet new openigi-mod -n MyMod --modId my-mod --modAuthor "Your name"            # code, with data/ beside it
dotnet new openigi-mod -n MyPack --modId my-pack --modAuthor "Your name" --kind data   # files alone
cd MyMod && dotnet build -c Release
```

The project is the mod's properties and nothing else:

```xml
<Project Sdk="OpenIGI.Modding.Sdk/0.5.0">
  <PropertyGroup>
    <ModId>my-mod</ModId>
    <ModName>My mod</ModName>
    <Version>1.0.0</Version>
    <ModSummary>What it does, in a line.</ModSummary>
    <Authors>Your name</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <ModTitles>igi1</ModTitles>                      <!-- the games it is for: igi1, igi2, or both; empty for every game -->
    <ModRequiresTitleData></ModRequiresTitleData>    <!-- other games whose installed data it reads -->
    <ModAffectsSimulation>true</ModAffectsSimulation><!-- whether it changes what happens in play, not only how it looks -->
  </PropertyGroup>
  <ItemGroup>
    <ModDependency Include="other-mod" Version=">=1.2" />
    <ModCampaign Include="Operation Nightfall" Missions="901;902;903" />
    <ModPostPass Include="Greyscale" Toggle="mod_mymod_greyscale" Parameters="mod_mymod_amount" />
  </ItemGroup>
</Project>
```

`OpenIGI.Modding.Sdk` is a project SDK. It brings:

- the modding API, and both games' gameplay and presentation assemblies to build against;
- the source generator that writes the mod's `Configure` from its attributes;
- the `mod.json`, written from the properties above (`ModConflicts`, `ModHomepage` and `ModMinGameVersion` too);
- the build that writes `bin/Release/net10.0/mod/`, the folder as players install it.

Beside a checkout of OpenIGI (named by `OPENIGI_ROOT`, or an `igi-remastered` folder up to three levels above the project)
the SDK builds against the game's own projects, and reads its own files from the checkout, so a mod and the game can be
changed together. Otherwise it builds against the game's packages at the SDK's version. The SDK's version is the game
build the mod is built against; a mod built against version V works with game builds from V on that still offer its
`apiVersion`.

Nothing of the game's goes into the mod's folder. At run time a mod is given the game's own assemblies, and any library
the game ships, so its types are the game's. The folder carries its `mod.json`; its assembly, symbols and dependency
manifest; its `data/` and `shaders/`; and the assemblies of the packages the project references itself.

## Files: `data/`

Files under the project's `data/` folder mirror the game's installation and stand in for the game's own at the same paths
while the mod is on: `data/common/sounds/x.wav` for `common/sounds/x.wav`. A file in an archive is stood in for by the
archive's folder and its own name: `data/common/textures/x.tex` replaces `x.tex` in `common/textures/textures.res`. A
`.res` the mod ships there counts as its entries.

A later mod's files win over an earlier one's, and a mod's win over the player's texture packs and the game folder's
`overrides`. Every reader of the game's files sees them. A file only the mod has is found too, so a whole new mission
needs no code: its folder under `data/missions/`, with its `mission.qvm` defining it (`DefineMission`) and its packs
beside it, is found by the game's own mission scan.

**Campaigns.** A mod's missions are listed in the menu as a campaign of their own when the project names one
(`<ModCampaign>`, its missions in order). They are kept out of the game's own campaign, and all of them are open.

## Code

```csharp
using OpenIGI.Game.Events;
using OpenIGI.Game.Hosting;
using OpenIGI.Modding;

[OpenIgiMod]
public sealed partial class Mod
{
    [ScriptConstant("MyMod_Answer")]
    public const int Answer = 42;                                    // a constant the level scripts read

    [AddsLevelSystem<MissionLevelBuild>(After = MissionSystemNames.Player)]
    private void Arrive(MissionLevelBuild build) { }                 // a step of each mission's bring-up

    [OnGameEvent]
    private void GuardDown(in HumanKilled killed) { }                // told as each guard dies

    [OnModLoaded(Title = "igi1")]
    private void Loaded(IModContext context) { }                     // the session's services, files, events and log

    partial void OnConfigure(IModBuilder builder) { }                // anything the attributes do not say
}

[ReplacesTask("HumanSoldier", Title = "igi1")]                       // the level's soldiers are made of this class
public sealed class MySoldier : GameTask { }
```

The class is `partial`; the generator writes its `Configure` and names it to the loader. A member's `Title` limits it to
one game. What the generator cannot write is an error at build time (`OIGI101`-`OIGI109`): a class that is not partial,
a method of the wrong shape, a level system placed twice or not at all, a task class that cannot be made.

The builder's own members, for `OnConfigure` or a hand-written `IOpenIgiMod`:

| Member | What it does |
|---|---|
| `AddModule` | a content module: task types, natives and services, registered after the game's |
| `AddTask`, `ReplaceTask` | a task type of the mod's own, or the game's made of a mod's class |
| `AddConstant` | an integer the scripts read by name |
| `MountTitleData` | another game's installed data as a device of its own, `IGI2:path` |
| `EditLevelSystems` | insert, replace or remove a step of each level's bring-up |
| `Listen` | a listener of one kind of game event |
| `OnLoaded` | runs once a session has loaded its tables, with an `IModContext` |

`ModConfig.Read` reads a settings file the player may keep beside the mod, else the copy the mod embeds.

**Events** (`OpenIGI.Game.Events`), raised by both games once a tick, after the level has run, and only for what is
listened to: `LevelLoaded`, `LevelTicked`, `MissionEnded`, `ObjectiveChanged`, `PlayerDamaged`, `PlayerKilled`,
`HumanKilled`, `AlarmChanged`. Nothing in the simulation reads what a listener does, so a mod that listens plays as one
that does not.

**Settings.** A `[CVarContainer]` class of the mod's registers its variables as the mod loads. Name them `mod_<id>_...`:
the settings screen lists them on its **Mods** page, and a switched-off mod's values are kept in `settings.cfg` until it
is on again.

**Drawing.** A class that also implements `IOpenIgiPresentationMod` (`OpenIGI.Modding.Presentation`) adds to the game's
presenters: `ForTask` draws a task type, `ForLevel` draws over the level, and `ForHud` lays an `IHudOverlay` over the
player's view after the game's HUD, drawn with the menus' painter and face. `HudOverlayFrame.ToScreen` places a point of
the world on the screen.

## Shaders

A pass over the lit scene, before the bloom and the tone map, is one compute shader written against
`Shaders/Source/Mod/PostPass.slang` in OpenIGI's shader library:

```slang
#include "Mod/PostPass.slang"

[shader("compute")]
[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= uint2(p.size.xy))) { return; }
    float4 colour = uSource[id.xy];
    float grey = dot(colour.rgb, float3(0.2126, 0.7152, 0.0722));
    uTarget.Store(id.xy, float4(lerp(colour.rgb, grey.xxx, saturate(p.values[0].x)), colour.a));
}
```

The mod's `shaders/` folder is laid out as the game's library is: a `ShaderModules.json` (pinning the same Slang and DXC
as the game's), its sources under `Source/`, its compiled forms under `Generated/`. Compile it with the game's shader tool,
from a checkout, and commit what it writes, as the game does:

```sh
dotnet run --project ../igi-remastered/tools/OpenIGI.Tools.ShaderBuild -- --project shaders
```

`<ModPostPass>` names the module, the setting that switches it on and the settings whose values it reads (`p.values`,
sixteen of them). The passes run in the mods' order, on Vulkan, Direct3D 12 and WebGPU alike; a module that does not bind
what the contract names is left out, with the reason in the log.

## Saves

A save records the mods that change play it was made with (`ModAffectsSimulation`). One made with a mod that is off now
loads only once the player has been told so and chooses it again. An I.G.I. 2 save keeps the list in a file beside it, so
the save itself stays the format the original game reads.

## Testing

`ModLoader.Load` loads a folder of mods as the game does, and `ModSet.Extend` configures them into a game's runtime, so a
test can check what a mod registers and where its level systems land. A mod built beside a checkout can use the game's
own test support (`tests/OpenIGI.TestSupport`): retail game data by `IGI_GAME_PATH` and `IGI2_GAME_PATH`, tests that
skip without it, and recording graphics and audio devices.
[OpenIGI/igi2-soldiers](https://github.com/OpenIGI/igi2-soldiers) is a full example in a repository of its own: Project
I.G.I.'s missions with I.G.I. 2's soldiers.

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
- **Isolation:** each mod with code loads into its own assembly load context, once a process. It shares the game's
  assemblies and libraries, and keeps its other dependencies to itself.

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
through Trusted Publishing, once the repository's `NUGET_USER` variable names the nuget.org profile whose policy
trusts `release.yml`. Until a version is on nuget.org, a mod's repository can pack it from a checkout into a local
folder its `nuget.config` names, as igi2-soldiers does.
