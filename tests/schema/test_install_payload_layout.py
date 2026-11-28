import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

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
