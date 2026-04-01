#!/usr/bin/env python3
"""Extract Starfield CELL -> MUSC -> MUST chain into SQLite.

Runs locally against the Starfield ESM. Produces a small SQLite database
that the web app reads at runtime.
"""

import sqlite3
import struct
import sys
import zlib
from pathlib import Path

STARFIELD_DATA = Path.home() / ".steam/steam/steamapps/common/Starfield/Data"
ESM_PATH = STARFIELD_DATA / "Starfield.esm"
DB_PATH = Path(__file__).parent / "data" / "starfield_music.db"

COMPRESSED_FLAG = 0x00040000


# ---------------------------------------------------------------------------
# ESM binary helpers
# ---------------------------------------------------------------------------

def read_header(f):
    """Read a 24-byte record or GRUP header. Returns dict or None at EOF."""
    buf = f.read(24)
    if len(buf) < 24:
        return None
    tag = buf[0:4]
    if tag == b"GRUP":
        size, label, gtype, stamp, unk = struct.unpack_from("<IIIII", buf, 4)
        return {"tag": "GRUP", "size": size, "label": label, "gtype": gtype}
    tag_str = tag.decode("ascii", errors="replace")
    data_size, flags, form_id, vc, unk = struct.unpack_from("<IIIII", buf, 4)
    return {"tag": tag_str, "data_size": data_size, "flags": flags, "form_id": form_id}


def read_record_data(f, hdr):
    """Read a record's payload, decompressing if needed."""
    raw = f.read(hdr["data_size"])
    if hdr["flags"] & COMPRESSED_FLAG:
        dec_size = struct.unpack_from("<I", raw, 0)[0]
        raw = zlib.decompress(raw[4:], bufsize=dec_size)
    return raw


def iter_subrecords(data):
    """Yield (type_str, bytes) for each subrecord."""
    pos = 0
    xxxx_size = None
    while pos + 6 <= len(data):
        sub_type = data[pos : pos + 4].decode("ascii", errors="replace")
        sub_len = struct.unpack_from("<H", data, pos + 4)[0]
        pos += 6

        if sub_type == "XXXX":
            xxxx_size = struct.unpack_from("<I", data, pos)[0]
            pos += sub_len
            continue

        if xxxx_size is not None:
            sub_len = xxxx_size
            xxxx_size = None

        yield sub_type, data[pos : pos + sub_len]
        pos += sub_len


def walk_grup(f, end, callback, tags=None):
    """Recursively walk a GRUP, calling callback(header, data) for records.

    If *tags* is given, only decompress/read records whose tag is in the set;
    everything else is seeked past.
    """
    while f.tell() < end:
        hdr = read_header(f)
        if hdr is None:
            break
        if hdr["tag"] == "GRUP":
            walk_grup(f, f.tell() - 24 + hdr["size"], callback, tags)
        elif tags and hdr["tag"] not in tags:
            f.seek(hdr["data_size"], 1)
        else:
            callback(hdr, read_record_data(f, hdr))


def enter_grup(f, offset):
    """Seek to a GRUP, read its header, return the byte offset of its end."""
    f.seek(offset)
    hdr = read_header(f)
    assert hdr and hdr["tag"] == "GRUP", f"Expected GRUP at 0x{offset:08X}, got {hdr}"
    return offset + hdr["size"]


# ---------------------------------------------------------------------------
# ESM record parsers
# ---------------------------------------------------------------------------

def parse_musc(f, offset):
    """MUSC GRUP -> (types dict, type_tracks list).

    types:       {form_id: editor_id}
    type_tracks: [(musc_form_id, must_form_id), ...]
    """
    end = enter_grup(f, offset)
    types = {}
    links = []

    def on_record(hdr, data):
        if hdr["tag"] != "MUSC":
            return
        fid = hdr["form_id"]
        edid = None
        for st, sd in iter_subrecords(data):
            if st == "EDID":
                edid = sd.rstrip(b"\x00").decode("utf-8", errors="replace")
            elif st == "TNAM" and len(sd) >= 4:
                links.append((fid, struct.unpack_from("<I", sd)[0]))
        types[fid] = edid

    walk_grup(f, end, on_record, {"MUSC"})
    return types, links


def parse_must(f, offset):
    """MUST GRUP -> dict keyed by form_id.

    Each value: {editor_id, mtsh, snam_targets: [form_id, ...]}
    """
    end = enter_grup(f, offset)
    tracks = {}

    def on_record(hdr, data):
        if hdr["tag"] != "MUST":
            return
        fid = hdr["form_id"]
        edid = None
        mtsh = None
        snams = []
        for st, sd in iter_subrecords(data):
            if st == "EDID":
                edid = sd.rstrip(b"\x00").decode("utf-8", errors="replace")
            elif st == "MTSH":
                mtsh = sd[:16].hex() if len(sd) >= 16 else sd.hex()
            elif st == "SNAM" and len(sd) >= 4:
                snams.append(struct.unpack_from("<I", sd)[0])
        tracks[fid] = {"editor_id": edid, "mtsh": mtsh, "snam_targets": snams}

    walk_grup(f, end, on_record, {"MUST"})
    return tracks


def resolve_must_chain(must_records):
    """Resolve parent->child SNAM chains.

    Returns a dict: parent_form_id -> [leaf_form_ids].
    Leaf tracks (those with MTSH and no SNAM) map to themselves.
    """
    resolved = {}
    def _resolve(fid, seen=None):
        if fid in resolved:
            return resolved[fid]
        seen = seen or set()
        if fid in seen:
            return []
        seen.add(fid)
        rec = must_records.get(fid)
        if rec is None:
            return []
        if rec["snam_targets"]:
            leaves = []
            for child in rec["snam_targets"]:
                leaves.extend(_resolve(child, seen))
            resolved[fid] = leaves
            return leaves
        resolved[fid] = [fid]
        return [fid]

    for fid in must_records:
        _resolve(fid)
    return resolved


def parse_cells(f, offset):
    """Scan a top-level GRUP (CELL or WRLD) for CELL records with XCMO."""
    end = enter_grup(f, offset)
    cells = {}

    def on_record(hdr, data):
        if hdr["tag"] != "CELL":
            return
        fid = hdr["form_id"]
        edid = None
        musc_fid = None
        for st, sd in iter_subrecords(data):
            if st == "EDID":
                edid = sd.rstrip(b"\x00").decode("utf-8", errors="replace")
            elif st == "XCMO" and len(sd) >= 4:
                musc_fid = struct.unpack_from("<I", sd)[0]
        if musc_fid is not None:
            cells[fid] = {"editor_id": edid, "musc_form_id": musc_fid}

    walk_grup(f, end, on_record, {"CELL"})
    return cells


# ---------------------------------------------------------------------------
# SQLite output
# ---------------------------------------------------------------------------

SCHEMA = """
CREATE TABLE music_types (
    form_id   INTEGER PRIMARY KEY,
    editor_id TEXT
);
CREATE TABLE music_tracks (
    form_id   INTEGER PRIMARY KEY,
    editor_id TEXT,
    mtsh      TEXT
);
CREATE TABLE music_type_tracks (
    musc_form_id INTEGER REFERENCES music_types(form_id),
    must_form_id INTEGER REFERENCES music_tracks(form_id),
    PRIMARY KEY (musc_form_id, must_form_id)
);
CREATE TABLE cells (
    form_id      INTEGER PRIMARY KEY,
    editor_id    TEXT,
    musc_form_id INTEGER REFERENCES music_types(form_id)
);
CREATE VIEW cell_music AS
SELECT
    c.form_id     AS cell_form_id,
    c.editor_id   AS cell_name,
    mt.form_id    AS music_type_form_id,
    mt.editor_id  AS music_type,
    mk.form_id    AS track_form_id,
    mk.editor_id  AS track_name,
    mk.mtsh       AS track_hash
FROM cells c
JOIN music_types mt ON c.musc_form_id = mt.form_id
JOIN music_type_tracks mtt ON mt.form_id = mtt.musc_form_id
JOIN music_tracks mk ON mtt.must_form_id = mk.form_id;
"""


def write_database(cells, music_types, type_tracks, must_records, chain):
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    if DB_PATH.exists():
        DB_PATH.unlink()

    conn = sqlite3.connect(str(DB_PATH))
    c = conn.cursor()
    c.executescript(SCHEMA)

    for fid, edid in music_types.items():
        c.execute("INSERT INTO music_types VALUES (?,?)", (fid, edid))

    for fid, rec in must_records.items():
        c.execute("INSERT INTO music_tracks VALUES (?,?,?)",
                  (fid, rec["editor_id"], rec["mtsh"]))

    # Resolve MUSC -> leaf MUST links through the SNAM chain
    for musc_fid, must_fid in type_tracks:
        for leaf_fid in chain.get(must_fid, [must_fid]):
            c.execute("INSERT OR IGNORE INTO music_type_tracks VALUES (?,?)",
                      (musc_fid, leaf_fid))

    for fid, cell in cells.items():
        c.execute("INSERT OR IGNORE INTO cells VALUES (?,?,?)",
                  (fid, cell["editor_id"], cell["musc_form_id"]))

    conn.commit()

    print("\nDatabase contents:")
    for tbl in ("cells", "music_types", "music_type_tracks", "music_tracks"):
        n = c.execute(f"SELECT COUNT(*) FROM {tbl}").fetchone()[0]
        print(f"  {tbl}: {n}")
    n = c.execute("SELECT COUNT(*) FROM cell_music").fetchone()[0]
    print(f"  cell_music (view): {n}")

    print("\nSample rows from cell_music:")
    for row in c.execute("SELECT * FROM cell_music LIMIT 5").fetchall():
        print(f"  {row}")

    conn.close()


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    if not ESM_PATH.exists():
        print(f"ESM not found: {ESM_PATH}", file=sys.stderr)
        sys.exit(1)

    print(f"Opening {ESM_PATH.name} ({ESM_PATH.stat().st_size / 1e9:.2f} GB)")

    with open(ESM_PATH, "rb") as f:
        # Skip TES4 header record
        hdr = read_header(f)
        f.seek(hdr["data_size"], 1)

        # Discover top-level GRUPs
        grups = {}
        while True:
            pos = f.tell()
            hdr = read_header(f)
            if hdr is None:
                break
            if hdr["tag"] != "GRUP":
                f.seek(hdr.get("data_size", 0), 1)
                continue
            label = struct.pack("<I", hdr["label"]).decode("ascii", errors="replace").rstrip("\x00")
            grups[label] = (pos, hdr["size"])
            f.seek(pos + hdr["size"])

        print(f"Found {len(grups)} top-level GRUPs")

        # --- MUSC ---
        if "MUSC" not in grups:
            print("MUSC GRUP not found", file=sys.stderr)
            sys.exit(1)
        pos, sz = grups["MUSC"]
        print(f"Parsing MUSC ({sz:,} bytes at 0x{pos:08X})...")
        music_types, type_tracks = parse_musc(f, pos)
        print(f"  {len(music_types)} music types, {len(type_tracks)} type->track links")

        # --- MUST ---
        if "MUST" not in grups:
            print("MUST GRUP not found", file=sys.stderr)
            sys.exit(1)
        pos, sz = grups["MUST"]
        print(f"Parsing MUST ({sz:,} bytes at 0x{pos:08X})...")
        must_records = parse_must(f, pos)
        with_mtsh = sum(1 for r in must_records.values() if r["mtsh"])
        with_snam = sum(1 for r in must_records.values() if r["snam_targets"])
        print(f"  {len(must_records)} tracks ({with_mtsh} leaf w/ MTSH, {with_snam} parents w/ SNAM)")

        chain = resolve_must_chain(must_records)
        leaf_count = sum(len(v) for k, v in chain.items()
                         if k in {t for _, t in type_tracks})
        print(f"  Resolved MUSC->leaf tracks: {leaf_count}")

        # --- CELL ---
        cells = {}
        if "CELL" in grups:
            pos, sz = grups["CELL"]
            print(f"Parsing CELL ({sz:,} bytes at 0x{pos:08X})...")
            cells = parse_cells(f, pos)
            print(f"  {len(cells)} interior cells with music")

        # --- WRLD ---
        if "WRLD" in grups:
            pos, sz = grups["WRLD"]
            print(f"Parsing WRLD ({sz:,} bytes at 0x{pos:08X})...")
            wrld_cells = parse_cells(f, pos)
            new = sum(1 for fid in wrld_cells if fid not in cells)
            cells.update(wrld_cells)
            print(f"  {len(wrld_cells)} worldspace cells with music ({new} new)")

        print(f"Total cells with music: {len(cells)}")

    # --- Write DB ---
    print(f"\nWriting {DB_PATH}...")
    write_database(cells, music_types, type_tracks, must_records, chain)
    print(f"\nDone! ({DB_PATH.stat().st_size / 1024:.1f} KB)")


if __name__ == "__main__":
    main()
