# Security Audit Report - Book Wheel

Date: 2026-05-30
Auditor: GitHub Copilot (GPT-5.3-Codex)
Scope: Application project in BookWheel and solution-level dependency/testing review

## Executive Summary

Current audit checks passed and previously remediated critical/high findings remain addressed.

Overall posture: Low to Moderate risk
- Critical findings: 0
- High findings: 0
- Medium findings: 2
- Low findings: 1

## Verification Results (2026-05-30)

- dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive: no known vulnerable packages
- dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive: no known vulnerable packages
- dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter security tests: 3 passed, 0 failed
- dotnet test BookWheel.slnx: 13 passed, 0 failed

## Findings (Ordered by Severity)

### 1) Medium - Persistent audit logs are plaintext JSONL with no retention policy

Risk:
- Audit metadata may be exposed if filesystem/backups are compromised.
- Log growth is unbounded without retention/rotation.

Recommended actions:
1. Restrict ACLs for BookWheel/App_Data/logs.
2. Add retention and rotation limits.
3. Prefer centralized logging in production.

### 2) Medium - Login throttling relies on RemoteIpAddress only

Risk:
- Reverse-proxy deployments may collapse users into shared buckets.
- Effective brute-force protection can degrade without true client IP forwarding.

Recommended actions:
1. Configure forwarded headers for production topology.
2. Add username-aware throttling or short lockout/backoff.

### 3) Low - Data Protection key storage not explicitly configured for production

Risk:
- Key management may be less predictable for multi-instance/migration scenarios.

Recommended actions:
1. Configure explicit key storage location.
2. Use shared key ring/managed key store for multi-instance deployments.

## Conclusion

No known package vulnerabilities were found, and security regression tests are passing. Remaining risks are operational and should be addressed before broader production exposure.
