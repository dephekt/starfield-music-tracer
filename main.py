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
        SELECT cell_form_id, cell_name, music_type_form_id, music_type,
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
        SELECT cell_form_id, cell_name, music_type_form_id, music_type,
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
        SELECT DISTINCT cell_form_id, cell_name, music_type, track_name
        FROM cell_music
        WHERE cell_name LIKE ? OR music_type LIKE ? OR track_name LIKE ?
        ORDER BY cell_name
        LIMIT 100
    """, (like, like, like)).fetchall()
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
