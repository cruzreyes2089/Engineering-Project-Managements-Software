"""Qualify synthetic Access migration on an already installed Windows DAO engine.

No installation, download, registry write, real Access database, credentials, or
distribution. Registration alone is never qualification. A missing engine is
reported separately from a failed test and does not authorize migration.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
PROBE = """$ErrorActionPreference = 'Stop'
try {
    if ($null -eq [type]::GetTypeFromProgID('DAO.DBEngine.120',$false)) { exit 3 }
    exit 0
} catch { exit 2 }
"""


def candidates(system, is64, python64):
    # Registry/COM view must match the Windows PowerShell process, not Python.
    base = "WindowsPowerShell/v1.0/powershell.exe"
    if is64:
        return [("64", system / ("System32" if python64 else "Sysnative") / base),
                ("32", system / "SysWOW64" / base)]
    return [("32", system / "System32" / base)]


def run(candidates, invoke=subprocess.run):
    report = {"format": "CRC-Synthetic-Installed-DAO-Qualification-v1",
              "status": "NotQualified", "reason": "NoRegisteredEngine",
              "selectedBitness": None, "probes": [], "historicalPassed": False,
              "crmPassed": False, "realMigrationAuthorized": False,
              "releaseAuthorized": False}
    selected = None
    for bitness, powershell in candidates:
        if not powershell.is_file():
            report["probes"].append({"bitness": bitness, "status": "ShellUnavailable"})
            continue
        probe = invoke([str(powershell), "-NoProfile", "-NonInteractive", "-Command", PROBE],
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
        if probe.returncode not in (0, 3):
            report["probes"].append({"bitness": bitness, "status": "ProbeFailed"})
            report.update(status="Failed", reason="RegistrationProbeFailed")
            return report
        report["probes"].append({"bitness": bitness,
                                "status": "Registered" if probe.returncode == 0 else "NotRegistered"})
        if probe.returncode == 0 and selected is None:
            selected = bitness
    if selected is None:
        return report
    report["selectedBitness"] = selected
    # One explicitly selected engine (prefer native 64-bit). No fall-back after
    # a failed qualification; a different engine cannot conceal a defect.
    for script, field in (("qualify_synthetic_historical_projects.py", "historicalPassed"),
                          ("qualify_synthetic_crm.py", "crmPassed")):
        result = invoke([sys.executable, str(HERE / script), "--confirm-synthetic",
                         "--dao-bitness", selected, "--native-smoke"],
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=240)
        if result.returncode != 0:
            report.update(status="Failed", reason=field + ":ExecutionFailed")
            return report
        try:
            payload = json.loads(result.stdout.decode("utf-8"))
            passed = (isinstance(payload, dict) and payload.get("daoFixtureChecksPassed") is True
                      and payload.get("nativeImportExecuted") is True
                      and payload.get("releaseAuthorized") is False)
            checks = payload.get("checks")
            if field == "historicalPassed":
                passed = passed and isinstance(checks, list) and all(label in checks for label in (
                    "Same-source CRM full values and independent identities reconciled",
                    "All normalized historical values match fixed fixture",
                    "Digest-bound combined native import and recovery",
                    "Final source bytes unchanged"))
            else:
                passed = passed and isinstance(checks, dict) and set(checks) == {
                    "populated", "emptyContacts", "sharedContactKey"} and (
                    payload.get("nativeCases") == ["populated", "emptyContacts"]
                    and payload.get("sharedKeyNativeQualified") is False)
                passed = passed and all(isinstance(labels, list) and
                    "All normalized fields match fixed expected fixture" in labels and
                    "Final source hash unchanged" in labels for labels in checks.values())
                passed = passed and all("Native fixed-fixture import and rollback checks" in checks[k]
                    for k in ("populated", "emptyContacts"))
        except (UnicodeError, ValueError, TypeError, KeyError, AttributeError):
            passed = False
        if not passed:
            report.update(status="Failed", reason=field + ":CompletionEvidenceMissing")
            return report
        report[field] = True
    report.update(status="PassedSynthetic", reason="SelectedInstalledEngine")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--confirm-synthetic-ci", action="store_true")
    args = parser.parse_args()
    if os.name != "nt" or not args.confirm_synthetic_ci:
        print("Blocked: Windows and explicit synthetic confirmation required.", file=sys.stderr)
        return 2
    try:
        system = Path(os.environ["SystemRoot"])
        report = run(candidates(system, (system / "SysWOW64").is_dir(), sys.maxsize > 2**32))
    except (OSError, KeyError, ValueError, subprocess.TimeoutExpired):
        print("Installed DAO qualification failed or timed out; no qualification inferred.", file=sys.stderr)
        return 2
    print(json.dumps(report, sort_keys=True))
    if report["status"] == "NotQualified":
        print("NOT QUALIFIED: no installed DAO engine found; synthetic DAO execution not performed.")
    return 2 if report["status"] == "Failed" else 0


if __name__ == "__main__":
    sys.exit(main())
