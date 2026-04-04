#!/usr/bin/env python3
"""Extract a compiled Papyrus (.pex) file from Starfield's ``Starfield - Misc.ba2``.

Scripts are not in the ESM; they live in BA2 archives. Husbandry/greenhouse logic is in
``scripts/outpostharvesterfaunascript.pex`` (and related .pex files).

Usage:
  export STARFIELD_DATA=/path/to/Starfield/Data
  ./tools/extract_misc_ba2_script.py --name outpostharvesterfaunascript.pex -o /tmp/

Requires: Python 3, zlib (stdlib). No third-party deps.

Decompiling ``.pex`` → ``.psc`` (Champollion under Wine): see
``research/outpost-organic-husbandry.md`` (subsection "PEX → PSC (Champollion + Wine)").
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

from starfield_misc_ba2 import default_misc_ba2_path, extract_named_file


def main() -> None:
    ap = argparse.ArgumentParser(description="Extract one file from Starfield - Misc.ba2")
    ap.add_argument(
        "--data",
        type=Path,
        default=None,
        help="Starfield Data directory (default: STARFIELD_DATA or Linux Steam path)",
    )
    ap.add_argument(
        "--name",
        required=True,
        help="Archive path, e.g. scripts/outpostharvesterfaunascript.pex (case-insensitive match)",
    )
    ap.add_argument("-o", "--output", type=Path, required=True, help="Output file path")
    args = ap.parse_args()

    data = args.data or Path(
        os.environ.get(
            "STARFIELD_DATA",
            str(Path.home() / ".steam/steam/steamapps/common/Starfield/Data"),
        )
    )
    ba2 = default_misc_ba2_path(data)
    if not ba2.is_file():
        raise SystemExit(f"BA2 not found: {ba2}")

    want = args.name.strip().lower()
    if not want.startswith("scripts/"):
        want = "scripts/" + want

    hit = extract_named_file(ba2, want)
    if hit is None:
        raise SystemExit(f"No file named (case-insensitive) {want!r} in {ba2}")

    name, raw = hit
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(raw)
    print(f"Extracted {name!r} -> {args.output} ({len(raw)} bytes)")


if __name__ == "__main__":
    main()
