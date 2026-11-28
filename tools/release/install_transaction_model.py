"""Pure synthetic installation transaction model. Never touches files or launches code."""
from dataclasses import dataclass
from enum import Enum
import re

class Phase(Enum):
    PLANNED = "planned"
    STAGED = "staged"
    SWITCH_INTENT = "switch-intent"
    ACTIVATED = "activated"
    ACCEPTED = "accepted"

class Disposition(Enum):
    STAGE = "review staging"
    RECORD_INTENT = "review activation intent"
    REVIEW_SWITCH = "review pointer switch"
    REVIEW_SWITCH_RESULT = "review observed activation"
    REVIEW_ACCEPTANCE = "review application acceptance"
    RETAIN = "retain accepted version"
    HOLD = "stop for manual recovery review"

def digest(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None

@dataclass(frozen=True)
class Version:
    commit: str
    payload_sha256: str
    schema: int

    def __post_init__(self):
        if (not isinstance(self.commit, str) or not re.fullmatch(r"[0-9a-f]{40}", self.commit)
                or not digest(self.payload_sha256) or type(self.schema) is not int
                or not 1 <= self.schema <= 10000):
            raise ValueError("Invalid exact version identity")

@dataclass(frozen=True)
class Plan:
    previous: Version | None
    candidate: Version
    source_sha256: str | None
    recovery_sha256: str | None
    rehearsed_source_sha256: str | None
    rehearsed_candidate: Version | None
    rehearsed_recovery_sha256: str | None

    def __post_init__(self):
        if not isinstance(self.candidate, Version):
            raise ValueError("Candidate identity required")
        if self.previous is None:
            if any(v is not None for v in (self.source_sha256, self.recovery_sha256,
                   self.rehearsed_source_sha256, self.rehearsed_candidate,
                   self.rehearsed_recovery_sha256)):
                raise ValueError("Fresh install must not assume existing data")
        elif (not isinstance(self.previous, Version) or self.previous == self.candidate
              or self.candidate.schema < self.previous.schema
              or not digest(self.source_sha256) or not digest(self.recovery_sha256)
              or self.rehearsed_source_sha256 != self.source_sha256
              or self.rehearsed_recovery_sha256 != self.recovery_sha256
              or self.rehearsed_candidate != self.candidate):
            raise ValueError("Upgrade requires exact source, recovery and rehearsal bindings")

@dataclass(frozen=True)
class Observation:
    # Values model independently checked facts, not real attestations.
    active: Version | None
    staged: Version | None
    active_verified: bool
    staged_verified: bool
    application_closed: bool
    exclusive_lock: bool
    data_schema: int | None
    data_sha256: str | None
    recovery_sha256: str | None
    acceptance_verified: bool = False

@dataclass(frozen=True)
class Decision:
    disposition: Disposition
    next_phase: Phase | None = None
    # These flags are invariant even for an accepted simulation.
    release_authorized: bool = False
    execution_authorized: bool = False
    automatic_rollback: bool = False
    data_removal_authorized: bool = False

def assess(plan, phase, obs):
    """A conservative state decision. Caller must independently verify all evidence."""
    hold = Decision(Disposition.HOLD)
    if not isinstance(plan, Plan) or not isinstance(phase, Phase) or not isinstance(obs, Observation):
        return hold
    if any(type(v) is not bool for v in (obs.active_verified, obs.staged_verified,
           obs.application_closed, obs.exclusive_lock, obs.acceptance_verified)):
        return hold
    if not (obs.application_closed and obs.exclusive_lock and obs.active_verified):
        return hold
    if obs.active not in (plan.previous, plan.candidate):
        return hold
    if obs.staged is not None and (obs.staged != plan.candidate or not obs.staged_verified):
        return hold
    if plan.previous is None:
        source_unchanged = obs.data_schema is None and obs.data_sha256 is None and obs.recovery_sha256 is None
    else:
        if obs.recovery_sha256 != plan.recovery_sha256:
            return hold
        source_unchanged = (type(obs.data_schema) is int and obs.data_schema == plan.previous.schema
                            and obs.data_sha256 == plan.source_sha256)
    target_data_valid = (type(obs.data_schema) is int and obs.data_schema == plan.candidate.schema
                         and digest(obs.data_sha256))
    candidate_staged = obs.staged == plan.candidate and obs.staged_verified
    # Before cutover, neither a changed DB nor a changed active pointer is normal.
    if phase in (Phase.PLANNED, Phase.STAGED):
        if obs.active != plan.previous or not source_unchanged:
            return hold
        if not candidate_staged:
            return Decision(Disposition.STAGE) if phase is Phase.PLANNED else hold
        if phase is Phase.PLANNED:
            return Decision(Disposition.RECORD_INTENT, Phase.STAGED)
        return Decision(Disposition.RECORD_INTENT, Phase.SWITCH_INTENT)
    if phase is Phase.SWITCH_INTENT:
        if not candidate_staged or not source_unchanged:
            return hold
        if obs.active == plan.previous:
            return Decision(Disposition.REVIEW_SWITCH)
        # Pointer may have switched before completion was durably recorded.
        # Never repeat or reverse the switch automatically.
        return Decision(Disposition.REVIEW_SWITCH_RESULT, Phase.ACTIVATED)
    if obs.active != plan.candidate:
        return hold
    if phase is Phase.ACTIVATED:
        if not (source_unchanged or target_data_valid):
            return hold
        if obs.acceptance_verified and target_data_valid:
            return Decision(Disposition.REVIEW_ACCEPTANCE, Phase.ACCEPTED)
        return Decision(Disposition.REVIEW_ACCEPTANCE)
    if phase is Phase.ACCEPTED and obs.acceptance_verified and target_data_valid:
        return Decision(Disposition.RETAIN)
    return hold

def uninstall_review(active, installed, application_closed, exclusive_lock, verified_payload):
    """Model eligibility to review code-only removal. No filesystem paths or deletion."""
    if (isinstance(installed, Version) and active == installed
            and application_closed is True and exclusive_lock is True and verified_payload is True):
        return {"reviewCodeRemoval": True, "preserveAllData": True,
                "preserveRecoveryEvidence": True, "removeUnknownFiles": False,
                "uninstallExecuted": False, "releaseAuthorized": False}
    return {"reviewCodeRemoval": False, "preserveAllData": True,
            "preserveRecoveryEvidence": True, "removeUnknownFiles": False,
            "uninstallExecuted": False, "releaseAuthorized": False}
