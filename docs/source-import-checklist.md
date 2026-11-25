# Native baseline import and certification

- [ ] Locate the original v0.4.0 archive and retain a backup.
- [ ] Extract to a clean working folder; keep Access FE/BE and production records outside it.
- [ ] Review every source/config/test fixture/document and any existing history for secrets, private data and third-party redistribution rights. Use synthetic fixtures.
- [ ] Enable secret scanning and push protection; manually inspect staged file list and diff. Ignore rules do not remove tracked content.
- [ ] Import the reviewed baseline through a pull request, preserving solution and project paths.
- [ ] Inspect target framework and actual build instructions before enabling Windows CI. Limit workflow token permissions; never expose secrets to untrusted forks.
- [ ] Verify restore, Release build, tests, publish and clean Windows startup with a disposable synthetic database.
- [ ] Record exact commit, build results, smoke-test results and limitations.
- [ ] Protect main against force pushes and deletion; require PRs and verified CI once established. Require independent review for external contributions. Do not impose impossible self-approval rules for a solo maintainer.
- [ ] Enable private vulnerability reporting and dependency alerts.
- [ ] Do not call the application production-ready until independently reviewed.

This checklist is not evidence of completed source review or build certification.
