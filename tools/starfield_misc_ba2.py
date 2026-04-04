"""Read individual files from Starfield's ``Starfield - Misc.ba2`` (BTDX / zlib)."""

from __future__ import annotations

import struct
import zlib
from pathlib import Path


def extract_named_file(ba2_path: Path, target_lower: str) -> tuple[str, bytes] | None:
    """Return ``(archive_path, raw_bytes)`` for an exact case-insensitive path match."""
    target_lower = target_lower.strip().lower()
    with open(ba2_path, "rb") as f:
        magic = f.read(4)
        if magic != b"BTDX":
            return None
        version = struct.unpack("<I", f.read(4))[0]
        f.read(4)
        file_count = struct.unpack("<I", f.read(4))[0]
        name_table_offset = struct.unpack("<Q", f.read(8))[0]
        if version >= 2:
            f.read(8)

        records = []
        for _ in range(file_count):
            rec = f.read(36)
            offset = struct.unpack_from("<Q", rec, 16)[0]
            packed = struct.unpack_from("<I", rec, 24)[0]
            unpacked = struct.unpack_from("<I", rec, 28)[0]
            records.append((offset, packed, unpacked))

        f.seek(name_table_offset)
        for i in range(file_count):
            nlen = struct.unpack("<H", f.read(2))[0]
            name = f.read(nlen).decode("utf-8", errors="replace")
            if name.lower() == target_lower:
                off, packed, unpacked = records[i]
                f.seek(off)
                raw = f.read(packed if packed else unpacked)
                if packed and packed != unpacked:
                    raw = zlib.decompress(raw)
                return name, raw

    return None


def default_misc_ba2_path(data_dir: Path) -> Path:
    return data_dir / "Starfield - Misc.ba2"
