"""Render the two Micro Surface pages from the current WPF palette and geometry."""

from __future__ import annotations

from io import BytesIO
from pathlib import Path
import re

import cairosvg
from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "ux"
ASSETS = ROOT / "virtual-micro" / "src" / "DeepSeekKeypad.Gpui" / "assets"
S = 3
W, H = 590, 610
SIZE = (W * S, H * S)

WHITE = (255, 255, 255)
BLUE = (0x30, 0x4F, 0xFE)
GREEN = (0x00, 0xFF, 0x4C)
AMBER = (0xFF, 0x6D, 0x00)
RED = (0xFF, 0x00, 0x33)


def q(value: float) -> int:
    return round(value * S)


def rect(box: tuple[float, float, float, float]) -> tuple[int, int, int, int]:
    return tuple(q(v) for v in box)


def rgba(color: tuple[int, int, int], alpha: float = 1) -> tuple[int, int, int, int]:
    return (*color, round(max(0, min(1, alpha)) * 255))


def mix(a: int, b: int, t: float) -> int:
    return round(a + (b - a) * t)


def over(base: Image.Image, painter) -> None:
    layer = Image.new("RGBA", SIZE)
    painter(ImageDraw.Draw(layer))
    base.alpha_composite(layer)


def round_box(
    base: Image.Image,
    box: tuple[float, float, float, float],
    radius: float,
    fill: tuple[int, int, int, int] | None = None,
    outline: tuple[int, int, int, int] | None = None,
    width: float = 1,
) -> None:
    def paint(draw: ImageDraw.ImageDraw) -> None:
        draw.rounded_rectangle(rect(box), radius=q(radius), fill=fill, outline=outline, width=q(width))

    over(base, paint)


def ellipse(
    base: Image.Image,
    box: tuple[float, float, float, float],
    fill: tuple[int, int, int, int] | None = None,
    outline: tuple[int, int, int, int] | None = None,
    width: float = 1,
) -> None:
    over(base, lambda d: d.ellipse(rect(box), fill=fill, outline=outline, width=q(width)))


def line(
    base: Image.Image,
    points: list[tuple[float, float]],
    fill: tuple[int, int, int, int],
    width: float = 1,
    joint: str = "curve",
) -> None:
    over(base, lambda d: d.line([(q(x), q(y)) for x, y in points], fill=fill, width=q(width), joint=joint))


def gradient_box(
    base: Image.Image,
    box: tuple[float, float, float, float],
    radius: float,
    stops: list[tuple[float, tuple[int, int, int]]],
) -> None:
    x0, y0, x1, y1 = rect(box)
    width, height = x1 - x0, y1 - y0
    layer = Image.new("RGBA", (width, height))
    draw = ImageDraw.Draw(layer)
    for y in range(height):
        t = y / max(1, height - 1)
        for index in range(len(stops) - 1):
            if stops[index][0] <= t <= stops[index + 1][0]:
                s0, c0 = stops[index]
                s1, c1 = stops[index + 1]
                u = (t - s0) / (s1 - s0)
                color = tuple(mix(c0[k], c1[k], u) for k in range(3))
                break
        else:
            color = stops[-1][1]
        draw.line((0, y, width, y), fill=(*color, 255))
    mask = Image.new("L", (width, height))
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, width - 1, height - 1), radius=q(radius), fill=255)
    layer.putalpha(mask)
    base.alpha_composite(layer, (x0, y0))


def blur_shape(
    base: Image.Image,
    box: tuple[float, float, float, float],
    color: tuple[int, int, int],
    alpha: float,
    blur: float,
    radius: float | None = None,
) -> None:
    mask = Image.new("L", SIZE)
    d = ImageDraw.Draw(mask)
    if radius is None:
        d.ellipse(rect(box), fill=255)
    else:
        d.rounded_rectangle(rect(box), radius=q(radius), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(q(blur)))
    mask = mask.point(lambda v: round(v * alpha))
    overlay = Image.new("RGBA", SIZE, (*color, 0))
    overlay.putalpha(mask)
    base.alpha_composite(overlay)


def icon(base: Image.Image, name: str, cx: float, cy: float, size: float = 28) -> None:
    if name == "codex":
        source = (ROOT / "virtual-micro" / "src" / "CodexMicro.Desktop" / "Controls" / "KeycapIcon.cs").read_text(encoding="utf-8")
        block = re.search(r"CodexGeometry = CreateGeometry\((.*?)\);\s*// Exact fish", source, re.S)
        assert block is not None
        paths = re.findall(r'"([^"]+)"', block.group(1))
        markup = "".join(f'<path fill="#171717" d="{path}" />' for path in paths)
        svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20">{markup}</svg>'
    else:
        svg = (ASSETS / f"{name}.svg").read_text(encoding="utf-8").replace("currentColor", "#171717")
    pixels = q(size)
    image = Image.open(BytesIO(cairosvg.svg2png(bytestring=svg.encode(), output_width=pixels, output_height=pixels))).convert("RGBA")
    base.alpha_composite(image, (q(cx - size / 2), q(cy - size / 2)))


def draw_body(base: Image.Image, active_page: int) -> None:
    # Window transparency, crystal edge, pearl guide, and inset panel follow MainWindow.xaml.
    blur_shape(base, (20, 22, 572, 590), (67, 90, 95), 0.24, 14, 69)
    blur_shape(base, (28, 25, 564, 584), (74, 166, 151), 0.15, 22, 66)
    gradient_box(base, (23, 22, 567, 587), 66, [(0, (255, 255, 255)), (0.50, (247, 250, 249)), (0.82, (229, 238, 237)), (1, (194, 207, 208))])
    round_box(base, (24, 23, 566, 586), 65, outline=rgba(WHITE, 0.94), width=2)
    gradient_box(base, (31, 30, 559, 578), 59, [(0, (251, 254, 253)), (0.63, (243, 249, 247)), (1, (220, 231, 230))])
    round_box(base, (45, 45, 545, 565), 51, outline=rgba((145, 163, 165), 0.5), width=2)
    round_box(base, (51, 51, 539, 559), 46, outline=rgba(WHITE, 0.87), width=2)
    gradient_box(base, (54, 53, 536, 556), 44, [(0, (249, 253, 254)), (0.7, (249, 253, 253)), (1, (243, 249, 249))])
    round_box(base, (55, 54, 535, 555), 43, outline=rgba(WHITE, 0.77), width=1)
    blur_shape(base, (105, 545, 485, 552), (109, 215, 191), 0.13, 6, 4)

    # The XAML page selector uses a 20 x 7 capsule and a 7 x 7 dot.
    centers = (277, 313)
    for page, cx in enumerate(centers):
        if page == active_page:
            blur_shape(base, (cx - 12, 61, cx + 12, 70), (113, 133, 234), 0.22, 5, 5)
            round_box(base, (cx - 10, 62, cx + 10, 69), 4, rgba((113, 133, 234), 1))
        else:
            ellipse(base, (cx - 3.5, 62, cx + 3.5, 69), rgba((113, 133, 146), 0.34))

    # Fine etched rules and the small bottom mark keep the hardware feel without labels.
    line(base, [(69, 269), (69, 338)], rgba((108, 132, 140), 0.24), 1)
    line(base, [(521, 269), (521, 338)], rgba((108, 132, 140), 0.24), 1)
    ellipse(base, (290.5, 546.5, 299.5, 555.5), outline=rgba((110, 132, 138), 0.45), width=1)
    line(base, [(293, 549), (295, 547.5), (297, 549), (297, 552), (295, 553.5), (293, 552), (293, 549)], rgba((110, 132, 138), 0.40), 0.7)


def agent_glow(base: Image.Image, cx: float, cy: float, state: str, selected: bool = False) -> None:
    if state == "idle_off":
        return
    color = {"white": WHITE, "blue": BLUE, "green": GREEN, "amber": AMBER, "red": RED}[state]
    wide = 0.90 if selected and state == "white" else 0.82 if selected else 0.42
    near = 0.50 if selected and state == "white" else 0.48 if selected else 0.22
    if state == "white":
        blur_shape(base, (cx - 53, cy - 53, cx + 53, cy + 53), (115, 138, 147), 0.14, 18, 21)
    blur_shape(base, (cx - 53, cy - 53, cx + 53, cy + 53), color, wide * (0.34 if state == "white" else 0.24), 24, 21)
    blur_shape(base, (cx - 50, cy - 50, cx + 50, cy + 50), color, near * (0.48 if state == "white" else 0.35), 14, 18)


def agent_cap(base: Image.Image, cx: float, cy: float, state: str, selected: bool = False, faded: bool = False) -> None:
    color = {"white": WHITE, "blue": BLUE, "green": GREEN, "amber": AMBER, "red": RED, "idle_off": (141, 181, 255)}[state]
    active = state != "idle_off"
    blur_shape(base, (cx - 46, cy - 43, cx + 46, cy + 49), (61, 70, 68), 0.16, 4, 15)
    round_box(base, (cx - 46, cy - 47, cx + 46, cy + 47), 14, rgba((244, 247, 245), 1), outline=rgba(WHITE, 0.86), width=1)
    round_box(base, (cx - 44.5, cy - 45.5, cx + 44.5, cy + 45.5), 13, outline=rgba((103, 114, 108), 0.12), width=1)
    if active:
        cap_opacity = 0.16 if state == "white" else 0.28 if selected else 0.12
        round_box(base, (cx - 42, cy - 43, cx + 42, cy + 43), 10, rgba(color, cap_opacity))
        blur_shape(base, (cx - 41, cy - 41, cx + 41, cy + 41), color, 0.22 if state == "white" else 0.32, 5)
    ellipse(base, (cx - 38, cy - 38, cx + 38, cy + 38), rgba((247, 248, 246), 1))
    if active:
        well_alpha = 0.38 if state == "white" else 0.48 if selected else 0.40
        ellipse(base, (cx - 38, cy - 38, cx + 38, cy + 38), rgba(color, well_alpha))
    else:
        ellipse(base, (cx - 38, cy - 38, cx + 38, cy + 38), rgba((233, 238, 237), 0.22))
    ellipse(base, (cx - 38, cy - 38, cx + 38, cy + 38), outline=rgba((116, 123, 119), 0.16), width=1.2)
    ellipse(base, (cx - 37, cy - 37, cx + 37, cy + 37), outline=rgba(WHITE, 0.87), width=1)
    if selected and state == "white":
        ellipse(base, (cx - 39, cy - 39, cx + 39, cy + 39), rgba(WHITE, 0.78))
        ellipse(base, (cx - 42, cy - 42, cx + 42, cy + 42), outline=rgba((151, 172, 179), 0.35), width=2.2)
        ellipse(base, (cx - 41, cy - 41, cx + 41, cy + 41), outline=rgba(WHITE, 0.97), width=1.5)
    if active:
        blur_shape(base, (cx - 10, cy - 10, cx + 10, cy + 10), color, 0.34, 4)
        ellipse(base, (cx - 9, cy - 9, cx + 9, cy + 9), rgba(color, 0.89 if state != "white" else 0.94), outline=rgba((86, 95, 94), 0.30), width=0.7)
    else:
        ellipse(base, (cx - 9, cy - 9, cx + 9, cy + 9), rgba((164, 174, 174), 0.43), outline=rgba((116, 127, 127), 0.24), width=0.7)
    if faded:
        round_box(base, (cx - 46, cy - 47, cx + 46, cy + 47), 14, rgba((250, 252, 251), 0.36))


def command_key(base: Image.Image, cx: float, cy: float, width: float = 96, symbol: str | None = None) -> None:
    x0, x1 = cx - width / 2, cx + width / 2
    blur_shape(base, (x0 + 1, cy - 43, x1 - 1, cy + 49), (60, 65, 61), 0.17, 4, 14)
    round_box(base, (x0 + 1, cy - 47, x1 - 1, cy + 47), 14, rgba((252, 251, 249), 1), outline=rgba(WHITE, 0.91), width=1.4)
    round_box(base, (x0 + 2, cy - 46, x1 - 2, cy + 45), 13, outline=rgba((83, 88, 82), 0.14), width=1)
    inner_half = (width - 20) / 2
    round_box(base, (cx - inner_half, cy - 38, cx + inner_half, cy + 38), 11, rgba((255, 255, 253), 0.45), outline=rgba((109, 119, 115), 0.10), width=1)
    if symbol:
        icon(base, symbol, cx, cy, 30 if symbol != "codex" else 32)


def dial(base: Image.Image) -> None:
    cx, cy = 136, 146
    blur_shape(base, (94, 104, 178, 188), (41, 50, 52), 0.22, 5)
    gradient_box(base, (95, 104, 177, 186), 41, [(0, (255, 255, 255)), (0.55, (246, 250, 251)), (1, (217, 227, 232))])
    ellipse(base, (95, 104, 177, 186), outline=rgba((122, 140, 147), 0.18), width=1)
    line(base, [(139, 137), (154, 120)], rgba((87, 100, 108), 0.94), 5.8)


def joystick(base: Image.Image) -> None:
    cx, cy = 454, 146
    command_key(base, cx, cy, 88)
    blur_shape(base, (423, 116, 485, 178), (29, 32, 31), 0.40, 3)
    ellipse(base, (425, 117, 483, 175), rgba((33, 35, 33), 1), outline=rgba((3, 4, 4), 0.64), width=1)
    blur_shape(base, (432, 122, 463, 151), (146, 150, 146), 0.10, 9)
    stroke = rgba((111, 116, 114), 0.74)
    line(base, [(450, 111), (454, 107), (458, 111)], stroke, 1.1)
    line(base, [(450, 181), (454, 185), (458, 181)], stroke, 1.1)
    line(base, [(417, 142), (413, 146), (417, 150)], stroke, 1.1)
    line(base, [(491, 142), (495, 146), (491, 150)], stroke, 1.1)


def model_knob(base: Image.Image) -> None:
    for index, color in enumerate([(96, 131, 245), (119, 194, 118), (169, 175, 126)]):
        cy = 451 + 11 * index
        blur_shape(base, (98, cy - 4, 106, cy + 4), color, 0.34, 3)
        ellipse(base, (99, cy - 3, 105, cy + 3), rgba(color, 0.91))
    blur_shape(base, (121, 434, 181, 494), (26, 27, 26), 0.34, 3)
    ellipse(base, (123, 436, 179, 492), rgba((35, 36, 34), 1), outline=rgba((93, 92, 87), 0.65), width=1)
    line(base, [(146, 463), (156, 463)], rgba((194, 195, 186), 0.72), 1.1)
    line(base, [(146, 467), (156, 467)], rgba((194, 195, 186), 0.50), 1.1)


def render_first() -> Image.Image:
    base = Image.new("RGBA", SIZE, (247, 249, 248, 255))
    draw_body(base, 0)
    slots = [
        (242, 146, "white", True),
        (348, 146, "blue", False),
        (136, 252, "green", False),
        (242, 252, "amber", False),
        (348, 252, "red", False),
        (454, 252, "idle_off", False),
    ]
    for cx, cy, state, selected in slots:
        agent_glow(base, cx, cy, state, selected)
    dial(base)
    joystick(base)
    for cx, cy, state, selected in slots:
        agent_cap(base, cx, cy, state, selected)
    for cx, symbol in [(136, "fast"), (242, "approve"), (348, "reject"), (454, "fork")]:
        command_key(base, cx, 358, symbol=symbol)
    model_knob(base)
    command_key(base, 295, 464, 202, "microphone")
    command_key(base, 454, 464, 96, "codex")
    return base


def render_second() -> Image.Image:
    base = Image.new("RGBA", SIZE, (247, 249, 248, 255))
    draw_body(base, 1)
    states = [
        ("white", True), ("blue", False), ("green", False), ("amber", False),
        ("red", False), ("idle_off", False), ("blue", False), ("green", False),
        ("amber", False), ("red", False), ("blue", False), ("idle_off", False),
        ("green", False), ("amber", False),
    ]
    slots = []
    for index, (state, selected) in enumerate(states):
        cell = index if index < 12 else index + 1
        row, col = divmod(cell, 4)
        slots.append((136 + 106 * col, 146 + 106 * row, state, selected))
    for cx, cy, state, selected in slots:
        agent_glow(base, cx, cy, state, selected)
    for cx, cy, state, selected in slots:
        agent_cap(base, cx, cy, state, selected)
    model_knob(base)
    command_key(base, 454, 464, 96, "codex")
    return base


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for filename, render in [
        ("codex-micro-first-screen-lighting.png", render_first),
        ("codex-micro-second-screen-lighting.png", render_second),
    ]:
        path = OUT / filename
        render().convert("RGB").save(path, optimize=True)
        print(path)


if __name__ == "__main__":
    main()
