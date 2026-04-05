#!/usr/bin/env python3
"""Extract vanilla outpost organic harvester ``.pex`` files and print printable strings.

Pulls from ``Starfield - Misc.ba2``:

- ``scripts/outpostharvesterfaunascript.pex``
- ``scripts/outpostharvesterflorascript.pex``
- ``scripts/outpostharvesterfloraplanterscript.pex``

By default prints strings that look relevant (keywords, script paths, identifiers).
Use ``--all`` to print every ASCII run (still de-duplicated per file).

Usage:
  export STARFIELD_DATA=/path/to/Starfield/Data
  ./tools/dump_outpost_husbandry_pex_strings.py
  ./tools/dump_outpost_husbandry_pex_strings.py --only fauna
  ./tools/dump_outpost_husbandry_pex_strings.py --all > /tmp/husbandry_pex_strings.txt

Requires: Python 3 stdlib only.

Full decompilation: ``research/outpost-organic-husbandry.md`` (subsection "PEX → PSC (Champollion + Wine)").
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

# Allow ``python tools/dump_....py`` (repo root not on path).
_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

from starfield_misc_ba2 import default_misc_ba2_path, extract_named_file

HUSBANDRY_PEX = (
    "scripts/outpostharvesterfaunascript.pex",
    "scripts/outpostharvesterflorascript.pex",
    "scripts/outpostharvesterfloraplanterscript.pex",
)

# Keys for ``--only`` (basename without path).
_ONLY_MAP = {
    "fauna": ("scripts/outpostharvesterfaunascript.pex",),
    "flora": ("scripts/outpostharvesterflorascript.pex",),
    "planter": ("scripts/outpostharvesterfloraplanterscript.pex",),
}

# Broad filter: husbandry / scanning / resources / workshop / flora-fauna context.
_FILTER_RE = re.compile(
    r"(outpost|harvester|fauna|flora|scan|scanned|resource|keyword|creature|organic|"
    r"quest|workshop|planet|biome|domestic|greenhouse|pen|actor|global|handscanner|"
    r"sq_parent|container|plant|seed|planter|location|respawn|menu|builder|vmad|pex|psc|"
    r"::|#\d)",
    re.IGNORECASE,
)


def iter_ascii_strings(data: bytes, min_len: int = 4) -> list[str]:
    out: list[str] = []
    cur: list[int] = []
    for b in data:
        if 32 <= b < 127:
            cur.append(b)
        else:
            if len(cur) >= min_len:
                out.append(bytes(cur).decode("ascii", errors="replace"))
            cur = []
    if len(cur) >= min_len:
        out.append(bytes(cur).decode("ascii", errors="replace"))
    return out


def main() -> None:
    ap = argparse.ArgumentParser(description="Dump strings from outpost husbandry/greenhouse .pex")
    ap.add_argument(
        "--data",
        type=Path,
        default=None,
        help="Starfield Data directory (default: STARFIELD_DATA or Linux Steam path)",
    )
    ap.add_argument(
        "--all",
        action="store_true",
        help="Print every ASCII string run (min length 4), not only filter matches",
    )
    ap.add_argument(
        "--min-len",
        type=int,
        default=4,
        help="Minimum string length (default: 4)",
    )
    ap.add_argument(
        "--only",
        choices=("fauna", "flora", "planter", "all"),
        default="all",
        help="Limit to one harvester .pex (default: all three)",
    )
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

    pex_list = HUSBANDRY_PEX if args.only == "all" else _ONLY_MAP[args.only]

    for rel in pex_list:
        hit = extract_named_file(ba2, rel)
        if hit is None:
            print(f"=== MISSING {rel!r} in {ba2} ===", file=sys.stderr)
            continue
        name, raw = hit
        print(f"=== {name} ({len(raw)} bytes) ===")
        strings = iter_ascii_strings(raw, min_len=args.min_len)
        for s in sorted(set(strings)):
            if not args.all and not _FILTER_RE.search(s):
                continue
            print(s)
        print()


if __name__ == "__main__":
    main()
