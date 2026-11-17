"""Loopback-only preview origin for Tailscale Serve; no project/source exposure."""
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

BASE = Path(__file__).resolve().parent
ROOTS = {'crucible': BASE / 'Web', 'viewer-lib': BASE.parent / 'NewMeridian' / 'viewer-lib'}
EXTENSIONS = {'.html', '.js', '.css', '.png', '.jpg', '.jpeg', '.webp', '.gif', '.glb', '.json', '.ico'}

def preview_path(url):
    parts = unquote(urlsplit(url).path).strip('/').split('/')
    if not parts or parts[0] not in ROOTS or any(p in ('.', '..') or '\\' in p for p in parts):
        return None
    root = ROOTS[parts[0]].resolve()
    path = root.joinpath(*parts[1:]).resolve()
    if not path.is_relative_to(root) or path.suffix.lower() not in EXTENSIONS or not path.is_file():
        return None
    if path.suffix == '.glb' and path.name != 'scene-review.glb':
        return None
    if path.suffix == '.json' and path.name != 'scene-review.json':
        return None
    return path

class Preview(SimpleHTTPRequestHandler):
    def send_head(self):
        if urlsplit(self.path).path in ('/', '/crucible', '/crucible/'):
            self.send_response(302)
            self.send_header('Location', '/crucible/review-3d.html')
            self.end_headers()
            return None
        path = preview_path(self.path)
        if path is None:
            self.send_error(404)
            return None
        return super().send_head()

    def translate_path(self, path):
        return str(preview_path(path) or BASE / '__not_served__')

    def end_headers(self):
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Cache-Control', 'no-cache')
        super().end_headers()

    def list_directory(self, path):
        self.send_error(404)
        return None

if __name__ == '__main__':
    ThreadingHTTPServer(('127.0.0.1', 8769), Preview).serve_forever()

