"""What the out-of-memory recovery is allowed to ask for, and what it must not be holding while it asks.

Written after a remaster pass over the game's pictures died on a 10 GiB card reporting 32 GiB allocated.
Two faults, compounding, and neither needs a card to reproduce:

The tiled path sized its batches by COUNT. The tile side is chosen so that one tile with its context fills
the budget exactly, so thirty-two of them is thirty-two times the budget -- 7.9 M pixels through a network
whose budget is 250 k. Nothing had exercised it, because until the remaster pass existed the upscalers only
ever saw source textures, which fit whole; the remaster pass hands them the finished size instead.

And the recovery retried from inside its own `except` block. A live exception owns its traceback, which
owns the frames it passed through, which own their locals -- the whole activation set of the attempt that
just failed. So every halving ADDED an attempt's worth of memory rather than replacing it, and the halving
written to recover memory was the reason there was none left.
"""

from __future__ import annotations

import sys

import numpy as np
import pytest

from texture_forge import runner


def _runner(scale: int, budget: int, descriptor) -> runner.Runner:
    """A Runner around a fake network, with no weights and no card.

    Built by hand rather than constructed: `__init__` loads weights from disk and asks a GPU what it
    supports, and none of that is what these tests are about.
    """
    import torch

    made = object.__new__(runner.Runner)
    made._torch = torch
    made._device = torch.device("cpu")
    made.dtype = torch.float32
    made.scale = scale
    made.architecture = "DAT"
    made._descriptor = descriptor
    made.budget = budget
    return made


class _Recorder:
    """A network that records the shape of every pass and can refuse the large ones."""

    def __init__(self, *, refuse_over: int | None = None) -> None:
        self.passes: list[int] = []
        self.refuse_over = refuse_over
        self.handled_while_called: list[object] = []

    def __call__(self, tensor):
        # The leak guard. If the recovery retried from inside its handler, the exception it is handling is
        # still live right here -- and everything that exception's traceback reaches is still allocated.
        self.handled_while_called.append(sys.exc_info()[1])

        pixels = tensor.shape[0] * tensor.shape[2] * tensor.shape[3]
        self.passes.append(pixels)

        if self.refuse_over is not None and pixels > self.refuse_over:
            import torch

            raise torch.cuda.OutOfMemoryError("CUDA out of memory. Tried to allocate 592.00 MiB")

        return tensor


def test_a_tiled_pass_never_asks_for_more_than_the_budget():
    """The fault that started it: tiles sized to the budget, then batched thirty-two at a time."""
    recorder = _Recorder()
    made = _runner(scale=1, budget=250_000, descriptor=recorder)

    # The remaster pass's own shape: a 256x256 source upscaled fourfold, handed back to an upscaler.
    made._run_tiled(np.zeros((1024, 1024, 3), dtype=np.float32), True, True)

    assert recorder.passes, "nothing was run"
    assert max(recorder.passes) <= made.budget, (
        f"a pass asked for {max(recorder.passes)} pixels against a budget of {made.budget}"
    )


def test_the_whole_image_is_still_covered():
    """Smaller batches must not mean missing tiles: every texel comes back, and from the right place."""
    made = _runner(scale=1, budget=250_000, descriptor=_Recorder())

    image = np.random.default_rng(7).random((600, 400, 3), dtype=np.float32)
    out = made._run_tiled(image, False, False)

    assert out.shape == image.shape
    # The fake network is the identity, so the reassembled image must be the source exactly.
    assert np.array_equal(out, image)


def test_the_retry_is_not_holding_the_failed_attempt():
    """The leak, caught without a card: nothing may be under handling when a retry calls the network."""
    recorder = _Recorder(refuse_over=120_000)
    made = _runner(scale=1, budget=250_000, descriptor=recorder)

    made._run_tiled(np.zeros((512, 512, 3), dtype=np.float32), False, False)

    assert len(recorder.passes) > 1, "the refusal never triggered a retry"
    assert recorder.handled_while_called == [None] * len(recorder.passes), (
        "a retry ran inside the handler for the failure it was recovering from, "
        "which pins that attempt's tensors for the whole of it"
    )


def test_a_card_that_refuses_everything_says_so_once():
    """Not a chain of every halving: concise, unchained, and still recognisable as an out-of-memory."""
    recorder = _Recorder(refuse_over=0)
    made = _runner(scale=1, budget=250_000, descriptor=recorder)

    with pytest.raises(Exception) as caught:  # noqa: PT011 - the type varies by torch release
        made._run_tiled(np.zeros((256, 256, 3), dtype=np.float32), False, False)

    assert runner._is_out_of_memory(caught.value)
    assert caught.value.__cause__ is None
    assert caught.value.__context__ is None


def test_the_budget_is_lowered_for_the_rest_of_the_run():
    """A budget that failed once must not be asked for again -- a corpus would hit the same wall per image."""
    made = _runner(scale=1, budget=250_000, descriptor=_Recorder(refuse_over=120_000))

    made._run_tiled(np.zeros((512, 512, 3), dtype=np.float32), False, False)

    assert made.budget < 250_000


def test_the_floor_stops_the_halving_rather_than_tiling_into_texels():
    assert runner._MIN_BUDGET == 4096

    made = _runner(scale=1, budget=runner._MIN_BUDGET, descriptor=_Recorder(refuse_over=0))

    with pytest.raises(Exception) as caught:  # noqa: PT011
        made._run_tiled(np.zeros((256, 256, 3), dtype=np.float32), False, False)

    assert runner._is_out_of_memory(caught.value)
    assert made.budget == runner._MIN_BUDGET
