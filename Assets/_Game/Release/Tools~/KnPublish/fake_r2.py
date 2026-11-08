#!/usr/bin/env python3
"""fake_r2: a stand-in for the R2 bucket, on this PC only, for testing kn_publish without any account or network.

It checks every request's AWS Signature V4 (so a wrong signer fails here, not at Cloudflare), keeps objects in a folder,
answers PUT / GET / HEAD / DELETE, `If-None-Match: *`, `Range`, and refuses to overwrite or delete anything under the
locked prefix `files/` (as the R2 bucket lock does). `GET /pub/<key>` is the public address (no signature), like the
custom domain. Run alone:  python fake_r2.py --root <folder> --port 9000
"""
import argparse
import email.utils
import hashlib
import os
import re
import sys
import threading
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kn_publish  # noqa: E402

BUCKET = "kn-game"
ACCESS = "TESTACCESSKEY"
SECRET = "test/secret+key"


class FakeR2:
    def __init__(self, root, bucket=BUCKET, access=ACCESS, secret=SECRET, locked=("files/",)):
        self.root, self.bucket, self.access, self.secret, self.locked = root, bucket, access, secret, tuple(locked)
        self.ops = []              # (method, key) in arrival order
        self.fail_puts = 0         # the next N PUTs answer 503
        self.bad_signatures = 0
        self._lock = threading.Lock()
        os.makedirs(root, exist_ok=True)
        owner = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def do_GET(self): owner.handle(self, "GET")
            def do_HEAD(self): owner.handle(self, "HEAD")
            def do_PUT(self): owner.handle(self, "PUT")
            def do_DELETE(self): owner.handle(self, "DELETE")

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.port = self.server.server_address[1]
        self.endpoint = "http://127.0.0.1:%d" % self.port
        self.public = self.endpoint + "/pub"

    def start(self):
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        return self

    def stop(self):
        self.server.shutdown()
        self.server.server_close()

    def path(self, key):
        return os.path.join(self.root, *key.split("/"))

    def list(self, h, query):
        """ListObjectsV2 (prefix only, one page): enough for kn_publish's status."""
        prefix = (query.get("prefix") or [""])[0]
        keys = []
        for folder, _, names in os.walk(self.root):
            for name in names:
                if ".part" in name:
                    continue
                key = os.path.relpath(os.path.join(folder, name), self.root).replace(os.sep, "/")
                if key.startswith(prefix):
                    keys.append(key)
        body = ('<?xml version="1.0" encoding="UTF-8"?><ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">'
                "<Name>%s</Name><Prefix>%s</Prefix><IsTruncated>false</IsTruncated>%s</ListBucketResult>"
                % (self.bucket, prefix, "".join("<Contents><Key>%s</Key></Contents>" % k for k in sorted(keys)))).encode("utf-8")
        return self.reply(h, 200, body, {"Content-Type": "application/xml"})

    # -------------------------------------------------------------- request handling

    def reply(self, h, status, body=b"", headers=None, head=False):
        h.send_response(status)
        for k, v in (headers or {}).items():
            h.send_header(k, v)
        h.send_header("Content-Length", str(len(body)))
        h.end_headers()
        if body and not head:
            h.wfile.write(body)

    def authorised(self, h, method, path):
        auth = h.headers.get("Authorization", "")
        m = re.match(r"AWS4-HMAC-SHA256 Credential=([^/]+)/(\d{8})/([^/]+)/s3/aws4_request, SignedHeaders=([^,]+), Signature=([0-9a-f]+)$", auth)
        if not m or m.group(1) != self.access:
            return False
        names = m.group(4).split(";")
        received = {n: h.headers.get(n) for n in names}
        if any(v is None for v in received.values()):
            return False
        expected = kn_publish.sign_headers(method, received["host"], path, urllib.parse.urlsplit(h.path).query, received,
                                           received["x-amz-content-sha256"], self.access, self.secret, m.group(3), "s3", received["x-amz-date"])
        return expected["Authorization"] == auth

    def handle(self, h, method):
        parsed = urllib.parse.urlsplit(h.path)
        path = urllib.parse.unquote(parsed.path)
        public = path.startswith("/pub/")
        if public:
            if method not in ("GET", "HEAD"):
                return self.reply(h, 403)
            key = path[len("/pub/"):]
        else:
            if not path.startswith("/%s/" % self.bucket):
                return self.reply(h, 404)
            key = path[len("/%s/" % self.bucket):]
            if not self.authorised(h, method, urllib.parse.quote(path, safe="/~")):
                with self._lock:
                    self.bad_signatures += 1
                return self.reply(h, 403, b"SignatureDoesNotMatch")
        if not public and not key and method == "GET":
            return self.list(h, urllib.parse.parse_qs(parsed.query))
        if ".." in key.split("/") or not key:
            return self.reply(h, 400)
        with self._lock:
            self.ops.append((method, key))
        target = self.path(key)

        if method == "PUT":
            length = int(h.headers.get("Content-Length", 0))
            with self._lock:
                if self.fail_puts > 0:
                    self.fail_puts -= 1
                    h.rfile.read(length)
                    return self.reply(h, 503)
            exists = os.path.isfile(target)
            digest = hashlib.sha256()
            os.makedirs(os.path.dirname(target), exist_ok=True)
            part = target + ".part%d" % threading.get_ident()
            with open(part, "wb") as f:
                left = length
                while left > 0:
                    chunk = h.rfile.read(min(1 << 20, left))
                    if not chunk:
                        break
                    digest.update(chunk)
                    f.write(chunk)
                    left -= len(chunk)
            declared = h.headers.get("x-amz-content-sha256")
            if left or (declared != kn_publish.UNSIGNED_PAYLOAD and declared != digest.hexdigest()):
                os.remove(part)
                return self.reply(h, 400, b"BadDigest")
            if exists and h.headers.get("If-None-Match") == "*":
                os.remove(part)
                return self.reply(h, 412, b"PreconditionFailed")
            if exists and key.startswith(self.locked):
                os.remove(part)
                return self.reply(h, 409, b"ObjectLocked")
            os.replace(part, target)
            return self.reply(h, 200, b"", {"ETag": '"%s"' % digest.hexdigest()[:32]})

        if method == "DELETE":
            if key.startswith(self.locked):
                return self.reply(h, 409, b"ObjectLocked")
            if os.path.isfile(target):
                os.remove(target)
            return self.reply(h, 204)

        # GET / HEAD
        if not os.path.isfile(target):
            return self.reply(h, 404, b"NoSuchKey", head=method == "HEAD")
        size = os.path.getsize(target)
        headers = {"Accept-Ranges": "bytes", "Last-Modified": email.utils.formatdate(os.path.getmtime(target), usegmt=True)}
        start, end, status = 0, size - 1, 200
        rng = h.headers.get("Range")
        if rng:
            m = re.match(r"bytes=(\d*)-(\d*)$", rng)
            if not m or (not m.group(1) and not m.group(2)):
                return self.reply(h, 416)
            if m.group(1):
                start = int(m.group(1))
                end = min(int(m.group(2)), size - 1) if m.group(2) else size - 1
            else:
                start = max(0, size - int(m.group(2)))
            if start >= size or start > end:
                return self.reply(h, 416, headers={"Content-Range": "bytes */%d" % size})
            status = 206
            headers["Content-Range"] = "bytes %d-%d/%d" % (start, end, size)
        count = end - start + 1
        h.send_response(status)
        for k, v in headers.items():
            h.send_header(k, v)
        h.send_header("Content-Length", str(count))
        h.end_headers()
        if method == "GET":
            with open(target, "rb") as f:
                f.seek(start)
                left = count
                while left > 0:
                    chunk = f.read(min(1 << 20, left))
                    if not chunk:
                        break
                    h.wfile.write(chunk)
                    left -= len(chunk)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--port", type=int, default=0)
    args = parser.parse_args()
    fake = FakeR2(args.root)
    fake.server.server_close()
    fake.server = ThreadingHTTPServer(("127.0.0.1", args.port), fake.server.RequestHandlerClass)
    fake.port = fake.server.server_address[1]
    fake.endpoint = "http://127.0.0.1:%d" % fake.port
    print("fake R2 on %s  bucket=%s  access=%s  public=%s/pub" % (fake.endpoint, BUCKET, ACCESS, fake.endpoint), flush=True)
    fake.server.serve_forever()
