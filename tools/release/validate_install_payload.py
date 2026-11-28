"""Read-only static installer payload inventory validation. No install or execution."""
import argparse
import hashlib
import json
import os
import stat
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

class PayloadValidationError(ValueError):
    pass

def require(condition, reason):
    if not condition:
        raise PayloadValidationError(reason)

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

def verify_payload(raw, expected_hash, root):
    """Read actual published bytes against the exact inventory, never execute.
    Trusted, quiescent CI filesystem only; not a race-resistant sandbox or
    independent security attestation.
    """
    report = validate(raw, expected_hash)
    manifest = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_keys)
    expected = {row["path"]: row for row in manifest["files"]}
    allowed_dirs = {"/".join(path.split("/")[:i])
                    for path in expected for i in range(1, len(path.split("/")))}
    root = Path(os.path.abspath(root))

    def plain(path, directory):
        st = path.lstat()
        require(not stat.S_ISLNK(st.st_mode)
                and not getattr(st, "st_file_attributes", 0) & 0x400,
                "Redirected payload entry")
        require(stat.S_ISDIR(st.st_mode) if directory else stat.S_ISREG(st.st_mode),
                "Unexpected payload entry type")
        return st

    # Reject redirected ancestors before walking. This is a point-in-time check.
    for parent in reversed(root.parents):
        plain(parent, True)
    plain(root, True)

    def inventory_tree():
        found = set()
        pending = [(root, "")]
        entries = 0
        while pending:
            directory, prefix = pending.pop()
            plain(directory, True)
            with os.scandir(directory) as children:
                for child in children:
                    entries += 1
                    require(entries <= MAX_FILES * 8, "Payload entry limit exceeded")
                    relative = prefix + child.name
                    path_parts(relative)
                    path = Path(child.path)
                    st = path.lstat()
                    if stat.S_ISDIR(st.st_mode):
                        plain(path, True)
                        require(relative in allowed_dirs, "Unexpected payload directory")
                        pending.append((path, relative + "/"))
                    else:
                        plain(path, False)
                        require(relative in expected and relative not in found,
                                "Unexpected payload file")
                        found.add(relative)
        require(found == set(expected), "Missing payload file")
        return found

    def identity(st):
        return (st.st_dev, st.st_ino, st.st_size, st.st_mtime_ns, st.st_ctime_ns)

    def opened_identity_matches(path_stat, handle_stat):
        # Windows path and descriptor stat results can use different device,
        # inode and timestamp representations. Compare like with like there;
        # both observations must still report the exact manifest size.
        if os.name == "nt":
            return path_stat.st_size == handle_stat.st_size
        return identity(path_stat) == identity(handle_stat)

    inventory_tree()
    for name, row in expected.items():
        path = root.joinpath(*path_parts(name))
        for parent in reversed(path.parents):
            plain(parent, True)
        before = plain(path, False)
        require(before.st_size == row["bytes"], "Payload file size mismatch")
        hashed = hashlib.sha256()
        count = 0
        with path.open("rb") as stream:
            opened = os.fstat(stream.fileno())
            require(stat.S_ISREG(opened.st_mode) and opened_identity_matches(before, opened),
                    "Payload file changed before reading")
            while chunk := stream.read(1024 * 1024):
                count += len(chunk)
                require(count <= row["bytes"], "Payload file grew during reading")
                hashed.update(chunk)
            require(identity(os.fstat(stream.fileno())) == identity(opened),
                    "Payload file changed while reading")
        # Reopen via the original path and compare descriptor observations;
        # on Windows this avoids comparing two incompatible stat interfaces.
        with path.open("rb") as reopened:
            require(identity(os.fstat(reopened.fileno())) == identity(opened),
                    "Payload file identity changed on reopen")
        require(identity(plain(path, False)) == identity(before)
                and count == row["bytes"] and hashed.hexdigest() == row["sha256"],
                "Payload bytes do not match inventory")
    inventory_tree()
    report["payloadBytesMatchInventory"] = True
    return report


def render_nsis_payload_source(raw, expected_hash, root):
    """Generate only the NSIS extraction macro; never invoke the compiler.

    The future installer must supply a newly reserved, verified private staging
    directory in $CrcStageRoot and a trusted compile-time CRC_PAYLOAD_ROOT.
    This macro alone is not a complete installer or a runtime hash verifier.
    """
    verify_payload(raw, expected_hash, root)
    manifest = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_keys)
    lines = [
        "; Generated from exact verified inventory; not release authorization.",
        "; Inventory SHA-256: " + expected_hash,
        "!ifndef CRC_PAYLOAD_ROOT",
        '  !error "Reviewed compile-time payload root required"',
        "!endif",
        "!macro CRC_ExtractReviewedPayload",
        "  SetOverwrite off",
    ]
    for row in sorted(manifest["files"], key=lambda row: row["path"]):
        parts = path_parts(row["path"])
        directory = "\\".join(parts[:-1])
        relative = "\\".join(parts)
        target = "$CrcStageRoot" + ("\\" + directory if directory else "")
        lines.extend((
            "  ClearErrors",
            f'  SetOutPath "{target}"',
            "  IfErrors 0 +2",
            '  Abort "Cannot prepare program staging directory."',
            f'  IfFileExists "$OUTDIR\\{parts[-1]}" 0 +2',
            '  Abort "Program staging file already exists."',
            "  ClearErrors",
            '  File "${CRC_PAYLOAD_ROOT}\\' + relative + '"',
            "  IfErrors 0 +2",
            '  Abort "Cannot extract reviewed program file."',
        ))
    lines.extend(("!macroend", ""))
    return "\n".join(lines)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inventory", type=Path)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--payload-root", type=Path, help="Read-only rehash of exact local output tree")
    args = parser.parse_args()
    try:
        with args.inventory.open("rb") as stream:
            raw = stream.read(LIMIT + 1)
        report = (verify_payload(raw, args.expected_sha256, args.payload_root)
                  if args.payload_root is not None else validate(raw, args.expected_sha256))
    except (ValueError, OSError, UnicodeError, RecursionError) as exc:
        # Fixed validation messages identify the failed invariant. Never print
        # raw OS errors or JSON input, which may contain local paths/data.
        print("Installer payload rejected: " + (str(exc) if isinstance(exc, PayloadValidationError) else type(exc).__name__), file=sys.stderr)
        return 2
    print(json.dumps(report, sort_keys=True))
    return 0

if __name__ == "__main__":
    sys.exit(main())
