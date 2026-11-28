"""Validate exact published inventory bytes; no executable execution or release authority."""
import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

LIMIT = 4 * 1024 * 1024
REQUIRED = {"CRC.PracticeManager.exe", "CRC.PracticeManager.dll",
            "CRC.PracticeManager.deps.json", "CRC.PracticeManager.runtimeconfig.json",
            "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "e_sqlite3.dll"}

def require(ok, message):
    if not ok:
        raise ValueError(message)

def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate inventory key")
        result[key] = value
    return result

def validate(raw, expected):
    require(isinstance(raw, bytes) and 0 < len(raw) <= LIMIT, "Inventory size")
    require(isinstance(expected, str) and re.fullmatch(r"[0-9a-f]{64}", expected), "Expected digest")
    require(hashlib.sha256(raw).hexdigest() == expected, "Inventory digest mismatch")
    data = json.loads(raw.decode("utf-8"), object_pairs_hook=unique)
    require(isinstance(data, dict) and set(data) == {"format", "architecture", "fileCount", "bytes", "files"}, "Inventory fields")
    require(data["format"] == "CRC-Development-Build-Inventory-v1" and data["architecture"] == "win-x64", "Inventory identity")
    rows = data["files"]
    require(isinstance(rows, list) and 0 < len(rows) <= 10000, "Inventory file count")
    require(type(data["fileCount"]) is int and data["fileCount"] == len(rows), "File count mismatch")
    seen = set()
    total = 0
    for row in rows:
        require(isinstance(row, dict) and set(row) == {"path", "bytes", "sha256"}, "Inventory row fields")
        path = row["path"]
        require(isinstance(path, str) and len(path) <= 1024 and re.fullmatch(r"[A-Za-z0-9_./+-]+", path), "Inventory path")
        require(all(part not in ("", ".", "..") and not part.endswith(".") for part in path.split("/")), "Unsafe inventory path")
        require(path.lower() not in seen, "Duplicate inventory path")
        seen.add(path.lower())
        require(type(row["bytes"]) is int and 0 <= row["bytes"] <= 2**63-1, "File size")
        require(isinstance(row["sha256"], str) and re.fullmatch(r"[0-9a-f]{64}", row["sha256"]), "File digest")
        require(not re.search(r"\.(accdb|mdb|db|sqlite|sqlite3|pfx|p12|pem|key)$|\.db-(wal|shm|journal)$", path, re.I), "Unexpected database or credential extension")
        if path.lower() in {name.lower() for name in REQUIRED}:
            require(row["bytes"] > 0, "Empty required component")
        total += row["bytes"]
    require({name.lower() for name in REQUIRED} <= seen, "Required deployment files missing")
    require(type(data["bytes"]) is int and data["bytes"] == total, "Total bytes mismatch")
    return {"format": "CRC-Inventory-Structure-Report-v1", "inventorySha256": expected,
            "fileCount": len(rows), "bytes": total, "structureValid": True,
            "actualFilesRehashed": False, "releaseAuthorized": False}

def read(path):
    with Path(path).open("rb") as stream:
        return stream.read(LIMIT + 1)

def main():
    p = argparse.ArgumentParser(description=__doc__)
    source = p.add_mutually_exclusive_group(required=True)
    source.add_argument("--inventory", type=Path)
    source.add_argument("--stdin", action="store_true")
    p.add_argument("--expected-sha256", required=True)
    args = p.parse_args()
    try:
        raw = sys.stdin.buffer.read(LIMIT + 2) if args.stdin else read(args.inventory)
        # PowerShell pipeline adds a single line terminator; inventory file must
        # contain the exact original compact UTF-8 bytes, without a newline.
        if args.stdin:
            if raw.endswith(b"\r\n"):
                raw = raw[:-2]
            elif raw.endswith(b"\n"):
                raw = raw[:-1]
        report = validate(raw, args.expected_sha256)
    except (ValueError, OSError, UnicodeError, RecursionError):
        print("Inventory evidence rejected.", file=sys.stderr)
        return 2
    print(json.dumps(report, sort_keys=True))
    return 0

if __name__ == "__main__":
    sys.exit(main())
