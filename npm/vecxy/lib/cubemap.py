"""Project an equirectangular panorama onto six OpenGL cubemap faces."""

import argparse
import math
from pathlib import Path

from PIL import Image


# Image rows start at the top of the PNG, which Vecxy uploads as the first GL row.
DIRECTIONS = {
    "px": lambda s, t: (1, -t, -s),
    "nx": lambda s, t: (-1, -t, s),
    "py": lambda s, t: (s, 1, t),
    "ny": lambda s, t: (s, -1, -t),
    "pz": lambda s, t: (s, -t, 1),
    "nz": lambda s, t: (-s, -t, -1),
}


def sample(pixels, width, height, x, y, z):
    longitude = math.atan2(z, x)
    latitude = math.atan2(y, math.hypot(x, z))
    col = (longitude / math.tau + 0.5) * width - 0.5
    row = max(0.0, min(height - 1.0, (0.5 - latitude / math.pi) * height - 0.5))
    left = math.floor(col)
    top = math.floor(row)
    horizontal, vertical = col - left, row - top
    right = (left + 1) % width
    left %= width
    bottom = min(top + 1, height - 1)
    a, b, c, d = pixels[left, top], pixels[right, top], pixels[left, bottom], pixels[right, bottom]
    return tuple(round((p + (q - p) * horizontal) * (1 - vertical) + (r + (s - r) * horizontal) * vertical)
                 for p, q, r, s in zip(a, b, c, d))


def project(panorama, output, size):
    width, height = panorama.size
    pixels = panorama.load()
    coordinates = [2 * (i + 0.5) / size - 1 for i in range(size)]
    output.mkdir(parents=True, exist_ok=True)
    for name, direction in DIRECTIONS.items():
        data = bytearray()
        for t in coordinates:
            for s in coordinates:
                data.extend(sample(pixels, width, height, *direction(s, t)))
        face = Image.frombytes("RGB", (size, size), bytes(data))
        face.save(output / f"{name}.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("size", type=int)
    args = parser.parse_args()
    if not 16 <= args.size <= 2048:
        parser.error("face size must be between 16 and 2048 pixels")
    with Image.open(args.source) as source:
        if source.width != 2 * source.height:
            parser.error("the source must be a 2:1 equirectangular panorama")
        project(source.convert("RGB"), args.output, args.size)
    print(f"Created six {args.size}×{args.size} cubemap faces in {args.output}")


if __name__ == "__main__":
    main()
