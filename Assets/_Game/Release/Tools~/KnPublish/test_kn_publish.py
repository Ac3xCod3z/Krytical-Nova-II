"""Tests for kn_publish against the local stand-in bucket (no network, no credentials).
Run:  python -I test_kn_publish.py
"""
import gzip
import hashlib
import io
import json
import os
import shutil
import sys
import tempfile
import unittest
from contextlib import redirect_stdout

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import fake_r2  # noqa: E402
import kn_publish  # noqa: E402


def make_store(folder, build, payloads, launcher=b"LAUNCHER-" + b"x" * 1000):
    """A staged release like ReleaseTool stage writes: files/<sha>.gz and manifest.signed (signature line + JSON)."""
    os.makedirs(os.path.join(folder, "files"), exist_ok=True)
    files = []

    def add(name, raw):
        sha = hashlib.sha256(raw).hexdigest()
        packed = gzip.compress(raw, mtime=0)
        with open(os.path.join(folder, "files", sha + ".gz"), "wb") as f:
            f.write(packed)
        return {"path": name, "size": len(raw), "sha256": sha, "packedSize": len(packed)}

    for name, raw in payloads.items():
        files.append(add(name, raw))
    manifest = {"format": 1, "build": build, "version": "v0.1.0 build %d" % build, "created": "2026-10-29T00:00:00Z",
                "gameExe": "Game.exe", "files": files, "launcher": add("Krytical Nova Launcher.exe", launcher)}
    body = json.dumps(manifest, separators=(",", ":")).encode()
    with open(os.path.join(folder, "manifest.signed"), "wb") as f:
        f.write(b"FAKESIGNATURE==\n" + body)
    return manifest


def run(*argv):
    out = io.StringIO()
    with redirect_stdout(out):
        code = kn_publish.main(list(argv))
    lines = out.getvalue().strip().splitlines()
    return code, json.loads(lines[-1]), out.getvalue()


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="kn_publish_test_")
        self.fake = fake_r2.FakeR2(os.path.join(self.tmp, "bucket")).start()
        self.keyfile = os.path.join(self.tmp, "keys.json")
        with open(self.keyfile, "w") as f:
            json.dump({"access_key_id": fake_r2.ACCESS, "secret_access_key": fake_r2.SECRET}, f)
        self.common = ["--endpoint", self.fake.endpoint, "--bucket", fake_r2.BUCKET, "--key-file", self.keyfile]
        self.store1 = os.path.join(self.tmp, "store1")
        self.m1 = make_store(self.store1, 1, {"a.bin": os.urandom(5000), "b.bin": os.urandom(7000), "same1.txt": b"same" * 100, "same2.txt": b"same" * 100})

    def tearDown(self):
        self.fake.stop()
        shutil.rmtree(self.tmp, ignore_errors=True)

    def cli(self, *rest):
        return run(*self.common, *rest)

    def read(self, key):
        with open(self.fake.path(key), "rb") as f:
            return f.read()


class SigningTests(unittest.TestCase):
    def test_aws_published_vector(self):
        # AWS Signature V4 test suite, "get-vanilla": the signer must reproduce Amazon's own signature.
        out = kn_publish.sign_headers("GET", "example.amazonaws.com", "/", "", {}, hashlib.sha256(b"").hexdigest(),
                                      "AKIDEXAMPLE", "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY", "us-east-1", "service", "20150830T123600Z")
        self.assertTrue(out["Authorization"].endswith("Signature=5fa00fa31553b73ebf1942676e86291e8372ff2a2260956d9b8aae1d763fbf31"), out["Authorization"])


class UploadTests(Base):
    def test_upload_puts_blobs_then_manifest_last(self):
        code, result, _ = self.cli("upload", "--store", self.store1)
        self.assertEqual(code, 0, result)
        puts = [k for m, k in self.fake.ops if m == "PUT"]
        self.assertEqual(puts[-2:], ["archive/build-1.signed", "test/manifest.signed"])
        self.assertTrue(all(k.startswith("files/") for k in puts[:-2]))
        self.assertEqual(len([k for k in puts if k.startswith("files/")]), 4)
        self.assertEqual(self.fake.bad_signatures, 0)
        self.assertEqual(self.read("test/manifest.signed"), open(os.path.join(self.store1, "manifest.signed"), "rb").read())
        self.assertFalse(os.path.exists(self.fake.path("live/manifest.signed")))

    def test_identical_files_upload_once(self):
        self.cli("upload", "--store", self.store1)
        blobs = [k for m, k in self.fake.ops if m == "PUT" and k.startswith("files/")]
        self.assertEqual(len(blobs), len(set(blobs)))
        self.assertEqual(len(blobs), 4)  # a, b, the shared text, the launcher

    def test_second_upload_sends_only_new_files(self):
        self.cli("upload", "--store", self.store1)
        store2 = os.path.join(self.tmp, "store2")
        shutil.copytree(self.store1, store2)
        new = make_store(os.path.join(self.tmp, "s2tmp"), 2, {"c.bin": os.urandom(3000)})
        # the second build keeps the first build's a.bin and adds c.bin
        shutil.copytree(os.path.join(self.tmp, "s2tmp", "files"), os.path.join(store2, "files"), dirs_exist_ok=True)
        keep = [f for f in self.m1["files"] if f["path"] in ("a.bin", "b.bin")] + [f for f in new["files"] if f["path"] == "c.bin"]
        manifest = dict(self.m1, build=2, files=keep, launcher=self.m1["launcher"])
        with open(os.path.join(store2, "manifest.signed"), "wb") as f:
            f.write(b"FAKESIGNATURE==\n" + json.dumps(manifest, separators=(",", ":")).encode())
        before = len([1 for m, k in self.fake.ops if m == "PUT" and k.startswith("files/")])
        code, result, _ = self.cli("upload", "--store", store2)
        self.assertEqual(code, 0, result)
        after = len([1 for m, k in self.fake.ops if m == "PUT" and k.startswith("files/")])
        self.assertEqual(after - before, 1)
        self.assertEqual(result["uploaded"], 1)

    def test_blob_in_bucket_with_other_size_is_refused_and_never_overwritten(self):
        sha = self.m1["files"][0]["sha256"]
        os.makedirs(os.path.dirname(self.fake.path("files/x")), exist_ok=True)
        with open(self.fake.path("files/%s.gz" % sha), "wb") as f:
            f.write(b"WRONG")
        code, result, _ = self.cli("upload", "--store", self.store1)
        self.assertEqual(code, 1)
        self.assertIn("never overwritten", result["error"])
        self.assertEqual(self.read("files/%s.gz" % sha), b"WRONG")
        self.assertFalse(os.path.exists(self.fake.path("test/manifest.signed")))

    def test_missing_local_blob_stops_before_the_manifest(self):
        os.remove(os.path.join(self.store1, "files", self.m1["files"][1]["sha256"] + ".gz"))
        code, result, _ = self.cli("upload", "--store", self.store1)
        self.assertEqual(code, 1)
        self.assertFalse(os.path.exists(self.fake.path("test/manifest.signed")))

    def test_one_server_hiccup_is_retried(self):
        self.fake.fail_puts = 1
        code, result, _ = self.cli("upload", "--store", self.store1)
        self.assertEqual(code, 0, result)

    def test_dead_server_fails_without_publishing_the_manifest(self):
        self.fake.fail_puts = 1000
        code, result, _ = self.cli("upload", "--store", self.store1)
        self.assertEqual(code, 1)
        self.assertFalse(os.path.exists(self.fake.path("test/manifest.signed")))

    def test_dry_run_touches_no_network(self):
        self.fake.stop()
        code, result, _ = run("upload", "--store", self.store1, "--dry-run")
        self.assertEqual(code, 0)
        self.assertEqual(result["to_upload"], 4)
        self.fake = fake_r2.FakeR2(os.path.join(self.tmp, "b2")).start()  # for tearDown

    def test_wrong_secret_is_rejected_by_the_bucket(self):
        with open(self.keyfile, "w") as f:
            json.dump({"access_key_id": fake_r2.ACCESS, "secret_access_key": "wrong"}, f)
        code, result, _ = self.cli("upload", "--store", self.store1)
        self.assertEqual(code, 1)
        self.assertGreater(self.fake.bad_signatures, 0)

    def test_secrets_are_never_printed(self):
        _, _, text = self.cli("upload", "--store", self.store1)
        self.assertNotIn(fake_r2.SECRET, text)


class PromoteTests(Base):
    def test_promote_copies_test_to_live_and_archives(self):
        self.cli("upload", "--store", self.store1)
        code, result, _ = self.cli("promote")
        self.assertEqual(code, 0, result)
        self.assertEqual(self.read("live/manifest.signed"), self.read("test/manifest.signed"))
        self.assertEqual(self.read("archive/build-1.signed"), self.read("test/manifest.signed"))

    def test_promote_refuses_when_a_file_is_missing(self):
        self.cli("upload", "--store", self.store1)
        os.remove(self.fake.path("files/%s.gz" % self.m1["files"][0]["sha256"]))
        code, result, _ = self.cli("promote")
        self.assertEqual(code, 1)
        self.assertFalse(os.path.exists(self.fake.path("live/manifest.signed")))

    def test_promote_without_a_test_release_fails(self):
        code, result, _ = self.cli("promote")
        self.assertEqual(code, 1)

    def _two_builds(self):
        self.cli("upload", "--store", self.store1)
        self.cli("promote")
        store2 = os.path.join(self.tmp, "store2")
        make_store(store2, 2, {"a.bin": os.urandom(4000)})
        self.cli("upload", "--store", store2)

    def test_lower_build_is_refused_unless_asked(self):
        self._two_builds()
        self.cli("promote")  # live = 2
        code, result, _ = self.cli("promote", "--from-archive", "1")
        self.assertEqual(code, 1)
        self.assertIn("rollback", result["error"])
        self.assertEqual(kn_publish.parse_signed(self.read("live/manifest.signed"))["build"], 2)

    def test_rollback_from_archive_with_allow_lower(self):
        self._two_builds()
        self.cli("promote")
        code, result, _ = self.cli("promote", "--from-archive", "1", "--allow-lower")
        self.assertEqual(code, 0, result)
        self.assertEqual(kn_publish.parse_signed(self.read("live/manifest.signed"))["build"], 1)

    def test_old_blobs_stay_for_the_old_build(self):
        self._two_builds()
        for sha in kn_publish.blob_entries(self.m1):
            self.assertTrue(os.path.isfile(self.fake.path("files/%s.gz" % sha)))


class PublisherAppTests(Base):
    """The commands the Publisher app drives: dots line, status, rollback, promote between lines."""

    def test_status_shows_every_line_and_the_archive(self):
        self.cli("upload", "--store", self.store1)
        code, out, _ = self.cli("status")
        self.assertEqual(code, 0, out)
        self.assertEqual(out["channels"]["test"]["build"], 1)
        self.assertIsNone(out["channels"]["live"])
        self.assertIsNone(out["channels"]["dots"])
        self.assertEqual(out["archive"], [1])
        self.assertTrue(out["channels"]["test"]["modified"])
        self.assertGreater(out["channels"]["test"]["packed"], 0)

    def test_dots_line_is_its_own(self):
        code, out, _ = self.cli("upload", "--store", self.store1, "--channel", "dots")
        self.assertEqual(code, 0, out)
        _, status, _ = self.cli("status")
        self.assertEqual(status["channels"]["dots"]["build"], 1)
        self.assertIsNone(status["channels"]["test"])

    def test_rollback_puts_an_older_build_back_on_live(self):
        store2 = os.path.join(self.tmp, "store2")
        make_store(store2, 2, {"a.bin": os.urandom(3000)})
        self.cli("upload", "--store", self.store1)
        self.cli("promote")
        self.cli("upload", "--store", store2)
        code, out, _ = self.cli("promote")
        self.assertEqual((code, out["build"]), (0, 2))
        code, out, _ = self.cli("rollback", "--channel", "live", "--build", "1")
        self.assertEqual(code, 0, out)
        _, status, _ = self.cli("status")
        self.assertEqual(status["channels"]["live"]["build"], 1)
        self.assertEqual(status["channels"]["test"]["build"], 2)
        self.assertEqual(status["archive"], [2, 1])

    def test_rollback_to_a_build_never_archived_fails(self):
        self.cli("upload", "--store", self.store1)
        code, out, _ = self.cli("rollback", "--build", "77")
        self.assertEqual(code, 1)
        self.assertIn("archive/build-77", out["error"])

    def test_promote_refuses_same_line(self):
        self.cli("upload", "--store", self.store1)
        code, out, _ = self.cli("promote", "--from", "test", "--to", "test")
        self.assertEqual(code, 1)


class VerifyAndRangeTests(Base):
    def setUp(self):
        super().setUp()
        self.cli("upload", "--store", self.store1)
        self.cli("promote")

    def test_verify_deep_passes(self):
        code, result, _ = run("verify", "--base-url", self.fake.public, "--channel", "live", "--deep")
        self.assertEqual(code, 0, result)
        self.assertEqual(result["checked"], 4)

    def test_verify_catches_a_corrupted_blob(self):
        target = self.fake.path("files/%s.gz" % self.m1["files"][0]["sha256"])
        data = bytearray(open(target, "rb").read())
        data[len(data) // 2] ^= 0xFF
        open(target, "wb").write(bytes(data))
        code, result, _ = run("verify", "--base-url", self.fake.public, "--channel", "live", "--deep")
        self.assertEqual(code, 1)
        self.assertGreater(result["problems"], 0)

    def test_range_resume_rebuilds_the_exact_file(self):
        import http.client
        sha = self.m1["files"][0]["sha256"]
        whole = self.read("files/%s.gz" % sha)
        cut = len(whole) // 3
        conn = http.client.HTTPConnection("127.0.0.1", self.fake.port)
        conn.request("GET", "/pub/files/%s.gz" % sha, headers={"Range": "bytes=%d-" % cut})
        reply = conn.getresponse()
        rest = reply.read()
        self.assertEqual(reply.status, 206)
        self.assertEqual(reply.getheader("Content-Range"), "bytes %d-%d/%d" % (cut, len(whole) - 1, len(whole)))
        self.assertEqual(whole[:cut] + rest, whole)
        self.assertEqual(gzip.decompress(whole[:cut] + rest) and hashlib.sha256(gzip.decompress(whole[:cut] + rest)).hexdigest(), sha)

    def test_public_address_cannot_write(self):
        import http.client
        conn = http.client.HTTPConnection("127.0.0.1", self.fake.port)
        conn.request("PUT", "/pub/files/evil.gz", body=b"x", headers={"Content-Length": "1"})
        self.assertEqual(conn.getresponse().status, 403)

    def test_unsigned_write_is_refused(self):
        import http.client
        conn = http.client.HTTPConnection("127.0.0.1", self.fake.port)
        conn.request("PUT", "/%s/live/manifest.signed" % fake_r2.BUCKET, body=b"x", headers={"Content-Length": "1"})
        self.assertEqual(conn.getresponse().status, 403)

    def test_locked_prefix_refuses_overwrite_and_delete(self):
        s3 = kn_publish.S3(self.fake.endpoint, fake_r2.BUCKET, fake_r2.ACCESS, fake_r2.SECRET)
        key = "files/%s.gz" % self.m1["files"][0]["sha256"]
        self.assertEqual(s3.put_bytes(key, b"evil", "no-cache").status, 409)
        self.assertEqual(s3.request("DELETE", key).status, 409)


if __name__ == "__main__":
    unittest.main()
