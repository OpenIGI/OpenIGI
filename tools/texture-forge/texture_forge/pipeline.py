"""The whole run: what happens to a texture, in what order, and what each stage is defending against.

    read -> key to alpha -> BLEED -> [delight] -> upscale RGB and alpha separately -> re-threshold
         -> BLEED AGAIN -> normal / roughness / height -> occlusion -> pack

Most of those stages exist because of one specific failure, and each is documented where it lives:
`alpha.py` for the two bleeds and the re-threshold, `runner.py` for the wrap padding and the batching,
`delight.py` for the optional albedo recovery, `classical.py` for the derivations, `pack.py` for the layout.

WHAT RUNS AS A BATCH AND WHAT RUNS PER TEXTURE. The models want batches -- at 128x128 a forward pass is
dominated by kernel-launch overhead, so feeding them one image at a time leaves the card idle. Everything
around them is numpy over a single image. So each stage is run over a whole GROUP of textures at once, and
the per-texture derivations finish the job afterwards.

WHY A GROUP AND NOT THE WHOLE CORPUS. Staging the entire 1,363 textures through each stage in turn was the
first shape of this and it was wrong twice over. A 128-pixel texture at 4x is a megabyte, four maps of it
are four more, and holding all of that for the whole corpus is about ten gigabytes -- survivable on this
machine and not on a smaller one. Worse, nothing at all is written until the last model has finished with
the last texture, so a twenty-minute run shows an empty output directory for nineteen of them and there is
no way to tell a slow run from a hung one. Chunked, memory is bounded by the chunk, the first files land in
seconds, and the batches are still easily large enough to keep the card busy.
"""

from __future__ import annotations

import hashlib

from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

from . import alpha as alpha_ops
from . import classical, images, pack, tex, tiling
from . import delight as delight_ops

__all__ = [
    "Settings",
    "Texture",
    "Outputs",
    "Session",
    "CHUNK",
    "source_paths",
    "group_by_content",
    "relative_name",
    "write_asset",
    "read_texture",
    "load_textures",
    "prepare",
    "upscale_all",
    "finish_alpha",
    "derive_all",
]


CHUNK = 96
"""
How many textures are carried through the stages together.

Large enough that the shape-grouped batches inside a stage still fill the card -- the commonest size in
this game's art is 64x64 at 294 of 1,363 textures, so a chunk this size holds about twenty of them -- and
small enough that the peak working set is a few hundred megabytes rather than ten gigabytes.
"""


class Session:
    """The loaded models, held across chunks.

    This is the whole reason chunking does not cost anything. Loading a model is reading weights off disk
    and initialising a network; doing that per chunk would reintroduce, at a coarser grain, exactly the
    per-file reload that makes naive upscaler scripts a hundred times slower than they need to be. Loaded
    once on first use, kept until the run ends.

    A model that will not load is remembered as absent rather than retried, so a missing weight costs one
    failed attempt for the run instead of one per chunk.
    """

    def __init__(self, *, warn: Callable[[str], None] | None = None) -> None:
        self._loaded: dict[str, object | None] = {}
        self._warn = warn

    def get(self, key: str):
        """Returns a loaded model, or None if it is unavailable. Reports the reason once."""
        if key in self._loaded:
            return self._loaded[key]

        from . import runner

        try:
            model = runner.load(key)
        except Exception as error:  # noqa: BLE001 - reported once, then fallen back from
            if self._warn is not None:
                self._warn(f"the {key} model did not load ({error}); falling back.")
            model = None

        self._loaded[key] = model
        return model

    def close(self) -> None:
        """Drops every model and hands the memory back."""
        for model in self._loaded.values():
            if model is not None:
                model.close()

        self._loaded.clear()

    def __enter__(self) -> "Session":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


@dataclass
class Settings:
    """Everything the run was asked for, gathered in one place."""

    scale: int = 4
    upscaler: str = "dat2"
    use_ai: bool = True
    """Whether any model runs at all. Off gives a complete, correct pack derived entirely classically."""

    use_ai_maps: bool = True
    """Whether the three 1x models run. Off keeps the AI upscale but derives the maps the old way."""

    delight: bool = False
    delight_strength: float = 0.7
    metallic: float = 0.0
    normal_strength: float = 1.0
    derive_pbr: bool = True
    """Whether any surface map is derived at all, or the run only upscales the diffuse.

    The maps are INVENTED DATA -- a normal, a roughness and an occlusion inferred from a photograph of a
    surface, none of which the game ever shipped. A player who wants the original art at a higher
    resolution and nothing else should not have to take them, and should not pay for them either: the
    derivation is the slow half of a run and the detail maps are most of the output on disk. Off means no
    detail map is written, no inspection map is written, and the three 1x models never load.
    """

    write_inspection: bool = True
    """Whether the normal/orm/roughness/occlusion/height PNGs are written beside the detail map.

    On by default and worth the disk: a packed texture is unreadable at a glance, and these maps are
    invented data that has to be judged by eye. The renderer reads exactly one of them.
    """


@dataclass
class Texture:
    """One texture as it moves through the run."""

    name: str
    path: Path
    rgb: np.ndarray
    alpha: np.ndarray | None
    cutout: bool = False
    coverage: float = 1.0
    """The fraction of texels above the alpha cutoff before the upscale, so it can be restored after."""

    tiles: tiling.Tiling = field(default_factory=lambda: tiling.Tiling(True, True))
    """
    Which axes this texture repeats along, measured before anything touches it.

    Measured on the SOURCE rather than on the upscaled result, because it is a property of the art and the
    source is the only version of it nothing has altered yet.
    """

    names: tuple[str, ...] = ()
    """Every path this identical image is addressed at, `name` included.

    The overrides folder mirrors the installation, so an asset packed into eight levels is addressed at
    eight paths -- and the extractor writes it eight times, because each of those paths has to be
    replaceable on its own. The IMAGE is the same one. Upscaling it eight times would be five times the
    GPU work for byte-identical output, so it is upscaled once and written to every path.

    Measured on the retail game: 7199 paths, 1444 distinct images.
    """


@dataclass
class Outputs:
    """Where each kind of result is written.

    THE ROOT IS AN OVERRIDES FOLDER. Not a directory of PNGs the renderer is pointed at by a setting of
    its own -- that is gone -- but the game's own layout, holding the game's own container format, so the
    whole output can be dropped in as `<game>/overrides` and every asset is found by the path it already
    had. See `tex.py` for why one file carries both the diffuse and the map derived from it.

    The inspection maps are NOT part of that tree. They are for looking at, nothing loads them, and a
    thousand stray files inside the overrides folder would be indexed by the game on every scan for no
    reason -- so they go to a sibling directory.
    """

    root: Path

    def asset(self, name: str) -> Path:
        """The container for one texture, at the path the installation addresses it by."""
        return self.root / f"{name}.tex"

    def inspection(self, kind: str) -> Path:
        return self.root.with_name(self.root.name + ".inspection") / kind


def source_paths(source: Path) -> list[Path]:
    """Every PNG under a directory, as `ResExtract images` wrote them: a tree, not a flat directory.

    A tree because a texture NAME is not unique in this game -- the art is layered, and a level can ship
    its own `000_04_1` that looks nothing like the shared one. The path disambiguates them, and it is the
    path the overrides folder addresses each by, so the tree is carried through unchanged from the
    extractor to the finished pack.

    Paths rather than pixels, and sorted, for two reasons. `resume` decides what to skip from the path
    alone, so a skipped texture is never decoded; and a stable order means an interrupted run resumes
    where it stopped instead of somewhere else in the corpus.
    """
    return sorted(source.rglob("*.png"), key=lambda item: item.as_posix().lower())


def relative_name(source: Path, path: Path) -> str:
    """The name a texture is addressed by: its path under the source, without the extension."""
    return path.relative_to(source).with_suffix("").as_posix()


def group_by_content(source: Path, paths: Sequence[Path]) -> list[tuple[Path, tuple[str, ...]]]:
    """Collapses byte-identical sources into one job each, keeping every name they answer to.

    Most of this game's art is packed into every level that draws it, so the extractor's tree holds the
    same image many times over -- 7199 paths for 1444 distinct images. Upscaling each path would be five
    times the GPU work for output that is identical byte for byte.

    Grouped on the FILE, not the decoded pixels: the extractor wrote these, so identical assets produce
    identical PNGs, and hashing bytes is a read where decoding is a decode. The representative is the
    first path in sorted order, so a run is reproducible and an interrupted one resumes in the same place.
    """
    digests: dict[bytes, list[Path]] = {}

    for path in paths:
        digest = hashlib.sha256(path.read_bytes()).digest()
        digests.setdefault(digest, []).append(path)

    groups = [
        (members[0], tuple(relative_name(source, member) for member in members))
        for members in digests.values()
    ]

    return sorted(groups, key=lambda group: group[0].as_posix().lower())


def read_texture(path: Path, source: Path | None = None, names: tuple[str, ...] = ()) -> Texture:
    """Reads one PNG into the form the stages want."""
    rgb, alpha = images.read(path)
    name = path.stem if source is None else relative_name(source, path)

    return Texture(name=name, path=path, rgb=rgb, alpha=alpha, names=names or (name,))


def load_textures(source: Path) -> list[Texture]:
    """Reads a whole directory at once.

    The run reads a chunk at a time instead -- holding 1,363 decoded sources costs several hundred
    megabytes for no reason, and pays it before the first texture is written. This stays for tests and for
    anything driving the stages directly over a handful of files.
    """
    return [read_texture(path, source) for path in source_paths(source)]


def prepare(texture: Texture, settings: Settings) -> Texture:
    """Everything that has to happen before a texture meets the network.

    The order matters and is not arbitrary: the bleed comes before the delight, because delighting a texture
    whose holes are still full of arbitrary black would let that black into the illumination estimate and
    darken the correction for the whole image.
    """
    # Measured first, on untouched pixels. The bleed below rewrites everything under the alpha, which
    # is exactly the region a decal's border lives in, and measuring after it would call a decal tiling.
    texture.tiles = tiling.detect(texture.rgb, texture.alpha)

    if texture.alpha is not None:
        texture.cutout = alpha_ops.is_cutout(texture.alpha)
        above = np.count_nonzero(texture.alpha >= alpha_ops.CUTOUT_CUTOFF)
        texture.coverage = float(above / texture.alpha.size)
        texture.rgb = alpha_ops.bleed(texture.rgb, texture.alpha)

    if settings.delight:
        texture.rgb = delight_ops.delight(texture.rgb, strength=settings.delight_strength)

    return texture


def _resample(image: np.ndarray, factor: float) -> np.ndarray:
    """Lanczos, for the no-model path and for coming back down after a chain overshoots.

    Not an upscale in the sense this tool means -- it invents nothing, and the result is exactly as blurry
    as the source, only larger. It exists so `--no-ai` produces a complete and usable pack rather than an
    error, and so the AI result has something honest to be compared against.
    """
    from PIL import Image

    height, width = image.shape[:2]
    target = (max(int(round(width * factor)), 1), max(int(round(height * factor)), 1))

    if target == (width, height):
        return image

    single = image.ndim == 2
    data = images.quantise(image)
    mode = "L" if single else "RGB"

    resized = Image.fromarray(data, mode=mode).resize(target, Image.Resampling.LANCZOS)
    return (np.asarray(resized, dtype=np.float32) / 255.0).astype(np.float32)


def upscale_all(
    textures: Sequence[Texture],
    settings: Settings,
    session: Session,
    *,
    progress: Callable[[int], None] | None = None,
) -> None:
    """Runs a chunk through the upscaler: colour first, then alpha as its own pass.

    Alpha goes through the SAME model as a greyscale triple rather than through a resize, because the edge
    of a cut-out is exactly the high-frequency detail a model is good at and a resize is not. PBRify's own
    chain does the same, and it is what keeps a wire fence a wire fence instead of a grey smear.
    """
    model = session.get(settings.upscaler) if settings.use_ai else None

    if model is None:
        for texture in textures:
            texture.rgb = _resample(texture.rgb, settings.scale)

            if texture.alpha is not None:
                texture.alpha = _resample(texture.alpha, settings.scale)

            if progress is not None:
                progress(1)

        return

    # Chaining beats one pass followed by a resize, because the second pass has real detail to work from
    # where a resize has only the first pass's output to interpolate. But only while it does not OVERSHOOT.
    #
    # A 4x model asked for 8x used to run twice, reaching 16x, and come back down by half. That renders
    # sixteen times the pixels to keep eight, and the discarded three quarters are not free: the second
    # pass of a 256-pixel texture is a 1024-pixel input, which is five times a DAT-2 batch budget on its
    # own. The card cannot hold it, Windows quietly spills the difference into system memory rather than
    # failing, and the run does not crash -- it just appears to stop. So the chain stops at the last whole
    # multiple that does not exceed what was asked for, and the remainder is a resample.
    rounds = 1

    while model.scale ** (rounds + 1) <= settings.scale:
        rounds += 1

    for _ in range(rounds):
        wrapping = [(item.tiles.x, item.tiles.y) for item in textures]

        for texture, result in zip(
            textures, model.run([item.rgb for item in textures], wrap=wrapping)
        ):
            texture.rgb = result

        keyed = [texture for texture in textures if texture.alpha is not None]

        if keyed:
            greys = [np.repeat(texture.alpha[..., None], 3, axis=-1) for texture in keyed]
            keyed_wrap = [(item.tiles.x, item.tiles.y) for item in keyed]

            for texture, result in zip(keyed, model.run(greys, wrap=keyed_wrap)):
                texture.alpha = result[..., 0]

    # One tick per texture, whatever the chain did. Reporting a tick per ROUND meant the bar's total had to
    # predict how many rounds there would be, and when the chain stopped overshooting the prediction was
    # wrong and every run sat at half.
    if progress is not None:
        progress(len(textures))

    achieved = model.scale**rounds

    if achieved != settings.scale:
        correction = settings.scale / achieved

        for texture in textures:
            texture.rgb = _resample(texture.rgb, correction)

            if texture.alpha is not None:
                texture.alpha = _resample(texture.alpha, correction)


def finish_alpha(textures: Sequence[Texture]) -> None:
    """Restores the binary silhouette and re-bleeds, after the network has had its way with both.

    Both halves matter. The upscaler returns a ramp where the source had one bit, and it writes its own
    colour back into the region the first bleed filled -- so a run that skips this ships soft-edged foliage
    with a fringe, which is the exact pair of faults the two operations exist to prevent.
    """
    for texture in textures:
        if texture.alpha is None:
            continue

        if texture.cutout:
            texture.alpha = alpha_ops.rethreshold(texture.alpha, texture.coverage)

        texture.rgb = alpha_ops.bleed(texture.rgb, texture.alpha)


def _ai_maps(
    textures: Sequence[Texture],
    options: classical.Options,
    session: Session,
) -> dict[str, list[np.ndarray]]:
    """Runs the three 1x models over the upscaled albedo, returning whatever loaded.

    A model that will not load is an absent key, not a failure: the classical derivation covers every map,
    so a partial AI result is a partial improvement rather than a broken run.
    """
    produced: dict[str, list[np.ndarray]] = {}

    for key in ("normal", "roughness", "height"):
        model = session.get(key)

        if model is None:
            continue

        results = model.run(
            [texture.rgb for texture in textures],
            wrap=[(item.tiles.x, item.tiles.y) for item in textures],
        )

        if key == "normal":
            # PBRify emits DirectX handedness -- green DOWN -- and the shader reconstructs Z assuming
            # OpenGL, green UP. A normal map read under the wrong convention lights every bump from the
            # opposite side, which reads as the surface being inverted rather than as an error, and is
            # therefore easy to ship.
            #
            # MEASURED, not inferred from the `_normal_dx` filename in the publisher's chain. Correlating
            # this model's output against `classical.normal_from` -- the port of the generator whose
            # handedness the shader already agrees with -- over a whole texture gives +0.73 with the flip
            # below and -0.73 without it, while the x channel agrees at +0.59 either way and so confirms
            # the two are describing the same surface. Recorded here because the next person to doubt it
            # should re-run that correlation rather than squint at a picture.
            produced["normal_x"] = [(item[..., 0] * 2.0) - 1.0 for item in results]
            produced["normal_y"] = [1.0 - (item[..., 1] * 2.0) for item in results]
        elif key == "roughness":
            # The model returns roughness in [0, 1]; the shader wants a MULTIPLIER around one for whatever
            # materials.json gave the surface. Mapped so mid-grey is unity, and moved by the same range the
            # classical derivation is allowed -- this is a texture's busyness standing in for a measurement
            # either way, and letting the model swing further would not make it more of a measurement.
            produced["roughness"] = [
                (1.0 + ((item.mean(axis=-1) - 0.5) * 2.0 * options.roughness_range)).astype(np.float32)
                for item in results
            ]
        else:
            produced["height"] = [item.mean(axis=-1).astype(np.float32) for item in results]

    return produced


def write_asset(
    texture: Texture,
    outputs: Outputs,
    detail: tuple[np.ndarray, np.ndarray] | None = None,
) -> None:
    """Writes one texture's container to every path that addresses it.

    ONE FILE, not two. The upscaled diffuse and the map derived from it travel as layers of the same
    asset, so they cannot be half-installed or pointed at by two settings that disagree; `detail` absent
    means the run derived none and the file carries the diffuse alone.

    WRITTEN ONCE PER NAME. An image packed into eight levels is addressed at eight paths, and each of
    those has to be replaceable on its own -- but it was upscaled once. Writing bytes is not what a run
    spends its time on.
    """
    for name in texture.names or (texture.name,):
        tex.write(outputs.asset(name), texture.rgb, texture.alpha, detail)


def derive_all(
    textures: Sequence[Texture],
    settings: Settings,
    outputs: Outputs,
    session: Session,
    *,
    progress: Callable[[int], None] | None = None,
) -> int:
    """Derives every surface map from the upscaled albedo and writes the pack.

    The AI models run on the UPSCALED image rather than the source, which is what PBRify's own chain does
    and for the obvious reason: there is more to look at. Occlusion is ALWAYS the classical horizon search
    -- there is no AI occlusion model at any licence, and the search wants a height field rather than a
    picture, so it is the one map with only one implementation.
    """
    options = classical.Options(normal_strength=settings.normal_strength)
    ai: dict[str, list[np.ndarray]] = {}

    if settings.derive_pbr and settings.use_ai and settings.use_ai_maps:
        ai = _ai_maps(textures, options, session)

    written = 0

    for index, texture in enumerate(textures):
        if not settings.derive_pbr:
            # Upscale only. Written here and skipped entirely rather than derived and thrown away: the
            # derivation is the slow half of a run, and a player who asked for the original art at a
            # higher resolution has not asked for a normal map inferred from a photograph of it.
            write_asset(texture, outputs)
            written += 1

            if progress is not None:
                progress(written)

            continue

        # The classical elevation, always, because two things below need a field whose DEPTH means
        # something rather than one that merely has the right shape. The height model's output is an
        # estimate with no absolute scale and is markedly smoother than this; anything that measures a
        # magnitude off it inherits that flatness.
        classical_field = classical.elevation_from(
            texture.rgb, texture.alpha, options, texture.tiles
        )

        # What gets WRITTEN as the height map, and what the roughness falls back to, is still the model's
        # when one loaded -- it is the better picture of the surface, and neither of those reads a
        # magnitude off it.
        height_field = ai["height"][index] if "height" in ai else classical_field

        if "normal_x" in ai:
            # The model's shape, the classical derivation's depth. A neural normal model has no absolute
            # scale -- nothing in its training ties the depth of a mortar line to a number -- and on this
            # game's art its output is far flatter than the calibrated derivation: a median tilt of 3.83
            # degrees against 13.47 on the same image, measured over 24 textures. A pack built from it
            # gave the renderer a working strength control over almost no signal, which is exactly how
            # "the PBR maps do nothing at any r_material_detail" presents. See classical.match_relief.
            normal_x = ai["normal_x"][index]
            normal_y = ai["normal_y"][index]

            # The reference comes from the CLASSICAL elevation, never from `height_field`. That variable
            # holds the height MODEL's output when one loaded, and that output is itself smoothed -- so
            # calibrating against it measures one neural estimate with another and inherits the flatness
            # the calibration exists to correct. It undershot by about five times when this read
            # `height_field`, which is the sort of mistake that looks like it worked: every texture moved
            # in the right direction and none of them arrived.
            reference_x, reference_y = classical.normal_from(
                classical_field, options, texture.tiles
            )
            normal_x, normal_y = classical.match_relief(
                normal_x, normal_y, reference_x, reference_y
            )
        else:
            normal_x, normal_y = classical.normal_from(height_field, options, texture.tiles)

        if "roughness" in ai:
            roughness = ai["roughness"][index]
        else:
            roughness = classical.roughness_from(height_field, options, texture.tiles)

        # From the classical field, not from `height_field`. A horizon search measures how deep a texel
        # sits relative to its neighbours, which is a magnitude -- and run against the height model's
        # smoothed output it finds almost nothing: measured over the probe set, median occlusion 0.988
        # with 55% of texels shaded at all, against 0.808 and 92% from the classical elevation. Six
        # times too little contact shading, and it would have read as "the AO channel does nothing",
        # which is precisely the report this whole pass exists to answer. It is also what makes this
        # tool agree with MaterialMapGenerator, which derives the same map for players who have never
        # run this.
        occlusion = classical.occlusion_from(classical_field, options, texture.tiles)

        # Occlusion rather than settings.metallic in the fourth slot: the detail map's alpha carries the
        # horizon search now. Metal is per-material and lives in materials.json, which is the only place
        # anything here could honestly say a surface is steel -- the inspection ORM below still carries
        # settings.metallic, because that file exists for interchange with tools that expect it there.
        write_asset(texture, outputs, pack.detail(normal_x, normal_y, roughness, occlusion))

        if settings.write_inspection:
            name = f"{texture.name}.png"

            images.write_rgb(
                outputs.inspection("normal") / name, pack.normal_image(normal_x, normal_y)
            )
            images.write_rgb(
                outputs.inspection("orm") / name,
                pack.orm_image(occlusion, roughness, settings.metallic),
            )
            images.write_grey(
                outputs.inspection("roughness") / name, pack.roughness_display(roughness)
            )
            images.write_grey(outputs.inspection("occlusion") / name, occlusion)
            images.write_grey(outputs.inspection("height") / name, height_field)

        written += 1

        if progress is not None:
            progress(1)

    return written
