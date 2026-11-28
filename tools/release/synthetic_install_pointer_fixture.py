"""Disposable text-only staging and pointer fault fixture, not an installer.

No external input paths, real payload, executable, application launch, data
migration or uninstall. In-process lock only; never an OS installer lock.
"""
from contextlib import closing
import hashlib
import os
from pathlib import Path
import sqlite3
import stat
import tempfile
import threading

import install_transaction_model as m
import synthetic_install_review_journal as j

OLD = b"CRC synthetic version 46 text marker\n"
NEW = b"CRC synthetic version 47 text marker\n"
DATA = b"CRC synthetic unchanged data marker\n"
RECOVERY = b"CRC synthetic recovery marker\n"
OLD_POINTER = b"synthetic-old\n"
NEW_POINTER = b"synthetic-new\n"

def sha(data):
    return hashlib.sha256(data).hexdigest()

class InjectedFault(RuntimeError):
    pass

def require(condition, message):
    if not condition:
        raise ValueError(message)

def plain(path, directory=False):
    info = path.lstat()
    require(not stat.S_ISLNK(info.st_mode) and
            not (getattr(info, "st_file_attributes", 0) & 0x400),
            "Redirected synthetic path")
    require(stat.S_ISDIR(info.st_mode) if directory else
            stat.S_ISREG(info.st_mode) and info.st_nlink == 1,
            "Unexpected synthetic path type or link count")

def exact(path, expected):
    plain(path)
    with path.open("rb") as stream:
        require(stream.read(len(expected)+1) == expected, "Synthetic marker mismatch")

def new_file(path, data):
    with path.open("xb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())

class Fixture:
    def __init__(self):
        self._temp = tempfile.TemporaryDirectory(prefix="crc-synthetic-pointer-")
        self.root = Path(self._temp.name)
        self._lock = threading.Lock()
        self.old = m.Version("a"*40, sha(OLD), 46)
        self.new = m.Version("b"*40, sha(NEW), 47)
        self.plan = m.Plan(self.old, self.new, sha(DATA), sha(RECOVERY),
                           sha(DATA), self.new, sha(RECOVERY))
        self.pointer = self.root / "active.txt"
        self.pending = self.root / "pending.txt"
        self.stage_dir = self.root / "staged"
        self.data = self.root / "data-marker.txt"
        self.recovery = self.root / "recovery-marker.txt"
        self.journal = self.root / "review.db"
        try:
            new_file(self.root / "old-marker.txt", OLD)
            new_file(self.pointer, OLD_POINTER)
            new_file(self.data, DATA)
            new_file(self.recovery, RECOVERY)
            new_file(self.journal, b"")
            with closing(self.connect()) as db:
                self.checkpoint = j.initialize(db, self.plan)
        except BaseException:
            self._temp.cleanup()
            raise

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self._temp.cleanup()

    def connect(self):
        plain(self.root, directory=True)
        plain(self.journal)
        db = sqlite3.connect(self.journal, timeout=5)
        try:
            db.execute("PRAGMA journal_mode=DELETE")
            db.execute("PRAGMA synchronous=FULL")
            return db
        except BaseException:
            db.close()
            raise

    def _observe(self):
        # A pending marker is uncertain evidence; never overwrite or discard it.
        plain(self.root, directory=True)
        require(not os.path.lexists(self.pending), "Pending switch requires review")
        exact(self.root / "old-marker.txt", OLD)
        exact(self.data, DATA)
        exact(self.recovery, RECOVERY)
        plain(self.pointer)
        with self.pointer.open("rb") as stream:
            value = stream.read(32)
        require(value in (OLD_POINTER, NEW_POINTER), "Unrecognized active marker")
        active = self.old if value == OLD_POINTER else self.new
        staged = None
        if os.path.lexists(self.stage_dir):
            plain(self.stage_dir, directory=True)
            require({p.name for p in self.stage_dir.iterdir()} == {"version.txt", "complete.txt"},
                    "Incomplete or unexpected staging content")
            exact(self.stage_dir / "version.txt", NEW)
            exact(self.stage_dir / "complete.txt", sha(NEW).encode("ascii"))
            staged = self.new
        require(active != self.new or staged == self.new, "Active marker has no complete staged version")
        return m.Observation(active, staged, True, staged is not None, True, True,
                             46, sha(DATA), sha(RECOVERY))

    def observe(self):
        with self._lock:
            return self._observe()

    def stage(self, expected, fault=None):
        require(fault in (None, "partial"), "Unknown synthetic fault")
        with self._lock, closing(self.connect()) as db:
            checkpoint = j.reconcile(db, self.plan, expected)
            require(checkpoint.phase is m.Phase.PLANNED, "Staging phase required")
            obs = self._observe()
            require(m.assess(self.plan, checkpoint.phase, obs).disposition is m.Disposition.STAGE,
                    "Fresh staging review required")
            self.stage_dir.mkdir(exist_ok=False)
            new_file(self.stage_dir / "version.txt", NEW)
            if fault == "partial":
                raise InjectedFault("Interrupted before stage completion")
            new_file(self.stage_dir / "complete.txt", sha(NEW).encode("ascii"))
            return self._observe()

    def record(self, expected):
        with self._lock, closing(self.connect()) as db:
            # Actual fixture observations replace caller-supplied observations.
            return j.append_review(db, self.plan, expected, self._observe())

    def switch(self, expected, fault=None):
        require(fault in (None, "before-replace", "after-replace"), "Unknown synthetic fault")
        with self._lock, closing(self.connect()) as db:
            checkpoint = j.reconcile(db, self.plan, expected)
            obs = self._observe()
            require(checkpoint.phase is m.Phase.SWITCH_INTENT and
                    m.assess(self.plan, checkpoint.phase, obs).disposition is m.Disposition.REVIEW_SWITCH,
                    "Exact switch intent and old marker required")
            new_file(self.pending, NEW_POINTER)
            if fault == "before-replace":
                raise InjectedFault("Interrupted before pointer replace")
            # Same directory, fixed text filenames. No real activation or launch.
            exact(self.pointer, OLD_POINTER)
            exact(self.pending, NEW_POINTER)
            os.replace(self.pending, self.pointer)
            if fault == "after-replace":
                raise InjectedFault("Interrupted after pointer replace")
            return self._observe()
