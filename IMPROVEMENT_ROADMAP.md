# Book Wheel Improvement Roadmap

## Purpose

This document outlines practical ways to improve Book Wheel across product experience, security, reliability, operations, and long-term maintainability. It is intended to help prioritize work beyond the current MVP feature set.

## Current Strengths

- Simple first-run account setup and login flow
- Clean book management flow with add, edit, remove, and spin actions
- Light/dark theme support with saved browser preference
- Persistent storage for books, credentials, logs, and Data Protection keys
- Working Docker-based deployment path
- Security hardening already in place for encrypted credential storage, HTTPS redirection, HSTS, and login rate limiting
- Automated integration coverage for key API, frontend, and security behavior

## Improvement Priorities

### Priority 1: Security and Production Hardening

These items provide the highest operational value and align with the latest security audit.

1. Add log retention and rotation.
2. Restrict access to `App_Data/logs` and document recommended filesystem permissions.
3. Configure forwarded headers for reverse-proxy deployments.
4. Add username-aware throttling or short lockout/backoff for repeated failed logins.
5. Configure explicit Data Protection key storage for production environments.
6. Add CI secret scanning to prevent accidental credential or token commits.

Expected outcome:

- Lower operational risk
- Safer production deployment profile
- Clearer incident response and log management

### Priority 2: User Experience Improvements

The current interface is usable, but still minimal. The next step is making the tool feel faster, clearer, and more polished.

1. Add confirmation and success toasts instead of relying only on inline text messages.
2. Add empty-state guidance for first-time users with no books added yet.
3. Improve loading and disabled states during login, save, delete, and spin actions.
4. Add keyboard accessibility improvements for dialogs, lists, and wheel actions.
5. Improve mobile layout polish for the wheel and management area.
6. Add optional categories, tags, or reading status to books.

Expected outcome:

- Better first-use experience
- Fewer user mistakes
- A more polished feel on desktop and mobile

### Priority 3: Book Discovery and Selection Features

The core idea of the app is strong, but the book list can become more useful with lightweight organization features.

1. Add search/filter support for active books.
2. Allow sorting by title, recently added, or recently selected.
3. Add optional weights so some books are more or less likely to be selected.
4. Add exclude-from-next-spin or temporary skip behavior.
5. Track spin history so users can review past selections.
6. Add import/export for the book list using JSON or CSV.

Expected outcome:

- Better usefulness for larger reading lists
- More control over randomization
- Easier recovery and portability of data

### Priority 4: Multi-User and Identity Improvements

The current single-account model works for a personal or internal tool, but it limits future growth.

1. Move from single shared credentials to per-user accounts.
2. Add password change and account recovery flows.
3. Consider ASP.NET Core Identity or external OIDC if broader usage is expected.
4. Add role separation if admin-only management becomes necessary.
5. Scope book collections per user or household.

Expected outcome:

- Better security model
- More flexible deployment scenarios
- Clear path to broader adoption

### Priority 5: Reliability and Data Management

The current file-based approach is simple, but it will eventually become limiting.

1. Add backup and restore guidance for `App_Data`.
2. Add file corruption handling and recovery messaging.
3. Add versioned data schema support for future migrations.
4. Consider moving from flat files to SQLite for stronger consistency and easier querying.
5. Add health checks for storage, logging, and app readiness.

Expected outcome:

- Better recovery story
- Safer long-term data evolution
- More reliable deployments

### Priority 6: Observability and Operations

The app already captures useful audit logs, but operations can be improved substantially.

1. Add structured application metrics such as login failures, book count, and spin count.
2. Add request logging correlation guidance for troubleshooting.
3. Support log shipping to a centralized sink in production.
4. Add startup diagnostics for missing directories, permission issues, and storage configuration.
5. Add a release checklist for Docker publish, image tagging, and deployment verification.

Expected outcome:

- Faster troubleshooting
- Better production visibility
- Fewer deployment surprises

### Priority 7: Testing and Engineering Quality

The project already has solid integration coverage for its size. The next step is broadening confidence around change.

1. Add tests for error states such as missing data files, corrupt data files, and permission failures.
2. Add proxy-aware rate-limiting tests once forwarded headers are introduced.
3. Add container-focused smoke tests for startup and writable volume paths.
4. Add browser-level UI tests for login, theme toggle, and book workflows.
5. Add CI automation for build, test, and vulnerability scan steps.

Expected outcome:

- Higher confidence in releases
- Better protection against regressions
- Safer infrastructure changes

## Suggested Delivery Phases

### Phase 1: Hardening

- Log rotation and retention
- Forwarded headers support
- Explicit Data Protection key configuration
- CI secret scanning
- Docker deployment checklist

### Phase 2: UX and Workflow

- Better feedback states
- Accessibility improvements
- Search, filter, and sorting
- Spin history

### Phase 3: Data and Platform

- Import/export
- Backup and restore guidance
- SQLite migration evaluation
- Health checks and operational metrics

### Phase 4: Identity Expansion

- Per-user accounts
- Account management features
- Optional external identity provider

## Recommended Next Three Changes

If only three improvements are chosen next, these should provide the best immediate value:

1. Add log retention/rotation and log directory hardening.
2. Add forwarded headers support plus username-aware throttling.
3. Add search/filter and spin history to improve day-to-day usability.

## Success Metrics

These metrics can help determine whether improvements are working.

- Fewer deployment issues caused by volume permissions or missing configuration
- Reduced login abuse risk and clearer audit trail handling
- Faster user completion time for adding and selecting books
- Lower support effort when recovering data or inspecting logs
- Higher confidence in releases through automated validation

## Summary

Book Wheel is already in a good state for a small internal or personal tool. The most valuable next step is to treat it like a production-capable application: tighten operational security, improve data reliability, and make the everyday user workflow more polished and scalable.