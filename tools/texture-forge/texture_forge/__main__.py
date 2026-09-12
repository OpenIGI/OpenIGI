"""Upscales Project I.G.I.'s textures and derives the surface maps a physically-based renderer needs.

    texture-forge models                          what weights are catalogued, and under what licence
    texture-forge all      <png-dir> <out-dir>    upscale and derive, the whole job
    texture-forge upscale  <png-dir> <out-dir>    upscale only
    texture-forge maps     <png-dir> <out-dir>    derive only, from PNGs that are already the size you want
    texture-forge sheet    <png-dir> <out.png>    a contact sheet of before and after, for judging a setting

The input is whatever `ResExtract images` wrote -- a flat directory of PNGs named after the textures:

    dotnet run --project tools/OpenIGI.Tools.ResExtract -- images "<game>" <png-dir>

A run RESUMES by default: a texture whose outputs are all present, whole, and the size this command would
produce is left alone, so an interrupted run is continued by repeating the command. `--force` redoes
everything. See `resume.py` for what counts as finished, and why existence alone does not.

The output is two directories the renderer reads, plus five it does not:

    <out-dir>/hd/            the upscaled diffuse          -> r_hd_textures
    <out-dir>/detail/        the packed surface map        -> r_material_pack
    <out-dir>/{normal,orm,roughness,occlusion,height}/     for looking at, and for hand-correcting

NOTHING HERE MAY BE COMMITTED. The game's art with a filter over it is still Innerloop's -- CONTRIBUTING.md
says so in those words -- which is why the renderer derives its maps from the player's own installation at
runtime and caches them per user. Point the cvars at whatever this writes; do not add it to the tree.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

__all__ = ["main"]


def _console():
    from rich.console import Console

    return Console(stderr=True, highlight=False)


def _models_command(_: argparse.Namespace) -> int:
    from rich.table import Table

    from . import models

    console = _console()
    root = models.cache_root()

    table = Table(title="texture-forge weights", title_style="bold", header_style="bold")
    table.add_column("key")
    table.add_column("licence")
    table.add_column("architecture")
    table.add_column("x", justify="right")
    table.add_column("state")
    table.add_column("role", overflow="fold")

    for model in models.CATALOGUE:
        path = root / model.filename
        state = "[dim]not fetched[/dim]"

        if path.exists():
            ok = models._digest(path) == model.sha256  # noqa: SLF001 - the audit is the point
            state = "[green]ok[/green]" if ok else "[bold red]BAD HASH[/bold red]"

        table.add_row(
            model.key, model.licence, model.architecture, str(model.scale), state, model.role
        )

    console.print(table)
    console.print(f"[dim]Weights directory:[/dim] {root}")
    console.print()
    console.print(
        "Every weight above is [bold]CC0-1.0[/bold], verified from the publisher's LICENSE and from\n"
        "OpenModelDB's catalogue. The recorded SHA-256 is the audit: a file that does not match it is\n"
        "not the file whose licence was checked, and will be refused."
    )
    console.print()

    for model in models.CATALOGUE:
        console.print(f"  [cyan]{model.key:<10}[/cyan] [dim]{model.url}[/dim]")

    return 0


def _run(arguments: argparse.Namespace, *, upscale: bool, derive: bool) -> int:
    from rich.progress import (
        BarColumn,
        MofNCompleteColumn,
        Progress,
        SpinnerColumn,
        TextColumn,
        TimeElapsedColumn,
        TimeRemainingColumn,
    )

    from . import pipeline, resume

    console = _console()
    source = Path(arguments.source)
    destination = Path(arguments.destination)

    if not source.is_dir():
        console.print(f"[bold red]error:[/bold red] {source} is not a directory.")
        console.print("Run [cyan]ResExtract images[/cyan] first; see --help for the command.")
        return 2

    settings = pipeline.Settings(
        scale=arguments.scale,
        upscaler=arguments.model,
        use_ai=not arguments.no_ai,
        use_ai_maps=not arguments.classical_maps,
        delight=arguments.delight,
        delight_strength=arguments.delight_strength,
        metallic=arguments.metallic,
        normal_strength=arguments.normal_strength,
        write_inspection=not arguments.no_inspection,
    )

    sources = pipeline.source_paths(source)

    if not sources:
        console.print(f"[bold red]error:[/bold red] no PNGs in {source}.")
        return 2

    outputs = pipeline.Outputs(destination)
    swept = resume.sweep(outputs)

    if swept:
        console.print(f"[dim]Removed {swept} half-written file(s) from an interrupted run.[/dim]")

    # `maps` does not upscale, so its diffuse comes out the size it went in -- and the size is half of how
    # a finished texture is recognised. Passing the requested scale here regardless would declare every
    # already-derived texture unfinished and redo the whole corpus, which is the bug this guards against.
    effective_scale = arguments.scale if upscale else 1

    if arguments.force:
        todo = sources
    else:
        todo = resume.pending(
            sources,
            outputs,
            scale=effective_scale,
            derive=derive,
            inspection=settings.write_inspection,
        )

    done = len(sources) - len(todo)

    console.print(
        f"[bold]{len(sources)}[/bold] texture(s) from [cyan]{source}[/cyan] "
        f"-> [cyan]{destination}[/cyan]"
    )

    if done:
        console.print(
            f"[green]{done}[/green] already finished in the output; "
            f"[bold]{len(todo)}[/bold] to do. [dim](--force redoes everything.)[/dim]"
        )

    if not todo:
        console.print("[green]Nothing to do -- every texture is already there.[/green]")
        return 0

    if settings.use_ai:
        from . import runner

        console.print(f"[dim]{runner.torch_device_report()}[/dim]")

    if settings.delight:
        console.print(
            "[yellow]--delight is on.[/yellow] This is a classical estimate, not a model; "
            "judge it on a contact sheet before trusting a whole run."
        )

    warnings: list[str] = []
    columns = (
        SpinnerColumn(),
        TextColumn("[progress.description]{task.description}"),
        BarColumn(),
        MofNCompleteColumn(),
        TimeElapsedColumn(),
        TimeRemainingColumn(),
    )

    from . import images

    written = 0
    total = len(todo)

    with Progress(*columns, console=console) as progress, pipeline.Session(
        warn=warnings.append
    ) as session:
        upscale_task = (
            progress.add_task("upscaling", total=total) if upscale else None
        )
        derive_task = progress.add_task("deriving maps" if derive else "writing", total=total)

        # Chunked, so memory is bounded by the chunk rather than by the corpus and the first files land in
        # seconds. The models are held by the session across every chunk; reloading them per chunk would
        # reintroduce, at a coarser grain, the per-file reload this whole design exists to avoid.
        for start in range(0, total, pipeline.CHUNK):
            # Read here rather than up front. Reading the corpus first would decode every texture the
            # resume scan just decided to skip, and would hold all of them for the length of the run.
            chunk = [pipeline.read_texture(path) for path in todo[start : start + pipeline.CHUNK]]

            for texture in chunk:
                pipeline.prepare(texture, settings)

            if upscale:
                pipeline.upscale_all(
                    chunk,
                    settings,
                    session,
                    progress=lambda step: progress.advance(upscale_task, step),
                )
                pipeline.finish_alpha(chunk)

            if derive:
                written += pipeline.derive_all(
                    chunk,
                    settings,
                    outputs,
                    session,
                    progress=lambda step: progress.advance(derive_task, step),
                )
            else:
                for texture in chunk:
                    path = outputs.hd / f"{texture.name}.png"

                    if texture.alpha is not None:
                        images.write_rgba(path, texture.rgb, texture.alpha)
                    else:
                        images.write_rgb(path, texture.rgb)

                    written += 1
                    progress.advance(derive_task)

            # The chunk's pixels are on disk now. Dropping them is what keeps the peak bounded.
            for texture in chunk:
                texture.rgb = None  # type: ignore[assignment]
                texture.alpha = None

    for warning in dict.fromkeys(warnings):
        console.print(f"[yellow]warning:[/yellow] {warning}")

    skipped = f" [dim]({done} already done)[/dim]" if done else ""
    console.print(f"[green]Wrote {written} texture(s).[/green]{skipped}")
    console.print()
    console.print("Point the renderer at them (note [cyan]-lockstep[/cyan]: without it the saved")
    console.print("settings silently override every [cyan]+cvar[/cyan] on the command line):")
    console.print()
    console.print(
        f"  [dim]OpenIGI.Desktop.exe -lockstep +r_hd_textures {outputs.hd} "
        f"+r_material_pack {outputs.detail} +r_material_detail 0.5[/dim]"
    )

    return 0


def _sheet_command(arguments: argparse.Namespace) -> int:
    from . import sheet

    console = _console()
    source = Path(arguments.source)

    if not source.is_dir():
        console.print(f"[bold red]error:[/bold red] {source} is not a directory.")
        return 2

    destination = Path(arguments.destination)
    count = sheet.write(
        source,
        destination,
        Path(arguments.compare) if arguments.compare else None,
        limit=arguments.limit,
        width=arguments.columns,
    )

    if count == 0:
        console.print("[bold red]error:[/bold red] nothing to put on the sheet.")
        return 2

    console.print(f"[green]Wrote {destination}[/green] ({count} texture(s)).")
    return 0


def _add_run_arguments(parser: argparse.ArgumentParser) -> None:
    from . import models

    parser.add_argument("source", help="directory of PNGs, as ResExtract images writes")
    parser.add_argument("destination", help="where the packs are written")
    parser.add_argument(
        "--scale",
        type=int,
        default=4,
        choices=(1, 2, 4, 8),
        help=(
            "how much larger. 4 is the default and the sensible one: the renderer uploads uncompressed "
            "RGBA8, so 8x is four times the video memory for a level and will not fit comfortably on a "
            "10 GB card"
        ),
    )
    parser.add_argument(
        "--model",
        default="dat2",
        choices=models.UPSCALERS,
        help=(
            "which upscaler. dat2 is the default and was chosen by measurement, not by reading the model "
            "cards: on this game's art rplksr leaves a quarter of adjacent pixels EXACTLY equal (45%% of "
            "them on a wrapped portrait), which is visible posterisation, against 16%% for dat2 -- and "
            "unlike rplksr, dat2 is indifferent to whether the texture was wrapped. rplksr is roughly "
            "eight times faster and is the right choice for a quick look"
        ),
    )
    parser.add_argument(
        "--no-ai",
        action="store_true",
        help="use no models at all; resample and derive everything classically",
    )
    parser.add_argument(
        "--classical-maps",
        action="store_true",
        help="keep the AI upscale but derive the surface maps the way the renderer does",
    )
    parser.add_argument(
        "--delight",
        action="store_true",
        help=(
            "try to remove the painted-in lighting. OFF by default and deliberately so: there is no "
            "delighting model at any usable licence, this is a classical estimate, and it cannot tell a "
            "painted shadow from a genuinely dark material"
        ),
    )
    parser.add_argument("--delight-strength", type=float, default=0.7, help="0 to 1 (default 0.7)")
    parser.add_argument(
        "--normal-strength",
        type=float,
        default=1.0,
        help=(
            "how far the derived normal may tilt (default 1.0). It reaches BOTH paths: the classical one "
            "scales its gradient by this, and the model's output is calibrated against that same "
            "gradient, so raising it raises both together"
        ),
    )
    parser.add_argument(
        "--metallic",
        type=float,
        default=0.0,
        help=(
            "what to write into the INSPECTION ORM's metallic channel, for interchange with tools that "
            "expect one there. It no longer reaches the renderer: the detail map's alpha carries "
            "occlusion now, and metal is authored per material in materials.json, because no diffuse "
            "texture distinguishes painted steel from grey paint and guessing puts a mirror on half the "
            "level"
        ),
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help=(
            "redo textures that are already in the output directory. A run resumes by default: any "
            "texture whose outputs are all present, whole, and the size this command would produce is "
            "left alone, so an interrupted run is restarted by repeating the same command"
        ),
    )
    parser.add_argument(
        "--no-inspection",
        action="store_true",
        help="write only the two packs the renderer reads, not the five for looking at",
    )


def main(argv: list[str] | None = None) -> int:
    """Entry point. Returns a process exit code: 0 success, 1 usage, 2 failure."""
    parser = argparse.ArgumentParser(
        prog="texture-forge",
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    commands = parser.add_subparsers(dest="command")

    models_parser = commands.add_parser(
        "models", help="list the catalogued weights, their licences and their sources"
    )
    models_parser.set_defaults(handler=_models_command)

    for name, help_text, does_upscale, does_derive in (
        ("all", "upscale and derive: the whole job", True, True),
        ("upscale", "upscale only", True, False),
        ("maps", "derive surface maps only, from PNGs already the size you want", False, True),
    ):
        command_parser = commands.add_parser(name, help=help_text)
        _add_run_arguments(command_parser)
        command_parser.set_defaults(
            handler=lambda arguments, up=does_upscale, dr=does_derive: _run(
                arguments, upscale=up, derive=dr
            )
        )

    sheet_parser = commands.add_parser(
        "sheet", help="a contact sheet of before and after, for judging a setting before a whole run"
    )
    sheet_parser.add_argument("source", help="directory of PNGs")
    sheet_parser.add_argument("destination", help="the PNG to write")
    sheet_parser.add_argument(
        "--compare", help="a second directory to show beside the first, matched by filename"
    )
    sheet_parser.add_argument("--limit", type=int, default=64, help="how many textures (default 64)")
    sheet_parser.add_argument("--columns", type=int, default=8, help="how many across (default 8)")
    sheet_parser.set_defaults(handler=_sheet_command)

    arguments = parser.parse_args(argv)

    if arguments.command is None:
        parser.print_help()
        return 1

    try:
        return arguments.handler(arguments)
    except KeyboardInterrupt:
        print("\ninterrupted", file=sys.stderr)
        return 2
    except (OSError, KeyError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
