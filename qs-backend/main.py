"""Vercel entrypoint: Vercel's FastAPI detection looks for an `app` object in main.py."""
from qs.api import create_app

app = create_app()
