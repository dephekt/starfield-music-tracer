"""Starfield Music Tracer -- FastAPI web app."""

import sqlite3
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Query, Request
from fastapi.responses import HTMLResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

DB_PATH = Path(__file__).parent / "data" / "starfield_music.db"

_db: sqlite3.Connection | None = None


def get_db() -> sqlite3.Connection:
    assert _db is not None
    return _db


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _db
    _db = sqlite3.connect(f"file:{DB_PATH}?mode=ro", uri=True)
    _db.row_factory = sqlite3.Row
    yield
    _db.close()
    _db = None


app = FastAPI(title="Starfield Music Tracer", lifespan=lifespan)
app.mount("/static", StaticFiles(directory=Path(__file__).parent / "static"), name="static")
templates = Jinja2Templates(directory=Path(__file__).parent / "templates")


def form_id_hex(value):
    """Jinja2 filter: format an integer form ID as hex."""
    if value is None:
        return ""
    return f"{value:08X}"


templates.env.filters["formid"] = form_id_hex


# ---------------------------------------------------------------------------
# HTML pages
# ---------------------------------------------------------------------------

@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    db = get_db()
    rows = db.execute("""
        SELECT cell_form_id, cell_name, cell_full_name,
               music_type_form_id, music_type,
               track_form_id, track_name, track_hash
        FROM cell_music
        ORDER BY cell_name
    """).fetchall()
    ost_lookup = _build_ost_lookup(db)
    return templates.TemplateResponse(request, "index.html", {
        "rows": rows,
        "total": len(rows),
        "ost_lookup": ost_lookup,
    })


@app.get("/cell/{editor_id}", response_class=HTMLResponse)
async def cell_detail(request: Request, editor_id: str):
    db = get_db()
    rows = db.execute("""
        SELECT cell_form_id, cell_name, cell_full_name,
               music_type_form_id, music_type,
               track_form_id, track_name, track_hash
        FROM cell_music
        WHERE cell_name = ?
    """, (editor_id,)).fetchall()

    if not rows:
        return templates.TemplateResponse(request, "detail.html", {
            "cell": None,
            "editor_id": editor_id,
        })

    cell = {
        "form_id": rows[0]["cell_form_id"],
        "editor_id": rows[0]["cell_name"],
        "full_name": rows[0]["cell_full_name"],
        "music_type": rows[0]["music_type"],
        "music_type_form_id": rows[0]["music_type_form_id"],
        "tracks": [{
            "form_id": r["track_form_id"],
            "name": r["track_name"],
            "hash": r["track_hash"],
            "ost_match": _get_ost_match(db, r["track_name"]),
        } for r in rows],
    }
    return templates.TemplateResponse(request, "detail.html", {
        "cell": cell,
        "editor_id": editor_id,
    })


@app.get("/music-type/{editor_id}", response_class=HTMLResponse)
async def music_type_detail(request: Request, editor_id: str):
    db = get_db()
    info = db.execute(
        "SELECT form_id, editor_id FROM music_types WHERE editor_id = ?",
        (editor_id,),
    ).fetchone()
    if not info:
        return templates.TemplateResponse(request, "music_type.html", {
            "music_type": None, "editor_id": editor_id,
        })

    cells = db.execute("""
        SELECT cell_form_id, cell_name, cell_full_name, track_form_id, track_name
        FROM cell_music
        WHERE music_type = ?
        ORDER BY cell_name
    """, (editor_id,)).fetchall()

    tracks = db.execute("""
        SELECT DISTINCT mk.form_id, mk.editor_id, mk.mtsh
        FROM music_type_tracks mtt
        JOIN music_tracks mk ON mtt.must_form_id = mk.form_id
        WHERE mtt.musc_form_id = ?
    """, (info["form_id"],)).fetchall()

    return templates.TemplateResponse(request, "music_type.html", {
        "music_type": {"form_id": info["form_id"], "editor_id": info["editor_id"]},
        "editor_id": editor_id,
        "cells": cells,
        "tracks": tracks,
    })


@app.get("/track/{editor_id}", response_class=HTMLResponse)
async def track_detail(request: Request, editor_id: str):
    db = get_db()
    info = db.execute(
        "SELECT form_id, editor_id, mtsh FROM music_tracks WHERE editor_id = ?",
        (editor_id,),
    ).fetchone()
    if not info:
        return templates.TemplateResponse(request, "track.html", {
            "track": None, "editor_id": editor_id,
        })

    cells = db.execute("""
        SELECT cell_form_id, cell_name, cell_full_name, music_type_form_id, music_type
        FROM cell_music
        WHERE track_name = ?
        ORDER BY cell_name
    """, (editor_id,)).fetchall()

    music_types = db.execute("""
        SELECT DISTINCT mt.form_id, mt.editor_id
        FROM music_type_tracks mtt
        JOIN music_types mt ON mtt.musc_form_id = mt.form_id
        WHERE mtt.must_form_id = ?
    """, (info["form_id"],)).fetchall()

    ost_match = _get_ost_match(db, editor_id)

    return templates.TemplateResponse(request, "track.html", {
        "track": {"form_id": info["form_id"], "editor_id": info["editor_id"], "mtsh": info["mtsh"]},
        "editor_id": editor_id,
        "cells": cells,
        "music_types": music_types,
        "ost_match": ost_match,
    })


def _has_table(db, table_name: str) -> bool:
    row = db.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name=?",
        (table_name,),
    ).fetchone()
    return row is not None


def _build_ost_lookup(db) -> dict:
    """Build a dict mapping normalized track names to their best OST match."""
    if not _has_table(db, "fingerprint_matches"):
        return {}
    matches = db.execute("""
        SELECT fm.wwise_name, fm.confidence,
               ot.track_number, ot.title
        FROM fingerprint_matches fm
        JOIN ost_tracks ot ON fm.ost_track_number = ot.track_number
        ORDER BY fm.confidence DESC
    """).fetchall()
    clips_dir = Path(__file__).parent / "static" / "clips"
    lookup = {}
    for m in matches:
        key = m["wwise_name"].replace("_", "").rsplit("\\", 1)[-1].rsplit(".", 1)[0]
        if key not in lookup:
            tn = m["track_number"]
            lookup[key] = {
                "title": m["title"],
                "track_number": tn,
                "confidence": m["confidence"],
                "has_clip": (clips_dir / f"{tn:02d}_ost.opus").exists(),
            }
    return lookup


def _get_ost_match(db, track_editor_id: str):
    """Return best OST match for a MUST track editor ID, or None."""
    if not _has_table(db, "fingerprint_matches"):
        return None
    row = db.execute("""
        SELECT fm.wwise_name, fm.confidence,
               ot.track_number, ot.title, ot.duration_secs
        FROM fingerprint_matches fm
        JOIN ost_tracks ot ON fm.ost_track_number = ot.track_number
        WHERE REPLACE(fm.wwise_name, '_', '') LIKE '%' || ? || '%'
        ORDER BY fm.confidence DESC
        LIMIT 1
    """, (track_editor_id,)).fetchone()
    if row is None:
        return None
    tn = row["track_number"]
    clips_dir = Path(__file__).parent / "static" / "clips"
    has_clip = (clips_dir / f"{tn:02d}_ost.opus").exists()
    return {
        "track_number": tn,
        "title": row["title"],
        "confidence": row["confidence"],
        "ost_duration": row["duration_secs"],
        "has_clip": has_clip,
    }


@app.get("/fingerprints", response_class=HTMLResponse)
async def fingerprints_page(request: Request):
    db = get_db()
    if not _has_table(db, "fingerprint_matches"):
        return templates.TemplateResponse(request, "fingerprints.html", {
            "matches": [],
            "matched_count": 0,
            "total_ost": 0,
            "game_count": 0,
            "high_count": 0,
            "medium_count": 0,
            "low_count": 0,
            "unmatched_count": 0,
            "unmatched_game": [],
        })

    rows = db.execute("""
        SELECT ot.track_number, ot.title, ot.duration_secs,
               fm.wwise_id, fm.wwise_name, fm.confidence, fm.game_duration
        FROM ost_tracks ot
        LEFT JOIN fingerprint_matches fm ON ot.track_number = fm.ost_track_number
        ORDER BY COALESCE(fm.confidence, 0) DESC
    """).fetchall()

    matches = []
    for r in rows:
        wwise_name = r["wwise_name"] or ""
        short = wwise_name.rsplit("\\", 1)[-1].replace(".wav", "") if wwise_name else ""
        conf = r["confidence"] or 0
        if conf >= 0.5:
            cls = "high"
        elif conf >= 0.25:
            cls = "medium"
        else:
            cls = "low"
        matches.append({
            "track_number": r["track_number"],
            "title": r["title"],
            "ost_duration": r["duration_secs"] or 0,
            "wwise_id": r["wwise_id"],
            "wwise_name": wwise_name,
            "short_name": short,
            "confidence": conf,
            "confidence_class": cls,
            "game_duration": r["game_duration"] or 0,
            "has_clips": (
                (Path(__file__).parent / "static" / "clips" / f"{r['track_number']:02d}_ost.opus").exists()
                and (Path(__file__).parent / "static" / "clips" / f"{r['track_number']:02d}_game.opus").exists()
            ),
        })

    matched_count = sum(1 for m in matches if m["confidence"] >= 0.15)
    total_ost = len(matches)
    high = sum(1 for m in matches if m["confidence"] >= 0.5)
    medium = sum(1 for m in matches if 0.25 <= m["confidence"] < 0.5)
    low = sum(1 for m in matches if 0.15 <= m["confidence"] < 0.25)
    unmatched = sum(1 for m in matches if m["confidence"] < 0.15)

    matched_wwise_ids = {m["wwise_id"] for m in matches if m["wwise_id"]}
    all_game = db.execute(
        "SELECT DISTINCT wwise_id, wwise_name FROM fingerprint_matches"
    ).fetchall() if _has_table(db, "fingerprint_matches") else []
    game_count = len({r["wwise_id"] for r in all_game})

    unmatched_game_rows = db.execute("""
        SELECT DISTINCT fm.wwise_id, fm.wwise_name
        FROM fingerprint_matches fm
        WHERE fm.wwise_id NOT IN (
            SELECT fm2.wwise_id FROM fingerprint_matches fm2
            JOIN ost_tracks ot ON fm2.ost_track_number = ot.track_number
            GROUP BY fm2.wwise_id
            HAVING MAX(fm2.confidence) >= 0.5
        )
    """).fetchall() if _has_table(db, "fingerprint_matches") else []

    unmatched_game = [{
        "wwise_id": r["wwise_id"],
        "short_name": (r["wwise_name"] or "").rsplit("\\", 1)[-1].replace(".wav", ""),
    } for r in unmatched_game_rows]

    return templates.TemplateResponse(request, "fingerprints.html", {
        "matches": matches,
        "matched_count": matched_count,
        "total_ost": total_ost,
        "game_count": game_count,
        "high_count": high,
        "medium_count": medium,
        "low_count": low,
        "unmatched_count": unmatched,
        "unmatched_game": unmatched_game,
    })


# ---------------------------------------------------------------------------
# JSON API
# ---------------------------------------------------------------------------

@app.get("/api/search")
async def api_search(q: str = Query("", min_length=0)):
    if not q:
        return {"results": []}
    db = get_db()
    like = f"%{q}%"
    rows = db.execute("""
        SELECT DISTINCT cell_form_id, cell_name, cell_full_name, music_type, track_name
        FROM cell_music
        WHERE cell_name LIKE ? OR cell_full_name LIKE ?
              OR music_type LIKE ? OR track_name LIKE ?
        ORDER BY cell_name
        LIMIT 100
    """, (like, like, like, like)).fetchall()
    return {"results": [dict(r) for r in rows]}


@app.get("/api/cells")
async def api_cells(
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=500),
):
    db = get_db()
    total = db.execute("SELECT COUNT(*) FROM cell_music").fetchone()[0]
    rows = db.execute("""
        SELECT cell_form_id, cell_name, music_type_form_id, music_type,
               track_form_id, track_name, track_hash
        FROM cell_music
        ORDER BY cell_name
        LIMIT ? OFFSET ?
    """, (limit, offset)).fetchall()
    return {
        "total": total,
        "offset": offset,
        "limit": limit,
        "results": [dict(r) for r in rows],
    }
