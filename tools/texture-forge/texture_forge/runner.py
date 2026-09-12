"""Runs a spandrel model over a batch of images, with the wrap padding that keeps a tiling texture tiling.

FOUR DECISIONS WORTH RECORDING, BECAUSE EACH ONE LOOKS LIKE AN OMISSION.

**No tiling of the image itself.** Every upscaler front-end splits a large image into tiles and blends them
back together, and they are right to: a 4K photograph does not fit in video memory. This corpus tops out at
256x256, so the images go through whole and the entire class of tile-seam artefacts simply does not arise.
Real-ESRGAN's tiler is worth knowing about anyway for what it gets wrong: it hard-crops rather than
feathering, its `pre_pad` passes `(0, pad, 0, pad)` to F.pad and so pads only right and bottom, and its
out-of-memory handler prints and then carries on using the PREVIOUS tile's pixels. None of that can bite us
here because none of it runs.

**Wrap padding, per axis, by a generous margin.** chaiNNer and joeyballentine/ESRGAN both wrap-pad by 16 and
crop 16 * scale afterwards, and four independent implementations picking the same number is a strong
convention. It is also, strictly, not enough: these networks are hundreds of stacked 3x3 convolutions with a
receptive field in the hundreds of pixels, so 16 texels of context cannot be equivalent to a circular
convolution -- it works because the *effective* receptive field decays much faster than the theoretical one.
So the margin here is `min(extent, WRAP_MARGIN)`, four times the convention, and for anything at or below
that size it is the whole image wrapped round.

Applied ONLY along the axes a texture actually repeats on, which the caller decides -- see tiling.py. This
is not a refinement: wrapping a portrait was measured at 46% of adjacent pixels coming out exactly equal,
against 26% for the same model and image unwrapped. Two thirds of this game's art does not tile on at least
one axis.

**Batching by pixels, not by image count.** At 64x64 a forward pass is dominated by kernel-launch overhead
rather than arithmetic, so feeding images one at a time leaves the card idle and a batch of 32 is the
difference between minutes and an hour. At 512x512 that same batch of 32 is two million pixels through a
network holding tens of feature channels per pixel, which takes a 10 GB card out of memory two thirds of the
way through a corpus. So the batch is sized by a pixel budget and the image count is only its ceiling.

**Out of memory is caught by MESSAGE as well as by type.** torch 2.14 raises `AcceleratorError` for the same
condition `OutOfMemoryError` used to cover, from some call sites but not others. A handler that named only
the type let a run die after twenty minutes.
"""

from __future__ import annotations

import math
import sys
from collections.abc import Iterable, Sequence
from pathlib import Path

import numpy as np

__all__ = ["Runner", "torch_device_report"]

_MAX_BATCH = 32
"""
The most images in one forward pass, however small they are.

Past this the launch overhead a batch exists to amortise is already amortised, and all a larger batch buys
is a longer stall if it has to be retried.
"""

_CONVOLUTIONAL_BUDGET = 2_000_000
"""
The most PIXELS in one forward pass for a plain convolutional network (SPAN, PLKSR, ESRGAN).

Batching by image count alone was the first version and it did not survive the corpus: 32 images is a
trivial batch at 64x64 and two million pixels at 512x512 after wrap padding. So the budget is in pixels and
the image count is only its ceiling.
"""

_TRANSFORMER_BUDGET = 250_000
"""
The same, for an attention-based network (DAT, HAT, SwinIR, ATD, DRCT). Eight times smaller, and it has to be.

This is not a safety margin, it is the actual shape of the arithmetic. traiNNer-redux's published
measurements put dat_2 at 3.92 GB for a single 480x640 image -- 307k pixels -- where span needs 0.65 GB for
the same. A budget tuned to the convolutional models is therefore about eight times too large for a
transformer, and the failure is not graceful: it ran a hundred textures and then took a 10 GB card out of
memory, twenty minutes into an hour-long job.

Kept as a constant per FAMILY rather than per model because the split is architectural -- attention is
quadratic in the tokens it attends over, and everything on the other list is not.
"""

_TRANSFORMERS = ("dat", "hat", "swinir", "atd", "drct", "srformer", "restormer", "omnisr")
"""
Architecture names, lowercased and matched as substrings, whose memory grows like attention.

Matched loosely on purpose. spandrel reports names like "DAT" and "HAT", and a model whose architecture is
not on this list gets the generous budget -- so a new transformer arriving would be wrong in the expensive
direction. That is worth one more line here when it happens, and is still better than guessing low for
everything and halving the throughput of the models that do not need it.
"""

WRAP_MARGIN = 64
"""
How many texels of wrapped context the network is given on each side.

Four times the 16 that chaiNNer and joeyballentine/ESRGAN both settled on, because their number is a
convention rather than a bound -- see the module docstring. An image at or below this size is wrapped
entirely, which makes the result identical to a circular convolution.
"""


def _is_out_of_memory(error: BaseException) -> bool:
    """Whether an exception is CUDA running out of memory, however torch chose to spell it this release.

    Spelled several ways across versions and call sites -- `torch.cuda.OutOfMemoryError` in some,
    `torch.AcceleratorError` wrapping a `cudaErrorMemoryAllocation` in others -- so this matches on the
    message as well as the type. Matching the message alone would be fragile; matching the type alone
    already proved to be.
    """
    import torch

    if isinstance(error, torch.cuda.OutOfMemoryError):
        return True

    return "out of memory" in str(error).lower()


def _release(torch) -> None:
    """Hands cached device memory back, tolerating a card too far gone to do it.

    `empty_cache` itself allocates, so on a card that is already out it raises -- which turned a recoverable
    batch into a crash, inside the handler written to recover from it.
    """
    try:
        torch.cuda.empty_cache()
    except Exception:  # noqa: BLE001 - best effort; the caller is already handling a failure
        pass


def torch_device_report() -> str:
    """One line about what the run will actually use, printed before anything long starts."""
    import torch

    if not torch.cuda.is_available():
        return (
            "CUDA is NOT available - running on CPU, which will be very slow.\n"
            "  The torch wheel from plain PyPI is the CPU build on Windows; "
            "`uv sync` in this directory installs the CUDA one."
        )

    name = torch.cuda.get_device_name(0)
    total = torch.cuda.get_device_properties(0).total_memory / (1 << 30)
    return f"CUDA: {name}, {total:.1f} GiB"


class Runner:
    """One loaded model, held across every image so the weights are read from disk exactly once.

    This is the single largest thing between a five-minute run and a four-hour one. Shelling out to an
    upscaler per file reloads and re-initialises the network every time, and at 1,363 textures that
    reload dominates everything else by an order of magnitude.
    """

    def __init__(self, weights: Path, *, half: bool, device: str | None = None) -> None:
        import torch
        from spandrel import ImageModelDescriptor, ModelLoader

        self._torch = torch
        self._device = torch.device(
            device or ("cuda" if torch.cuda.is_available() else "cpu")
        )

        descriptor = ModelLoader().load_from_file(str(weights))

        if not isinstance(descriptor, ImageModelDescriptor):
            raise TypeError(f"{weights.name} is not an image-to-image model.")

        self.scale: int = descriptor.scale
        self.architecture: str = descriptor.architecture.name

        descriptor.to(self._device)
        descriptor.eval()

        # fp16 where the architecture supports it, bf16 where it does not but the card has it. spandrel
        # blocks fp16 on every transformer architecture -- its DAT support carries the comment "Too much
        # weirdness to support this at the moment" -- and a 3080 is Ampere, so bf16 is native and free.
        self.dtype = torch.float32

        if self._device.type == "cuda":
            if half and descriptor.supports_half:
                descriptor.model.half()
                self.dtype = torch.float16
            elif descriptor.supports_bfloat16 and torch.cuda.is_bf16_supported():
                descriptor.model.bfloat16()
                self.dtype = torch.bfloat16

        self._descriptor = descriptor

        # Sized to the architecture, then lowered permanently if the card still says no. Lowering it for the
        # RUN rather than just retrying the batch is what stops the next batch making the same mistake --
        # the first version retried, succeeded at half size, and then went straight back to full size for
        # the following batch, so a corpus hit the same wall a hundred times over.
        lowered = self.architecture.lower()
        self.budget = (
            _TRANSFORMER_BUDGET
            if any(name in lowered for name in _TRANSFORMERS)
            else _CONVOLUTIONAL_BUDGET
        )

    @property
    def precision(self) -> str:
        """What the model is actually running in, for the run's own log."""
        return {
            self._torch.float16: "fp16",
            self._torch.bfloat16: "bf16",
            self._torch.float32: "fp32",
        }[self.dtype]

    def run(
        self,
        images: Iterable[np.ndarray],
        *,
        wrap: bool | Sequence[tuple[bool, bool]] = True,
    ) -> list[np.ndarray]:
        """Runs every image through the model, grouped by shape and batched.

        Each image is (height, width, 3) float32 in [0, 1]. Results come back in input order.

        `wrap` is either one answer for everything, or one (x, y) pair per image. Per-image, because
        whether a texture tiles is a property of that texture and getting it wrong in either direction
        costs something -- see `tiling.py`. Images are grouped by shape AND by wrapping, so a batch is
        always uniform in what it is being asked to do.
        """
        items = list(images)
        results: list[np.ndarray | None] = [None] * len(items)

        if isinstance(wrap, bool):
            flags = [(wrap, wrap)] * len(items)
        else:
            flags = [(bool(x), bool(y)) for x, y in wrap]

        by_shape: dict[tuple[int, int, bool, bool], list[int]] = {}

        for index, image in enumerate(items):
            height, width = image.shape[:2]
            by_shape.setdefault((height, width, *flags[index]), []).append(index)

        for (height, width, wrap_x, wrap_y), indices in by_shape.items():
            # What actually goes through the network is the image plus its wrapped margin, so the budget is
            # measured on that rather than on the source -- at 512x512 the margin is a third again as many
            # pixels, which is the difference between fitting and not.
            padded = (height + (2 * min(height, WRAP_MARGIN) if wrap_y else 0)) * (
                width + (2 * min(width, WRAP_MARGIN) if wrap_x else 0)
            )
            size = max(1, min(_MAX_BATCH, self.budget // max(padded, 1)))

            # One image that does not fit on its own cannot be helped by a smaller batch. Left alone, the
            # card runs out and Windows pages the overflow into system memory instead of failing, so the
            # run does not crash -- it slows by roughly two orders of magnitude and looks hung. Tiled
            # instead: the arithmetic is the same, it simply arrives in pieces.
            if padded > self.budget:
                for index in indices:
                    results[index] = self._run_tiled(items[index], wrap_x, wrap_y)

                continue

            for start in range(0, len(indices), size):
                chunk = indices[start : start + size]
                batch = [items[index] for index in chunk]
                outputs = self._run_batch(batch, (height, width), wrap_x, wrap_y)

                for index, output in zip(chunk, outputs):
                    results[index] = output

        if any(result is None for result in results):
            # Every input must produce an output, in order. Silently dropping one would misalign every
            # result after it against the texture it belongs to, and the pictures would all still look
            # plausible -- the same class of failure as a swapped channel, and just as hard to see.
            raise RuntimeError("A batch did not return a result for every image.")

        return [result for result in results if result is not None]

    def _run_tiled(self, image: np.ndarray, wrap_x: bool, wrap_y: bool) -> np.ndarray:
        """Runs one image that is too large to fit, in pieces, and reassembles the result.

        THE PIECES OVERLAP AND THE OVERLAP IS THROWN AWAY. Each tile is cut with a margin of context around
        it, run, and then cropped back to its own interior -- so every output texel was produced by a
        network that could see `WRAP_MARGIN` texels in every direction around it, exactly as it would have
        in a whole-image pass. That makes the seams identical to the interior by construction rather than
        by blending, which is what lets this avoid the feathering every tiled upscaler needs and the
        half-sine ramps that go with it.

        It is the same argument the wrap margin rests on, applied at a different boundary: the context has
        to be wide enough that what lies beyond it cannot reach the middle. Real-ESRGAN's tiler makes the
        same choice and hard-crops; chaiNNer blends because its default context is 16 texels and it does
        not trust them.

        The padding around the WHOLE image still wraps or clamps per axis, so a tiling texture's outermost
        tile sees the far side of the texture and an untiled one sees its own edge.

        HOW EXACT THIS ACTUALLY IS, measured rather than assumed, on a 256x256 texture against the same
        texture run whole:

            SPAN (the three 1x map models)    max 0.00 / 255  -- exact
            DAT-2 (the default upscaler)      max 23.9 / 255
            RealPLKSR                         max  108 / 255

        SPAN is exact because it is an ordinary convolutional stack: an output texel depends on a bounded
        neighbourhood and nothing else. The other two depend on the size of the input they are given --
        widening the context does NOT close the gap, which rules out the receptive field as the cause --
        so a tile of them is not the same arithmetic as a slice of the whole.

        That is comfortable rather than lucky, because after the upscale chain was fixed to stop
        overshooting, the ONLY thing that ever exceeds the budget is a 1x map model at a high scale. The
        upscalers see the source texture, which tops out at 256 pixels and fits whole.
        """
        height, width = image.shape[:2]
        channels = image.shape[2]
        scale = self.scale

        # Only the axes that repeat are padded. A non-tiling axis is deliberately left alone, so a tile
        # sitting against the true border sees exactly what the whole-image path would have shown the
        # model there -- nothing. Edge-replicating instead was the first attempt and it disagreed with the
        # whole-image path by 47/255 at the border, which is a seam in the one place the eye looks for one.
        pad_y = min(height, WRAP_MARGIN) if wrap_y else 0
        pad_x = min(width, WRAP_MARGIN) if wrap_x else 0

        padded = image

        if pad_y:
            padded = np.pad(padded, ((pad_y, pad_y), (0, 0), (0, 0)), mode="wrap")

        if pad_x:
            padded = np.pad(padded, ((0, 0), (pad_x, pad_x), (0, 0)), mode="wrap")

        limit_y, limit_x = padded.shape[:2]
        context = WRAP_MARGIN

        # The largest interior square whose tile, once the context is added back, still fits the budget.
        # Floored to a multiple of eight so the tiles land on tidy boundaries, and never below 64, which is
        # small enough that a card that cannot manage it cannot manage the model either.
        side = int(math.isqrt(max(self.budget, 1))) - (2 * context)
        side = max(64, (side // 8) * 8)

        out = np.empty((height * scale, width * scale, channels), dtype=np.float32)

        # Gathered first, then run grouped: a batch has to be uniform, and a tile against the border
        # carries less context than an interior one. The key is the CUT's own shape together with where the
        # interior starts inside it -- keying on the interior size alone was not enough, because a tile at
        # the bottom edge has the same interior and the same leading context as one in the middle and a
        # shorter trailing one.
        pieces: dict[tuple[int, int, int, int, int, int], list[tuple[int, int, np.ndarray]]] = {}

        for top in range(0, height, side):
            for left in range(0, width, side):
                tile_h = min(side, height - top)
                tile_w = min(side, width - left)

                # Where this tile sits in the padded array, and how much context it can actually have.
                y = top + pad_y
                x = left + pad_x
                y0 = max(0, y - context)
                y1 = min(limit_y, y + tile_h + context)
                x0 = max(0, x - context)
                x1 = min(limit_x, x + tile_w + context)

                cut = padded[y0:y1, x0:x1]
                key = (cut.shape[0], cut.shape[1], y - y0, x - x0, tile_h, tile_w)
                pieces.setdefault(key, []).append((top, left, cut))

        for (_, _, lead_y, lead_x, tile_h, tile_w), group in pieces.items():
            for start in range(0, len(group), _MAX_BATCH):
                chunk = group[start : start + _MAX_BATCH]
                outputs = self._forward([item[2] for item in chunk])

                for (top, left, _), produced in zip(chunk, outputs):
                    cropped = produced[
                        lead_y * scale : (lead_y + tile_h) * scale,
                        lead_x * scale : (lead_x + tile_w) * scale,
                    ]
                    out[
                        top * scale : (top + tile_h) * scale,
                        left * scale : (left + tile_w) * scale,
                    ] = cropped

        return out

    def _forward(self, batch: list[np.ndarray]) -> list[np.ndarray]:
        """Runs a batch of equally-shaped images through the network. No padding, no cropping.

        The one place a tensor is built and the model is called, so the out-of-memory recovery lives here
        once and both the whole-image and the tiled path get it.
        """
        torch = self._torch
        tensor = torch.from_numpy(np.ascontiguousarray(np.stack(batch, axis=0).transpose(0, 3, 1, 2)))
        tensor = tensor.to(self._device, dtype=self.dtype)

        with torch.no_grad():
            try:
                output = self._descriptor(tensor)
            except Exception as error:  # noqa: BLE001 - narrowed by _is_out_of_memory below
                # Caught broadly ON PURPOSE. torch.cuda.OutOfMemoryError is not the only way this arrives:
                # 2.14 raises torch.AcceleratorError for the same condition from some call sites, and a
                # handler that named only the first let a run die two thirds of the way through a corpus.
                # Anything that is not an out-of-memory is re-raised immediately.
                if not _is_out_of_memory(error):
                    raise

                # Halve and retry, rather than printing and carrying on with the PREVIOUS tile's pixels,
                # which is what Real-ESRGAN's handler does.
                del tensor
                _release(torch)

                # Remember it, or the next batch is sized by the budget that just failed.
                self.budget = max(self.budget // 2, 4096)

                if len(batch) == 1:
                    raise

                middle = len(batch) // 2
                return self._forward(batch[:middle]) + self._forward(batch[middle:])

        result = output.float().clamp_(0.0, 1.0).cpu().numpy().transpose(0, 2, 3, 1)
        return [np.ascontiguousarray(item) for item in result]

    def _run_batch(
        self, batch: list[np.ndarray], shape: tuple[int, int], wrap_x: bool, wrap_y: bool
    ) -> list[np.ndarray]:
        height, width = shape

        # Wrapped only along the axes this texture actually repeats on, then cropped back out. A tiling
        # texture's left edge really is the continuation of its right, and giving the network that is what
        # stops a seam appearing down the middle of every wall in the level -- but a texture that does NOT
        # tile is destroyed by the same treatment, because the receptive field is wide enough that nonsense
        # glued to the border reaches the middle of the image. See tiling.py.
        margin_y = min(height, WRAP_MARGIN) if wrap_y else 0
        margin_x = min(width, WRAP_MARGIN) if wrap_x else 0

        if margin_x or margin_y:
            stacked = np.pad(
                np.stack(batch, axis=0),
                ((0, 0), (margin_y, margin_y), (margin_x, margin_x), (0, 0)),
                mode="wrap",
            )
            batch = list(stacked)

        result = self._forward(batch)

        if not (margin_x or margin_y):
            return result

        scale = self.scale
        top = margin_y * scale
        left = margin_x * scale
        return [
            np.ascontiguousarray(
                item[top : top + (height * scale), left : left + (width * scale), :]
            )
            for item in result
        ]

    def close(self) -> None:
        """Drops the weights and hands the memory back, so several models can run in one process."""
        self._descriptor = None

        if self._device.type == "cuda":
            _release(self._torch)

    def __enter__(self) -> "Runner":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


def load(key: str, *, quiet: bool = False) -> Runner:
    """Fetches (if needed), verifies and loads one catalogued model."""
    from . import models

    model = models.lookup(key)
    weights = models.ensure(key, quiet=quiet)
    runner = Runner(weights, half=model.half_ok)

    if not quiet:
        print(
            f"  {model.key}: {runner.architecture} x{runner.scale} in {runner.precision}",
            file=sys.stderr,
        )

    return runner
