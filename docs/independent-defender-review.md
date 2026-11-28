# Independent scanner review of the held artifact

The owner again observed Chrome's dangerous-download verdict. Continue source-only work. The exact original archive has prior ClamAV no-detection evidence; this is not an independent second engine.

A separate ephemeral Windows workflow attempts Microsoft Defender static custom scans of the exact SHA256-verified original ZIP and its 409 safely inventoried members, with existing protection and normal remediation enabled. No sample execution, exclusions, security-setting changes, upload or repackaging. Scanner availability, active protection, fresh signatures, signed scanner, scan status, unchanged files and completion/detection events are required. GitHub-hosted runner support is not assumed; unavailable service or evidence means review incomplete. The archive expires under its original retention; no reupload to extend retention. This does not adjudicate Chrome's warning or release the product.

Microsoft documentation: https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus and https://learn.microsoft.com/powershell/module/defender/get-mpcomputerstatus .
