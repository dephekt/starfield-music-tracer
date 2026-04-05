#!/usr/bin/env python3
"""Search null-separated entries in Starfield Localization BA2 string tables (research helper).

Example:
  python3 tools/localization_ba2_string_grep.py --contains "Outpost production"
  python3 tools/localization_ba2_string_grep.py --contains Temperament --lang en --also-dlstrings
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

from starfield_misc_ba2 import iter_misc_ba2_entries


def main() -> None:
    ap = argparse.ArgumentParser(description="Grep substrings in Starfield Localization.ba2 string bundles")
    ap.add_argument(
        "--data",
        type=Path,
        default=None,
        help="Starfield Data directory (default: STARFIELD_DATA or Linux Steam path)",
    )
    ap.add_argument(
        "--archive",
        default="Starfield - Localization.ba2",
        help="BA2 filename under Data (default: Starfield - Localization.ba2)",
    )
    ap.add_argument("--lang", default="en", help="Language code for strings/starfield_{lang}.* (default: en)")
    ap.add_argument(
        "--also-dlstrings",
        action="store_true",
        help="Also scan .dlstrings for the same language",
    )
    ap.add_argument(
        "--also-ilstrings",
        action="store_true",
        help="Also scan .ilstrings for the same language",
    )
    ap.add_argument(
        "--contains",
        action="append",
        default=[],
        help="Substring to match (case-sensitive); repeat flag for multiple",
    )
    args = ap.parse_args()

    data = args.data or Path(
        os.environ.get(
            "STARFIELD_DATA",
            str(Path.home() / ".steam/steam/steamapps/common/Starfield/Data"),
        )
    )
    ba2 = data / args.archive
    if not ba2.is_file():
        raise SystemExit(f"BA2 not found: {ba2}")

    suffixes = [".strings"]
    if args.also_dlstrings:
        suffixes.append(".dlstrings")
    if args.also_ilstrings:
        suffixes.append(".ilstrings")

    targets = [f"strings/starfield_{args.lang}{suf}" for suf in suffixes]
    needles = [n for n in args.contains if n]
    if not needles:
        needles = [
            "Outpost production allowed",
            "Outpost planet survey production boost",
            "Temperament:",
            "Heals rapidly",
        ]

    wanted_lower = {t.lower() for t in targets}
    for archive_path, raw in iter_misc_ba2_entries(ba2):
        if archive_path.lower() not in wanted_lower:
            continue
        parts = raw.split(b"\x00")
        texts = [p.decode("utf-8", errors="replace") for p in parts if p]
        for text in texts:
            for n in needles:
                if n in text:
                    print(f"{archive_path}\t{n}\t{text[:500]}{'…' if len(text) > 500 else ''}")
                    break


if __name__ == "__main__":
    main()
