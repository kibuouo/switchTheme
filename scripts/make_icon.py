"""Create our own small moon icon without external packages."""
import pathlib
import struct

root = pathlib.Path(__file__).resolve().parents[1]
target = root / "src" / "switchTheme" / "Assets" / "app.ico"
target.parent.mkdir(parents=True, exist_ok=True)
images = []
for size in (16, 32, 48, 256):
    pixels = bytearray()
    for y in reversed(range(size)):
        for x in range(size):
            samples = []
            for sy in (0.25, 0.75):
                for sx in (0.25, 0.75):
                    nx, ny = (x + sx) / size, (y + sy) / size
                    inside = (nx - 0.5)**2 + (ny - 0.5)**2 <= 0.47**2
                    moon = (nx - 0.53)**2 / 0.31**2 + (ny - 0.50)**2 / 0.35**2 <= 1
                    cut = (nx - 0.72)**2 + (ny - 0.32)**2 <= 0.27**2
                    samples.append((244, 244, 244, 255) if inside and moon and not cut else (23, 23, 23, 255) if inside else (0, 0, 0, 0))
            alpha = sum(p[3] for p in samples) // 4
            solid = [p for p in samples if p[3]]
            color = [sum(p[c] for p in solid) // len(solid) if solid else 0 for c in range(3)]
            pixels.extend((*color, alpha))
    mask = bytes(((size + 31) // 32) * 4 * size)
    header = struct.pack("<IIIHHIIIIII", 40, size, size * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0)
    images.append(header + pixels + mask)
offset = 6 + 16 * len(images)
data = bytearray(struct.pack("<HHH", 0, 1, len(images)))
for size, payload in zip((16, 32, 48, 256), images):
    data.extend(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(payload), offset))
    offset += len(payload)
for payload in images:
    data.extend(payload)
target.write_bytes(data)
print(target)
