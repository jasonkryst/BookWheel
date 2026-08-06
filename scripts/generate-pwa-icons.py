#!/usr/bin/env python3
"""Generate BookWheel PWA icon PNGs using only the Python standard library.

Rasterizes a simplified version of the app's own spin wheel (a dark
background square with a circle divided into the six --wheel-slice-*
colors from site.css, plus a light hub at the center) at each size the
PWA manifest and index.html reference. Re-run this script and commit
the output whenever the wheel-slice palette in site.css changes.
"""
import math
import os
import struct
import zlib

BG_COLOR = (0x0F, 0x17, 0x2A)
HUB_COLOR = (0xF1, 0xF5, 0xF9)
SLICE_COLORS = [
    (0x38, 0xBD, 0xF8),
    (0x60, 0xA5, 0xFA),
    (0x81, 0x8C, 0xF8),
    (0xF4, 0x72, 0xB6),
    (0x34, 0xD3, 0x99),
    (0xFB, 0xBF, 0x24),
]

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ICONS_DIR = os.path.join(SCRIPT_DIR, "..", "BookWheel", "wwwroot", "icons")


def _png_chunk(chunk_type, data):
    return (
        struct.pack(">I", len(data))
        + chunk_type
        + data
        + struct.pack(">I", zlib.crc32(chunk_type + data) & 0xFFFFFFFF)
    )


def write_png(path, size, pixels):
    raw = bytearray()
    for y in range(size):
        raw.append(0)  # filter type: none
        row_start = y * size * 4
        raw.extend(pixels[row_start:row_start + size * 4])

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", ihdr)
        + _png_chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + _png_chunk(b"IEND", b"")
    )

    with open(path, "wb") as handle:
        handle.write(png)


def render_wheel_icon(size, wheel_radius_fraction):
    pixels = bytearray(size * size * 4)
    center = size / 2.0
    wheel_radius = size * wheel_radius_fraction
    hub_radius = size * 0.08

    for y in range(size):
        dy = (y + 0.5) - center
        for x in range(size):
            dx = (x + 0.5) - center
            dist = math.hypot(dx, dy)

            if dist <= wheel_radius:
                if dist <= hub_radius:
                    color = HUB_COLOR
                else:
                    angle = math.atan2(dy, dx)
                    normalized = (angle + math.pi) / (2 * math.pi)
                    index = int(normalized * len(SLICE_COLORS)) % len(SLICE_COLORS)
                    color = SLICE_COLORS[index]
            else:
                color = BG_COLOR

            offset = (y * size + x) * 4
            pixels[offset] = color[0]
            pixels[offset + 1] = color[1]
            pixels[offset + 2] = color[2]
            pixels[offset + 3] = 255

    return pixels


# (filename, size, wheel radius as a fraction of size — smaller for the
# maskable icon so the wheel stays inside Android's adaptive-icon safe zone)
ICONS = [
    ("icon-192.png", 192, 0.47),
    ("icon-512.png", 512, 0.47),
    ("icon-512-maskable.png", 512, 0.38),
    ("icon-180.png", 180, 0.47),
    ("favicon-32.png", 32, 0.47),
]


def main():
    os.makedirs(ICONS_DIR, exist_ok=True)
    for filename, size, radius_fraction in ICONS:
        pixels = render_wheel_icon(size, radius_fraction)
        write_png(os.path.join(ICONS_DIR, filename), size, pixels)
        print(f"wrote {filename} ({size}x{size})")


if __name__ == "__main__":
    main()
