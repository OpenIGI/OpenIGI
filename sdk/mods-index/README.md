# OpenIGI mods

The index of mods for [OpenIGI](https://github.com/OpenIGI/OpenIGI). Every mod listed here appears on the
OpenIGI launcher's **Mods** page, where players install it, switch it on and off for each game, and choose the order
mods are played in.

This repository holds no mods. It holds a small JSON file per mod, saying who may publish it and where its source
is, and one per version, naming the commit it is built from. When a maintainer merges a new version, it is built
here from that commit and published to OpenIGI's content store, and the launcher offers it within minutes, once
openigi.com is rebuilt.

## How a mod appears on the launcher

The listing comes from the mod's own `mod.json`, as built:

| On the launcher | From `mod.json` |
| --- | --- |
| Name, line under it | `name`, `summary` |
| BY … | `authors` |
| RUNS CODE tag, BUILT FROM … | `assembly`, and the commit in this index |
| Shown for Project I.G.I. / I.G.I. 2 | `titles` |
| NEEDS I.G.I. 2 … | `requiresTitleData` |
| NEEDS THE MOD … | `dependencies` |
| NEEDS NEWER GAME | `minGameVersion` |

A picture is optional: put `thumbnail.png` (or `.jpg`, `.webp`; at most 2 MiB, 16:9 reads best) beside the mod's
`index.json`.

## Publishing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Layout

```
mods/
  <id>/
    index.json        who may publish it and where its source is
    <version>.json    one per published version: the commit it is built from, or its files
    thumbnail.png     optional
```
