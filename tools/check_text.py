#!/usr/bin/env python3
"""Sanity check: report Cyrillic volume and leftover mojibake markers per file."""
import glob
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MARKERS = ("РЎ", "Рѕ", "Рћ", "Р±", "СЂ", "Р°", "Рµ", "Рё", "РЅ", "С‚", "СЊ", "С‹")

rows = []
for path in sorted(glob.glob(os.path.join(ROOT, "src", "*.cs"))):
    raw = open(path, "rb").read()
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    text = raw.decode("utf-8")
    cyr = sum(1 for ch in text if "\u0400" <= ch <= "\u04FF")
    markers = sum(text.count(m) for m in MARKERS)
    rows.append((os.path.basename(path), cyr, markers))

width = max(len(r[0]) for r in rows)
for name, cyr, markers in rows:
    flag = "  <-- CHECK" if markers else ""
    print(f"{name:<{width}}  cyrillic={cyr:4d}  mojibake={markers}{flag}")

total_markers = sum(r[2] for r in rows)
print("\ntotal mojibake markers:", total_markers)
