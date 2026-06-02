# Security Audit Report - Book Wheel

Date: 2026-06-01
Auditor: GitHub Copilot (GPT-5.3-Codex)
Scope: Application project in BookWheel and solution-level dependency review

## Executive Summary

This audit was performed after introducing multi-user authentication, first-user administrator assignment, admin-only user management, and per-user book scoping.

Overall posture: Low risk

- Critical findings: 0
- High findings: 0
- Medium findings: 0
- Low findings: 1

Security improvements verified in this revision:

- First account created during setup is marked as administrator
- User credential store now supports multiple encrypted account records with user ids and admin flags
- User-management APIs are restricted to authenticated administrators
- User deletion is restricted to administrators and blocks deletion of the first account
- User deletion invalidates active sessions and removes user-scoped books
- Administrators no longer set user passwords directly; reset links are generated instead
- Password reset links expire in 24 hours and are one-time use
- Password reset token records are hashed and encrypted at rest
- Password hashes remain non-exported in user-management responses
- Book data is now isolated by user id in `books.json`
- Existing login-failure and rate-limit security logs still operate after auth changes
- JSONL logs now include retention and size-based rotation controls
- Request correlation id propagation (`X-Correlation-ID`) is active for troubleshooting
- Forwarded headers are configured for reverse-proxy deployments
- Username-aware lockout/backoff is enforced for repeated failed logins
- Startup diagnostics validate writable storage paths on boot
- Structured metrics endpoint is available to administrators (`/api/metrics`)
- Optional centralized log shipping to HTTP sink is supported by configuration

Dependency scan status:

Verification refreshed on 2026-06-01.

- `dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive`: no known vulnerable packages
- `dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive`: no known vulnerable packages

Security verification status (2026-06-01):

- `dotnet test BookWheel.slnx`: 39 passed, 0 failed
- `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "Failed_Login_Is_Recorded_As_Structured_Warning_Log|Login_Is_Rate_Limited_After_Repeated_Failed_Attempts|Login_Rate_Limiter_Uses_Forwarded_Client_Ip_When_Present|Non_Admin_User_Cannot_Access_User_Management_Endpoints|Password_Reset_Link_Can_Be_Generated_And_Used_Once|Disabled_User_Cannot_Log_In|Request_Correlation_Header_Is_Propagated"`: 7 passed, 0 failed
- `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "Startup_Health_And_Version_Endpoints_Return_Success|Writable_App_Data_Paths_Are_Available_During_Runtime|Docker_Artifacts_Define_Persistent_Data_And_Runtime_Probe_Configuration|Login_Theme_And_Book_Workflow_Is_End_To_End_Reachable"`: 4 passed, 0 failed

## Methodology

- Manual review of authentication, session handling, authorization gates, API responses, data persistence, logging, and HTTP pipeline
- Static checks for transport hardening and validation controls on new request models
- NuGet vulnerability scan for direct and transitive packages
- Regression verification with targeted security tests plus full solution test run

## Findings (Ordered by Severity)

### 1) Low - Data Protection key storage is not explicitly configured in application startup code

Evidence:

- The application uses ASP.NET Core Data Protection for cookie/session protection and credential-file encryption
- Docker and compose deployment paths persist Data Protection key volume mounts, but startup code does not yet enforce an explicit key repository when running outside containers

Risk:

- Local development is fine, but non-containerized multi-instance deployments can have inconsistent key management
- Key restore and migration operations may be harder to validate during incident response if external key location is not standardized

Recommendations:

1. Configure an explicit key storage location for production startup.
2. Use a shared key ring or managed key store if running multiple instances.
3. Document key backup and rotation procedures.

## Positive Observations

- The credential store remains encrypted at rest and continues using password hashing.
- New multi-user credential operations do not expose password hashes through user-management APIs.
- Password reset links are now generated for users and consumed through one-time token completion.
- First-user-admin bootstrap behavior is deterministic and covered by tests.
- Non-admin users are denied user-management endpoint access.
- First-account deletion protections and user-delete cascade behavior are covered by integration tests.
- Per-user book isolation is covered by integration tests.
- Corrupt credential/book payloads are quarantined and reported with operator-facing recovery messages.
- Health checks and startup diagnostics validate writable runtime paths.
- Request correlation logging and admin metrics improve incident troubleshooting visibility.
- Optional centralized log shipping is available for production aggregation.
- Auth cookies remain `HttpOnly` and `SameSite=Strict`.
- HTTPS redirection and HSTS are enabled outside testing.
- Dependency scan showed no known vulnerable NuGet packages at audit time.

## Prioritized Remediation Plan

### Immediate (0-2 days)

1. Lock down file-system ACLs for `App_Data`, including logs and quarantine folders.
2. Validate centralized log shipping endpoint and alerting in production.

### Short Term (1-2 weeks)

1. Configure explicit Data Protection key storage for non-containerized targets.
2. Add operational guidance for correlation-id based tracing across upstream components.
3. Add production runbooks for metrics collection and alert thresholds.

### Mid Term (2-6 weeks)

1. Add CI secret scanning and dependency-audit gating in the pipeline.
2. Add infrastructure-level synthetic probes for `/health/live` and `/health/ready`.
3. Evaluate stronger identity lifecycle controls (password reset, account lock/disable).

## Audit Limitations

- No dynamic penetration testing was performed.
- No infrastructure, reverse proxy, firewall, or environment hardening review was performed.
- No external SAST/DAST tool results were included beyond NuGet vulnerability scanning and integration tests.

## Conclusion

The application now includes materially stronger operational security controls, observability diagnostics, and regression coverage. Remaining risk is primarily around explicit production key repository standardization and deployment-level hardening practices. With those closed, the solution is well-positioned for reliable production operation.
