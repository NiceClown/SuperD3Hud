"""Generates src/app.ico for SuperD3Hud.

The icon mirrors the app itself: a dark rounded tile, a gold frame and the
blue isometric map with the gold player dot, so it is recognisable in a folder
full of other shortcuts.
"""
import io
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "app.ico")

BG = (22, 22, 26, 255)
FRAME = (232, 163, 61, 255)
MAP_FILL = (120, 160, 255, 235)
MAP_EDGE = (185, 205, 255, 255)
PLAYER = (255, 235, 140, 255)


def draw(size):
    scale = 8  # supersampling
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = int(s * 0.045)
    radius = int(s * 0.22)
    d.rounded_rectangle([pad, pad, s - pad, s - pad], radius=radius,
                        fill=BG, outline=FRAME, width=max(1, int(s * 0.035)))

    # Isometric map: three connected tiles, 45 degrees like the game map.
    cx, cy = s * 0.50, s * 0.52
    w, h = s * 0.30, s * 0.175

    def tile(dx, dy, sx, sy):
        x = cx + dx * s
        y = cy + dy * s
        return [
            (x, y - h * sy),
            (x + w * sx, y),
            (x, y + h * sy),
            (x - w * sx, y),
        ]

    for dx, dy, sx, sy in ((0.0, -0.10, 1.0, 1.0),
                           (0.0, 0.10, 1.0, 1.0),
                           (0.19, 0.0, 0.55, 0.55)):
        d.polygon(tile(dx, dy, sx, sy), fill=MAP_FILL, outline=MAP_EDGE,
                  width=max(1, int(s * 0.012)))

    # Player dot at the centre.
    r = s * 0.055
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=PLAYER)

    return img.resize((size, size), Image.LANCZOS)


def main():
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [draw(s) for s in sizes]

    # Vista and newer accept PNG-compressed images inside the ICO container.
    payloads = []
    for im in images:
        buf = io.BytesIO()
        im.save(buf, format="PNG", optimize=True)
        payloads.append((im.width, im.height, buf.getvalue()))

    header = bytearray()
    header += (0).to_bytes(2, "little")          # reserved
    header += (1).to_bytes(2, "little")          # type: icon
    header += len(payloads).to_bytes(2, "little")

    offset = 6 + 16 * len(payloads)
    directory = bytearray()
    for w, h, data in payloads:
        directory += bytes([0 if w >= 256 else w, 0 if h >= 256 else h,
                            0, 0])
        directory += (1).to_bytes(2, "little")   # color planes
        directory += (32).to_bytes(2, "little")  # bits per pixel
        directory += len(data).to_bytes(4, "little")
        directory += offset.to_bytes(4, "little")
        offset += len(data)

    with open(OUT, "wb") as f:
        f.write(header)
        f.write(directory)
        for _, _, data in payloads:
            f.write(data)

    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
