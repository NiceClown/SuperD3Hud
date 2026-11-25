#!/usr/bin/env python3
"""Repair double-encoded UTF-8 source files.

A PowerShell pass read UTF-8 files as cp1251 and wrote them back as UTF-8,
which produced mojibake like "РћР±Р·РѕСЂ" instead of "Обзор".
This script reverses that transformation safely and writes UTF-8 with BOM.

Windows-1251 leaves byte 0x98 undefined; .NET maps it to U+0098 while Python
raises, so we use a small custom codec that mirrors the .NET behaviour.
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
BOM = b"\xef\xbb\xbf"

MARKERS = ("РЎ", "Рѕ", "Рћ", "Р±", "СЂ", "Р°", "Рµ", "Рё", "РЅ", "С‚", "СЊ", "С‹")


def decode_cp1251_like(data: bytes) -> str:
    """cp1251 decode that keeps undefined bytes as their C1 codepoint."""
    out = []
    for byte in data:
        try:
            out.append(bytes([byte]).decode("cp1251"))
        except UnicodeDecodeError:
            out.append(chr(byte))
    return "".join(out)


def encode_cp1251_like(text: str) -> bytes:
    out = bytearray()
    for ch in text:
        code = ord(ch)
        if code < 0x100:
            out.append(code)
        else:
            out.extend(ch.encode("cp1251"))
    return bytes(out)


def looks_double_encoded(text: str) -> bool:
    return sum(text.count(m) for m in MARKERS) >= 3


def repair(raw: bytes):
    has_bom = raw.startswith(BOM)
    body = raw[len(BOM):] if has_bom else raw
    try:
        text = body.decode("utf-8")
    except UnicodeDecodeError:
        return None, "not utf-8"

    if not looks_double_encoded(text):
        return None, "clean"

    try:
        candidate = encode_cp1251_like(text)
    except UnicodeEncodeError:
        return None, "chars outside cp1251"

    if decode_cp1251_like(candidate) != text:
        return None, "cp1251 round-trip mismatch"

    try:
        fixed = candidate.decode("utf-8")
    except UnicodeDecodeError:
        return None, "reverse bytes are not utf-8"

    if "\ufffd" in fixed:
        return None, "replacement characters"

    return fixed, "repaired"


def main() -> int:
    if not os.path.isdir(SRC):
        print("src folder not found:", SRC)
        return 1

    only = set(a.lower() for a in sys.argv[1:])
    total = 0
    for name in sorted(os.listdir(SRC)):
        if not name.endswith(".cs"):
            continue
        if only and name.lower() not in only:
            continue
        path = os.path.join(SRC, name)
        with open(path, "rb") as fh:
            raw = fh.read()
        fixed, status = repair(raw)
        total += 1
        if fixed is None:
            print(f"{name:22s} {status}")
            continue
        with open(path, "wb") as fh:
            fh.write(BOM + fixed.encode("utf-8"))
        print(f"{name:22s} {status}")

    print(f"\nchecked {total} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
