"""Decode extracted original PTX/PSK atlas regions for local visual inspection only."""
import io
from pathlib import Path
import struct
import sys
from PIL import Image

root = Path(sys.argv[1])
prefix = sys.argv[2] if len(sys.argv) > 2 else "CHUNK_0_PART0002-"
data = (root / (prefix + "PACENOTES_ICONS.PTX")).read_bytes()
assert data[:4] == b"RIFF" and data[60:64] == b"DXT5"
width, height = struct.unpack_from("<II", data, 64)
pixels = []

def chunks(start, end):
    while start + 8 <= end:
        kind, size = struct.unpack_from("<4sI", data, start)
        payload = start + 8
        assert payload + size <= end
        if kind == b"LIST":
            chunks(payload + 4, payload + size)
        elif kind == b"Pixl":
            pixels.append(data[payload:payload + size])
        start = payload + size + (size & 1)

chunks(12, len(data))
pixels = b"".join(pixels)
assert len(pixels) == width * height
header = struct.pack("<7I", 124, 0x81007, height, width, len(pixels), 0, 1)
header += bytes(44) + struct.pack("<II4s5I", 32, 4, b"DXT5", 0, 0, 0, 0, 0)
header += struct.pack("<5I", 0x1000, 0, 0, 0, 0)
atlas = Image.open(io.BytesIO(b"DDS " + header + pixels))
skin = (root / (prefix + "PACENOTES_ICONS.PSK")).read_bytes()
count_offset = int(sys.argv[3]) if len(sys.argv) > 3 else 104
count = struct.unpack_from("<I", skin, count_offset)[0]
offset = count_offset + 4
for _ in range(count):
    length = struct.unpack_from("<I", skin, offset)[0]
    offset += 4
    name = skin[offset:offset+length].decode("utf-8").rstrip("\0")
    offset += length
    uv = struct.unpack_from("<4f", skin, offset)
    offset += 16
    if name.startswith("XL_") and any(key in name for key in ("TurnAround", "Chicane")):
        box = tuple(round(value * (width if i % 2 == 0 else height)) for i, value in enumerate(uv))
        print(name, uv, box)
        atlas.crop(box).save(root / (name + ".png"))
        preview = Image.new("RGBA", (box[2] - box[0], box[3] - box[1]), "#303030")
        preview.alpha_composite(atlas.crop(box).convert("RGBA"))
        preview.save(root / (name + "-preview.png"))
