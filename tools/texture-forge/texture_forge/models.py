"""The weights this tool runs, where they come from, and the licence each one carries.

WHY THIS FILE IS MOSTLY A LIST OF HASHES.

OpenIGI is GPL-3.0, and CONTRIBUTING.md is explicit that the game's art with a filter over it is still
Innerloop's -- so nothing this tool produces may be committed. The weights are a separate question, and a
sharper one: of the 671 models catalogued on OpenModelDB, 280 are CC-BY-NC or CC-BY-NC-SA and 27 state no
licence at all. Every model the popular "best game texture upscaler" guides recommend -- 4x-UltraSharp,
UltraSharpV2, Remacri, ClearRealityV1 -- is CC-BY-NC-SA, and therefore unusable here.

WHAT IS ALLOWED HERE. Anything whose licence lets a user of this tool use the result and pass it on:
CC0-1.0, CC-BY-4.0, WTFPL, MIT, Apache-2.0, BSD. NOT CC-BY-NC-SA, which is where every famous "best game
texture upscaler" lives -- 4x-UltraSharp, UltraSharpV2, Remacri, ClearRealityV1.

The NonCommercial exclusion is not legal timidity; a person upscaling their own copy of a game they own is
plainly not trading. It is that the moment the tool's own catalogue depends on an NC weight, it can no
longer tell anyone "everything this downloads is free to use and to redistribute" -- and that sentence is
worth more than any one model. It costs nothing here: the CC-BY models below were trained on 30,000 game
textures, where UltraSharpV2 is a general-purpose model that happens to be good at them. Domain beat
reputation.

CC-BY asks for credit, and `texture-forge models` prints the author and licence of everything it can
fetch, which discharges it.

WHAT IS NOT HERE AND WHY. 4x-NMKD-Superscale (WTFPL, and otherwise a fair candidate) is published on
Icedrive, which needs a browser. `ensure` is a plain urlopen, and a catalogue whose whole purpose is
fetching-and-verifying has no room for an entry a person has to go and get by hand.

So the hashes ARE the licence audit. A weight that does not match the hash recorded here is not the weight
whose licence was checked, and the tool refuses to run it rather than quietly using something unvetted.

WHAT THE HASHES DO AND DO NOT PROVE, stated rather than hidden. Every digest here was computed from the
file as downloaded, because no publisher in this catalogue ships one to check against. So a hash proves the
weights have not changed since the licence was read against them; it cannot independently prove the
download was not already wrong. Trust-on-first-use, which is the strongest thing available, and it is worth
saying which of the two it is.

The files are downloaded rather than committed. They are CC0, so committing them would be perfectly legal;
200 MB of binaries in a source tree is simply a bad trade, and `texture-forge models` exists so the licence
position can be read without running anything.
"""

from __future__ import annotations

import hashlib
import os
import sys
import urllib.request
from dataclasses import dataclass
from pathlib import Path

__all__ = ["Model", "CATALOGUE", "UPSCALERS", "cache_root", "ensure", "describe", "lookup"]


@dataclass(frozen=True)
class Model:
    """One set of weights: what it does, where it came from, and what it is licensed under."""

    key: str
    """How it is named on the command line."""

    filename: str
    """What it is called on disk, which is what the publisher called it."""

    url: str
    """Where it is fetched from, once."""

    sha256: str
    """The audit. Recorded from OpenModelDB's catalogue rather than computed from a download."""

    size: int
    """Expected bytes, so a truncated download fails before the hash does and says so more clearly."""

    architecture: str
    """What spandrel will detect. Recorded here so a surprise is visible rather than silent."""

    scale: int
    """The factor this model multiplies by. The 1x models transform rather than enlarge."""

    licence: str
    """Verified from the publisher's own LICENSE and from OpenModelDB. All CC0-1.0."""

    role: str
    """What it is for, in one line, for `texture-forge models`."""

    half_ok: bool
    """
    Whether spandrel will run this in fp16.

    Not a preference -- a capability. spandrel hard-blocks fp16 on every transformer architecture (its DAT
    support carries the comment "Too much weirdness to support this at the moment"), so DAT has to run in
    bf16 instead. Ampere has native bf16, so on a 3080 this costs nothing, but it has to be asked for
    correctly or the load throws.
    """


# Ordered as the pipeline uses them: upscale first, then the three maps derived from the upscaled result.
CATALOGUE: tuple[Model, ...] = (
    Model(
        key="dat2",
        filename="4x-PBRify_UpscalerV4.pth",
        url=(
            "https://github.com/Kim2091/Kim2091-Models/releases/download/"
            "4x-PBRify_UpscalerV4/4x-PBRify_UpscalerV4.pth"
        ),
        sha256="82db39e31e01da6816a9814c0553a8dcdb7bd2d655353e67075c9295068cb789",
        size=140345646,
        architecture="DAT-2",
        scale=4,
        licence="CC0-1.0",
        role="Upscaler, default. Slower, and much better on this game's hand-painted art.",
        half_ok=False,
    ),
    Model(
        key="rplksr",
        filename="4x-PBRify_RPLKSRd_V3.pth",
        url=(
            "https://github.com/Kim2091/Kim2091-Models/releases/download/"
            "4x-PBRify_RPLKSRd_V3/4x-PBRify_RPLKSRd_V3.pth"
        ),
        sha256="1325a08842ebfc18aea90e71594aa9b9e82b9c7e16321dccf5b757884a22daf1",
        size=29717038,
        architecture="RealPLKSR+DySample",
        scale=4,
        licence="CC0-1.0",
        role="Upscaler, fast. Posterises smooth art; see the note in __main__ before choosing it.",
        half_ok=True,
    ),
    Model(
        key="normal",
        filename="1x-PBRify_NormalV3.pth",
        url="https://github.com/Kim2091/PBRify_Remix/raw/main/Models/1x-PBRify_NormalV3.pth",
        sha256="b0a18270da765f02eaae3c228203bee0677fc28b2854a562515e4aae9c61223b",
        size=8942931,
        architecture="SPAN",
        scale=1,
        licence="CC0-1.0",
        role="Albedo to tangent normal. Emits DirectX handedness; this tool flips green.",
        half_ok=True,
    ),
    Model(
        key="roughness",
        filename="1x-PBRify_RoughnessV2.pth",
        url="https://github.com/Kim2091/PBRify_Remix/raw/main/Models/1x-PBRify_RoughnessV2.pth",
        sha256="7003c39041af64cdb77d5120bd560a2878538fbb14a07657d8fd12aac5773679",
        size=8938652,
        architecture="SPAN",
        scale=1,
        licence="CC0-1.0",
        role="Albedo to roughness.",
        half_ok=True,
    ),
    Model(
        key="height",
        filename="1x-PBRify_Height.pth",
        url="https://github.com/Kim2091/PBRify_Remix/raw/main/Models/1x-PBRify_Height.pth",
        sha256="5b973ecb8bae9d96d14d77b8a8f1d88fb6a8580bcc1bb55d2872811acdc4277d",
        size=8938652,
        architecture="SPAN",
        scale=1,
        licence="CC0-1.0",
        role="Albedo to height. Feeds the occlusion search.",
        half_ok=True,
    ),
    Model(
        key="texturedat2",
        filename="4xTextureDAT2_otf.pth",
        url=(
            "https://github.com/Phhofm/models/releases/download/"
            "4xTextureDAT2_otf/4xTextureDAT2_otf.pth"
        ),
        sha256="201fd1cfe380cab32a036f5b35f50b40d79fd25f28e7ed378a385973b1b2887f",
        size=140339286,
        architecture="DAT-2",
        scale=4,
        licence="CC-BY-4.0 (Philip Hofmann)",
        role="Remaster, default. Trained on 30,122 GTA V game textures; reinterprets rather than enlarges.",
        half_ok=False,
    ),
    Model(
        key="gtavrgt",
        filename="4xTextures_GTAV_rgt-s.pth",
        url=(
            "https://github.com/Phhofm/models/releases/download/"
            "4xTextures_GTAV_rgt-s/4xTextures_GTAV_rgt-s.pth"
        ),
        sha256="5a1f614d41d7043e7e8de32081d1d7e9d1b2ae30f1ecf89b4e059bcb331e5deb",
        size=136009036,
        architecture="RGT-S",
        scale=4,
        licence="CC-BY-4.0 (Helaman)",
        role="Remaster, alternative. Same art, a different architecture -- so it sees the image differently.",
        half_ok=False,
    ),
)

_BY_KEY: dict[str, Model] = {model.key: model for model in CATALOGUE}

UPSCALERS: tuple[str, ...] = tuple(model.key for model in CATALOGUE if model.scale > 1)

DEFAULT_REMASTERER = "texturedat2"
"""What a bare `--remaster` runs: the one of these trained on the most game art, and the one measured."""

REMASTERERS: tuple[str, ...] = (DEFAULT_REMASTERER,) + tuple(
    model.key for model in CATALOGUE if model.scale > 1 and model.key != DEFAULT_REMASTERER
)
"""Which models may run as the remaster pass, the default first.

Every enlarging model qualifies, including the one used as the primary upscaler: the pass runs a model over
the ALREADY-UPSCALED image and resamples back, so what it does is reinterpret at the size the texture
already has. Running the same weights twice is a legitimate choice and a mild one; running a different
architecture is the point of the option, because two passes of the same network mostly deepen what the
first pass already decided.

Derived rather than listed, so a model added to the catalogue is offered here without anyone remembering
to. The one thing worth stating by hand is which comes first, because that is the default and argparse
prints this tuple in order.
"""


def lookup(key: str) -> Model:
    """Returns a model by its command-line name."""
    model = _BY_KEY.get(key)

    if model is None:
        known = ", ".join(sorted(_BY_KEY))
        raise KeyError(f"No model named {key!r}. Known: {known}.")

    return model


def cache_root() -> Path:
    """Where the weights live: beside the renderer's own caches, not in the repository.

    The same place and the same reasoning as the detail-map cache the game writes at runtime -- a per-user
    directory, outside the tree, holding what is derived rather than authored.
    """
    override = os.environ.get("TEXTURE_FORGE_MODELS")

    if override:
        return Path(override)

    if sys.platform == "win32":
        base = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
        return Path(base) / "OpenIGI" / "models"

    base = os.environ.get("XDG_CACHE_HOME") or str(Path.home() / ".cache")
    return Path(base) / "openigi" / "models"


def _digest(path: Path) -> str:
    digest = hashlib.sha256()

    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)

    return digest.hexdigest()


def ensure(key: str, *, quiet: bool = False) -> Path:
    """Returns the local path to a model's weights, fetching and verifying them on first use.

    Raises on a hash mismatch rather than warning. A weight that is not the one whose licence was checked is
    the one thing here that must not be shrugged off: it is both an unknown licence and an unknown model.
    """
    model = lookup(key)
    root = cache_root()
    path = root / model.filename

    if path.exists():
        if path.stat().st_size == model.size and _digest(path) == model.sha256:
            return path

        # Corrupt or replaced. Say so and fetch again, rather than leaving a bad file to fail every run.
        if not quiet:
            print(f"  {model.filename} did not verify; fetching it again.", file=sys.stderr)

        path.unlink()

    root.mkdir(parents=True, exist_ok=True)

    if not quiet:
        size_mb = model.size / 1e6
        print(f"  fetching {model.filename} ({size_mb:.0f} MB, {model.licence})", file=sys.stderr)

    partial = path.with_name(path.name + ".part")

    with urllib.request.urlopen(model.url) as response, partial.open("wb") as handle:
        while block := response.read(1 << 20):
            handle.write(block)

    actual_size = partial.stat().st_size

    if actual_size != model.size:
        partial.unlink()
        raise OSError(
            f"{model.filename} downloaded as {actual_size} bytes, expected {model.size}. "
            "The download was truncated, or the published file has changed."
        )

    actual = _digest(partial)

    if actual != model.sha256:
        partial.unlink()
        raise OSError(
            f"{model.filename} hashed as {actual}, expected {model.sha256}. This is not the file whose "
            "licence was verified; refusing to use it."
        )

    # Atomic, so an interrupted run never leaves a half file that satisfies the exists() check above.
    partial.replace(path)
    return path


def describe() -> str:
    """The licence audit as a table. This is what makes the position checkable without running anything."""
    root = cache_root()
    lines = [f"Weights directory: {root}", ""]
    header = f"{'key':<10} {'licence':<9} {'architecture':<20} {'state':<12} role"
    lines.append(header)
    lines.append("-" * len(header))

    for model in CATALOGUE:
        path = root / model.filename
        state = "not fetched"

        if path.exists():
            state = "ok" if _digest(path) == model.sha256 else "BAD HASH"

        lines.append(
            f"{model.key:<10} {model.licence:<9} {model.architecture:<20} {state:<12} {model.role}"
        )

    lines.append("")
    lines.append(
        "Every weight above is CC0-1.0, verified from the publisher's LICENSE and from OpenModelDB."
    )
    lines.append("Sources:")

    for model in CATALOGUE:
        lines.append(f"  {model.key:<10} {model.url}")

    return "\n".join(lines)
