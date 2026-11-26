"""Packs a Project I.G.I. or I.G.I. 2 installation into the bundles the browser build downloads in place of a picked folder.

    python tools/web-data/pack.py <game folder> <output folder> [--title igi1|igi2] [--config <clean config.qvm>]

The page (apps/OpenIGI.Web/wwwroot/game-files.js, fromHosted) reads <output>/index.json and then a handful of
bundles instead of thousands of files: one "base" bundle with everything read before the game starts, and the
rest read when a mission that needs them is chosen. Each bundle says which folders wait for it ("for"): a mission
folder (one holding a mission.qvm) for that mission's large files, a location's shared folder for its packs
(I.G.I. 2's missions/locationN/common, which every level of the location opens), or "*" for what every mission
needs and the menu does not. The page's load gate asks for a mission's folder and its location's shared folder
(WebLoadGate.cs), so these are the only two kinds of folder a bundle can wait for.

A bundle is its files' bytes one after another, gzipped (the browser's DecompressionStream reads gzip
everywhere), and named by the hash of what is served, so it can be cached for good: index.json is the only file
that changes from one packing to the next. Bundles already in the output folder are left there, so a page still
holding the previous index.json finds its bundles; prune them by hand once nobody can.

Each game goes in its own folder on the data host (data.openigi.com/igi1/, /igi2/), which the page picks by its
game switch.
"""
import argparse
import gzip
import hashlib
import json
import os
import posixpath

LARGE = 256 * 1024

# What the game reads, per game: its asset folders and the configuration beside them. Nothing else in the folder --
# the executable (the page writes an empty one where the host looks for it), mods' overrides, a desktop build of
# OpenIGI, the intro videos the browser does not play, the original's sound drivers -- is packed. Folder names are
# matched without regard to case: I.G.I. 2 ships its folders in capitals.
GAMES = {
    "igi1": {
        "name": "Project I.G.I.",
        "folders": ["ammo", "animtrigger", "common", "computer", "humanplayer", "language", "magicobj", "material",
                    "menusystem", "missions", "physicsobj", "screens", "weapons"],
        "top_level": lambda name: name in {"config.qvm", "lod.qvm"},
        "skip": [],
        "shared": [],
        "location_packs": False,
    },
    "igi2": {
        "name": "I.G.I. 2: Covert Strike",
        "folders": ["animtrigger", "common", "computer", "humanplayer", "language", "material", "menusystem",
                    "missions", "physicsobj", "screens", "weapons"],
        "top_level": lambda name: name.endswith(".qvm"),
        # The multiplayer maps: multiplayer is the first game's alone for now.
        "skip": ["missions/multiplayer"],
        # The gameplay sounds, a quarter of what the menu would wait for and not heard until a mission.
        "shared": ["common/sounds/sounds.res"],
        "location_packs": True,
    },
}
SKIPPED_EXTENSIONS = {".avi"}


def detect(game):
    names = {name.lower(): name for name in os.listdir(game)}
    if os.path.isfile(os.path.join(game, names.get("igi2.exe", "igi2.exe"))):
        return "igi2"
    if "config.qvm" in names:
        return "igi1"
    return None


def collect(game, spec):
    files = []
    entries = sorted(os.listdir(game))
    for name in entries:
        if os.path.isfile(os.path.join(game, name)) and spec["top_level"](name.lower()):
            files.append(name)
    # In the list's order, which is the order the bundles hold them in: a repacking of the same files makes the
    # same bundles, so a player's downloaded ones stay good.
    on_disk = {name.lower(): name for name in entries}
    for folder in spec["folders"]:
        top = os.path.join(game, on_disk.get(folder, folder))
        if not os.path.isdir(top):
            continue
        for at, dirs, names in os.walk(top):
            dirs.sort()
            for file in sorted(names):
                if os.path.splitext(file)[1].lower() in SKIPPED_EXTENSIONS:
                    continue
                path = os.path.relpath(os.path.join(at, file), game).replace(os.sep, "/")
                if any(path.lower().startswith(skip + "/") for skip in spec["skip"]):
                    continue
                files.append(path)
    return files


def location_pack_folders(mission_folders):
    """The shared folder of each location: missions/<location>/common beside the location's mission folders."""
    return {posixpath.join(posixpath.dirname(folder), "common") for folder in mission_folders}


def bundle_of(path, size, spec, mission_folders, pack_folders):
    """The bundle a file goes in, and the folder that waits for it (None for base)."""
    lower = path.lower()
    if lower in spec["shared"]:
        return "shared", "*"
    if size < LARGE:
        return "base", None
    folder = posixpath.dirname(lower)
    while folder:
        if folder in mission_folders or folder in pack_folders:
            return folder.replace("/", "-"), folder
        folder = posixpath.dirname(folder)
    return "base", None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("game")
    parser.add_argument("output")
    parser.add_argument("--title", choices=sorted(GAMES), help="which game the folder is; found from the folder when not given")
    parser.add_argument(
        "--config",
        help="the config.qvm to ship in place of the installation's, which holds the profiles of whoever played it: "
        "every player of the site starts from it. Write a clean one with the engine's defaults (one profile, Joe).")
    args = parser.parse_args()

    title = args.title or detect(args.game)
    if title is None:
        parser.error(f"{args.game} is not a Project I.G.I. or I.G.I. 2 installation (no config.qvm, no igi2.exe).")
    spec = GAMES[title]

    paths = collect(args.game, spec)
    if "config.qvm" not in {p.lower() for p in paths}:
        parser.error(f"{args.game} is not a {spec['name']} installation (no config.qvm).")

    def source_of(path):
        return args.config if args.config and path.lower() == "config.qvm" else os.path.join(args.game, path)

    sizes = {p: os.path.getsize(source_of(p)) for p in paths}
    mission_folders = {posixpath.dirname(p).lower() for p in paths if posixpath.basename(p).lower() == "mission.qvm"}
    pack_folders = location_pack_folders(mission_folders) if spec["location_packs"] else set()

    groups = {}
    waits = {}
    for path in paths:
        name, folder = bundle_of(path, sizes[path], spec, mission_folders, pack_folders)
        groups.setdefault(name, []).append(path)
        if folder:
            waits[name] = folder

    os.makedirs(args.output, exist_ok=True)
    bundles = {}
    entries = []
    for name in sorted(groups, key=lambda n: (n != "base", n)):
        raw = bytearray()
        for path in groups[name]:
            entries.append({"path": path, "size": sizes[path], "bundle": name, "offset": len(raw)})
            with open(source_of(path), "rb") as f:
                raw += f.read()
        packed = gzip.compress(bytes(raw), compresslevel=9, mtime=0)
        digest = hashlib.sha256(packed).hexdigest()
        url = f"{name}-{digest[:16]}.bin.gz"
        with open(os.path.join(args.output, url), "wb") as f:
            f.write(packed)
        bundles[name] = {"url": url, "size": len(packed), "raw": len(raw), "sha256": digest}
        if name in waits:
            bundles[name]["for"] = [waits[name]]
        print(f"{url}: {len(groups[name])} files, {len(raw) / 1048576:.1f} MB -> {len(packed) / 1048576:.1f} MB")

    index = {"title": title, "bundles": bundles, "files": entries}
    body = json.dumps(index, separators=(",", ":")).encode("utf-8")
    with open(os.path.join(args.output, "index.json"), "wb") as f:
        f.write(body)
    total = sum(b["size"] for b in bundles.values())
    print(f"index.json ({title}): {len(entries)} files in {len(bundles)} bundles, "
          f"{bundles['base']['size'] / 1048576:.1f} MB to start, {total / 1048576:.1f} MB in all.")


if __name__ == "__main__":
    main()
