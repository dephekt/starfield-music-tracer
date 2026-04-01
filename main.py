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
    return templates.TemplateResponse(request, "index.html", {
        "rows": rows,
        "total": len(rows),
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

    return templates.TemplateResponse(request, "track.html", {
        "track": {"form_id": info["form_id"], "editor_id": info["editor_id"], "mtsh": info["mtsh"]},
        "editor_id": editor_id,
        "cells": cells,
        "music_types": music_types,
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
