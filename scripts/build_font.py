#!/usr/bin/env python3
"""Build the game's own font, 模境体 (MoJingTi), from ZCOOL KuaiLe (SIL OFL 1.1, no reserved name).

- Glyphs: ZCOOL KuaiLe (assets/fonts/ZCOOLKuaiLe-Regular.ttf), chunky and playful like the Meshy art.
- Fill-ins: every character the game's code uses, all of GB2312 and the common symbol blocks that
  KuaiLe lacks (arrows, ≥, ▲▼, ◎●, ...) are copied in from the bundled Noto Sans SC (also OFL),
  converted to TrueType curves and set on KuaiLe's baseline, so no text ever falls back to boxes.
- Renamed (the OFL asks modified versions to carry their own name) with a note on where it came from.

Run: uv run --with fonttools python3 scripts/build_font.py
Writes unity/Assets/Resources/Fonts/MoJingTi.ttf (licence: assets/fonts/OFL-ZCOOLKuaiLe.txt).
"""
import pathlib

from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
BASE = ROOT / "assets/fonts/ZCOOLKuaiLe-Regular.ttf"
FILL = ROOT / "assets/fonts/NotoSansSC-Regular.otf"  # outside Resources: only its glyphs ship, inside MoJingTi
OUT = ROOT / "unity/Assets/Resources/Fonts/MoJingTi.ttf"

font = TTFont(BASE)
fill = TTFont(FILL)
have = font.getBestCmap()
fill_cmap = fill.getBestCmap()
fill_glyphs = fill.getGlyphSet()
scale = font["head"].unitsPerEm / fill["head"].unitsPerEm


def wanted():
    chars = {chr(c) for c in range(0x20, 0x7F)}
    for f in (ROOT / "unity/Assets/Scripts").glob("*.cs"):
        chars |= {ch for ch in f.read_text(encoding="utf-8") if ord(ch) > 0x7E}
    # GB2312: rows 16-87 hold the 6763 common hanzi, rows 1-9 the punctuation and symbols.
    for row in list(range(1, 10)) + list(range(16, 88)):
        for col in range(1, 95):
            try:
                chars.add(bytes([0xA0 + row, 0xA0 + col]).decode("gb2312"))
            except UnicodeDecodeError:
                pass
    # Arrows, maths, box/geometric shapes and dingbats that menus and tips like to use.
    for lo, hi in ((0x2190, 0x21FF), (0x2200, 0x22FF), (0x25A0, 0x25FF), (0x2600, 0x26FF), (0x2700, 0x27BF)):
        chars |= {chr(c) for c in range(lo, hi + 1)}
    return {ord(c) for c in chars if len(c) == 1}


glyf, hmtx = font["glyf"], font["hmtx"]
added = 0
for code in sorted(wanted()):
    if code in have or code not in fill_cmap:
        continue
    src = fill_cmap[code]
    name = f"uni{code:04X}"
    if name in glyf.glyphs:
        name += ".fill"
    tt = TTGlyphPen(None)
    fill_glyphs[src].draw(TransformPen(Cu2QuPen(tt, max_err=1.0, reverse_direction=True), (scale, 0, 0, scale, 0, 0)))
    glyf[name] = tt.glyph()
    advance, _ = fill["hmtx"][src]
    glyf[name].recalcBounds(glyf)
    hmtx[name] = (round(advance * scale), getattr(glyf[name], "xMin", 0))
    for table in font["cmap"].tables:
        if table.isUnicode() and (table.format != 4 or code <= 0xFFFF):
            table.cmap[code] = name
    added += 1
# glyf appends each new glyph to the font's glyph order itself.
font["maxp"].numGlyphs = len(font.getGlyphOrder())

names = font["name"]
for rec in list(names.names):
    if rec.nameID in (1, 3, 4, 6, 16, 17, 18):
        names.removeNames(nameID=rec.nameID)
for pid, eid, lid in ((3, 1, 0x409), (3, 1, 0x804)):
    zh = lid == 0x804
    names.setName("模境体" if zh else "MoJingTi", 1, pid, eid, lid)
    names.setName("模境体 Regular" if zh else "MoJingTi Regular", 4, pid, eid, lid)
    names.setName("MoJingTi-Regular;2026", 3, pid, eid, lid)
names.setName("MoJingTi-Regular", 6, 3, 1, 0x409)
names.setName("Modified 2026 for Dangerous Dungeon (危险地牢) from ZCOOL KuaiLe, with symbols from Noto Sans SC. "
              "Licensed under the SIL Open Font License 1.1.", 5, 3, 1, 0x409)
font.save(OUT)
print(f"FONT {OUT.name}: {len(font.getGlyphOrder())} glyphs, {added} filled in from Noto Sans SC")
