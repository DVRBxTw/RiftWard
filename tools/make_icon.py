"""Renders tools/icon/riftward.svg into unity/Assets/Icon/icon.png (256 px, the Unity player icon) and
unity/Assets/Resources/icon.ico (16-256 px, the launcher icon).

Uses headless Chrome or Edge for the SVG rendering (transparent background) and packs the .ico itself, so no
Python packages are needed. Small sizes are stored as 32-bit BMP entries (best compatibility with the Windows
resource compiler), 256 px as PNG.
    py -3 tools/make_icon.py
"""
import os
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SVG = ROOT / "tools" / "icon" / "riftward.svg"
OUT = ROOT / "unity" / "Assets" / "Resources"
PNG_OUT = ROOT / "unity" / "Assets" / "Icon"
SIZES = [16, 24, 32, 48, 64, 128, 256]
BROWSERS = [r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"]


def render(size: int, work: Path) -> bytes:
    browser = next((b for b in BROWSERS if os.path.exists(b)), None)
    if browser is None:
        sys.exit("Chrome or Edge is needed to render the SVG")
    page = work / f"icon{size}.html"
    page.write_text(f"""<!doctype html><html><head><style>html,body{{margin:0;background:transparent;overflow:hidden}}
img{{display:block;width:{size}px;height:{size}px}}</style></head><body><img src="{SVG.as_uri()}"></body></html>""", encoding="utf-8")
    shot = work / f"icon{size}.png"
    subprocess.run([browser, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                    "--default-background-color=00000000", f"--window-size={size},{size}", f"--screenshot={shot}",
                    "--allow-file-access-from-files", page.as_uri()], check=True, capture_output=True, timeout=60)
    return shot.read_bytes()


def decode_png(data: bytes):
    """Minimal PNG decoder for the 8-bit RGBA / RGB screenshots Chrome writes. Returns (w, h, rows of RGBA bytes)."""
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, width = 8, b"", 0
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, depth, color = struct.unpack(">IIBB", body[:10])
            assert depth == 8 and color in (2, 6), (depth, color)
            channels = 4 if color == 6 else 3
        elif kind == b"IDAT":
            idat += body
        pos += 12 + length
    raw = zlib.decompress(idat)
    stride = width * channels
    rows, prev = [], bytearray(stride)
    for y in range(height):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - channels] if i >= channels else 0
            b = prev[i]
            c = prev[i - channels] if i >= channels else 0
            if f == 1: line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c; pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        prev = line
        if channels == 3:
            line = bytearray(b for k in range(width) for b in (*line[k * 3:k * 3 + 3], 255))
        rows.append(bytes(line))
    return width, height, rows


def bmp_entry(width, height, rows) -> bytes:
    """32-bit BGRA DIB (bottom-up) with an all-zero AND mask, as an ICO stores it."""
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b"".join(bytes(b for k in range(width) for b in (row[k * 4 + 2], row[k * 4 + 1], row[k * 4], row[k * 4 + 3])) for row in reversed(rows))
    mask = b"\x00" * (((width + 31) // 32) * 4 * height)
    return header + pixels + mask


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    entries = []
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        for size in SIZES:
            png = render(size, work)
            w, h, rows = decode_png(png)
            assert (w, h) == (size, size), (w, h, size)
            if size == 256:
                PNG_OUT.mkdir(parents=True, exist_ok=True)
                (PNG_OUT / "icon.png").write_bytes(png)
                entries.append((size, png))
            else:
                entries.append((size, bmp_entry(w, h, rows)))
    head = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    table, blobs = b"", b""
    for size, blob in entries:
        table += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset + len(blobs))
        blobs += blob
    (OUT / "icon.ico").write_bytes(head + table + blobs)
    print(f"ICON_READY {OUT / 'icon.ico'} sizes={SIZES}")


if __name__ == "__main__":
    main()
