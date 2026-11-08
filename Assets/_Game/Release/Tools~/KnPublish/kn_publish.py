#!/usr/bin/env python3
"""kn_publish: puts a staged Krytical Nova release on an S3-compatible bucket (Cloudflare R2), and promotes it.

Python standard library only (no boto3, no exe: an unsigned exe gets frozen by Defender / Norton). Plan:
Assets/_Game/_Docs/Plans/own-cdn-replace-ccd-plan.md, Stage 1.

Bucket layout (one bucket, one hostname):
    files/<sha256>.gz            immutable blobs (never overwritten, never deleted by this tool)
    test/manifest.signed         the TEST line
    live/manifest.signed         the LIVE line
    archive/build-<n>.signed     every promoted manifest, kept for rollback
The signed manifest is uploaded LAST, so a launcher never sees a list that names a file that is not there yet.

Commands
    upload  --store <Builds/Release/Store> [--channel test] [--dry-run]
    promote [--from-archive N] [--allow-lower] [--skip-verify]
    verify  --base-url <public url> [--channel live] [--deep]
Settings (flags or environment): endpoint KN_R2_ENDPOINT, bucket KN_R2_BUCKET, key file KN_R2_KEYFILE (a JSON file
{"access_key_id": "...", "secret_access_key": "..."} kept outside the repository). Secrets are never printed.
Exit code 0 = done, 1 = failed. The last line of output is a JSON object for the Editor to read.
"""
import argparse
import concurrent.futures
import datetime
import gzip
import hashlib
import hmac
import http.client
import json
import os
import sys
import re
import time
import urllib.parse

BLOB_CACHE = "public, max-age=31536000, immutable"
MANIFEST_CACHE = "no-cache"
SIGNED = "manifest.signed"
UNSIGNED_PAYLOAD = "UNSIGNED-PAYLOAD"
PARALLEL = 4


class PublishError(Exception):
    pass


# ---------------------------------------------------------------------- AWS Signature Version 4

def _hmac(key, text):
    return hmac.new(key, text.encode("utf-8"), hashlib.sha256).digest()


def _quote(text, safe="/~"):
    return urllib.parse.quote(text, safe=safe)


def sign_headers(method, host, path, query, headers, payload_hash, access_key, secret_key, region, service="s3", amz_date=None):
    """Returns `headers` plus x-amz-date, x-amz-content-sha256 (when `service` is s3) and Authorization."""
    amz_date = amz_date or datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    day = amz_date[:8]
    out = dict(headers)
    out["host"] = host
    out["x-amz-date"] = amz_date
    if service == "s3":
        out["x-amz-content-sha256"] = payload_hash
    lowered = {k.lower(): " ".join(str(v).split()) for k, v in out.items()}
    names = sorted(lowered)
    canonical_headers = "".join("%s:%s\n" % (n, lowered[n]) for n in names)
    signed = ";".join(names)
    canonical_query = "&".join(
        "%s=%s" % (_quote(k, "-_.~"), _quote(v, "-_.~")) for k, v in sorted(urllib.parse.parse_qsl(query, keep_blank_values=True)))
    canonical = "\n".join([method, _quote(path), canonical_query, canonical_headers, signed, payload_hash])
    scope = "%s/%s/%s/aws4_request" % (day, region, service)
    to_sign = "\n".join(["AWS4-HMAC-SHA256", amz_date, scope, hashlib.sha256(canonical.encode("utf-8")).hexdigest()])
    key = _hmac(_hmac(_hmac(_hmac(("AWS4" + secret_key).encode("utf-8"), day), region), service), "aws4_request")
    signature = hmac.new(key, to_sign.encode("utf-8"), hashlib.sha256).hexdigest()
    out["Authorization"] = "AWS4-HMAC-SHA256 Credential=%s/%s, SignedHeaders=%s, Signature=%s" % (access_key, scope, signed, signature)
    return out


# ---------------------------------------------------------------------- a tiny S3 client

class Response:
    def __init__(self, status, headers, body):
        self.status = status
        self.headers = {k.lower(): v for k, v in headers}
        self.body = body


class S3:
    """Path-style S3 requests (works on R2 and on fake_r2). One connection per request, retried on broken links."""

    def __init__(self, endpoint, bucket, access_key, secret_key, region="auto"):
        parsed = urllib.parse.urlsplit(endpoint)
        if parsed.scheme not in ("http", "https") or not parsed.netloc:
            raise PublishError("the endpoint must look like https://<account>.r2.cloudflarestorage.com")
        if parsed.scheme == "http" and parsed.hostname not in ("127.0.0.1", "localhost", "::1"):
            raise PublishError("a plain http endpoint is only allowed on this PC (the stand-in)")
        self.scheme, self.host = parsed.scheme, parsed.netloc
        self.bucket, self.access_key, self.secret_key, self.region = bucket, access_key, secret_key, region

    def _path(self, key):
        return "/%s/%s" % (self.bucket, key)

    def request(self, method, key, body=None, headers=None, payload_hash=None, length=None, retries=3, query=""):
        headers = dict(headers or {})
        data_hash = payload_hash or hashlib.sha256(body or b"").hexdigest()
        if length is not None:
            headers["Content-Length"] = str(length)
        elif body is not None:
            headers["Content-Length"] = str(len(body))
        last = None
        for attempt in range(1, retries + 1):
            signed = sign_headers(method, self.host, self._path(key), query, headers, data_hash, self.access_key, self.secret_key, self.region)
            conn = (http.client.HTTPSConnection if self.scheme == "https" else http.client.HTTPConnection)(self.host, timeout=600)
            try:
                payload = body
                if hasattr(body, "seek"):
                    body.seek(0)
                conn.request(method, _quote(self._path(key)) + ("?" + query if query else ""), body=payload, headers=signed)
                reply = conn.getresponse()
                data = reply.read()
                result = Response(reply.status, reply.getheaders(), data)
                if result.status >= 500 and attempt < retries:
                    last = PublishError("server said %d" % result.status)
                    time.sleep(attempt)
                    continue
                return result
            except (OSError, http.client.HTTPException) as e:
                last = e
                time.sleep(attempt)
            finally:
                conn.close()
        raise PublishError("could not reach the bucket (%s)" % last)

    def head(self, key):
        return self.request("HEAD", key)

    def get(self, key):
        return self.request("GET", key)

    def list_keys(self, prefix):
        """Every key under `prefix` (ListObjectsV2, paged)."""
        import xml.etree.ElementTree as ET
        keys, token = [], None
        while True:
            params = [("list-type", "2"), ("prefix", prefix)] + ([("continuation-token", token)] if token else [])
            query = "&".join("%s=%s" % (_quote(k, "-_.~"), _quote(v, "-_.~")) for k, v in sorted(params))
            reply = self.request("GET", "", query=query)
            if reply.status != 200:
                raise PublishError("listing %s failed: %d" % (prefix, reply.status))
            root = ET.fromstring(reply.body)
            ns = root.tag[:root.tag.index("}") + 1] if root.tag.startswith("{") else ""
            keys += [c.findtext(ns + "Key") for c in root.iter(ns + "Contents")]
            token = root.findtext(ns + "NextContinuationToken")
            if root.findtext(ns + "IsTruncated") != "true" or not token:
                return keys

    def put_bytes(self, key, data, cache, content_type="application/octet-stream", if_none_match=False):
        headers = {"Cache-Control": cache, "Content-Type": content_type}
        if if_none_match:
            headers["If-None-Match"] = "*"
        return self.request("PUT", key, body=data, headers=headers)

    def put_file(self, key, path, cache, if_none_match=True):
        headers = {"Cache-Control": cache, "Content-Type": "application/gzip"}
        if if_none_match:
            headers["If-None-Match"] = "*"
        with open(path, "rb") as f:
            return self.request("PUT", key, body=f, headers=headers, payload_hash=UNSIGNED_PAYLOAD, length=os.path.getsize(path))


# ---------------------------------------------------------------------- the signed manifest

def parse_signed(data):
    """The signed list is one signature line, then the manifest JSON (the exact signed bytes)."""
    newline = data.find(b"\n")
    if newline <= 0 or newline > 200:
        raise PublishError("manifest.signed has no signature line")
    try:
        manifest = json.loads(data[newline + 1:].decode("utf-8"))
    except ValueError:
        raise PublishError("manifest.signed does not hold a readable list")
    if not isinstance(manifest.get("files"), list) or "build" not in manifest:
        raise PublishError("manifest.signed is not an update list")
    return manifest


def blob_entries(manifest):
    """Every distinct blob the list needs: sha256 -> packed size (the game files and the launcher)."""
    entries = {}
    for f in list(manifest["files"]) + ([manifest["launcher"]] if manifest.get("launcher") else []):
        sha = f["sha256"]
        if len(sha) != 64 or any(c not in "0123456789abcdef" for c in sha):
            raise PublishError("the list holds a bad fingerprint: %r" % sha)
        if entries.get(sha, f["packedSize"]) != f["packedSize"]:
            raise PublishError("two entries disagree about the packed size of " + sha)
        entries[sha] = f["packedSize"]
    return entries


def blob_key(sha):
    return "files/%s.gz" % sha


# ---------------------------------------------------------------------- commands

def log(message):
    print(message, flush=True)


def finish(ok, **fields):
    fields["ok"] = ok
    print(json.dumps(fields, sort_keys=True), flush=True)
    return 0 if ok else 1


def cmd_upload(s3, args):
    signed_path = os.path.join(args.store, SIGNED)
    if not os.path.isfile(signed_path):
        raise PublishError("no %s in %s: stage a release first" % (SIGNED, args.store))
    with open(signed_path, "rb") as f:
        signed = f.read()
    manifest = parse_signed(signed)
    entries = blob_entries(manifest)
    build = manifest["build"]
    log("build %s: %d blobs, %.1f MB packed" % (build, len(entries), sum(entries.values()) / 1e6))

    todo, present = [], 0
    for sha, packed in sorted(entries.items()):
        local = os.path.join(args.store, "files", sha + ".gz")
        reply = None if args.dry_run and s3 is None else s3.head(blob_key(sha))
        if reply is not None and reply.status == 200:
            if int(reply.headers.get("content-length", -1)) != packed:
                raise PublishError("%s is in the bucket with another size (%s, the list says %d): blobs are never overwritten"
                                   % (blob_key(sha), reply.headers.get("content-length"), packed))
            present += 1
            continue
        if reply is not None and reply.status != 404:
            raise PublishError("%s: the bucket answered %d" % (blob_key(sha), reply.status))
        if not os.path.isfile(local):
            raise PublishError("%s is neither in the bucket nor in the store" % blob_key(sha))
        if os.path.getsize(local) != packed:
            raise PublishError("%s is %d bytes in the store, the list says %d" % (local, os.path.getsize(local), packed))
        todo.append((sha, local, packed))
    log("%d already there, %d to upload (%.1f MB)" % (present, len(todo), sum(t[2] for t in todo) / 1e6))
    if args.dry_run:
        return finish(True, build=build, dry_run=True, to_upload=len(todo), present=present)

    def one(item):
        sha, local, _ = item
        reply = s3.put_file(blob_key(sha), local, BLOB_CACHE)
        if reply.status == 412:  # somebody else put it between our check and now: fine if it is the same size
            check = s3.head(blob_key(sha))
            if check.status == 200 and int(check.headers.get("content-length", -1)) == os.path.getsize(local):
                return sha
        if reply.status not in (200, 201):
            raise PublishError("uploading %s failed: %d %s" % (blob_key(sha), reply.status, reply.body[:200]))
        return sha

    with concurrent.futures.ThreadPoolExecutor(PARALLEL) as pool:
        for i, _ in enumerate(pool.map(one, todo), 1):
            if i % 10 == 0 or i == len(todo):
                log("  uploaded %d of %d" % (i, len(todo)))

    # Everything the list names is there? Only then does the list itself go up.
    for sha in entries:
        if s3.head(blob_key(sha)).status != 200:
            raise PublishError("%s is missing after the upload: the list was NOT published" % blob_key(sha))
    put_manifest(s3, "archive/build-%d.signed" % build, signed)
    put_manifest(s3, "%s/%s" % (args.channel, SIGNED), signed)
    log("%s line is now build %s" % (args.channel, build))
    return finish(True, build=build, channel=args.channel, uploaded=len(todo), present=present)


def put_manifest(s3, key, data):
    reply = s3.put_bytes(key, data, MANIFEST_CACHE, "application/octet-stream")
    if reply.status not in (200, 201):
        raise PublishError("uploading %s failed: %d" % (key, reply.status))


def cmd_promote(s3, args):
    target = getattr(args, "to", "live") or "live"
    source = "archive/build-%d.signed" % args.from_archive if args.from_archive else "%s/%s" % (getattr(args, "source", "test") or "test", SIGNED)
    if source == "%s/%s" % (target, SIGNED):
        raise PublishError("the line to copy from and the line to copy to are the same")
    reply = s3.get(source)
    if reply.status != 200:
        raise PublishError("cannot read %s (%d): publish to test first" % (source, reply.status))
    signed = reply.body
    manifest = parse_signed(signed)
    build = manifest["build"]
    live = s3.get("%s/%s" % (target, SIGNED))
    if live.status == 200:
        live_build = parse_signed(live.body)["build"]
        if build < live_build and not args.allow_lower:
            raise PublishError("%s is build %d but %s is build %d: going back is a rollback (the rollback command), never a "
                               "promote." % (source, build, target.upper(), live_build))
    elif live.status != 404:
        raise PublishError("cannot read %s/%s (%d)" % (target, SIGNED, live.status))
    if not args.skip_verify:
        missing = [sha for sha in blob_entries(manifest) if s3.head(blob_key(sha)).status != 200]
        if missing:
            raise PublishError("%d files of build %d are missing in the bucket (first: %s): not promoted" % (len(missing), build, blob_key(missing[0])))
    put_manifest(s3, "archive/build-%d.signed" % build, signed)
    put_manifest(s3, "%s/%s" % (target, SIGNED), signed)
    log("%s is now build %d (from %s)" % (target.upper(), build, source))
    return finish(True, build=build, source=source, channel=target)


def cmd_rollback(s3, args):
    """Puts an archived build back on a line. Today's launchers take any build the line names; the v-next launcher (plan Stage 2)
    will refuse a lower number, and then a rollback must be a re-signed copy under a higher number."""
    args.from_archive, args.to, args.allow_lower = args.build, args.channel, True
    return cmd_promote(s3, args)


CHANNELS = ("test", "live", "dots")


def describe(signed, modified=None):
    m = parse_signed(signed)
    files = list(m["files"]) + ([m["launcher"]] if m.get("launcher") else [])
    return {"build": m["build"], "version": m.get("version", ""), "created": m.get("created", ""), "modified": modified or "",
            "files": len(m["files"]), "packed": sum(f["packedSize"] for f in files), "size": sum(f["size"] for f in files)}


def cmd_status(s3, args):
    """What every line holds now, and the archived builds a rollback can use. Last line: JSON for the Publisher app."""
    channels = {}
    for name in CHANNELS:
        reply = s3.get("%s/%s" % (name, SIGNED))
        if reply.status == 404:
            channels[name] = None
            continue
        if reply.status != 200:
            raise PublishError("cannot read %s/%s (%d)" % (name, SIGNED, reply.status))
        channels[name] = describe(reply.body, reply.headers.get("last-modified"))
        log("%-5s build %s  %s  %.1f MB" % (name.upper(), channels[name]["build"], channels[name]["created"], channels[name]["packed"] / 1e6))
    archive = []
    for key in s3.list_keys("archive/"):
        m = re.match(r"archive/build-(\d+)\.signed$", key or "")
        if m:
            archive.append(int(m.group(1)))
    archive.sort(reverse=True)
    return finish(True, channels=channels, archive=archive)


def _public_get(base, path, headers=None):
    parsed = urllib.parse.urlsplit(base)
    conn = (http.client.HTTPSConnection if parsed.scheme == "https" else http.client.HTTPConnection)(parsed.netloc, timeout=600)
    try:
        conn.request("GET", parsed.path.rstrip("/") + "/" + path, headers=headers or {})
        reply = conn.getresponse()
        reply.conn = conn  # the caller closes it with `done(reply)` once the body is read
        return reply.status, {k.lower(): v for k, v in reply.getheaders()}, reply
    except (OSError, http.client.HTTPException) as e:
        conn.close()
        raise PublishError("cannot reach %s (%s)" % (base, e))


def done(reply):
    reply.close()
    reply.conn.close()


def cmd_verify(args):
    """What a launcher would see through the public address: the list, every blob's size, a Range answer, and (--deep) every hash."""
    status, _, reply = _public_get(args.base_url, "%s/%s" % (args.channel, SIGNED))
    data = reply.read()
    done(reply)
    if status != 200:
        raise PublishError("%s/%s answered %d" % (args.channel, SIGNED, status))
    manifest = parse_signed(data)
    entries = blob_entries(manifest)
    problems = []
    for sha, packed in sorted(entries.items()):
        status, headers, reply = _public_get(args.base_url, blob_key(sha), {"Range": "bytes=0-15"})
        body = reply.read()
        done(reply)
        if status != 206 or not headers.get("content-range", "").endswith("/%d" % packed) or len(body) != min(16, packed):
            problems.append("%s: Range probe gave %d %s" % (blob_key(sha), status, headers.get("content-range")))
            continue
        if args.deep:
            status, headers, reply = _public_get(args.base_url, blob_key(sha))
            digest, size = hashlib.sha256(), 0
            try:
                with gzip.GzipFile(fileobj=reply) as unpacked:
                    for chunk in iter(lambda: unpacked.read(1 << 20), b""):
                        digest.update(chunk)
                        size += len(chunk)
            except (OSError, EOFError):
                problems.append("%s: not a readable gzip" % blob_key(sha))
                continue
            finally:
                done(reply)
            if digest.hexdigest() != sha:
                problems.append("%s: content hash differs" % blob_key(sha))
    for p in problems[:20]:
        log("PROBLEM " + p)
    return finish(not problems, build=manifest["build"], checked=len(entries), problems=len(problems), deep=bool(args.deep))


# ---------------------------------------------------------------------- main

def load_client(args):
    endpoint = args.endpoint or os.environ.get("KN_R2_ENDPOINT")
    bucket = args.bucket or os.environ.get("KN_R2_BUCKET")
    key_file = args.key_file or os.environ.get("KN_R2_KEYFILE")
    if not (endpoint and bucket and key_file):
        raise PublishError("set the endpoint, the bucket and the key file (flags or KN_R2_ENDPOINT / KN_R2_BUCKET / KN_R2_KEYFILE)")
    try:
        with open(key_file, "r", encoding="utf-8-sig") as f:
            keys = json.load(f)
        return S3(endpoint, bucket, keys["access_key_id"], keys["secret_access_key"])
    except (OSError, ValueError, KeyError):
        raise PublishError("the key file cannot be read (it must be JSON with access_key_id and secret_access_key)")


def build_parser():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for name in ("--endpoint", "--bucket", "--key-file"):
        parser.add_argument(name)
    sub = parser.add_subparsers(dest="command", required=True)
    up = sub.add_parser("upload")
    up.add_argument("--store", required=True)
    up.add_argument("--channel", default="test", choices=["test", "dots"])
    up.add_argument("--dry-run", action="store_true")
    pr = sub.add_parser("promote")
    pr.add_argument("--from-archive", type=int)
    pr.add_argument("--from", dest="source", default="test", choices=["test", "dots"])
    pr.add_argument("--to", default="live", choices=["live", "test"])
    pr.add_argument("--allow-lower", action="store_true")
    pr.add_argument("--skip-verify", action="store_true")
    rb = sub.add_parser("rollback")
    rb.add_argument("--channel", default="live", choices=list(CHANNELS))
    rb.add_argument("--build", type=int, required=True)
    rb.add_argument("--skip-verify", action="store_true")
    sub.add_parser("status")
    ve = sub.add_parser("verify")
    ve.add_argument("--base-url", required=True)
    ve.add_argument("--channel", default="live", choices=list(CHANNELS))
    ve.add_argument("--deep", action="store_true")
    return parser


def main(argv=None):
    args = build_parser().parse_args(argv)
    try:
        if args.command == "verify":
            return cmd_verify(args)
        if args.command == "upload" and args.dry_run:
            return cmd_upload(None, args)
        client = load_client(args)
        return {"upload": cmd_upload, "promote": cmd_promote, "rollback": cmd_rollback, "status": cmd_status}[args.command](client, args)
    except PublishError as e:
        log("FAILED: %s" % e)
        return finish(False, error=str(e))


if __name__ == "__main__":
    sys.exit(main())
