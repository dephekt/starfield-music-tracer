"""Export the FastAPI app as a static site."""

import os
import re
import shutil
import sqlite3
from pathlib import Path

from starlette.testclient import TestClient

from main import app, DB_PATH

SITE_DIR = Path(__file__).parent / "_site"
BASE_PATH = os.environ.get("SITE_BASE_PATH", "").rstrip("/")


def get_all_routes(db: sqlite3.Connection) -> list[str]:
    """Enumerate every HTML route that needs to be frozen."""
    routes = ["/", "/fingerprints"]

    cells = db.execute("SELECT DISTINCT cell_name FROM cell_music").fetchall()
    routes.extend(f"/cell/{r[0]}" for r in cells)

    music_types = db.execute("SELECT DISTINCT editor_id FROM music_types").fetchall()
    routes.extend(f"/music-type/{r[0]}" for r in music_types)

    tracks = db.execute("SELECT DISTINCT editor_id FROM music_tracks").fetchall()
    routes.extend(f"/track/{r[0]}" for r in tracks)

    return routes


def rewrite_paths(html: str, base: str) -> str:
    """Rewrite absolute paths to include the base path prefix."""
    if not base:
        return html
    html = re.sub(r'(href|src|action)="/', rf'\1="{base}/', html)
    html = re.sub(r"(href|src|action)='/", rf"\1='{base}/", html)
    return html


def freeze():
    if SITE_DIR.exists():
        shutil.rmtree(SITE_DIR)
    SITE_DIR.mkdir()

    db = sqlite3.connect(f"file:{DB_PATH}?mode=ro", uri=True)
    routes = get_all_routes(db)
    db.close()

    with TestClient(app) as client:
        for route in routes:
            resp = client.get(route)
            if resp.status_code != 200:
                print(f"  SKIP {route} ({resp.status_code})")
                continue

            html = rewrite_paths(resp.text, BASE_PATH)

            if route == "/":
                out = SITE_DIR / "index.html"
            else:
                out = SITE_DIR / route.lstrip("/") / "index.html"

            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_text(html)

    static_src = Path(__file__).parent / "static"
    static_dst = SITE_DIR / "static"
    if static_src.exists():
        shutil.copytree(static_src, static_dst)

    total_files = sum(1 for _ in SITE_DIR.rglob("*") if _.is_file())
    print(f"Frozen {len(routes)} pages + static assets to {SITE_DIR}")
    print(f"Total files: {total_files}")


if __name__ == "__main__":
    freeze()
