"""Packs a Project I.G.I. installation into the bundles the browser build downloads in place of a picked folder.

    python tools/web-data/pack.py <game folder> <output folder>

The page (apps/OpenIGI.Web/wwwroot/game-files.js, fromHosted) reads <output>/index.json and then a handful of
bundles instead of three thousand files: one "base" bundle with everything read before the game starts, and one
bundle per mission with the mission's large files, read when the mission is chosen. The split is the page's own
(game-files.js, deferredTo): a file of LARGE bytes or more inside a folder holding a mission.qvm waits for its
mission. Keep the two in step.

A bundle is its files' bytes one after another, gzipped (the browser's DecompressionStream reads gzip
everywhere), and named by the hash of what is served, so it can be cached for good: index.json is the only file
that changes from one packing to the next. Bundles already in the output folder are left there, so a page still
holding the previous index.json finds its bundles; prune them by hand once nobody can.
"""
import argparse
import gzip
import hashlib
import json
import os
import posixpath

LARGE = 256 * 1024

# What the game reads: its asset folders and the configuration beside them. Nothing else in the folder -- the
# executable, mods' overrides, a desktop build of OpenIGI, the intro videos the browser does not play -- is packed.
FOLDERS = ["ammo", "animtrigger", "common", "computer", "humanplayer", "language", "magicobj", "material",
           "menusystem", "missions", "physicsobj", "screens", "weapons"]
TOP_LEVEL = {"config.qvm", "lod.qvm"}
SKIPPED_EXTENSIONS = {".avi"}


def collect(game):
    files = []
    for name in sorted(os.listdir(game)):
        path = os.path.join(game, name)
        if os.path.isfile(path) and name.lower() in TOP_LEVEL:
            files.append(name)
    for folder in FOLDERS:
        top = os.path.join(game, folder)
        if not os.path.isdir(top):
            continue
        for at, dirs, names in os.walk(top):
            dirs.sort()
            for name in sorted(names):
                if os.path.splitext(name)[1].lower() in SKIPPED_EXTENSIONS:
                    continue
                files.append(os.path.relpath(os.path.join(at, name), game).replace(os.sep, "/"))
    return files


def bundle_of(path, size, mission_folders):
    if size < LARGE:
        return "base"
    folder = posixpath.dirname(path).lower()
    while folder:
        if folder in mission_folders:
            return folder.replace("/", "-")
        folder = posixpath.dirname(folder)
    return "base"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("game")
    parser.add_argument("output")
    args = parser.parse_args()

    paths = collect(args.game)
    if "config.qvm" not in {p.lower() for p in paths}:
        parser.error(f"{args.game} is not a Project I.G.I. installation (no config.qvm).")

    sizes = {p: os.path.getsize(os.path.join(args.game, p)) for p in paths}
    mission_folders = {posixpath.dirname(p).lower() for p in paths if posixpath.basename(p).lower() == "mission.qvm"}

    groups = {}
    for path in paths:
        groups.setdefault(bundle_of(path, sizes[path], mission_folders), []).append(path)

    os.makedirs(args.output, exist_ok=True)
    bundles = {}
    entries = []
    for name in sorted(groups, key=lambda n: (n != "base", n)):
        raw = bytearray()
        for path in groups[name]:
            entries.append({"path": path, "size": sizes[path], "bundle": name, "offset": len(raw)})
            with open(os.path.join(args.game, path), "rb") as f:
                raw += f.read()
        packed = gzip.compress(bytes(raw), compresslevel=9, mtime=0)
        digest = hashlib.sha256(packed).hexdigest()
        url = f"{name}-{digest[:16]}.bin.gz"
        with open(os.path.join(args.output, url), "wb") as f:
            f.write(packed)
        bundles[name] = {"url": url, "size": len(packed), "raw": len(raw), "sha256": digest}
        print(f"{url}: {len(groups[name])} files, {len(raw) / 1048576:.1f} MB -> {len(packed) / 1048576:.1f} MB")

    index = {"title": "igi1", "bundles": bundles, "files": entries}
    body = json.dumps(index, separators=(",", ":")).encode("utf-8")
    with open(os.path.join(args.output, "index.json"), "wb") as f:
        f.write(body)
    total = sum(b["size"] for b in bundles.values())
    print(f"index.json: {len(entries)} files in {len(bundles)} bundles, {total / 1048576:.1f} MB to download in all.")


if __name__ == "__main__":
    main()
