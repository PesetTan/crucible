#!/usr/bin/env python3
"""Build the local mana font from Crucible's SVGs (requires fonttools and brotli)."""
import math
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from fontTools.fontBuilder import FontBuilder
from fontTools.misc.transform import Transform
from fontTools.pens.areaPen import AreaPen
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.pointInsidePen import PointInsidePen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.pens.reverseContourPen import ReverseContourPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.svgLib import SVGPath

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/DeckTester/wwwroot/assets/mana"
SYMBOLS = dict(zip("WUBRGCT", ("white", "blue", "black", "red", "green", "colorless", "tap")))


def svg_transform(value):
    result = Transform()
    for name, args in re.findall(r"(\w+)\(([^)]+)\)", value):
        nums = [float(n) for n in re.split(r"[ ,]+", args.strip())]
        if name == "translate":
            result = result.translate(nums[0], nums[1] if len(nums) > 1 else 0)
        elif name == "scale":
            result = result.scale(nums[0], nums[1] if len(nums) > 1 else nums[0])
        elif name == "rotate":
            x, y = nums[1:] if len(nums) == 3 else (0, 0)
            result = result.translate(x, y).rotate(math.radians(nums[0])).translate(-x, -y)
        else:
            raise ValueError(f"Unsupported SVG transform: {name}")
    return result


def glyph(path):
    recording = RecordingPen()

    def draw(element, transform):
        transform = transform.transform(svg_transform(element.get("transform", "")))
        if element.tag.rsplit("}", 1)[-1] in ("path", "circle"):
            element.attrib.pop("transform", None)
            SVGPath.fromstring(ET.tostring(element), transform=transform).draw(recording)
        else:
            for child in element:
                draw(child, transform)

    draw(ET.parse(path).getroot(), Transform(10, 0, 0, -10, 0, 1000))
    contours = []
    for operation in recording.value:
        if operation[0] == "moveTo":
            contours.append(RecordingPen())
        contours[-1].value.append(("closePath", ()) if operation[0] == "endPath" else operation)

    pen = TTGlyphPen(None)
    quadratic = Cu2QuPen(pen, max_err=1)
    # SVG even-odd holes need alternating winding in a TrueType outline.
    for contour in contours:
        point = contour.value[0][1][0]
        depth = 0
        for other in contours:
            if other is contour:
                continue
            inside = PointInsidePen(None, point, evenOdd=True)
            other.replay(inside)
            depth += inside.getResult()
        area = AreaPen(None)
        contour.replay(area)
        contour.replay(ReverseContourPen(quadratic) if (area.value > 0) != (depth % 2 == 0) else quadratic)
    return pen.glyph()


def main():
    builder = FontBuilder(1000, isTTF=True)
    order = [".notdef", *SYMBOLS.values()]
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap({0xE600 + i: name for i, name in enumerate(SYMBOLS.values())})
    outlines = {".notdef": TTGlyphPen(None).glyph()}
    for name in SYMBOLS.values():
        outlines[name] = glyph(ASSETS / (f"crucible-{name}-small.svg" if name != "tap" else "crucible-tap.svg"))
    builder.setupGlyf(outlines)
    builder.setupHorizontalMetrics({name: (1000, outlines[name].xMin if outlines[name].numberOfContours else 0) for name in order})
    builder.setupHorizontalHeader(ascent=1000, descent=0)
    builder.setupNameTable({"familyName": "Crucible Mana", "styleName": "Regular", "uniqueFontIdentifier": "CrucibleMana-1", "fullName": "Crucible Mana", "psName": "CrucibleMana", "version": "Version 1.000"})
    builder.setupOS2(sTypoAscender=1000, sTypoDescender=0, sTypoLineGap=0, usWinAscent=1000, usWinDescent=0)
    builder.setupPost()
    output = ASSETS / "crucible-mana.woff2"
    builder.font.flavor = "woff2"
    builder.save(output)
    print(f"Built {output} ({output.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
