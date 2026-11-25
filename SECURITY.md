# Security policy

This project is under development and is not yet certified for production use.

Report suspected vulnerabilities using GitHub private vulnerability reporting (Security > Report a vulnerability) once enabled. If unavailable, request a private contact channel without disclosing sensitive details. Never publish vulnerabilities, credentials, production logs or personal records in public issues.

Use synthetic data in development. Keep operational databases, backups, contracts, financial records, credentials, API tokens and production configuration outside source control and CI artifacts. Grant least privilege to users, automation and integrations. Test releases against disposable synthetic data.

For credential leaks, revoke or rotate the secret immediately and notify the maintainer privately. Deleting a file from the latest commit is insufficient.
