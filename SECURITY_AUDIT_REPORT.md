# Security Audit Report - Book Wheel

Date: 2026-05-29
Auditor: GitHub Copilot (GPT-5.3-Codex)
Scope: Application project in BookWheel and solution-level dependency review

## Executive Summary

The application now has materially stronger security controls than the earlier version of the audit.
Previously identified critical and high-risk issues have been addressed:

- plaintext credentials were removed from `appsettings.json`
- first-run setup now creates an encrypted credential file only when the user explicitly creates an account
- HTTPS redirection and HSTS are enabled outside testing
- login rate limiting is in place
- server-side validation attributes are applied to request models
- failed-login and rate-limit events are captured as structured audit logs

Overall posture: Low to Moderate risk
- Critical findings: 0
- High findings: 0
- Medium findings: 2
- Low findings: 1

Dependency scan status:
- `dotnet list package --vulnerable --include-transitive` (BookWheel): no known vulnerable packages
- `dotnet list package --vulnerable --include-transitive` (BookWheel.Tests): no known vulnerable packages

## Methodology

- Manual code review of authentication, session handling, configuration, API endpoints, data persistence, logging, and HTTP pipeline
- Static checks for transport hardening and request validation controls
- NuGet vulnerability scan for direct and transitive packages

## Findings (Ordered by Severity)

### 1) Medium - Persistent audit logs are stored as plaintext JSONL files with no retention policy

Evidence:

- The application writes persistent JSONL logs under `BookWheel/App_Data/logs/` via `BookWheel/Logging/JsonFileLoggerProvider.cs`
- The log payload includes request metadata such as username, client IP, request ID, path, and user agent

Risk:

- If filesystem access, backups, or support bundles are compromised, audit metadata is exposed
- The log directory can grow without bound over time because there is no rotation or retention control

Recommendations:

1. Restrict file-system ACLs on `App_Data/logs/` so only the app identity and operators can read it.
2. Add a retention/rotation policy and maximum file size handling.
3. Consider shipping logs to a centralized logging sink instead of local plaintext storage.
4. If local log confidentiality is a concern, encrypt at rest or protect the directory with OS-level controls.

### 2) Medium - Login throttling is keyed to remote IP only and does not account for reverse-proxy forwarding

Evidence:

- Login rate limiting is implemented in `BookWheel/Program.cs`
- The partition key uses `context.Connection.RemoteIpAddress` only
- There is no forwarded-headers middleware configured in the pipeline

Risk:

- Behind a reverse proxy, the app may see the proxy IP instead of the true client IP
- Multiple users may collapse into the same rate-limit bucket, or anti-bruteforce controls may be less effective than intended

Recommendations:

1. Add forwarded-headers handling for the deployment topology used in production.
2. Consider layering username-based throttling or short lockout/backoff logic in addition to IP-based limits.
3. Monitor and alert on repeated rate-limit rejections for auth endpoints.

### 3) Low - Data Protection key storage is not explicitly configured for the deployment target

Evidence:

- The application uses ASP.NET Core Data Protection for cookie/session protection and credential-file encryption
- The runtime currently relies on the default key repository under the current user profile

Risk:

- This is acceptable for local development and a single-user deployment, but it is less predictable for multi-instance or service deployments
- Key management and restore behavior may be harder to control during migration or disaster recovery

Recommendations:

1. Configure a deliberate key storage location for production.
2. If the app will run on multiple instances, use a shared key ring or managed secret/key store.
3. Document key backup and rotation procedures.

## Positive Observations

- Credentials are no longer stored in `appsettings.json`.
- The first-run credential file is encrypted at rest and the password is hashed before storage.
- Auth cookies are `HttpOnly` and `SameSite=Strict`.
- HTTPS redirection and HSTS are enabled outside testing.
- Structured security audit logs are generated for failed logins and rate-limit rejections.
- Server-side validation is in place for auth and book request models.
- Dependency scan showed no known vulnerable NuGet packages at audit time.

## Prioritized Remediation Plan

### Immediate (0-2 days)

1. Add log retention/rotation and lock down access to `App_Data/logs/`.
2. Configure forwarded headers for any reverse-proxy deployment.

### Short Term (1-2 weeks)

1. Add username-aware throttling or lockout/backoff for auth attempts.
2. Configure explicit Data Protection key storage for the target environment.
3. Add operational guidance for log and key backup/restore.

### Mid Term (2-6 weeks)

1. Evaluate centralized logging for production deployments.
2. If this app is intended for broader use, consider ASP.NET Core Identity or an external OIDC provider.
3. Add CI checks to prevent accidental secret commits.

## Suggested Code Changes (High Value)

1. In logging:
- Add log rotation or size caps to `JsonFileLoggerProvider`.
- Emit to a centralized logging sink in production.

2. In HTTP pipeline:
- Add forwarded headers middleware for proxy-aware deployments.
- Consider per-username throttling in addition to IP-based rate limiting.

3. In deployment configuration:
- Explicitly configure Data Protection key storage.
- Document backup and recovery expectations for `App_Data`.

## Audit Limitations

- No dynamic penetration testing was performed.
- No infrastructure, reverse proxy, firewall, or deployment-environment review was performed.
- No SAST/DAST third-party tooling results were used beyond the NuGet vulnerability scan.

## Conclusion

The application is now in a reasonable state for a small internal tool. The remaining material risks are operational: local plaintext log storage, rate limiting that depends on the deployment topology, and explicit key-management configuration for production. Those should be addressed before wider distribution or multi-instance deployment.
# Security Audit Report - Book Wheel

Date: 2026-05-29
Auditor: GitHub Copilot (GPT-5.3-Codex)
Scope: Application project in BookWheel and solution-level dependency review

## Executive Summary

The application is a small cookie-authenticated ASP.NET Core tool with a relatively limited attack surface. The most important risks are related to secret management and transport/auth hardening.

Overall posture: Moderate risk

- Critical findings: 1
- High findings: 1
- Medium findings: 3
- Low findings: 1

Dependency scan status:

- dotnet list package --vulnerable --include-transitive (BookWheel): no known vulnerable packages
- dotnet list package --vulnerable --include-transitive (BookWheel.Tests): no known vulnerable packages

## Methodology

- Manual code review of authentication, session handling, configuration, API endpoints, data persistence, and HTTP pipeline
- Static checks for missing hardening middleware and controls
- NuGet vulnerability scan for direct and transitive packages

## Findings (Ordered by Severity)

### 1) Critical - Plaintext credentials and secret material in configuration file

Evidence:

- Hardcoded username/password and session secret in source-controlled settings at [BookWheel/appsettings.json](BookWheel/appsettings.json#L3), [BookWheel/appsettings.json](BookWheel/appsettings.json#L4), [BookWheel/appsettings.json](BookWheel/appsettings.json#L5)

Risk:

- Credential leakage via repository access, backups, logs, screenshots, or accidental sharing
- Immediate unauthorized access if defaults are reused in non-local environments

Recommendations:

1. Remove secrets from committed files.
2. Move credentials and secrets to environment variables or user secrets for local dev.
3. Rotate all current credentials immediately.
4. Add appsettings.Development.json.example only with placeholders.

### 2) High - No enforced HTTPS or HSTS in middleware pipeline

Evidence:

- Pipeline includes static files and controllers, but no HTTPS redirection or HSTS at [BookWheel/Program.cs](BookWheel/Program.cs#L13), [BookWheel/Program.cs](BookWheel/Program.cs#L14), [BookWheel/Program.cs](BookWheel/Program.cs#L15)
- Cookie Secure flag is conditional on request scheme at [BookWheel/Controllers/AuthController.cs](BookWheel/Controllers/AuthController.cs#L31)

Risk:

- If deployed behind or with misconfigured TLS termination, traffic and authentication cookie may be exposed over HTTP
- Session hijacking risk on untrusted networks

Recommendations:

1. Add UseHttpsRedirection and UseHsts in production.
2. Ensure forwarded headers are configured correctly behind reverse proxies.
3. Consider forcing secure cookies in production (SecurePolicy.Always).

### 3) Medium - No login throttling/rate limiting on authentication endpoint

Evidence:

- Login endpoint present at [BookWheel/Controllers/AuthController.cs](BookWheel/Controllers/AuthController.cs#L18) with direct credential check at [BookWheel/Services/AuthService.cs](BookWheel/Services/AuthService.cs#L18)
- No rate limiting middleware configured in [BookWheel/Program.cs](BookWheel/Program.cs#L1)

Risk:

- Brute-force credential attacks are feasible
- Increased credential stuffing exposure if reused passwords are used

Recommendations:

1. Add ASP.NET Core rate limiter policy for auth endpoints.
2. Add temporary lockout/backoff per username and per IP.
3. Log and alert on repeated failed authentication attempts.

### 4) Medium - Weak authentication design for anything beyond local/internal use

Evidence:

- Single shared credential pair validated by exact string compare at [BookWheel/Services/AuthService.cs](BookWheel/Services/AuthService.cs#L18)
- In-memory session store with 8-hour lifetime at [BookWheel/Services/AuthService.cs](BookWheel/Services/AuthService.cs#L11), [BookWheel/Services/AuthService.cs](BookWheel/Services/AuthService.cs#L27)

Risk:

- No user-specific identity, no password hashing, no MFA, and no durable/revocable session model
- Session behavior becomes inconsistent across multi-instance deployments

Recommendations:

1. If this app is intended for broader use, migrate to ASP.NET Core Identity or external OIDC provider.
2. Store password hashes (never plaintext) with a modern algorithm (PBKDF2/Argon2/bcrypt).
3. Use a distributed session/token store if running multiple instances.

### 5) Medium - Missing server-side input length limits for book title

Evidence:

- API only checks non-empty title at [BookWheel/Controllers/BooksController.cs](BookWheel/Controllers/BooksController.cs#L44) and [BookWheel/Controllers/BooksController.cs](BookWheel/Controllers/BooksController.cs#L61)
- Client-side maxlength exists, but this can be bypassed at [BookWheel/wwwroot/index.html](BookWheel/wwwroot/index.html#L47)

Risk:

- Oversized payloads can increase memory/file pressure and potentially degrade service

Recommendations:

1. Enforce server-side title length validation (for example 1-200 chars).
2. Add request body size limits as needed.
3. Return explicit validation errors for out-of-range data.

### 6) Low - Unused secret-like setting may create false confidence

Evidence:

- SessionTokenSecret defined in settings model at [BookWheel/Models/AppSettings.cs](BookWheel/Models/AppSettings.cs#L7) and populated in config at [BookWheel/appsettings.json](BookWheel/appsettings.json#L5), but not used in auth/session flow

Risk:

- Operators may assume cryptographic protection that does not exist

Recommendations:

1. Remove the unused setting, or
2. Implement a token/signing design that actually uses the secret.

## Positive Observations

- Auth cookie is HttpOnly and SameSite=Strict at [BookWheel/Controllers/AuthController.cs](BookWheel/Controllers/AuthController.cs#L29) and [BookWheel/Controllers/AuthController.cs](BookWheel/Controllers/AuthController.cs#L30)
- Dependency scan showed no known vulnerable NuGet packages at audit time
- Access control checks exist on books endpoints in [BookWheel/Controllers/BooksController.cs](BookWheel/Controllers/BooksController.cs#L19)

## Prioritized Remediation Plan

### Immediate (0-2 days)

1. Remove plaintext credentials/secrets from committed configuration and rotate them.
2. Enforce HTTPS + HSTS for non-local environments.
3. Add basic login rate limiting.

### Short Term (1-2 weeks)

1. Implement server-side validation attributes for request models.
2. Add structured security logging for failed login attempts and suspicious activity.
3. Add CI checks to prevent committing secrets (for example gitleaks).

### Mid Term (2-6 weeks)

1. Replace shared static credentials with per-user auth and hashed credentials.
2. Evaluate migrating to ASP.NET Core Identity or OIDC.
3. Add a security regression checklist and tests for auth hardening.

## Suggested Code Changes (High Value)

1. In Program pipeline:
- Add HTTPS redirection and HSTS in production.
- Add AddRateLimiter/UseRateLimiter with strict policy on /api/auth/login.

2. In request models:
- Add validation attributes such as Required, StringLength(200), and MinLength.
- Enforce model validation responses consistently.

3. In configuration:
- Replace committed secrets with environment-driven values.

## Audit Limitations

- No dynamic penetration testing performed.
- No infrastructure, reverse proxy, firewall, or deployment environment review.
- No SAST/DAST third-party tooling results beyond dotnet package vulnerability checks.

## Conclusion

The application is in reasonable shape for local tooling but is not production-hardened for internet exposure. Addressing secret management, HTTPS enforcement, and login throttling will significantly reduce risk quickly.
