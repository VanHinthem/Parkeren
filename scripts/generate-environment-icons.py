"""Generate blue ACC and orange DEV variants of the existing green PWA icons.

Requires Pillow: python -m pip install Pillow
"""
from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image

PUBLIC = Path(__file__).resolve().parents[1] / "src" / "Parkeren.Web" / "public"
TARGET_HUES = {"acc": 210 / 360, "dev": 28 / 360}


def recolor(source: Path, destination: Path, hue: float) -> int:
    with Image.open(source) as input_image:
        image = input_image.convert("RGBA")
    pixels = image.load()
    modified = 0

    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            original_hue, saturation, value = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            # Keep white details, shadows, and neutral grays entirely unchanged.
            if not (0.18 <= original_hue <= 0.49 and saturation >= 0.15 and g > r * 1.08 and g > b * 1.05):
                continue
            nr, ng, nb = colorsys.hsv_to_rgb(hue, saturation, value)
            pixels[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
            modified += 1

    if modified < image.width * image.height * 0.10:
        raise RuntimeError(f"Too few green pixels in {source}: {modified}")
    image.save(destination, "PNG", optimize=True)
    return modified


if __name__ == "__main__":
    for size in (192, 512):
        source = PUBLIC / f"pwa-{size}x{size}.png"
        for environment, hue in TARGET_HUES.items():
            target = PUBLIC / f"pwa-{size}x{size}-{environment}.png"
            count = recolor(source, target, hue)
            print(f"{target.name}: changed {count} pixels")
