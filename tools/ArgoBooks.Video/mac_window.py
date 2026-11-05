"""The Mac recordings: takes made with ARGO_VIDEO_MAC=1, shown under the title bar macOS draws for the app's window.

On a Mac the app hides its own title bar and window buttons and leaves both to macOS, so a Mac take is 28 points
shorter than the window and the bar is drawn here, above every frame, with the traffic lights and the window's title.
"""
import json
import subprocess

import numpy as np
from PIL import Image, ImageDraw

import make_video as mv

BAR = 56
WIDTH, HEIGHT = mv.SOURCE
TAKE_SIZE = (WIDTH, HEIGHT - BAR)
LIGHTS = [((255, 95, 87), (226, 70, 63)), ((254, 188, 46), (223, 158, 31)), ((40, 200, 64), (27, 170, 44))]


def title_bar(title):
    """The bar at twice the size of a point, as rows of RGB bytes ready to go above a frame."""
    bar = Image.new("RGB", (WIDTH, BAR), (236, 236, 236))
    draw = ImageDraw.Draw(bar)
    draw.line((0, BAR - 1, WIDTH, BAR - 1), fill=(210, 210, 210), width=2)
    for n, (fill, edge) in enumerate(LIGHTS):
        x = 28 + 40 * n
        draw.ellipse((x - 12, BAR / 2 - 12, x + 12, BAR / 2 + 12), fill=fill, outline=edge, width=1)
    draw.text((WIDTH / 2, BAR / 2), title, font=mv.font("SemiBold", 26), fill=(77, 77, 77), anchor="mm")
    return np.asarray(bar).tobytes()


def load(folder, name, title="Cedar Ridge IT - Argo Books", unsaved_after=None):
    """A Mac take, with the cursor moved down by the height of the bar so it stays over what it points at."""
    log = json.loads((folder / f"{name}.json").read_text())
    log["path"] = folder / f"{name}.mkv"
    for frame in log["frames"]:
        frame[1] += BAR
    for key, place in log.get("places", {}).items():
        place[1] += BAR
    log["bars"] = {"clean": title_bar(title)}
    if unsaved_after:
        # The app marks unsaved changes with an asterisk in its window title, which macOS shows in the bar.
        log["bars"]["unsaved"] = title_bar("*" + title)
        log["unsaved_from"] = round(log["marks"][unsaved_after] * mv.FPS)
    return log


def frames(take, start_frame, count):
    """Like make_video.take_frames, for a Mac take: each frame comes back full size, with the title bar above it."""
    total = len(take["frames"])
    available = max(0, min(count, total - start_frame))
    process = subprocess.Popen([mv.FFMPEG, "-v", "error", "-ss", f"{start_frame / mv.FPS:.4f}", "-i", str(take["path"]),
                                "-frames:v", str(available), "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], stdout=subprocess.PIPE)
    size = TAKE_SIZE[0] * TAKE_SIZE[1] * 3
    last = None
    for n in range(count):
        data = process.stdout.read(size) if available > 0 else b""
        if len(data) == size:
            unsaved = "unsaved" in take["bars"] and start_frame + n >= take["unsaved_from"]
            last = take["bars"]["unsaved" if unsaved else "clean"] + data
            available -= 1
        yield last
    process.stdout.close()
    process.wait()
