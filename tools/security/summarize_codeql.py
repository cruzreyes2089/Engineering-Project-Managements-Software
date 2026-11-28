"""Report local CodeQL SARIF results; never dismiss alerts or authorize release."""
import json
import re
import sys
from pathlib import Path

MAX_BYTES = 64 * 1024 * 1024
MAX_REPORTED = 250
LANGUAGES = {"csharp", "python", "actions"}

def summarize(document, language):
    if language not in LANGUAGES or document.get("version") != "2.1.0":
        raise ValueError("Unexpected language or SARIF version")
    runs = document.get("runs")
    if not isinstance(runs, list) or not runs:
        raise ValueError("Missing analysis runs")
    findings = []
    notifications = 0
    invocation_count = 0
    rule_count = 0
    for run in runs:
        if run.get("tool", {}).get("driver", {}).get("name") != "CodeQL":
            raise ValueError("Unexpected analysis tool")
        # SARIF may omit unused rule descriptors, and CodeQL may put rule
        # metadata in extensions rather than the driver. A zero descriptor
        # count is not proof that zero queries ran or that coverage is complete.
        for component in [run["tool"]["driver"], *run["tool"].get("extensions", [])]:
            rule_count += len(component.get("rules", []))
        invocations = run.get("invocations", [])
        invocation_count += len(invocations)
        for invocation in invocations:
            if invocation.get("executionSuccessful") is False:
                raise ValueError("Failed analysis invocation")
            notifications += len(invocation.get("toolExecutionNotifications", []))
            notifications += len(invocation.get("toolConfigurationNotifications", []))
        results = run.get("results")
        if not isinstance(results, list):
            raise ValueError("Missing findings array")
        for result in results:
            rule = result.get("ruleId", "")
            if not isinstance(rule, str) or not re.fullmatch(r"[A-Za-z0-9_./-]{1,160}", rule):
                rule = "[unrecognized rule]"
            kind = result.get("kind", "fail")
            if kind not in {"fail", "pass", "review", "open", "informational", "notApplicable"}:
                kind = "unknown"
            level = result.get("level", "unspecified")
            if level not in {"error", "warning", "note", "none"}:
                level = "unspecified"
            locations = result.get("locations", [])
            physical = locations[0].get("physicalLocation", {}) if locations else {}
            uri = physical.get("artifactLocation", {}).get("uri", "")
            # No absolute paths, URLs, source snippets, or arbitrary messages.
            if (not isinstance(uri, str) or not re.fullmatch(r"[A-Za-z0-9_./ -]{1,300}", uri)
                    or uri.startswith("/") or ".." in uri.split("/")):
                uri = "[location unavailable]"
            line = physical.get("region", {}).get("startLine")
            if type(line) is not int or line < 1:
                line = None
            findings.append({"rule": rule, "kind": kind, "level": level, "path": uri, "line": line})
    return {"language": language, "runCount": len(runs), "ruleDescriptorCount": rule_count, "queryCoverageVerified": False,
            "invocationCount": invocation_count, "notificationCount": notifications,
            "resultCount": len(findings), "truncated": len(findings) > MAX_REPORTED,
            "findings": findings[:MAX_REPORTED], "releaseAuthorized": False,
            "independentSecurityApproved": False}

def main():
    if len(sys.argv) != 3 or sys.argv[2] not in LANGUAGES:
        raise SystemExit("Expected SARIF directory and known language")
    language = sys.argv[2]
    path = Path(sys.argv[1]) / (language + ".sarif")
    if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_BYTES:
        raise SystemExit("Expected bounded regular SARIF report")
    with path.open("rb") as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise SystemExit("SARIF size limit exceeded")
    report = summarize(json.loads(raw), language)
    print("CRC_CODEQL_REVIEW " + json.dumps(report, ensure_ascii=True, separators=(",", ":")))

if __name__ == "__main__":
    main()
