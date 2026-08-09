from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets"
MASTER_SIZE = 1024
ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def render(size: int) -> Image.Image:
    scale = size / 512
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    margin = round(34 * scale)
    border = max(1, round(18 * scale))
    radius = round(88 * scale)
    draw.rounded_rectangle(
        (margin, margin, size - margin, size - margin),
        radius=radius,
        fill=(247, 250, 248, 255),
        outline=(14, 122, 104, 255),
        width=border,
    )

    points: list[tuple[float, float]] = []
    for index in range(121):
        angle = (index / 120) * math.tau
        x = (size / 2) + math.sin(angle) * 122 * scale
        y = (size / 2) + math.sin(angle * 2) * 56 * scale
        points.append((x, y))

    draw.line(
        points,
        fill=(22, 111, 98, 255),
        width=max(2, round(42 * scale)),
        joint="curve",
    )
    return image


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    master = render(MASTER_SIZE)
    preview = master.resize((512, 512), Image.Resampling.LANCZOS)
    preview.save(ASSETS / "CodexMicro.png", optimize=True)

    icon = master.resize((256, 256), Image.Resampling.LANCZOS)
    icon.save(
        ASSETS / "CodexMicro.ico",
        format="ICO",
        sizes=[(size, size) for size in ICON_SIZES],
    )


if __name__ == "__main__":
    main()
