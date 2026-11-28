# Simulated candidate posting pipeline

Test-only in-memory HttpMessageHandler combines the actual offline wire compiler, external witness, readback parser, recovery evaluator and dispatch classifier. It cannot contact Manager or the network. The handler simulates a provider commit before throwing a lost-response exception, and a successful status with the wrong external identity. Each test attempt retains its original operation key; all assessments deny real send/retry/posting authority.

This tests component interoperability, not a production transport or a fully integrated SQLite posting queue. It does not grant any permission to invoke API2 writes. The fake provider echoes the observed narrow readback shape; operation-derived reference acceptance and actual provider defaults still require a separate disposable business trial. The existing verified baseline and posting sandbox must remain unchanged. Security and production holds remain active.
