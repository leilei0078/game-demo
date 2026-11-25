import argparse
from pathlib import Path

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont


def build(source, destination):
    root = Path(__file__).resolve().parents[1] / 'UnityGatheringDemo/Assets/Gathering/Runtime'
    text = ''.join(path.read_text() for path in sorted(root.glob('*.cs')))
    required = {ord(char) for char in text if ord(char) >= 32}
    required.update(range(32, 127))
    required.update(ord(char) for char in '。，；：？！“”‘’（）【】《》—…·×°→＋％')
    with TTFont(source) as original:
        points = original.getBestCmap()
        missing = required.difference(points)
        if missing:
            raise ValueError('Missing glyphs: ' + ''.join(map(chr, sorted(missing))))
        mapping = {point: points[point] for point in sorted(required)}
        glyphs = ['.notdef', *sorted(set(mapping.values()) - {'.notdef'})]
        units = original['head'].unitsPerEm
        builder = FontBuilder(units, isTTF=True)
        builder.setupGlyphOrder(glyphs)
        builder.setupCharacterMap(mapping)
        outlines = original.getGlyphSet()
        quadratic = {}
        for name in glyphs:
            pen = TTGlyphPen(None)
            outlines[name].draw(Cu2QuPen(pen, units / 1000, reverse_direction=True))
            quadratic[name] = pen.glyph()
        builder.setupGlyf(quadratic)
        builder.setupHorizontalMetrics({name: original['hmtx'][name] for name in glyphs})
        header = original['hhea']
        builder.setupHorizontalHeader(ascent=header.ascent, descent=header.descent, lineGap=header.lineGap)
        names = original['name']
        builder.setupNameTable({0: names.getDebugName(0), 1: 'Camp Song UI', 2: 'Medium',
                                3: 'CampSongUI-Medium-1.0', 4: 'Camp Song UI Medium',
                                5: 'Version 1.0', 6: 'CampSongUI-Medium',
                                13: names.getDebugName(13), 14: names.getDebugName(14)})
        metrics = original['OS/2']
        builder.setupOS2(**{key: getattr(metrics, key) for key in ('sTypoAscender', 'sTypoDescender',
                         'sTypoLineGap', 'usWinAscent', 'usWinDescent', 'usWeightClass')})
        builder.setupPost()
        builder.save(destination)
        print(f'{len(mapping)} characters, {destination.stat().st_size:,} bytes')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Build the gameplay Song UI subset from Noto Serif CJK SC Medium.')
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    build(args.source, args.destination)
