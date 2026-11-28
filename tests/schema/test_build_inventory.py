import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("build_inventory", ROOT / "tools/release/validate_build_inventory.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.data = {"format": "CRC-Development-Build-Inventory-v1", "architecture": "win-x64",
                     "fileCount": len(m.REQUIRED), "bytes": len(m.REQUIRED),
                     "files": [{"path": n, "bytes": 1, "sha256": "a"*64} for n in sorted(m.REQUIRED)]}
    def validate(self, data):
        raw = json.dumps(data, separators=(",", ":")).encode()
        return m.validate(raw, hashlib.sha256(raw).hexdigest())
    def test_valid_is_not_release_authority(self):
        result = self.validate(self.data)
        self.assertTrue(result["structureValid"])
        self.assertFalse(result["actualFilesRehashed"])
        self.assertFalse(result["releaseAuthorized"])
    def test_wrong_digest(self):
        with self.assertRaises(ValueError):
            m.validate(json.dumps(self.data).encode(), "b"*64)
    def test_missing_component(self):
        self.data["files"].pop()
        self.data["fileCount"] -= 1
        self.data["bytes"] -= 1
        with self.assertRaises(ValueError):
            self.validate(self.data)
    def test_bad_paths(self):
        for path in ("../escape.dll", "/absolute.dll", "nested//bad.dll", "data.db", "secret.pfx", "nested./bad.dll", self.data["files"][0]["path"].upper()):
            with self.subTest(path=path):
                bad = copy.deepcopy(self.data)
                bad["files"].append({"path": path, "bytes": 1, "sha256": "b"*64})
                bad["fileCount"] += 1
                bad["bytes"] += 1
                with self.assertRaises(ValueError):
                    self.validate(bad)
    def test_wrong_count_size_or_hash(self):
        for field, value in (("fileCount", True), ("bytes", 0), ("architecture", "win-x86")):
            with self.subTest(field=field):
                bad = copy.deepcopy(self.data)
                bad[field] = value
                with self.assertRaises(ValueError):
                    self.validate(bad)
        self.data["files"][0]["sha256"] = "bad"
        with self.assertRaises(ValueError):
            self.validate(self.data)
    def test_duplicate_keys(self):
        raw = b'{"format":"a","format":"b"}'
        with self.assertRaises(ValueError):
            m.validate(raw, hashlib.sha256(raw).hexdigest())
