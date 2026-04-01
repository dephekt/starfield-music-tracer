#!/usr/bin/env python3
"""Match in-game Starfield music (.wem) to Official Soundtrack tracks.

Extracts music .wem files from BA2 archives, decodes them with pyvgmstream,
fingerprints everything with chromaprint, and cross-correlates to find matches.
Results are written into the existing SQLite database.

Requires: pyvgmstream, pyacoustid, libchromaprint, ffmpeg
Run with: LD_PRELOAD=/lib/x86_64-linux-gnu/libvorbisfile.so.3 uv run python3 fingerprint.py
"""

import json
import os
import re
import sqlite3
import struct
import subprocess
import sys
import time
import zlib
from pathlib import Path

import acoustid
import pyvgmstream

STARFIELD_DATA = Path.home() / ".steam/steam/steamapps/common/Starfield/Data"
OST_DIR = Path.home() / ".steam/steam/steamapps/music/STARFIELD OFFICIAL SOUNDTRACK"
DB_PATH = Path(__file__).parent / "data" / "starfield_music.db"

MIN_DURATION_SECS = 30
MATCH_THRESHOLD = 0.15


# ---------------------------------------------------------------------------
# BA2 index: build once, extract many
# ---------------------------------------------------------------------------

def build_ba2_index(ba2_paths):
    """Build {filename: (ba2_path, offset, packed, unpacked)} across archives.

    Later archives override earlier ones (patch semantics).
    """
    index = {}
    for ba2_path in ba2_paths:
        with open(ba2_path, "rb") as f:
            magic = f.read(4)
            if magic != b"BTDX":
                continue
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
                name = f.read(nlen).decode("utf-8", errors="replace").lower()
                off, pk, upk = records[i]
                index[name] = (ba2_path, off, pk, upk)

    return index


def extract_file(index, filename):
    """Extract a single file from the BA2 index."""
    key = filename.lower()
    if key not in index:
        return None
    ba2_path, offset, packed, unpacked = index[key]
    with open(ba2_path, "rb") as f:
        f.seek(offset)
        raw = f.read(packed if packed else unpacked)
        if packed and packed != unpacked:
            raw = zlib.decompress(raw)
    return raw


# ---------------------------------------------------------------------------
# soundbanksinfo.json: identify music Wwise IDs
# ---------------------------------------------------------------------------

def extract_file_substring(index, substring):
    """Extract a file matching a substring of the path (for soundbanksinfo etc)."""
    sub = substring.lower()
    for key in index:
        if sub in key:
            return extract_file(index, key)
    return None


def load_music_wwise_ids(ba2_index):
    """Parse soundbanksinfo.json and return {wwise_id: short_name} for music."""
    raw = extract_file_substring(ba2_index, "soundbanksinfo.json")
    if raw is None:
        print("ERROR: soundbanksinfo.json not found in any BA2", file=sys.stderr)
        sys.exit(1)

    info = json.loads(raw)
    sb = info["SoundBanksInfo"]
    streamed = {f["Id"]: f for f in sb.get("StreamedFiles", [])}

    mus_bank = next(
        (b for b in sb["SoundBanks"] if b.get("ShortName") == "Starfield_MUS"),
        None,
    )
    if mus_bank is None:
        print("ERROR: Starfield_MUS soundbank not found", file=sys.stderr)
        sys.exit(1)

    ref_ids = {f["Id"] for f in mus_bank["ReferencedStreamedFiles"]}
    result = {}
    for wid in ref_ids:
        meta = streamed.get(wid, {})
        result[wid] = meta.get("ShortName", f"unknown_{wid}")

    return result


# ---------------------------------------------------------------------------
# Fingerprint pipeline
# ---------------------------------------------------------------------------

def probe_wem(wem_bytes, wwise_id):
    """Get duration of a .wem buffer without decoding."""
    try:
        info = pyvgmstream.probe_buffer(wem_bytes, filename_hint=f"{wwise_id}.wem")
        return info.duration_seconds
    except Exception:
        return 0.0


WAV_DIR = Path(__file__).parent / "data" / "wav"


def decode_and_fingerprint(wem_bytes, wwise_id, wwise_name=""):
    """Decode .wem to WAV, fingerprint it. Returns (duration, fp_string) or None.

    Decoded WAVs are saved to data/wav/ for later spot-checking.
    """
    try:
        wav = pyvgmstream.decode_buffer_to_wav_bytes(
            wem_bytes, filename_hint=f"{wwise_id}.wem"
        )
    except Exception as e:
        print(f"    decode error {wwise_id}: {e}")
        return None

    WAV_DIR.mkdir(parents=True, exist_ok=True)
    short = wwise_name.replace("\\", "/").rsplit("/", 1)[-1].replace(".wav", "")
    wav_path = WAV_DIR / f"{short or wwise_id}.wav"
    wav_path.write_bytes(wav)

    try:
        dur, fp = acoustid.fingerprint_file(str(wav_path))
        return (dur, fp)
    except Exception as e:
        print(f"    fingerprint error {wwise_id}: {e}")
        return None


def fingerprint_ost_tracks(ost_dir):
    """Fingerprint all OST .wav files. Returns [(track_number, title, duration, fp), ...]."""
    results = []
    pattern = re.compile(r"^(\d+)\s*-\s*(.+)\.wav$")
    for wav_path in sorted(ost_dir.glob("*.wav")):
        m = pattern.match(wav_path.name)
        if not m:
            continue
        track_num = int(m.group(1))
        title = m.group(2).strip()
        try:
            dur, fp = acoustid.fingerprint_file(str(wav_path))
            results.append((track_num, title, dur, fp))
        except Exception as e:
            print(f"  OST fingerprint error {wav_path.name}: {e}")
    return results


# ---------------------------------------------------------------------------
# Cross-correlation
# ---------------------------------------------------------------------------

MAX_ALIGN_OFFSET = 120
MAX_BIT_ERROR = 2
DURATION_RATIO_CUTOFF = 2.0


def _compare_fingerprints(a_fp, b_fp):
    """Compare two chromaprint fingerprints using int.bit_count().

    Reimplements pyacoustid's compare_fingerprints without the slow
    bin(x).count('1') popcount -- int.bit_count() is a CPython built-in
    that compiles down to a single popcnt instruction on modern x86.
    """
    import chromaprint
    a = [int(x) for x in chromaprint.decode_fingerprint(a_fp)[0]]
    b = [int(x) for x in chromaprint.decode_fingerprint(b_fp)[0]]
    asize, bsize = len(a), len(b)
    if asize == 0 or bsize == 0:
        return 0.0
    counts = [0] * (asize + bsize + 1)
    for i in range(asize):
        ai = a[i]
        jbegin = max(0, i - MAX_ALIGN_OFFSET)
        jend = min(bsize, i + MAX_ALIGN_OFFSET)
        base_off = i + bsize
        for j in range(jbegin, jend):
            if (ai ^ b[j]).bit_count() <= MAX_BIT_ERROR:
                counts[base_off - j] += 1
    return max(counts) / min(asize, bsize)


def find_matches(game_fps, ost_fps):
    """Compare every game fingerprint against every OST fingerprint.

    Skips pairs where durations differ by more than DURATION_RATIO_CUTOFF.
    """
    matches = []
    total = len(ost_fps) * len(game_fps)
    done = 0
    skipped = 0

    for ost_num, ost_title, ost_dur, ost_fp in ost_fps:
        best_score = 0
        best_wid = None
        best_wname = None
        best_gdur = 0

        for wid, wname, gdur, gfp in game_fps:
            done += 1

            ratio = max(ost_dur, gdur) / max(min(ost_dur, gdur), 1)
            if ratio > DURATION_RATIO_CUTOFF:
                skipped += 1
                continue

            try:
                score = _compare_fingerprints(gfp, ost_fp)
            except Exception:
                score = 0.0
            if score > best_score:
                best_score = score
                best_wid = wid
                best_wname = wname
                best_gdur = gdur

        if done % 1000 == 0:
            print(f"  {done}/{total} compared, {skipped} skipped by duration filter")

        matches.append({
            "ost_track": ost_num,
            "ost_title": ost_title,
            "ost_duration": ost_dur,
            "wwise_id": best_wid,
            "wwise_name": best_wname,
            "confidence": best_score,
            "game_duration": best_gdur,
        })

    print(f"  {done}/{total} total, {skipped} skipped by duration filter")
    matches.sort(key=lambda m: m["confidence"], reverse=True)
    return matches


# ---------------------------------------------------------------------------
# Preview clip generation
# ---------------------------------------------------------------------------

CLIPS_DIR = Path(__file__).parent / "static" / "clips"
CLIP_DURATION = 10
CLIP_BITRATE = 32000


def _trim_and_encode_opus(input_wav, output_opus, duration_secs):
    """Trim a WAV to a 10s clip from ~30% in, then encode to Opus."""
    start = max(0, duration_secs * 0.3)
    trimmed = output_opus.with_suffix(".trim.wav")
    try:
        subprocess.run(
            [
                "ffmpeg", "-y", "-loglevel", "error",
                "-ss", str(start), "-t", str(CLIP_DURATION),
                "-i", str(input_wav),
                "-ac", "1", "-ar", "24000",
                str(trimmed),
            ],
            check=True, capture_output=True,
        )
        subprocess.run(
            [
                "opusenc", "--quiet",
                "--bitrate", str(CLIP_BITRATE // 1000),
                str(trimmed), str(output_opus),
            ],
            check=True, capture_output=True,
        )
    finally:
        trimmed.unlink(missing_ok=True)


def generate_preview_clips(matches, ost_dir):
    """Generate short Opus preview clips for each matched pair."""
    CLIPS_DIR.mkdir(parents=True, exist_ok=True)
    generated = 0
    skipped = 0
    pattern = re.compile(r"^(\d+)\s*-\s*(.+)\.wav$")

    ost_paths = {}
    for wav_path in ost_dir.glob("*.wav"):
        m = pattern.match(wav_path.name)
        if m:
            ost_paths[int(m.group(1))] = wav_path

    for m in matches:
        if m["confidence"] < MATCH_THRESHOLD or not m["wwise_id"]:
            continue

        track_num = m["ost_track"]
        ost_clip = CLIPS_DIR / f"{track_num:02d}_ost.opus"
        game_clip = CLIPS_DIR / f"{track_num:02d}_game.opus"

        ost_wav = ost_paths.get(track_num)
        wwise_name = m["wwise_name"] or ""
        short = wwise_name.replace("\\", "/").rsplit("/", 1)[-1].replace(".wav", "")
        game_wav = WAV_DIR / f"{short or m['wwise_id']}.wav"

        if not ost_wav or not ost_wav.exists():
            skipped += 1
            continue
        if not game_wav.exists():
            skipped += 1
            continue

        try:
            _trim_and_encode_opus(ost_wav, ost_clip, m["ost_duration"])
            _trim_and_encode_opus(game_wav, game_clip, m["game_duration"])
            generated += 1
        except Exception as e:
            print(f"  clip error track {track_num}: {e}")
            skipped += 1

    print(f"  Generated {generated} clip pairs, skipped {skipped}")


# ---------------------------------------------------------------------------
# Database output
# ---------------------------------------------------------------------------

FINGERPRINT_SCHEMA = """
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


def write_results(matches):
    """Write fingerprint match results into the existing database."""
    conn = sqlite3.connect(str(DB_PATH))
    c = conn.cursor()
    c.executescript(FINGERPRINT_SCHEMA)

    c.execute("DELETE FROM fingerprint_matches")
    c.execute("DELETE FROM ost_tracks")

    for m in matches:
        c.execute(
            "INSERT OR IGNORE INTO ost_tracks VALUES (?, ?, ?)",
            (m["ost_track"], m["ost_title"], m["ost_duration"]),
        )
        if m["wwise_id"] and m["confidence"] >= MATCH_THRESHOLD:
            c.execute(
                "INSERT OR IGNORE INTO fingerprint_matches VALUES (?, ?, ?, ?, ?)",
                (
                    m["ost_track"],
                    m["wwise_id"],
                    m["wwise_name"],
                    m["confidence"],
                    m["game_duration"],
                ),
            )

    conn.commit()

    n_matched = c.execute(
        "SELECT COUNT(DISTINCT ost_track_number) FROM fingerprint_matches"
    ).fetchone()[0]
    print(f"\nDatabase updated: {n_matched}/{len(matches)} OST tracks matched")
    conn.close()


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    if not STARFIELD_DATA.exists():
        print(f"Starfield data not found: {STARFIELD_DATA}", file=sys.stderr)
        sys.exit(1)
    if not OST_DIR.exists():
        print(f"OST directory not found: {OST_DIR}", file=sys.stderr)
        sys.exit(1)
    if not DB_PATH.exists():
        print(f"Database not found: {DB_PATH} (run extract.py first)", file=sys.stderr)
        sys.exit(1)

    t0 = time.time()

    # --- Step 1: Build BA2 index ---
    ba2_paths = sorted(STARFIELD_DATA.glob("*WwiseSounds*.ba2"))
    print(f"Building BA2 index from {len(ba2_paths)} archives...")
    ba2_index = build_ba2_index(ba2_paths)
    print(f"  {len(ba2_index)} files indexed")

    # --- Step 2: Get music Wwise IDs ---
    print("Loading music Wwise IDs from soundbanksinfo.json...")
    music_ids = load_music_wwise_ids(ba2_index)
    print(f"  {len(music_ids)} music files identified")

    # --- Step 3: Extract, probe, filter, decode, fingerprint game files ---
    print(f"Extracting and fingerprinting game music (>{MIN_DURATION_SECS}s)...")
    game_fps = []
    skipped_short = 0
    skipped_missing = 0
    skipped_error = 0

    for i, (wid, wname) in enumerate(sorted(music_ids.items()), 1):
        if i % 50 == 0 or i == len(music_ids):
            print(f"  [{i}/{len(music_ids)}] processed, {len(game_fps)} qualifying")

        ba2_key = f"sound/soundbanks/{wid}.wem"
        wem = extract_file(ba2_index, ba2_key)
        if wem is None:
            skipped_missing += 1
            continue

        dur = probe_wem(wem, wid)
        if dur < MIN_DURATION_SECS:
            skipped_short += 1
            continue

        fp_result = decode_and_fingerprint(wem, wid, wname)
        if fp_result is None:
            skipped_error += 1
            continue

        game_fps.append((wid, wname, fp_result[0], fp_result[1]))

    print(f"\n  Game fingerprints: {len(game_fps)}")
    print(f"  Skipped (short): {skipped_short}")
    print(f"  Skipped (missing): {skipped_missing}")
    print(f"  Skipped (error): {skipped_error}")

    # --- Step 4: Fingerprint OST ---
    print(f"\nFingerprinting {OST_DIR.name}...")
    ost_fps = fingerprint_ost_tracks(OST_DIR)
    print(f"  {len(ost_fps)} OST tracks fingerprinted")

    # --- Step 5: Cross-correlate ---
    print(f"\nCross-correlating {len(ost_fps)} OST x {len(game_fps)} game tracks...")
    matches = find_matches(game_fps, ost_fps)

    # --- Step 6: Report ---
    print("\n" + "=" * 78)
    print("RESULTS")
    print("=" * 78)
    high = [m for m in matches if m["confidence"] >= 0.5]
    medium = [m for m in matches if 0.25 <= m["confidence"] < 0.5]
    low = [m for m in matches if m["confidence"] < 0.25]
    print(f"  High confidence (>=50%): {len(high)}")
    print(f"  Medium (25-50%):         {len(medium)}")
    print(f"  Low (<25%):              {len(low)}")

    print(f"\nTop matches:")
    for m in matches[:20]:
        short = m["wwise_name"] or m["wwise_id"]
        if "\\" in short:
            short = short.split("\\")[-1].replace(".wav", "")
        print(
            f"  {m['confidence']:.1%}  OST {m['ost_track']:02d} \"{m['ost_title']}\""
            f"  <->  {short}"
        )

    print(f"\nUnmatched OST tracks (confidence < {MATCH_THRESHOLD:.0%}):")
    unmatched = [m for m in matches if m["confidence"] < MATCH_THRESHOLD]
    if unmatched:
        for m in unmatched:
            print(f"  OST {m['ost_track']:02d} \"{m['ost_title']}\" (best: {m['confidence']:.1%})")
    else:
        print("  (none)")

    # --- Step 7: Generate preview clips ---
    print("\nGenerating preview clips...")
    generate_preview_clips(matches, OST_DIR)

    # --- Step 8: Write to DB ---
    print(f"\nWriting results to {DB_PATH}...")
    write_results(matches)

    elapsed = time.time() - t0
    print(f"\nDone in {elapsed:.1f}s")


if __name__ == "__main__":
    main()
