import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/release"))
from validate_build_inventory import REQUIRED

class AcceptanceInventoryBindingTests(unittest.TestCase):
    def test_cli_requires_exact_inventory_before_initializing_and_reports_scope(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            inventory = root / "inventory.json"
            evidence = root / "evidence.json"
            files = [{"path": name, "bytes": 1, "sha256": "0"*64} for name in sorted(REQUIRED)]
            raw = json.dumps({"format": "CRC-Development-Build-Inventory-v1",
                              "architecture": "win-x64", "fileCount": len(files),
                              "bytes": len(files), "files": files}, separators=(",", ":")).encode()
            inventory.write_bytes(raw)
            digest = hashlib.sha256(raw).hexdigest()
            command = [sys.executable, str(ROOT / "tools/release/validate_acceptance_evidence.py"),
                       str(evidence), "--commit", "a"*40, "--artifact-sha256", digest]
            def run(*args):
                return subprocess.run(command + list(args), capture_output=True, text=True)
            self.assertEqual(run("--initialize").returncode, 2)
            self.assertFalse(evidence.exists())
            inventory.write_bytes(raw + b"\n")
            self.assertEqual(run("--inventory", str(inventory), "--initialize").returncode, 2)
            self.assertFalse(evidence.exists())
            inventory.write_bytes(raw)
            result = run("--inventory", str(inventory), "--initialize", "--stage1")
            self.assertEqual(result.returncode, 0, result.stderr)
            report = json.loads(result.stdout)
            self.assertEqual(report["masterCounts"]["Not run"], 49)
            self.assertTrue(report["inventoryStructureValid"])
            self.assertFalse(report["actualFilesRehashed"])
            self.assertFalse(report["releaseAuthorized"])
            self.assertFalse(report["evidenceAuthenticityVerified"])
            self.assertTrue(evidence.exists())
            before = evidence.read_bytes()
            self.assertEqual(run("--inventory", str(inventory), "--initialize").returncode, 2)
            self.assertEqual(evidence.read_bytes(), before)
