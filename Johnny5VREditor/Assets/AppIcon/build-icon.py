"""Package the approved C artwork (large white J5 and yellow play triangle)
into the shared Windows application icon. Prior sources remain available.
"""
from io import BytesIO
from pathlib import Path
import struct

from PIL import Image

SMALL_SOURCE = "animation-editor-c-source.png"   # used for 16-32 px
LARGE_SOURCE = "animation-editor-c-source.png"   # used for 40-256 px

root = Path(__file__).resolve().parent
sizes = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
small = Image.open(root / SMALL_SOURCE).convert("RGBA")
large = Image.open(root / LARGE_SOURCE).convert("RGBA")
frames = []
for size in sizes:
    source = small if size <= 32 else large
    stream = BytesIO()
    source.resize((size, size), Image.Resampling.LANCZOS).save(stream, format="PNG")
    frames.append(stream.getvalue())

offset = 6 + 16 * len(sizes)
directory = []
for size, frame in zip(sizes, frames):
    directory.append(struct.pack("<BBBBHHII", size % 256, size % 256,
                                 0, 0, 1, 32, len(frame), offset))
    offset += len(frame)
(root / "animation-editor.ico").write_bytes(
    struct.pack("<HHH", 0, 1, len(sizes)) + b"".join(directory) + b"".join(frames))
