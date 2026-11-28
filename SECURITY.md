# Security policy

This project is under development and is not yet certified for production use.

Report suspected vulnerabilities using GitHub private vulnerability reporting (Security > Report a vulnerability) once enabled. If unavailable, request a private contact channel without disclosing sensitive details. Never publish vulnerabilities, credentials, production logs or personal records in public issues.

Use synthetic data in development. Keep operational databases, backups, contracts, financial records, credentials, API tokens and production configuration outside source control and CI artifacts. Grant least privilege to users, automation and integrations. Test releases against disposable synthetic data.

For credential leaks, revoke or rotate the secret immediately and notify the maintainer privately. Deleting a file from the latest commit is insufficient.

## Public distribution boundary

The intended release is free for public commercial use. Only an explicitly approved signed release may be distributed as an executable; source availability is not executable approval. See [code signing policy](docs/code-signing-policy.md). No end user should be asked to disable antivirus, SmartScreen, firewall, code integrity, tamper protection, or install a private trust root to use an official public release. The original blocked archive remains excluded. A report of a new warning must trigger investigation, not a workaround. Affected release withdrawal, private security reporting and corrected signed releases are required capabilities before public release. Private reporting availability and maintainer response arrangements must be verified before publication.
