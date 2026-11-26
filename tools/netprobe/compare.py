#!/usr/bin/env python3
"""Sets a client's net trace beside its server's and says where the client drew the others wrongly.

Temporary debugging tool. The traces are written by the game's `dbg_trace on` (the client's) and the server's
`dbg trace on` (through `dbg` on a client, or a dedicated server's console); see NetProbe.cs for the lines.

    python compare.py client-YYYYMMDD-HHMMSS.csv server-YYYYMMDD-HHMMSS.csv [--soldiers] [--id N] [--worst 10]

For every human the client showed it reports, against where the server had it at the moment the client drew it:
  * error      where the client's game drew it, metres from the server's truth then (mean, 95th percentile, worst);
  * interp     where the snapshots alone put it (the interpolator's share of that error);
  * jumps      frames on which the drawn body moved more than half a metre;
  * state      frames drawn in another state than the server's, and the runs of them a sixth of a second or longer;
  * flicker    state changes drawn against the server's own over the same ticks;
  * alive      frames drawn dead while alive there, and how late each death was drawn.
And for the client's own player, the corrections and the largest error put right.
"""

import argparse
import bisect
import csv
import math
import statistics
import sys
from collections import defaultdict

UNITS_PER_METRE = 4096.0
SOLDIER_BASE = 100000


def read_server(path):
    ticks = defaultdict(dict)
    marks = []
    with open(path, newline="") as f:
        for row in csv.reader(f):
            if not row:
                continue
            if row[0] == "S":
                tick, hid = int(row[1]), int(row[2])
                ticks[tick][hid] = (float(row[3]), float(row[4]), float(row[5]), int(row[6]), row[7] == "1")
            elif row[0] == "M":
                marks.append((float(row[1]), ",".join(row[2:])))
    return ticks, marks


def read_client(path):
    shown = defaultdict(list)
    own = []
    marks = []
    with open(path, newline="") as f:
        for row in csv.reader(f):
            if not row:
                continue
            if row[0] == "C":
                drawn = None
                if row[9] != "":
                    drawn = (float(row[9]), float(row[10]), float(row[11]), int(row[12]), row[13] == "1")
                shown[int(row[3])].append({
                    "t": float(row[1]),
                    "tick": float(row[2]),
                    "sampled": (float(row[4]), float(row[5]), float(row[6]), int(row[7]), row[8] == "1"),
                    "drawn": drawn,
                })
            elif row[0] == "P":
                own.append((float(row[1]), float(row[2]), int(row[6]), float(row[7])))
            elif row[0] == "M":
                marks.append((float(row[1]), ",".join(row[2:])))
    return shown, own, marks


def truth_at(ticks, sorted_ticks, hid, moment):
    """The server's human at a fractional tick: its place blended between the ticks either side, its state and life the earlier's."""
    i = bisect.bisect_right(sorted_ticks, moment) - 1
    if i < 0:
        return None
    a_tick = sorted_ticks[i]
    a = ticks[a_tick].get(hid)
    if a is None:
        return None
    if i + 1 < len(sorted_ticks):
        b_tick = sorted_ticks[i + 1]
        b = ticks[b_tick].get(hid)
        if b is not None and b_tick > a_tick:
            t = min(max((moment - a_tick) / (b_tick - a_tick), 0.0), 1.0)
            return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, a[3], a[4], a_tick)
    return (a[0], a[1], a[2], a[3], a[4], a_tick)


def dist(p, q):
    return math.sqrt((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 + (p[2] - q[2]) ** 2) / UNITS_PER_METRE


def pct(values, q):
    if not values:
        return 0.0
    s = sorted(values)
    return s[min(len(s) - 1, int(q * len(s)))]


def mark_near(marks, tick):
    best = None
    for when, text in marks:
        if when <= tick and (best is None or when > best[0]):
            best = (when, text)
    return f" (after mark '{best[1]}' at {best[0]:.0f})" if best else ""


def analyse(hid, frames, ticks, sorted_ticks, server_deaths, marks, worst, everything):
    errors, interp_errors, title_errors = [], [], []
    jumps, mismatched, runs, run = [], 0, [], None
    down_here, worst_moments = 0, []
    client_changes = server_changes = 0
    last_drawn = last_state = None
    first_dead_tick = None
    for f in frames:
        truth = truth_at(ticks, sorted_ticks, hid, f["tick"])
        drawn = f["drawn"] or f["sampled"]
        if last_drawn is not None:
            step = dist(drawn, last_drawn)
            if step > 0.5:
                jumps.append((f["tick"], step, last_state, drawn[3]))
        if last_state is not None and drawn[3] != last_state:
            client_changes += 1
        last_drawn, last_state = drawn, drawn[3]
        if not drawn[4] and first_dead_tick is None:
            first_dead_tick = f["tick"]
        if truth is None:
            continue
        if truth[4] and drawn[4]:
            e = dist(drawn, truth)
            errors.append(e)
            interp_errors.append(dist(f["sampled"], truth))
            if f["drawn"]:
                title_errors.append(dist(f["drawn"], f["sampled"]))
            worst_moments.append((e, f["tick"], drawn[3], truth[3]))
        if truth[4] and not drawn[4]:
            down_here += 1
        if truth[4] and drawn[4] and drawn[3] != truth[3]:
            mismatched += 1
            if run is None:
                run = [f["tick"], f["tick"], drawn[3], truth[3]]
            else:
                run[1] = f["tick"]
        elif run is not None:
            runs.append(run)
            run = None
    if run is not None:
        runs.append(run)

    # The server's own state changes over the ticks the client drew.
    if frames:
        lo, hi = frames[0]["tick"], frames[-1]["tick"]
        prev = None
        for tick in sorted_ticks:
            if tick < lo or tick > hi:
                continue
            h = ticks[tick].get(hid)
            if h is None:
                continue
            if prev is not None and h[3] != prev:
                server_changes += 1
            prev = h[3]

    long_runs = [r for r in runs if r[1] - r[0] >= 5]
    worst_error = max(errors) if errors else 0.0
    notable = jumps or long_runs or down_here or worst_error > 0.25 or (hid in server_deaths)
    if not (notable or everything):
        return 0, 0, 0, worst_error, False
    kind = "soldier" if hid >= SOLDIER_BASE else "player"
    print(f"\n{kind} {hid}: {len(frames)} frames")
    if errors:
        print(f"  error   mean {statistics.mean(errors) * 100:.1f} cm, p95 {pct(errors, 0.95) * 100:.1f} cm, worst {max(errors):.2f} m")
        print(f"  interp  mean {statistics.mean(interp_errors) * 100:.1f} cm, worst {max(interp_errors):.2f} m"
              + (f"; title vs snapshots mean {statistics.mean(title_errors) * 100:.1f} cm, worst {max(title_errors):.2f} m" if title_errors else ""))
    print(f"  jumps   {len(jumps)} over 0.5 m in a frame")
    for tick, step, before, after in jumps[:worst]:
        print(f"          tick {tick:.1f}: {step:.2f} m, state {before} -> {after}{mark_near(marks, tick)}")
    print(f"  state   {mismatched} frames in another state than the server's; {len(long_runs)} runs of 5 ticks or more")
    for r in long_runs[:worst]:
        print(f"          ticks {r[0]:.1f}-{r[1]:.1f}: drawn {r[2]}, server {r[3]}{mark_near(marks, r[0])}")
    print(f"  flicker drawn {client_changes} state changes, server {server_changes}")
    death = server_deaths.get(hid)
    late = f", drawn dead at tick {first_dead_tick:.1f}, {first_dead_tick - death:+.1f} ticks from the server's death at {death}" if death is not None and first_dead_tick is not None else (
        f", server's death at {death} never drawn" if death is not None else "")
    print(f"  alive   {down_here} frames drawn dead while alive there{late}")
    for e, tick, ds, ss in sorted(worst_moments, reverse=True)[:min(worst, 3)]:
        if e > 0.25:
            print(f"          worst {e:.2f} m at tick {tick:.1f}, drawn state {ds}, server {ss}{mark_near(marks, tick)}")
    return len(jumps), len(long_runs), down_here, worst_error, True


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("client")
    ap.add_argument("server")
    ap.add_argument("--soldiers", action="store_true", help="only the level's soldiers")
    ap.add_argument("--id", type=int, action="append", help="only these entities")
    ap.add_argument("--worst", type=int, default=8, help="how many of each kind of moment to list")
    ap.add_argument("--all", action="store_true", help="every human, not only those with something wrong or that died")
    args = ap.parse_args()

    ticks, server_marks = read_server(args.server)
    shown, own, client_marks = read_client(args.client)
    sorted_ticks = sorted(ticks)
    if not sorted_ticks or not shown:
        print("One of the traces is empty.")
        return 1

    deaths = {}
    alive_before = {}
    for tick in sorted_ticks:
        for hid, h in ticks[tick].items():
            if alive_before.get(hid) and not h[4] and hid not in deaths:
                deaths[hid] = tick
            alive_before[hid] = h[4]

    marks = sorted(server_marks + client_marks)
    lo = min(f["tick"] for fs in shown.values() for f in fs)
    hi = max(f["tick"] for fs in shown.values() for f in fs)
    print(f"server ticks {sorted_ticks[0]}-{sorted_ticks[-1]}, client drew ticks {lo:.0f}-{hi:.0f}, {len(shown)} humans shown")
    for when, text in marks:
        print(f"  mark {when:.0f}: {text}")

    totals = [0, 0, 0, 0.0]
    quiet = 0
    for hid in sorted(shown):
        if args.soldiers and hid < SOLDIER_BASE:
            continue
        if args.id and hid not in args.id:
            continue
        j, r, d, e, told = analyse(hid, shown[hid], ticks, sorted_ticks, deaths, marks, args.worst, args.all)
        quiet += 0 if told else 1
        totals[0] += j
        totals[1] += r
        totals[2] += d
        totals[3] = max(totals[3], e)

    if own:
        corrections = own[-1][2] - own[0][2]
        print(f"\nown player: {corrections} corrections over {own[-1][0] - own[0][0]:.0f} s, largest error put right {max(o[3] for o in own) / UNITS_PER_METRE:.2f} m")
        prev = own[0][2]
        listed = 0
        for t, tick, c, err in own:
            if c != prev and listed < args.worst:
                print(f"          correction at tick {tick:.1f}: {err / UNITS_PER_METRE:.2f} m{mark_near(marks, tick)}")
                listed += 1
            prev = c

    print(f"\n{quiet} more drawn where the server had them, nothing to tell")
    print(f"all: {totals[0]} jumps, {totals[1]} long state mismatches, {totals[2]} frames dead here alive there, worst error {totals[3]:.2f} m")
    return 0


if __name__ == "__main__":
    sys.exit(main())
