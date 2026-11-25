#!/usr/bin/env python3
"""Show which typographic characters survived in the sources."""
import glob
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHARS = "—•→…«»"

for path in sorted(glob.glob(os.path.join(ROOT, "src", "*.cs"))):
    raw = open(path, "rb").read()
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    text = raw.decode("utf-8")
    counts = ", ".join(f"{c}x{text.count(c)}" for c in CHARS if text.count(c))
    if counts:
        print(f"{os.path.basename(path):<20} {counts}")
