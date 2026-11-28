import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/migration"))
import prepare_synthetic_archive_desktop as f
import historical_project_preflight as p

class ArchiveDesktopFixtureTests(unittest.TestCase):
    def test_fixed_deterministic_bytes_and_digests(self):
        files = f.package()
        self.assertEqual(files, f.package())
        manifest = json.loads(files["fixture-manifest.json"])
        for name, digest in manifest["fileSha256"].items():
            self.assertEqual(hashlib.sha256(files[name]).hexdigest(), digest)
        self.assertEqual(manifest["expected"]["projectCount"], 2)
        self.assertEqual(manifest["expected"]["duplicateNumberGroups"], 1)
        self.assertEqual(manifest["expected"]["inactiveClientProjectCount"], 1)
        for k in ("sourceIsAccessDatabase", "sourceAuthenticityVerified", "desktopExecuted", "workAuthorized", "releaseAuthorized"):
            self.assertIs(manifest[k], False)

    def test_valid_and_negative_census_are_separately_digest_bound(self):
        files = f.package()
        manifest = json.loads(files["fixture-manifest.json"])
        hashes = {k: manifest["hashes"][k] for k in p.HASHES}
        crm, projects, census, wrong = (json.loads(files[n]) for n in
                                        ("crm.json", "projects.json", "census.json", "wrong-parent-census.json"))
        self.assertTrue(p.validate(crm, projects, census, hashes)["identityMatch"])
        self.assertEqual(f.sha(files["wrong-parent-census.json"]), manifest["negativeCensusSha256"])
        with self.assertRaises(ValueError):
            p.validate(crm, projects, wrong, hashes)

    def test_exclusive_output_and_checkout_refusal(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "new"
            f.generate(target)
            before = {p.name: p.read_bytes() for p in target.iterdir()}
            with self.assertRaises(FileExistsError):
                f.generate(target)
            self.assertEqual(before, {p.name: p.read_bytes() for p in target.iterdir()})
        with self.assertRaises(ValueError):
            f.generate(f.ROOT / "synthetic-must-not-create")
