#!/usr/bin/env python3
"""Rebuild the fingerprint tables in data/starfield_music.db from frozen HTML.

Context
-------
The canonical way to produce ``ost_tracks`` and ``fingerprint_matches`` is
``fingerprint.py``, which needs the game's Wwise audio and the official
soundtrack. When that source data isn't available (e.g. CI on a hosted
runner), this script recovers the same two tables from a previously frozen
copy of the ``/fingerprints`` page, which renders every stored column in its
table-row ``data-*`` attributes and cells.

It was written to restore the tables after the project moved to a
GitHub-as-source-of-truth deployment, using the frozen static site that the
old Codeberg pipeline published. Get that HTML from the historical page, e.g.::

    git show pages:fingerprints/index.html > /tmp/fingerprints.html
    python scripts/reconstruct_fingerprint_db.py /tmp/fingerprints.html

The recovery is exact except ``game_duration``, which the page only rendered
to 0.1s -- the precision main.py displays anyway. Prefer regenerating from
source with fingerprint.py when the game data is at hand.
"""
import html
import re
import sqlite3
import sys
from pathlib import Path

DB_PATH = Path(__file__).resolve().parent.parent / "data" / "starfield_music.db"

# Mirror of fingerprint.py's FINGERPRINT_SCHEMA -- keep in sync.
SCHEMA = """
CREATE TABLE IF NOT EXISTS ost_tracks (
    track_number  INTEGER PRIMARY KEY,
    title         TEXT,
    duration_secs REAL
);
CREATE TABLE IF NOT EXISTS fingerprint_matches (
    ost_track_number INTEGER REFERENCES ost_tracks(track_number),
    wwise_id         TEXT,
    wwise_name       TEXT,
    confidence       REAL,
    game_duration    REAL,
    PRIMARY KEY (ost_track_number, wwise_id)
);
"""


def parse_rows(page_html: str) -> list[dict]:
    rows = re.findall(r"<tr data-confidence=.*?</tr>", page_html, re.S)
    out = []
    for r in rows:
        def attr(name: str):
            m = re.search(rf'data-{name}="([^"]*)"', r)
            return html.unescape(m.group(1)) if m else None

        wname = re.search(r'class="fp-game-name"[^>]*title="([^"]*)"', r)
        wid = re.search(r'class="cell-edid">ID:\s*([0-9]+)', r)
        gdur = re.search(r"/\s*([0-9.]+)s", r)
        out.append(
            {
                "track": int(attr("track")),
                "title": attr("title"),
                "confidence": float(attr("confidence")),
                "ost_duration": float(attr("ost_dur")),
                "wwise_name": html.unescape(wname.group(1)) if wname else None,
                "wwise_id": wid.group(1) if wid else None,
                "game_duration": float(gdur.group(1)) if gdur else None,
            }
        )
    return out


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__)
        return 2
    page_html = Path(argv[1]).read_text(encoding="utf-8")
    recs = parse_rows(page_html)
    if not recs:
        print("No fingerprint rows found in the given HTML.", file=sys.stderr)
        return 1

    conn = sqlite3.connect(str(DB_PATH))
    cur = conn.cursor()
    cur.executescript(SCHEMA)
    cur.execute("DELETE FROM fingerprint_matches")
    cur.execute("DELETE FROM ost_tracks")
    for x in recs:
        cur.execute(
            "INSERT OR IGNORE INTO ost_tracks VALUES (?, ?, ?)",
            (x["track"], x["title"], x["ost_duration"]),
        )
        if x["wwise_id"]:
            cur.execute(
                "INSERT OR IGNORE INTO fingerprint_matches VALUES (?, ?, ?, ?, ?)",
                (
                    x["track"],
                    x["wwise_id"],
                    x["wwise_name"],
                    x["confidence"],
                    x["game_duration"],
                ),
            )
    conn.commit()
    n_ost = cur.execute("SELECT count(*) FROM ost_tracks").fetchone()[0]
    n_fp = cur.execute("SELECT count(*) FROM fingerprint_matches").fetchone()[0]
    conn.close()
    print(f"Wrote {n_ost} ost_tracks and {n_fp} fingerprint_matches to {DB_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
