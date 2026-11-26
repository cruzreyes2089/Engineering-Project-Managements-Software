# Security review continuation

The first exact-archive ClamAV scan reported zero detections. Owner's Windows full scan reported no current threats. The blocked archive is not approved for use. No evidence establishes a Chrome false positive.

Second-stage audit safely inventories regular archive members, checks path safety, duplicate names, symlinks, encryption, expansion limits and CRC on read, and performs an additional recursive ClamAV scan of extracted members in a private ephemeral directory. No execution or republishing. This is greater coverage with the same scanner, NOT an independent second antivirus engine. Logs provide per-file SHA256, sizes and counts. Limits or errors block success. Source-only branch continues Windows regression tests and records fresh app hash/signature status without uploading executable artifacts on any event using this workflow.

Pinned first-party Node24 actions were resolved to exact commits in the official action repositories and action.yml runtimes verified. This reduces mutable-tag exposure; it is not a full external action code audit. Upload action removed while hold is active. No signature certificate purchased or publisher verification claimed. CI hold does not retroactively disable workflows on other branches. Do not merge accounting branch into main until reviewed.

Remaining gates: publisher/vendor review of Chrome verdict, independent security engine/source/dependency review, provenance and signing plan, local financial detail schema evidence, clone/restore procedure, and production posting/reconciliation. Do not alter packaging or name to evade detection.

## Repeated Chrome block and dependency-review hardening

Owner again reports a dangerous-file block for the original held artifact. No vendor response reported. Do not re-download, override or execute it; the existing hold is unchanged. The separate native read-only recovery adapter passed run 45769274848 (15 Python + 696 C# checks, zero compiler errors/warnings, NotSigned, no executable upload).

Add a CI-only direct/transitive dependency inventory and known-vulnerability report for the desktop and smoke-test projects, using the .NET 10 package-list JSON format. Treat NuGet advisory retrieval failures and low/moderate/high/critical advisory warnings as build failures, not clean scans. No package versions or packaging changed. This is dependency advisory coverage, not a complete source review or an independent antivirus scan. Exact-revision CI verification pending. Google publisher guidance: https://developers.google.com/search/docs/monitor-debug/security/malware ; CLI reference: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list . No verified control of github.com Search Console; no publisher appeal submitted by this change.
