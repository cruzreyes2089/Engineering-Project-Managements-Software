import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest
import tempfile
from unittest.mock import patch
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("payload", ROOT / "tools/release/validate_install_payload.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class InstallPayloadLayoutTests(unittest.TestCase):
    def setUp(self):
        self.inventory = {"format": "CRC-Development-Build-Inventory-v1",
                          "architecture": "win-x64", "files": [
                              {"path": p, "bytes": 128, "sha256": "a"*64}
                              for p in sorted(m.REQUIRED)]}
        self.totals()

    def totals(self):
        self.inventory["fileCount"] = len(self.inventory["files"])
        self.inventory["bytes"] = sum(x["bytes"] for x in self.inventory["files"])

    def validate(self):
        raw = json.dumps(self.inventory).encode()
        return m.validate(raw, hashlib.sha256(raw).hexdigest())

    def add(self, path):
        self.inventory["files"].append({"path": path, "bytes": 128, "sha256": "b"*64})
        self.totals()

    def test_expected_runtime_layout_never_authorizes_release(self):
        for path in ("createdump.exe", "CRC.PracticeManager.pdb", "fr/PresentationFramework.resources.dll"):
            self.add(path)
        result = self.validate()
        self.assertTrue(result["layoutValid"])
        for field in ("artifactBytesIndependentlyVerified", "installerExecuted", "securityApproved", "releaseAuthorized"):
            self.assertIs(result[field], False)

    def test_missing_runtime_or_app(self):
        original = copy.deepcopy(self.inventory)
        for name in m.REQUIRED:
            with self.subTest(name=name):
                self.inventory = copy.deepcopy(original)
                self.inventory["files"] = [x for x in self.inventory["files"] if x["path"] != name]
                self.totals()
                with self.assertRaises(ValueError):
                    self.validate()

    def test_unsafe_paths_and_unexpected_payload(self):
        original = copy.deepcopy(self.inventory)
        for path in ("../extra.dll", "/root.dll", "C:/root.dll", "a\\b.dll", "AUX.dll",
                     "NUL/extra.dll", "a./b.dll", "a//b.dll", "data.db", "settings.json",
                     "app.config", "install.ps1", "extra.exe", "extra.pdb", "a.dll:stream",
                     "a /b.dll", "a/../b.dll"):
            with self.subTest(path=path):
                self.inventory = copy.deepcopy(original)
                self.add(path)
                with self.assertRaises(ValueError):
                    self.validate()

    def test_case_alias_duplicate_and_file_directory_collision(self):
        original = copy.deepcopy(self.inventory)
        for paths in (("CRC.PracticeManager.exe",), ("crc.practicemanager.exe",),
                      ("en/a.dll", "EN/b.dll"), ("hostfxr.dll/a.dll",)):
            with self.subTest(paths=paths):
                self.inventory = copy.deepcopy(original)
                for path in paths:
                    self.add(path)
                with self.assertRaises(ValueError):
                    self.validate()

    def test_counts_hashes_and_types(self):
        original = copy.deepcopy(self.inventory)
        for key, value in (("fileCount", True), ("fileCount", 1), ("bytes", 0),
                           ("architecture", "win-arm64"), ("format", "unknown")):
            with self.subTest(key=key, value=value):
                self.inventory = copy.deepcopy(original)
                self.inventory[key] = value
                with self.assertRaises(ValueError):
                    self.validate()
        for key, value in (("bytes", False), ("bytes", -1), ("bytes", m.MAX_FILE+1),
                           ("sha256", "A"*64), ("sha256", None)):
            with self.subTest(key=key, value=value):
                self.inventory = copy.deepcopy(original)
                self.inventory["files"][0][key] = value
                with self.assertRaises(ValueError):
                    self.validate()

    def test_exact_bytes_duplicate_json_and_limit(self):
        raw = json.dumps(self.inventory).encode()
        with self.assertRaises(ValueError):
            m.validate(raw, "0"*64)
        duplicate = b'{"format":"one","format":"two"}'
        with self.assertRaises(ValueError):
            m.validate(duplicate, hashlib.sha256(duplicate).hexdigest())
        oversized = b" " * (m.LIMIT+1)
        with self.assertRaises(ValueError):
            m.validate(oversized, hashlib.sha256(oversized).hexdigest())

class InstallPayloadByteTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve() / "payload"
        self.root.mkdir()
        rows = []
        for name in sorted(m.REQUIRED | {"fr/PresentationFramework.resources.dll"}):
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            data = ("Synthetic text fixture " + name).encode()
            path.write_bytes(data)
            rows.append({"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
        self.raw = json.dumps({"format": "CRC-Development-Build-Inventory-v1",
                               "architecture": "win-x64", "files": rows,
                               "fileCount": len(rows), "bytes": sum(x["bytes"] for x in rows)}).encode()
        self.hash = hashlib.sha256(self.raw).hexdigest()

    def verify(self):
        return m.verify_payload(self.raw, self.hash, self.root)

    def test_exact_payload_bytes(self):
        result = self.verify()
        self.assertTrue(result["payloadBytesMatchInventory"])
        self.assertFalse(result["securityApproved"])
        self.assertFalse(result["releaseAuthorized"])
        self.assertFalse(result["installerExecuted"])
        self.assertFalse(result["artifactBytesIndependentlyVerified"])

    def test_nsis_source_includes_only_exact_verified_files(self):
        source = m.render_nsis_payload_source(self.raw, self.hash, self.root)
        rows = json.loads(self.raw)["files"]
        expected = ['  File "${CRC_PAYLOAD_ROOT}\\' + row["path"].replace("/", "\\") + '"'
                    for row in sorted(rows, key=lambda row: row["path"])]
        self.assertEqual([line for line in source.splitlines() if line.startswith("  File ")], expected)
        self.assertEqual(source.count("  IfFileExists "), len(rows))
        self.assertIn("SetOverwrite off", source)
        self.assertIn('SetOutPath "$CrcStageRoot\\fr"', source)
        self.assertNotIn("File /r", source)
        self.assertNotIn("Exec", source)
        self.assertIn(self.hash, source)
        self.assertEqual(source, m.render_nsis_payload_source(self.raw, self.hash, self.root))

    def test_nsis_source_refuses_modified_or_extra_payload(self):
        path = self.root / "hostfxr.dll"
        old = path.read_bytes()
        path.write_bytes(b"x" * len(old))
        with self.assertRaises(ValueError):
            m.render_nsis_payload_source(self.raw, self.hash, self.root)
        path.write_bytes(old)
        (self.root / "extra.dll").write_bytes(b"unexpected")
        with self.assertRaises(ValueError):
            m.render_nsis_payload_source(self.raw, self.hash, self.root)

    def test_descriptor_change_rejected(self):
        original = m.os.fstat
        calls = 0

        def changed(fd):
            nonlocal calls
            st = original(fd)
            calls += 1
            if calls == 2:
                return SimpleNamespace(st_dev=st.st_dev, st_ino=st.st_ino,
                                       st_size=st.st_size, st_mtime_ns=st.st_mtime_ns + 1,
                                       st_ctime_ns=st.st_ctime_ns, st_mode=st.st_mode)
            return st

        with patch.object(m.os, "fstat", changed):
            with self.assertRaisesRegex(ValueError, "changed while reading"):
                self.verify()

    def test_same_length_tampering(self):
        path = self.root / "CRC.PracticeManager.exe"
        path.write_bytes(b"x" * path.stat().st_size)
        with self.assertRaises(ValueError):
            self.verify()

    def test_missing_payload(self):
        (self.root / "hostfxr.dll").unlink()
        with self.assertRaises(ValueError):
            self.verify()

    def test_extra_file_or_directory(self):
        path = self.root / "unexpected.dll"
        path.write_bytes(b"synthetic")
        with self.assertRaises(ValueError):
            self.verify()
        path.unlink()
        (self.root / "unexpected").mkdir()
        with self.assertRaises(ValueError):
            self.verify()

    def test_redirected_payload_entry(self):
        path = self.root / "CRC.PracticeManager.exe"
        target = self.root.parent / "external.txt"
        target.write_bytes(path.read_bytes())
        path.unlink()
        try:
            path.symlink_to(target)
        except (OSError, NotImplementedError):
            self.skipTest("Symbolic link fixture not available")
        with self.assertRaises(ValueError):
            self.verify()
