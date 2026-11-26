import importlib.util
from pathlib import Path
import stat
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("archive_inventory", Path(__file__).resolve().parents[2] / "tools/security/archive_inventory.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class ArchiveInventoryTests(unittest.TestCase):
    def fixture(self, entries):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = Path(tmp.name)
        archive = root / "test.zip"
        with zipfile.ZipFile(archive, "w") as z:
            for name, data in entries:
                z.writestr(name, data)
        return archive, root / "members"

    def test_regular_inventory(self):
        a,d = self.fixture([("CRC.PracticeManager.exe", b"not an executable"), ("sub/a.dll", b"fixture")])
        rows = module.extract_inventory(a,d)
        self.assertEqual(len(rows), 2)
        self.assertEqual((d/"sub/a.dll").read_bytes(), b"fixture")

    def test_reject_unsafe_names(self):
        for path in ["../escape","/absolute","C:/bad","sub\\bad","a/../bad","NUL.txt","file."]:
            with self.subTest(path=path):
                a,d = self.fixture([("CRC.PracticeManager.exe",b"x"),(path,b"x")])
                with self.assertRaises(ValueError):
                    module.extract_inventory(a,d)
                self.assertFalse(d.exists())

    def test_case_collision(self):
        a,d = self.fixture([("CRC.PracticeManager.exe",b"x"),("a.dll",b"x"),("A.dll",b"x")])
        with self.assertRaises(ValueError):
            module.extract_inventory(a,d)

    def test_symlink_rejected(self):
        link = zipfile.ZipInfo("link")
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        a,d = self.fixture([("CRC.PracticeManager.exe",b"x"),(link,b"elsewhere")])
        with self.assertRaises(ValueError):
            module.extract_inventory(a,d)

    def test_missing_expected_file(self):
        a,d = self.fixture([("other.dll",b"x")])
        with self.assertRaises(ValueError):
            module.extract_inventory(a,d)

    def test_existing_destination(self):
        a,d = self.fixture([("CRC.PracticeManager.exe",b"x")])
        d.mkdir()
        with self.assertRaises(ValueError):
            module.extract_inventory(a,d)
