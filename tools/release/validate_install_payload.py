"""Read-only static installer payload inventory validation. No install or execution."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

LIMIT = 2 * 1024 * 1024
MAX_FILES = 2000
MAX_FILE = 250 * 1024 * 1024
MAX_TOTAL = 750 * 1024 * 1024
REQUIRED = frozenset((
    "CRC.PracticeManager.exe", "CRC.PracticeManager.dll",
    "CRC.PracticeManager.deps.json", "CRC.PracticeManager.runtimeconfig.json",
    "PracticeManager.Core.dll", "PracticeManager.Persistence.Sqlite.dll",
    "Microsoft.Data.Sqlite.dll", "e_sqlite3.dll", "SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.e_sqlite3.dll", "SQLitePCLRaw.batteries_v2.dll",
    "hostfxr.dll", "hostpolicy.dll", "coreclr.dll",
    "PresentationFramework.dll", "WindowsBase.dll"))
EXE = frozenset(("CRC.PracticeManager.exe", "createdump.exe"))
JSON = frozenset(("CRC.PracticeManager.deps.json", "CRC.PracticeManager.runtimeconfig.json"))
PDB = frozenset(("CRC.PracticeManager.pdb", "PracticeManager.Core.pdb",
                 "PracticeManager.Persistence.Sqlite.pdb"))
RESERVED = frozenset(("CON", "PRN", "AUX", "NUL",
                      *(f"COM{i}" for i in range(1, 10)),
                      *(f"LPT{i}" for i in range(1, 10))))

def require(condition, reason):
    if not condition:
        raise ValueError(reason)

def unique_keys(pairs):
    out = {}
    for key, value in pairs:
        require(key not in out, "Duplicate inventory key")
        out[key] = value
    return out

def digest(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None

def path_parts(path):
    require(isinstance(path, str) and 1 <= len(path) <= 180, "Invalid payload path length")
    parts = path.split("/")
    require(len(parts) <= 8 and all(re.fullmatch(r"[A-Za-z0-9_.+-]{1,100}", p)
            and p not in (".", "..") and not p.endswith(".")
            and p.split(".")[0].upper() not in RESERVED for p in parts), "Unsafe payload path")
    return parts

def validate(raw, expected_hash):
    require(isinstance(raw, bytes) and 1 <= len(raw) <= LIMIT, "Invalid inventory size")
    require(digest(expected_hash) and hashlib.sha256(raw).hexdigest() == expected_hash,
            "Inventory bytes do not match separately retained digest")
    manifest = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_keys)
    require(isinstance(manifest, dict) and set(manifest) ==
            {"format", "architecture", "fileCount", "bytes", "files"}, "Unexpected inventory shape")
    require(manifest["format"] == "CRC-Development-Build-Inventory-v1" and
            manifest["architecture"] == "win-x64", "Unexpected inventory type or architecture")
    rows = manifest["files"]
    require(isinstance(rows, list) and 1 <= len(rows) <= MAX_FILES and
            type(manifest["fileCount"]) is int and len(rows) == manifest["fileCount"],
            "Inventory file count mismatch")
    names = set()
    aliases = {}
    directories = set()
    total = 0
    for row in rows:
        require(isinstance(row, dict) and set(row) == {"path", "bytes", "sha256"},
                "Unexpected inventory row")
        path = row["path"]
        parts = path_parts(path)
        for i in range(1, len(parts) + 1):
            prefix = "/".join(parts[:i])
            key = prefix.lower()
            require(aliases.setdefault(key, prefix) == prefix, "Case-ambiguous payload path")
            if i != len(parts):
                directories.add(key)
        require(path not in names, "Duplicate payload file")
        names.add(path)
        # Current self-contained publish layout: reviewed executable names,
        # assembly files, exact application metadata and its three PDBs.
        # Runtime satellite assemblies may be in language subdirectories.
        require(path in EXE or path in JSON or path in PDB or path.endswith(".dll"),
                "Unexpected payload file type or name")
        require(type(row["bytes"]) is int and 1 <= row["bytes"] <= MAX_FILE and digest(row["sha256"]),
                "Invalid payload byte count or digest")
        total += row["bytes"]
        require(total <= MAX_TOTAL, "Payload exceeds size bound")
    require(not ({name.lower() for name in names} & directories), "File-directory collision")
    require(type(manifest["bytes"]) is int and manifest["bytes"] == total, "Inventory total mismatch")
    require(REQUIRED <= names, "Required application or runtime component missing")
    return {"format": "CRC-Install-Payload-Layout-Report-v1", "fileCount": len(rows),
            "bytes": total, "inventorySha256": expected_hash, "layoutValid": True,
            "artifactBytesIndependentlyVerified": False, "installerExecuted": False,
            "securityApproved": False, "releaseAuthorized": False}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inventory", type=Path)
    parser.add_argument("--expected-sha256", required=True)
    args = parser.parse_args()
    try:
        with args.inventory.open("rb") as stream:
            raw = stream.read(LIMIT + 1)
        report = validate(raw, args.expected_sha256)
    except (ValueError, OSError, UnicodeError, RecursionError):
        print("Installer layout rejected; inspect trusted inventory locally.", file=sys.stderr)
        return 2
    print(json.dumps(report, sort_keys=True))
    return 0

if __name__ == "__main__":
    sys.exit(main())
