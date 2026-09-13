"""Which architectures are known to grow like attention, and what it costs to get one wrong.

A model that is misclassified here still produces correct pixels. That is the problem: it asks the card for
an allocation it cannot serve, the recovery halves the batch and retries, and the run completes -- slower,
silently, inside a progress bar that was always going to take minutes. Nothing fails, so nothing is noticed
except a number that nobody had a baseline for.

RGT-S is the worked example. It reached the catalogue as the alternative remasterer, was not on the list,
and asked a 10 GiB card for 15.7 GiB on its first batch; the same eight textures took 2m52s that way and
1m29s once it was recognised.
"""

from __future__ import annotations

import pytest

from texture_forge import models, runner


@pytest.mark.parametrize(
    "architecture",
    ["DAT", "RGT", "SwinIR", "HAT", "ATD", "DRCT", "SRFormer", "OmniSR", "Restormer"],
)
def test_attention_architectures_get_the_small_budget(architecture: str):
    assert runner.budget_for(architecture) == runner._TRANSFORMER_BUDGET


@pytest.mark.parametrize("architecture", ["SPAN", "PLKSR", "RealPLKSR", "ESRGAN", "Compact"])
def test_convolutional_architectures_get_the_generous_one(architecture: str):
    """The other half of the trade: guessing low everywhere would halve the throughput of these."""
    assert runner.budget_for(architecture) == runner._CONVOLUTIONAL_BUDGET


def test_the_names_are_matched_however_spandrel_spells_them():
    """spandrel reports "DAT" for one model and "RGT" for another, and nothing guarantees the case."""
    assert runner.budget_for("rgt") == runner.budget_for("RGT") == runner.budget_for("RGT-S")


def test_every_catalogued_architecture_is_classified_deliberately():
    """The guard that would have caught RGT-S on the commit that added it.

    Substring matching means an unknown architecture does not raise -- it silently takes the convolutional
    budget. So the catalogue is checked against an explicit list of what is known to be convolutional, and
    anything new has to be put on one side or the other by hand before this passes.

    A PROXY, and worth knowing it is one. What the runner classifies is spandrel's own name for the loaded
    network; what is checked here is the catalogue's human-written label, which is close but not the same
    string ("RealPLKSR+DySample" against spandrel's "RealPLKSR"). That still catches the failure this
    exists for -- a new entry nobody thought about -- without needing weights on disk to run.
    """
    convolutional = {
        "SPAN",
        "PLKSR",
        "RealPLKSR",
        "RealPLKSR+DySample",
        "Compact",
        "ESRGAN",
        "RealESRGAN",
    }

    for model in models.CATALOGUE:
        classified = runner.budget_for(model.architecture) == runner._TRANSFORMER_BUDGET

        assert classified or model.architecture in convolutional, (
            f"{model.key} is a {model.architecture}, which this file has never been told about. "
            "Decide whether its memory grows like attention and say so, rather than letting it "
            "default into the expensive answer."
        )
