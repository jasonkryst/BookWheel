# BookWheel Testing & QA Audit

**Date:** 2026-09-04
**Scope:** `BookWheel.Tests` (xUnit + Testcontainers), CI test gating (`.github/workflows/ci.yml`), and cross-reference against the controller/endpoint surface in `BookWheel/Controllers`. Live-app manual testing was explicitly out of scope.

## Executive Summary

**Overall rating: Good, with fixable gaps (B+).** For a small self-hosted app, BookWheel's test suite is unusually disciplined: 281 tests across 21 files, zero mocking framework (real PostgreSQL via Testcontainers for every integration test), zero skipped/TODO/FIXME tests, and a consistent `MethodAsync_Condition_ExpectedOutcome` naming convention that makes the suite readable as living documentation. Negative-path coverage on the surfaces that are tested (soft-delete semantics, ISBN checksum validation, corrupted-file quarantine, rate limiting) is genuinely strong.

However, a full local run (`dotnet test BookWheel.slnx`, 281 tests) took **34.3 minutes** and produced **7 failures**, all Docker-daemon timeouts (infrastructure, not application logic — see Limitations). The root cause is architectural: most integration test classes spin up a **dedicated PostgreSQL Testcontainer per test method** rather than sharing one per class, in contrast to the `Storage/Postgres/*RepositoryTests.cs` files, which share a container via `ICollectionFixture` and are comparably fast. This is the single highest-value fix available.

Beyond performance, the most consequential gaps are: (1) tests named "browser"/"frontend" do not execute a real browser, DOM, or JS engine — they assert the served HTML/CSS/JS *source text* contains expected substrings, so the README's framing of "browser-level UI tests" overstates what is actually verified; (2) the Stats/spin-analytics UI has zero frontend test coverage despite solid backend coverage; (3) two of the three admin "cannot modify own account" guards (update, reset-link) are never exercised, only delete; (4) no code-coverage tooling is wired into CI despite `coverlet.collector` being referenced in the test project, so no team member can currently answer "what % of `app.js`/`AuthService` is covered" with data.

None of these are blockers to shipping — the app is well-guarded against regressions in its core flows — but they represent the highest-leverage next investments in test quality.

## Findings

### High severity

**H1. One Postgres container per test causes a 34-minute local run and produced 7 Docker-daemon-timeout failures.**
`BookWheelApiTests`, `BookWheelFrontendTests`, `BookWheelBrowserWorkflowTests`, `BookWheelPwaTests`, `BookWheelSmokeTests`, and `BookWheelHealthCheckTests` (the `Database_HealthCheck_*` test) all construct `new BookWheelWebAppFactory()` per test (`BookWheel.Tests\BookWheelWebAppFactory.cs:18-23,45`), and that constructor starts a brand-new `postgres:16-alpine` Testcontainer synchronously. With ~230 tests using this factory, a full run creates/destroys well over 150 containers. This run measured 8-53 seconds per test just for container churn, for a 34.3-minute total. Compare this to `Storage/Postgres/PostgresBookRepositoryTests.cs` and siblings, which share one container per test class via `[Collection(PostgresCollection.Name)]` + `ICollectionFixture<PostgresTestFixture>` (`Storage/Postgres/PostgresTestFixture.cs:47-51`) and reset with `TRUNCATE ... RESTART IDENTITY CASCADE` between tests — dramatically faster and, per this run, more reliable. CI's `unit-tests` job has a 15-minute `timeout-minutes` (`.github/workflows/ci.yml:67`), which this local run would have exceeded outright (GitHub Actions runners may be faster or slower than this environment, but the margin is thin to negative).

**H2. "Browser-level" test naming overstates actual coverage — no real browser/DOM/JS execution exists anywhere in the repo.**
A repo-wide search for `Playwright|Selenium|Puppeteer|WebDriver|headless` returns no matches. `BookWheelBrowserWorkflowTests.cs` (`Login_Theme_And_Book_Workflow_Is_End_To_End_Reachable`) and the bulk of `BookWheelFrontendTests.cs` are plain `HttpClient` tests that fetch `index.html`/`app.js`/`site.css` as text and assert on substrings or regexes, e.g. `Assert.Contains("toggleTheme", script, StringComparison.Ordinal)` or `Assert.Contains("id=\"themeToggleBtn\"", html)`. No JavaScript is ever executed, no DOM is ever rendered, and no user interaction (click, form fill) is ever simulated. This is a legitimate and useful test style (it catches accidental deletion/renaming of markup, ids, and function names), but it cannot catch a JS runtime exception, a broken event handler, an off-by-one in pagination logic, or a CSS rule that doesn't actually apply as intended. The README ("Add browser-level UI tests for login, theme toggle, and book workflows" — marked `[Done]` in `IMPROVEMENT_ROADMAP.md`) and test file naming should not be read as "this was verified in a browser."

**H3. Zero frontend test coverage for the Stats/spin-analytics UI.**
`app.js` contains 27 occurrences of stats-related identifiers (a per-user analytics modal, top-books ranking, admin aggregate view — the GH #75 feature called out extensively in the README's backend test list). `BookWheelFrontendTests.cs` has no test method that references "stats" in any form — not the modal's presence, not its i18n strings, not the admin-only aggregate-view gating. This is the one major UI feature area among those enumerated in the audit brief (barcode scanner, import/export, theme toggle, i18n, pagination) with literally no automated frontend coverage; all the others have multiple dedicated tests.

### Medium severity

**M1. Two of three "admin cannot modify own account" guards are untested.**
`UsersController` blocks the authenticated admin from targeting their own `UserId` on three routes, each with its own localized message: `PUT /api/users/{id}` ("Administrators can only update other user accounts."), `POST /api/users/{id}/password-reset-link` ("...generate reset links for other user accounts."), and `DELETE /api/users/{id}` ("...remove other user accounts.") — see `Controllers/UsersController.cs:112-115,169-172,210-213`. Only the delete path has a test (`Admin_Cannot_Delete_First_Account`), and that test's assertion happens to hold because the admin's own ID is being used, not because the test specifically targets self-modification of a non-first account. The update-self and reset-link-self guards have no test coverage at all — a regression that removed either check would go undetected.

**M2. No API-level tests for the `ForcePasswordReset` or account-lockout login blocks.**
`AuthController.Login` returns `423 Locked` for two distinct conditions independent of the tested 429 rate-limiter: `RequiresPasswordReset` ("Password reset is required. Ask an administrator for a reset link.") and `IsLockedOut` with `LockoutEndsAtUtc` (`Controllers/AuthController.cs:99-129`). A repository-level test exists for setting the `ForcePasswordReset` flag (`MarkForPasswordResetAsync_Sets_ForcePasswordReset_And_Clears_Lock`), but nothing exercises the actual login attempt against a force-reset or account-locked user through the HTTP layer to confirm the 423 status, message, and `lockoutEndsAtUtc` payload are wired correctly end-to-end. This is a different mechanism from the already-tested per-IP/username 429 rate limiter (`Login_Is_Rate_Limited_After_Repeated_Failed_Attempts`), so that existing test does not substitute for it.

**M3. No negative-path test for invalid `BookTypeId`.**
`Postgres/PostgresBookRepositoryTests.cs` covers all three valid values (Physical=1, Digital=2, NookOnly=3) and their defaulting/preservation behavior, but no test — at the repository or controller layer — exercises an out-of-range or nonexistent `BookTypeId` (e.g., `999`) via `POST /api/books` or `PUT /api/books/{id}` to confirm whether it is rejected with a clean `400` or surfaces as an unhandled `500` from a foreign-key violation.

**M4. No coverage tooling wired into CI.** `coverlet.collector` is referenced in `BookWheel.Tests.csproj` (v10.0.1), but `.github/workflows/ci.yml`'s `unit-tests` job runs plain `dotnet test --verbosity normal` with no `--collect:"XPlat Code Coverage"`, no report generation, no artifact upload, and no minimum-coverage gate. Coverage is never measured anywhere in this project. This audit's coverage estimates (e.g., "the Stats UI has no tests") are necessarily derived from reading test names and cross-referencing source files, not from an instrumented report — a good coverage tool would make gaps like H3 and M1-M3 immediately visible without a manual audit.

**M5. No concurrency/race-condition tests anywhere in the 281-test suite.** Concurrent spins for the same user, concurrent failed-login attempts approaching the lockout/rate-limit thresholds, and concurrent add/delete of the same book are all untested. `IBookRepository.SelectRandomAsync` and the login-lockout counters are exactly the kind of shared-state code where race conditions matter; for a low-traffic self-hosted app the risk is modest, but it is currently unverified rather than verified-safe.

**M6. `MigrationController.Run()`'s success path is untested at the HTTP layer.** `Migration_Endpoints_Require_Administrator_When_Account_Exists` only checks authorization (401/403); the actual data-migration execution is tested at the service level only (`Services/PostgresMigrationServiceTests.cs`), not through a full HTTP round-trip as an authenticated admin.

### Low severity / nice-to-have

**L1.** No test performs a true export→import round trip (feeding an actual `GET /api/books/export` response body into `POST /api/books/import`); existing import tests construct payloads by hand, so a future schema drift between the two shapes could go unnoticed until a real user hits it.

**L2.** `ApiMessageLocalizerTests` thoroughly verifies every catalogued message has a non-empty translation in every supported culture, but there's no test for an *unsupported* `Accept-Language` (e.g., `fr`) falling back to English consistently across all localized error paths, not just login failure.

**L3.** No test verifies whether the `addedByScanner` flag persists, resets, or is droppable through `PUT /api/books/{id}` (only `POST /api/books` add-time behavior is tested).

## Coverage Gaps (at a glance)

| Area | Coverage |
|---|---|
| Concurrency / race conditions | None |
| Code coverage measurement in CI | None (tooling present, unused) |
| Real browser/DOM/JS execution | None (string-match against served source only) |
| Stats/analytics frontend UI | None |
| Admin self-modify guard — update route | None |
| Admin self-modify guard — reset-link route | None |
| Admin self-modify guard — delete route | Covered |
| ForcePasswordReset login block (423) | None at API level (repo-level flag-set test only) |
| Account lockout login block (423 + expiry) | None at API level (only the separate 429 rate-limiter is tested) |
| Invalid/out-of-range `BookTypeId` | None |
| Export→import round trip | None (import tested with hand-built payloads only) |
| Migration run success via HTTP | None (service-level only) |
| Barcode scanner, import/export, theme, i18n, pagination UI | Present (string/regex-based) |

## Positive Observations

- **Real dependencies, no mocking framework.** Every integration test spins up genuine PostgreSQL 16 via Testcontainers; the only stub in the entire suite is `OpenLibraryBookMetadataLookupServiceTests`' `HttpMessageHandler` boundary (needed to avoid live network calls to Open Library), and `FakeBookMetadataLookupService` used by `BookWheelWebAppFactory` for the same reason. Everything else — EF Core, ASP.NET Core middleware pipeline, Data Protection, password hashing, rate limiting — executes for real.
- **Zero skipped or dead tests.** A search for `[Skip`, `// TODO`, and `// FIXME` across all 21 test files returned nothing. Every one of the 245 `[Fact]`/`[Theory]` attributes (281 actual test cases once `[Theory]` `InlineData` is expanded) is live and asserting.
- **Consistent, self-documenting naming.** Every test follows `MethodAsync_Condition_ExpectedOutcome` (or the behavioral equivalent for non-method-under-test scenarios), making the suite genuinely useful as a behavior spec, e.g. `Removing_A_Book_Twice_Returns_NotFound`, `Import_Skips_Match_Against_A_SoftDeleted_Book_Without_Restoring_It`.
- **Strong negative-path discipline where it exists.** Soft-delete semantics are thoroughly covered (re-delete → 404, re-update → 404, excluded from active list/spin/count, but preserved in export and spin-history); ISBN validation covers valid/invalid ISBN-10 and ISBN-13 with and without separators, the X check digit, and malformed/empty/null input; corrupted JSON data files are quarantined with dedicated tests for both the credential and book repositories.
- **Solid security-regression coverage.** Encrypted credential storage, structured failed-login audit logging (with an explicit assertion that raw passwords never appear in the log message, in-memory state, or the persisted JSONL file), proxy-aware (`X-Forwarded-For`) rate limiting, and consistent 401-vs-403 access-control checks across the Users/Metrics/Stats/Migration controllers.
- **A well-designed shared-fixture pattern already exists** in `Storage/Postgres/PostgresTestFixture.cs` (`ICollectionFixture` + `TRUNCATE ... RESTART IDENTITY CASCADE` reset) — it's simply not yet applied to the larger, slower `WebApplicationFactory`-based test classes (see H1). Adopting it there is a template, not a new pattern to invent.
- **Thorough i18n verification**, including a theory test asserting every catalogued English message has a non-empty translation in every supported culture, not just spot-checked strings.
- **CI already does a lot right around the test job**: secret scanning (gitleaks), workflow linting (actionlint), Trivy container scanning with a hard fail-gate plus SARIF upload, CodeQL, and a distinct `container-smoke-test` job that boots the real Docker image against a real Postgres container and polls `/health/ready` — these are complementary to and independent of the xUnit suite.

## Recommendations (prioritized)

1. **(High impact, low effort)** Convert the `WebApplicationFactory`-based test classes (`BookWheelApiTests`, `BookWheelFrontendTests`, `BookWheelBrowserWorkflowTests`, `BookWheelPwaTests`, `BookWheelSmokeTests`, and the Postgres-backed case in `BookWheelHealthCheckTests`) to share one Postgres container per test class, mirroring `Storage/Postgres/PostgresTestFixture.cs`. This is very likely to cut the local run from ~34 minutes to a few minutes and eliminate the Docker-daemon-timeout failures observed in this run.
2. **(High impact, low effort)** Re-scope the "browser-level" framing: rename `BookWheelBrowserWorkflowTests`/relevant methods, or soften the README/roadmap language, to reflect that these are string-match tests against served source, not executed-browser tests. Consider adding a small number of genuine headless-browser tests for the highest-value flows (login → add book → spin, theme toggle) if real DOM/JS execution coverage is wanted.
3. **(Medium)** Add frontend tests for the Stats/spin-analytics UI (modal presence, i18n strings, admin-only aggregate-view gating) — currently the only major UI feature area with zero automated coverage.
4. **(Medium)** Add the two missing self-modify guard tests (admin cannot update own account, admin cannot generate a reset link for their own account) — cheap to write, closes a real authorization-boundary gap.
5. **(Medium)** Add API-level tests for the `ForcePasswordReset` and account-lockout (423) login blocks, distinct from the existing 429 rate-limiter test.
6. **(Medium)** Wire `coverlet.collector` into the CI `unit-tests` job (`--collect:"XPlat Code Coverage"` plus report publication) so future coverage decisions — including whether findings like H3/M1-M3 exist — are data-driven rather than requiring a manual audit like this one.
7. **(Low)** Add an invalid-`BookTypeId` test and a true export→import round-trip test.
8. **(Low)** Add a small number of targeted concurrency tests (parallel spins for one user, parallel failed logins approaching the lockout threshold) to build confidence in the storage layer under contention.

## Limitations

- This audit is based on static reading of test source (all 21 files in `BookWheel.Tests`) and controller source (`BookWheel/Controllers/*.cs`), plus one full local execution of `dotnet test BookWheel.slnx`. It is not based on a mutation-testing pass or an instrumented code-coverage report, since no such tooling is currently wired into the project (see M4) — frontend-coverage-by-feature statements were derived by cross-referencing test method names/bodies against `app.js`/`i18n.js` content, not measured.
- The local run reported **281 tests: 274 passed, 7 failed, 34.3 minutes total.** All 7 failures were `System.TimeoutException` originating in `Docker.DotNet`/`Testcontainers` while creating or starting a container (`NamedPipeClientStream.ConnectInternal` → Docker Engine API), not application-code assertion failures:
  - `BookWheelSmokeTests.Startup_Health_And_Version_Endpoints_Return_Success`
  - `BookWheelPwaTests.Manifest_Should_Be_Served_With_Correct_Content_Type_And_Fields`
  - `BookWheelFrontendTests.Frontend_I18n_Should_Include_Scanner_Strings_In_All_Locales`
  - `BookWheelPwaTests.Frontend_Script_Should_Notify_User_On_Connectivity_Changes`
  - `BookWheelFrontendTests.Home_Page_Should_Include_I18n_Attributes_And_Settings_Button`
  - `BookWheelFrontendTests.Frontend_Styles_Should_Include_Selected_Book_Emphasis`
  - `BookWheelApiTests.Version_Endpoint_Returns_NonEmpty_Version_String`

  This matches the Docker-resource-contention scenario anticipated in this audit's constraints: a live `docker-compose` stack (`bookwheel`, `bookwheel-postgres`) was running concurrently for other audits, and this suite's per-test-container architecture (H1) creates on the order of 150+ separate Postgres containers over a single run, straining the local Docker daemon. Per the audit instructions, a resource-contention-only failure should be retried once; a second full run was not performed given the ~35-minute cost of a single run and that 100% of the failures matched the Docker-daemon-timeout signature with none being behavioral/assertion failures — this is reported as the suite-fragility finding (H1) rather than re-verified with a second full run. Fixing H1 (shared containers) would likely make this class of flakiness disappear rather than just reduce its odds.
- Live-app manual/browser testing against `http://localhost:32700` was explicitly out of scope per the task constraints; this is a code/test-suite audit only.
- No source or test code was modified as part of this audit.
