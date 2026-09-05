"""
Builds src/Resources/OTP_OriGlyph.ttf from the SVG icons in assets/.

The app draws its icons as text, so anything original has to become a font. This keeps that
conversion reproducible instead of leaving a binary in the tree with no way to regenerate it.

    python tools/build_origlyph.py

Metrics deliberately mirror the Material Symbols subset (960 upem, 1056/-96), so a glyph from
either font lands in the same place at the same point size.
"""

import pathlib

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.svgLib.path import SVGPath

ROOT = pathlib.Path(__file__).resolve().parent.parent
UPEM = 960
ASCENT, DESCENT = 1056, -96
ADVANCE, LSB = 960, 120

# Private use area: these are our own drawings, not standard characters.
GLYPHS = [
    ("qrCodeEdit", 0xE900, "assets/qr_code_2_edit.svg"),
]


def build_glyph(svg_path):
    pen = TTGlyphPen(None)
    # SVG y grows downwards and the icons sit in a 0..-960 box, so flip to put them above the
    # baseline. Cubic curves have to become quadratic on the way into a TrueType outline.
    SVGPath(str(svg_path), transform=(1, 0, 0, -1, 0, 0)).draw(Cu2QuPen(pen, max_err=1.0))
    return pen.glyph()


def main():
    order = [".notdef"] + [name for name, _, _ in GLYPHS]
    glyphs = {".notdef": TTGlyphPen(None).glyph()}
    metrics = {".notdef": (ADVANCE, 0)}
    cmap = {}

    for name, codepoint, relative in GLYPHS:
        source = ROOT / relative
        glyphs[name] = build_glyph(source)
        metrics[name] = (ADVANCE, LSB)
        cmap[codepoint] = name
        print(f"{name}: U+{codepoint:04X} from {relative}")

    fb = FontBuilder(UPEM, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=ASCENT, descent=DESCENT)
    fb.setupOS2(sTypoAscender=ASCENT, sTypoDescender=DESCENT, sTypoLineGap=0,
                usWinAscent=ASCENT, usWinDescent=abs(DESCENT))
    fb.setupNameTable({
        "familyName": "OTP_OriGlyph",
        "styleName": "Regular",
        "psName": "OTPOriGlyph-Regular",
        "version": "1.0",
        "copyright": "OTP_OriGlyph Copyright (C) 2026 OTP Manager. Derived from Google Material Symbols, Copyright (C) Google LLC. Licensed under the Apache License 2.0.",
    })
    fb.setupPost()

    out = ROOT / "src" / "Resources" / "OTP_OriGlyph.ttf"
    fb.save(str(out))
    print(f"wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
