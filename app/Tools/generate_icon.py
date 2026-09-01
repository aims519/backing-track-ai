#!/usr/bin/env python3
"""
Draws the BackingTrack app icon and launch-screen logo from code, so the assets are
reproducible and reviewable in a diff rather than opaque binary blobs. See
app/BackingTrack/Resources/README.md for how the output is wired into the asset catalog.

Concept: a cog with a beamed pair of eighth notes in its hub — "generated music". Kept to one
bold silhouette so it survives being rendered at ~29pt in iOS Settings.

Usage:
    python3 app/Tools/generate_icon.py                 # regenerate committed assets
    python3 app/Tools/generate_icon.py --preview DIR   # also drop accent-colour comparison 1024s

Requires Pillow (`pip install pillow`).
"""

from __future__ import annotations

import argparse
import math
import pathlib

from PIL import Image, ImageDraw

# Supersample factor — draw big, downscale once with a good filter for cheap antialiasing.
SS = 4

REPO = pathlib.Path(__file__).resolve().parents[2]
ASSETS = REPO / "app" / "BackingTrack" / "Resources" / "Assets.xcassets"

# Accent palettes: (gradient top, gradient bottom). The first is the shipped default.
ACCENTS = {
    "amber": ((0xF2, 0xA5, 0x4A), (0xD9, 0x6C, 0x2A)),
    "teal": ((0x2A, 0xA6, 0x9C), (0x14, 0x6E, 0x67)),
    "indigo": ((0x6E, 0x63, 0xD6), (0x45, 0x3C, 0xA8)),
}
DEFAULT_ACCENT = "amber"


def _lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def _vertical_gradient(size: int, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    grad = Image.new("RGB", (1, size))
    for y in range(size):
        grad.putpixel((0, y), _lerp(top, bottom, y / (size - 1)))
    return grad.resize((size, size))


def _gear_polygon(cx: float, cy: float, r_outer: float, r_inner: float, teeth: int) -> list[tuple[float, float]]:
    """Vertices for a gear rim. Each tooth spans one `step`: a valley corner at r_inner, the two
    corners of a flat tooth top at r_outer (narrower than the valley so the tooth has sloped
    sides), then the next valley corner — which is shared with the following tooth."""
    pts: list[tuple[float, float]] = []
    step = 2 * math.pi / teeth
    valley_half = step * 0.5   # valley corners sit midway between tooth centres
    tip_half = step * 0.26     # tooth top is narrower than its base
    for i in range(teeth):
        a = i * step
        for ang, radius in (
            (a - valley_half, r_inner),
            (a - tip_half, r_outer),
            (a + tip_half, r_outer),
            (a + valley_half, r_inner),
        ):
            pts.append((cx + radius * math.cos(ang), cy + radius * math.sin(ang)))
    return pts


# The glyph is drawn to fill `size`, then scaled to this fraction of the tile so the gear
# teeth don't touch the icon's rounded-rect edge.
GLYPH_SCALE = 0.92


def _tilted_ellipse(cx: float, cy: float, rx: float, ry: float, angle_deg: float, steps: int = 64) -> list[tuple[float, float]]:
    """Outline points of an ellipse rotated `angle_deg` about its centre (PIL only draws
    axis-aligned ellipses, so a tilted notehead has to be a polygon)."""
    a = math.radians(angle_deg)
    ca, sa = math.cos(a), math.sin(a)
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        ex, ey = rx * math.cos(t), ry * math.sin(t)
        pts.append((cx + ex * ca - ey * sa, cy + ex * sa + ey * ca))
    return pts


def _note_layer(size: int, fg: tuple[int, int, int, int]) -> Image.Image:
    """Two eighth notes joined by a beam — the ♫ shape, drawn like engraved notation: level
    noteheads, vertical stems rising from the *right* edge of each head, one horizontal beam."""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # Group centred in the hub, nudged left so the stems (which sit to the right of the heads)
    # don't push the visual weight off-centre.
    gx = size / 2 - size * 0.028
    gy = size / 2

    tilt = -25                                   # notehead tilts up towards the stem
    head_rx, head_ry = size * 0.066, size * 0.045
    stem_w = size * 0.021
    stem_h = size * 0.250
    beam_t = size * 0.048

    # Level noteheads. Their centres sit low; the stems and beam rise from there.
    head_y = gy + size * 0.098
    left_cx = gx - size * 0.092
    right_cx = gx + size * 0.092

    def stem_x(head_cx: float) -> tuple[float, float]:
        """The stem's right edge runs tangent to the notehead's right side, so the stem grows
        straight out of the head's outline instead of sitting beside it."""
        x1 = head_cx + head_rx * 0.90
        return x1 - stem_w, x1

    lx0, lx1 = stem_x(left_cx)
    rx0, rx1 = stem_x(right_cx)
    stem_bottom = head_y + head_ry * 0.10        # run well into the head so the fills merge
    beam_top = head_y - stem_h

    # Stems and beam first, then the noteheads on top — each ellipse then absorbs the bottom of
    # its stem, so the join reads as one shape rather than a bar bolted to a disc.
    d.rectangle([lx0, beam_top, lx1, stem_bottom], fill=fg)
    d.rectangle([rx0, beam_top, rx1, stem_bottom], fill=fg)
    d.rectangle([lx0, beam_top, rx1, beam_top + beam_t], fill=fg)
    for cx in (left_cx, right_cx):
        d.polygon(_tilted_ellipse(cx, head_y, head_rx, head_ry, tilt), fill=fg)

    return layer


def _glyph_layer(size: int, fg: tuple[int, int, int, int], hub_fill: tuple[int, int, int, int]) -> Image.Image:
    """The cog-plus-notes glyph on its own transparent RGBA layer, scaled to `GLYPH_SCALE` and
    centred so it doesn't crowd the tile edge.

    `fg` paints the gear and the notes; `hub_fill` paints the round hub the notes sit in (pass a
    transparent colour to punch it out so a background shows through)."""
    full = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(full)
    c = size / 2
    r_outer = size * 0.47
    r_inner = size * 0.395
    r_body = size * 0.335         # solid disc joining the teeth
    r_hub = size * 0.255          # inner circle the notes sit in

    draw.polygon(_gear_polygon(c, c, r_outer, r_inner, teeth=8), fill=fg)
    draw.ellipse([c - r_body, c - r_body, c + r_body, c + r_body], fill=fg)
    draw.ellipse([c - r_hub, c - r_hub, c + r_hub, c + r_hub], fill=hub_fill)
    full.alpha_composite(_note_layer(size, fg))

    inner_px = round(size * GLYPH_SCALE)
    scaled = full.resize((inner_px, inner_px), Image.LANCZOS)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(scaled, ((size - inner_px) // 2, (size - inner_px) // 2))
    return out


WHITE = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)


def render_icon(accent: str) -> Image.Image:
    """1024x1024 opaque App Store icon: white glyph on the accent gradient, the notes showing
    as negative space where the gradient reads through the gear's hub."""
    top, bottom = ACCENTS[accent]
    s = 1024 * SS
    gradient = _vertical_gradient(s, top, bottom).convert("RGBA")

    # glyph alpha = gear body minus hub, plus the notes drawn back in — exactly the pixels we
    # want painted white; everything else (background and hub ring around the notes) stays gradient.
    glyph = _glyph_layer(s, fg=WHITE, hub_fill=CLEAR)
    img = Image.composite(Image.new("RGBA", (s, s), WHITE), gradient, glyph.getchannel("A"))
    return img.resize((1024, 1024), Image.LANCZOS).convert("RGB")


def render_logo(px: int) -> Image.Image:
    """Transparent white glyph for the launch screen."""
    s = px * SS
    return _glyph_layer(s, fg=WHITE, hub_fill=CLEAR).resize((px, px), Image.LANCZOS)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--accent", default=DEFAULT_ACCENT, choices=sorted(ACCENTS))
    ap.add_argument("--preview", type=pathlib.Path, help="dir to also write accent comparison 1024s")
    args = ap.parse_args()

    appicon = ASSETS / "AppIcon.appiconset" / "AppIcon-1024.png"
    render_icon(args.accent).save(appicon)
    print(f"wrote {appicon.relative_to(REPO)}")

    logo_set = ASSETS / "LaunchLogo.imageset"
    for scale, name in ((1, "LaunchLogo.png"), (2, "LaunchLogo@2x.png"), (3, "LaunchLogo@3x.png")):
        render_logo(240 * scale).save(logo_set / name)
        print(f"wrote {(logo_set / name).relative_to(REPO)}")

    if args.preview:
        args.preview.mkdir(parents=True, exist_ok=True)
        for name in ACCENTS:
            out = args.preview / f"icon-{name}.png"
            render_icon(name).save(out)
            print(f"wrote {out}")


if __name__ == "__main__":
    main()
