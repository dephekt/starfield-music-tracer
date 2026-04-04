#!/usr/bin/env python3
"""List Misc.ba2 entries whose path or payload contains a substring (research helper).

Examples:
  ./tools/misc_ba2_grep.py OrganicResource
  ./tools/misc_ba2_grep.py --name-only outpost .pex
  ./tools/misc_ba2_grep.py SetScanned --suffix .pex
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

from starfield_misc_ba2 import default_misc_ba2_path, iter_misc_ba2_entries


def main() -> None:
    ap = argparse.ArgumentParser(description="Grep substring across Starfield - Misc.ba2")
    ap.add_argument("needle", help="Substring to search (UTF-8; also matched as bytes in file bodies)")
    ap.add_argument(
        "--data",
        type=Path,
        default=None,
        help="Starfield Data directory (default: STARFIELD_DATA or Linux Steam path)",
    )
    ap.add_argument(
        "--name-only",
        action="store_true",
        help="Match only archive path, do not scan file contents",
    )
    ap.add_argument(
        "--suffix",
        default="",
        help="Only consider paths ending with this suffix (e.g. .pex)",
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

    needle = args.needle
    needle_b = needle.encode("utf-8")
    suf = args.suffix.lower()

    for name, raw in iter_misc_ba2_entries(ba2):
        if suf and not name.lower().endswith(suf):
            continue
        nlow = name.lower()
        if needle.lower() in nlow:
            print(name)
            continue
        if args.name_only:
            continue
        if needle_b in raw:
            print(name)


if __name__ == "__main__":
    main()
