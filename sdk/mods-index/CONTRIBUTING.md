# Publishing a mod

A mod is published by pull request. CI checks it and builds it, a maintainer reviews it, and merging it publishes it.

## 1. Build the mod

```sh
dotnet new install OpenIGI.Modding.Templates
dotnet new openigi-mod -n MyMod --modId my-mod --modAuthor "Your name"
cd MyMod && dotnet build -c Release
```

The project references `OpenIGI.Modding.Sdk` at the version of the oldest game build it works with. Every build
writes `bin/Release/net10.0/mod/`, the folder exactly as players install it.

To try it, copy that folder into the launcher's library under `mods/local/` (the Mods page's
**OPEN LOCAL MODS FOLDER**), press **READ IT AGAIN**, switch it on and press Play. You can also run the game with
`OPENIGI_MODS=<a folder of mod folders>`.

## 2. Put the source in a public repository

A mod with code is built here from its source, never uploaded as built: players run what anyone can read. Tag or
note the commit you want to publish. The version in its `mod.json` is the version being published, as
`major.minor.patch`.

A mod of content alone (no `assembly` in its `mod.json`) may instead be a zip of its folder, with `mod.json` at the
root, at any https address.

## 3. Open a pull request

The first version of a new mod adds two files:

`mods/my-mod/index.json`
```json
{
  "owners": ["your-github-login"],
  "repository": "https://github.com/you/my-mod",
  "tags": ["weapons"]
}
```

`mods/my-mod/1.0.0.json`
```json
{ "source": { "commit": "<the full 40-character commit hash>", "project": "MyMod/MyMod.csproj" } }
```

or, for a mod of content alone:

```json
{ "files": { "url": "https://example.org/my-mod-1.0.0.zip", "sha256": "<the zip's SHA-256>" } }
```

Each later version adds one `mods/my-mod/<version>.json`.

## What CI checks

- The id is lowercase letters, digits and dashes, at most 64 characters, and not `local`.
- You are an owner of the mod, as its `index.json` stood before your pull request. Whoever adds a mod must list
  themselves among its owners.
- A published version file is never changed or removed. A fix is a new version.
- The pull request touches only `mods/<id>/`.
- The version builds from the named commit with nothing but the SDK, and what it builds is the id and version the
  file names.
- The package carries none of the game's own assemblies. A mod of content alone carries no code.

## What a maintainer checks

- The source does what the summary says, and nothing it does not say. Code that reaches the network, reads files
  outside the game's and its own, or starts programs must say so in the summary, and is refused when it has no
  reason to.
- No part of a retail game's data is redistributed. A mod that needs another game's data says so with
  `requiresTitleData`, and reads the player's own copy.
- The licence lets players use it.

## Withdrawing a version

Ask a maintainer in an issue. A withdrawn version is no longer offered. Players who installed it keep it, and the
launcher offers the newest version that is still listed.

## Reporting a mod

Open an issue naming the mod and what it does wrong. A mod that harms players is withdrawn at once.
