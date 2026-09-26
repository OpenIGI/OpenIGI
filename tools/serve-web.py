"""Serves the browser build, and an installation beside it, for development.

    python tools/serve-web.py <game folder> [--port 8780] [--app <wwwroot>]

The page is at http://127.0.0.1:<port>/ and reads the installation from /data/ when opened as
http://127.0.0.1:<port>/?data=/data/ -- the route an automated run takes, since nothing there can click
a folder picker. /data/manifest.json lists the installation's files and sizes; it is made on request.

<wwwroot> defaults to the publish output of apps/OpenIGI.Web (dotnet publish apps/OpenIGI.Web -c Release).
Where the publish wrote a .br or .gz beside a file, that is what is sent, with its Content-Encoding.
"""
import argparse
import functools
import http.server
import json
import os
import posixpath
import urllib.parse

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_APP = os.path.join(ROOT, "apps", "OpenIGI.Web", "bin", "Release", "net10.0", "publish", "wwwroot")


def manifest(game):
    files = []
    for folder, _, names in os.walk(game):
        for name in names:
            path = os.path.join(folder, name)
            files.append({"path": os.path.relpath(path, game).replace(os.sep, "/"), "size": os.path.getsize(path)})
    return json.dumps(files).encode("utf-8")


class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map,
                      ".js": "text/javascript", ".mjs": "text/javascript", ".wasm": "application/wasm"}

    def __init__(self, *args, app, game, **kwargs):
        self.game = game
        super().__init__(*args, directory=app, **kwargs)

    def translate_path(self, path):
        path = urllib.parse.unquote(urllib.parse.urlsplit(path).path)
        if path.startswith("/data/"):
            parts = [p for p in posixpath.normpath(path[len("/data/"):]).split("/") if p not in ("", ".", "..")]
            return os.path.join(self.game, *parts)
        return super().translate_path(path)

    def do_GET(self):
        if urllib.parse.urlsplit(self.path).path == "/data/manifest.json":
            body = manifest(self.game)
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if self.send_precompressed():
            return
        super().do_GET()

    def send_precompressed(self):
        """Sends the publish's .br or .gz of a file where the browser takes it, as a real host would.

        The runtime is some forty megabytes uncompressed and a fifth of that in Brotli, so serving the raw
        files made every load in development slower than any deployment would be.
        """
        path = urllib.parse.unquote(urllib.parse.urlsplit(self.path).path)
        if path.startswith("/data/"):
            return False

        local = self.translate_path(self.path)
        if not os.path.isfile(local):
            return False

        accepted = self.headers.get("Accept-Encoding", "")
        for encoding, suffix in (("br", ".br"), ("gzip", ".gz")):
            if encoding in accepted and os.path.isfile(local + suffix):
                with open(local + suffix, "rb") as source:
                    body = source.read()
                self.send_response(200)
                self.send_header("Content-Type", self.guess_type(local))
                self.send_header("Content-Encoding", encoding)
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Vary", "Accept-Encoding")
                self.end_headers()
                self.wfile.write(body)
                return True

        return False

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format, *args):
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("game")
    parser.add_argument("--port", type=int, default=8780)
    parser.add_argument("--app", default=DEFAULT_APP)
    options = parser.parse_args()

    handler = functools.partial(Handler, app=options.app, game=options.game)
    # The page reads an installation's files many at a time, and the listen backlog's default of five
    # refused the overflow on Windows: a load that failed partway with the connection refused.
    http.server.ThreadingHTTPServer.request_queue_size = 256
    server = http.server.ThreadingHTTPServer(("127.0.0.1", options.port), handler)
    print(f"http://127.0.0.1:{options.port}/?data=/data/")
    server.serve_forever()


if __name__ == "__main__":
    main()
