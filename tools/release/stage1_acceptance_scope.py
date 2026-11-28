"""Stage 1 planning coverage, not evidence of acceptance or release authority."""
from validate_acceptance_evidence import ORIGINAL, SUPPLEMENTAL, validate

DEFERRED_2 = frozenset({"FIN-03", "FIN-04", "FIN-05", "FIN-06", "FIN-07",
                        "FIN-08", "MIG-01", "MIG-02", "MIG-03", "CRC-ACCT-01"})
DEFERRED_3 = frozenset({"REL-03", "NET-01", "NET-02"})
# Mixed requirements must not be silently marked passed after a partial test.
MIXED = {
    "SEC-02": "Temporary password reset; legacy credentials remain Stage 2.",
    "SEC-04": "Restricted documents and exact record boundaries; network tenant isolation deferred.",
    "FIN-02": "Internal draft numbering and atomic allocations; issued invoice numbers deferred.",
    "FIN-09": "Internal draft and hold report reconciliation; authoritative settlement deferred.",
    "FIN-10": "Local decimal precision; legacy floating-point conversion deferred.",
    "REP-01": "Current operational and internal financial reports; full legacy parity outstanding.",
    "COM-01": "Local firm identity, prefix and templates; commercial branding and tenancy deferred."
}
SCOPE = ORIGINAL | SUPPLEMENTAL
REQUIRED = SCOPE - DEFERRED_2 - DEFERRED_3 - MIXED.keys()

def summarize(manifest, commit, digest):
    report = validate(manifest, SCOPE, commit, digest)
    rows = {row["testId"]: row["status"] for row in manifest["results"]}
    return {
        "format": "CRC-Stage1-Scope-Report-v1",
        "required": {key: rows[key] for key in sorted(REQUIRED)},
        "mixed": {key: {"masterStatus": rows[key], "stage1Review": MIXED[key]}
                  for key in sorted(MIXED)},
        "deferredStage2": {key: rows[key] for key in sorted(DEFERRED_2)},
        "deferredStage3": {key: rows[key] for key in sorted(DEFERRED_3)},
        "requiredUnpassed": sorted(key for key in REQUIRED if rows[key] != "Pass"),
        "masterCounts": report["counts"],
        "mixedReviewRequired": True,
        "securityClearanceVerified": False,
        "releaseAuthorized": False
    }
