"""Static evidence only; never execute or publish extracted files."""
from pathlib import Path, PurePosixPath
import hashlib
import re
import stat
import sys
import zipfile

MAX_MEMBERS = 2000
MAX_FILE = 250 * 1024 * 1024
MAX_TOTAL = 750 * 1024 * 1024

def extract_inventory(archive, destination):
    destination = Path(destination)
    # Require a fresh private directory, not an existing tree or symlink.
    if destination.exists() or destination.is_symlink():
        raise ValueError("Destination must not exist")
    with zipfile.ZipFile(archive) as z:
        items = z.infolist()
        if not items or len(items) > MAX_MEMBERS:
            raise ValueError("Archive entry count outside bounds")
        seen = set()
        files = []
        declared = 0
        for member in items:
            name = member.filename
            path = PurePosixPath(name)
            parts = name.rstrip("/").split("/")
            if (len(name) > 512 or len(parts) > 16 or
                any(not re.fullmatch(r"[A-Za-z0-9_. +@-]{1,180}", p)
                    or p in (".", "..") or p.endswith((" ", ".")) for p in parts)
                or path.is_absolute() or "\\" in name):
                raise ValueError("Unsafe archive path")
            for part in parts:
                if part.split(".")[0].upper() in {"CON","PRN","AUX","NUL",*[f"COM{i}" for i in range(1,10)],*[f"LPT{i}" for i in range(1,10)]}:
                    raise ValueError("Reserved path")
            normalized = "/".join(parts).casefold()
            if normalized in seen:
                raise ValueError("Duplicate archive path")
            seen.add(normalized)
            mode = (member.external_attr >> 16) & 0xffff
            kind = stat.S_IFMT(mode)
            if member.flag_bits & 1 or kind not in (0, stat.S_IFREG, stat.S_IFDIR):
                raise ValueError("Encrypted or special archive member")
            if member.is_dir():
                if kind == stat.S_IFREG or member.file_size:
                    raise ValueError("Invalid directory member")
                continue
            if kind == stat.S_IFDIR or member.file_size > MAX_FILE:
                raise ValueError("Invalid file size or type")
            declared += member.file_size
            if declared > MAX_TOTAL:
                raise ValueError("Archive expansion exceeds limit")
            files.append((member, parts))
        if not files or not any("/".join(parts) == "CRC.PracticeManager.exe" for _,parts in files):
            raise ValueError("Expected application missing")
        destination.mkdir(mode=0o700)
        actual_total = 0
        inventory = []
        for member, parts in files:
            target = destination.joinpath(*parts)
            target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            digest = hashlib.sha256()
            count = 0
            # Exclusive creation prevents path collisions; files have no execute bit.
            with z.open(member) as source, target.open("xb") as output:
                target.chmod(0o600)
                while chunk := source.read(1024 * 1024):
                    count += len(chunk)
                    actual_total += len(chunk)
                    if count > member.file_size or actual_total > MAX_TOTAL:
                        raise ValueError("Actual expansion exceeds bounds")
                    digest.update(chunk)
                    output.write(chunk)
            if count != member.file_size:
                raise ValueError("Archive length mismatch")
            inventory.append(("/".join(parts), count, digest.hexdigest()))
        return inventory

if __name__ == "__main__":
    rows = extract_inventory(sys.argv[1], sys.argv[2])
    print(f"Inventoried files: {len(rows)}; expanded bytes: {sum(x[1] for x in rows)}")
    for name, size, digest in sorted(rows):
        print(f"{digest}  {size:>12}  {name}")
