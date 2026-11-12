"""Takes the missions' pictures the menu shows of a map whose level is not behind it, and ships them small.

    python tools/mission-pictures.py [--title igi1] [--missions 3,4] [--keep <folder>]

Each mission is started in the Release build (apps/OpenIGI.Desktop), looked at from one of the places its menu
backdrop circles (Assets/menu-cameras.json), and photographed once the picture has settled. The frame is cut to the
plate's shape, scaled down and written as a baseline JPEG to Assets/missions/<title>/<mission>.jpg -- about 15 KB
each, which the game's own JPEG reader decodes.

One game at a time, in -lockstep with -nosave: a run depends only on its arguments and writes nothing of the
player's. Needs Pillow, and the title's game path in its environment variable.
"""
import argparse
import json
import math
import os
import subprocess
import tempfile

from PIL import Image, ImageStat

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(REPO, "apps", "OpenIGI.Desktop", "bin", "Release", "net10.0", "OpenIGI.Desktop.exe")
ASSETS = os.path.join(REPO, "apps", "OpenIGI.Composition", "Assets")

# The picture's size: the host panel's plate is the taller of the two that show it, and this is its shape.
WIDTH, HEIGHT = 640, 288

# Which of a mission's places to stand at, and how far round it, where the first place at its opening angle shows
# a hillside rather than the level: mission -> (place, angle in radians).
VIEWS = {
    "igi1": {4: (0, math.pi), 5: (0, math.pi), 8: (0, math.pi), 12: (3, None), 13: (0, math.pi)},
}


def pose(camera, place, angle):
    """Where the menu's orbit stands and looks, at a place and an angle round it."""
    point = camera["points"][place % len(camera["points"])]
    radius = point.get("radius", camera.get("radius", 140))
    height = point.get("height", camera.get("height", 55))
    angle = camera.get("startAngle", 0.0) if angle is None else angle
    x = point["x"] + math.cos(angle) * radius
    y = point["y"] + math.sin(angle) * radius
    yaw = math.atan2(-(point["x"] - x), point["y"] - y)
    pitch = -math.atan2(height, max(1.0, radius)) + camera.get("pitchBias", 0.0)
    return (x, y, point["z"] + height), yaw, pitch


def capture(title, camera, shot):
    """Runs the mission and writes one frame of it to shot. Whether it did."""
    place, angle = VIEWS.get(title, {}).get(camera["mission"], (0, None))
    (x, y, z), yaw, pitch = pose(camera, place, angle)
    args = [
        EXE, "-title", title, "-mission", str(camera["mission"]), "-skipcutscene", "-freecam",
        "-campos", f"{x:.2f},{y:.2f},{z:.2f}", "-yaw", f"{yaw:.4f}", "-pitch", f"{pitch:.4f}",
        "-size", "1280x720", "-lockstep", "-nosave", "-nostreamline", "-frames", "150", "-screenshot", shot,
        "+gfx_preset", "ultra",
    ]
    try:
        subprocess.run(args, cwd=os.path.dirname(EXE), capture_output=True, timeout=300, check=False)
    except subprocess.TimeoutExpired:
        return False
    return os.path.exists(shot)


def ship(shot, path):
    """Cuts a frame to the plate's shape about its middle, scales it down and writes it as a small JPEG."""
    frame = Image.open(shot).convert("RGB")
    width, height = frame.size
    cut = int(width * HEIGHT / WIDTH)
    top = (height - cut) // 2
    picture = frame.crop((0, top, width, top + cut)).resize((WIDTH, HEIGHT), Image.LANCZOS)

    # A night level is lifted a little, so its plate is a picture and not a dark rectangle.
    brightness = ImageStat.Stat(picture.convert("L")).mean[0]
    if brightness < 70:
        gamma = 0.72 if brightness < 45 else 0.85
        picture = picture.point(lambda value: int(255 * ((value / 255) ** gamma)))

    # Baseline, not progressive: the game's reader takes the one and not the other.
    picture.save(path, "JPEG", quality=72, optimize=True, progressive=False, subsampling="4:2:0")
    return os.path.getsize(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--title", default="igi1")
    parser.add_argument("--missions", help="comma-separated mission ids; every mission with a menu camera otherwise")
    parser.add_argument("--keep", help="a folder to leave the full frames in")
    options = parser.parse_args()

    wanted = {int(m) for m in options.missions.split(",")} if options.missions else None
    with open(os.path.join(ASSETS, "menu-cameras.json"), encoding="utf-8-sig") as file:
        cameras = [c for c in json.load(file) if c.get("points") and (wanted is None or c["mission"] in wanted)]

    out = os.path.join(ASSETS, "missions", options.title)
    os.makedirs(out, exist_ok=True)
    frames = options.keep or tempfile.mkdtemp(prefix="mission-pictures-")
    os.makedirs(frames, exist_ok=True)
    for camera in cameras:
        shot = os.path.join(frames, f"{camera['mission']}.bmp")
        if os.path.exists(shot):
            os.remove(shot)
        if not capture(options.title, camera, shot):
            print(f"mission {camera['mission']}: no frame")
            continue
        size = ship(shot, os.path.join(out, f"{camera['mission']}.jpg"))
        print(f"mission {camera['mission']}: {size} bytes")
        if not options.keep:
            os.remove(shot)


if __name__ == "__main__":
    main()
