# CI Workflow Restructure (`ci.yml`) — Design

## Context

GitHub issue [#17](https://github.com/jasonkryst/BookWheel/issues/17) asks for a `ci.yml`
workflow modeled on the one in [ThePlayground](https://github.com/jasonkryst/ThePlayground/tree/main/.github/workflows).
ThePlayground's `ci.yml` splits CI into independent parallel jobs (`lint`, `lint-css`,
`unit-tests`, `build`, `e2e`, `docker-build`, `npm-audit`, `trivy`, `lighthouse`) instead of
one sequential job, giving faster feedback and clearer per-concern pass/fail status.

BookWheel already has `.github/workflows/dotnet.yml`, which covers build, test, dependency
vulnerability scanning, secret scanning, and a Docker container smoke test — but as a single
sequential job. It does not have container image vulnerability scanning (Trivy).

## Goal

Replace `.github/workflows/dotnet.yml` with `.github/workflows/ci.yml`, restructured into
independent parallel jobs mirroring ThePlayground's pattern, adapted for the .NET/Docker stack
(BookWheel has no JS/SPA tooling, so `lint-css` and `lighthouse` have no equivalent here and are
out of scope). Add Trivy container image scanning, which BookWheel doesn't have today.

## Jobs

| Job | Steps | Maps to ThePlayground's... |
|---|---|---|
| `secret-scan` | `gitleaks/gitleaks-action@v2` | `lint` (fast static check) |
| `build` | `actions/setup-dotnet@v4` → `dotnet restore` → `dotnet build` with `InformationalVersion` set | `build` |
| `unit-tests` | `dotnet restore` → `dotnet test` (single full run) | `unit-tests` |
| `dependency-audit` | `dotnet list package --vulnerable --include-transitive` for `BookWheel/BookWheel.csproj` and `BookWheel.Tests/BookWheel.Tests.csproj` | `npm-audit` |
| `docker-build` | `docker build` the image (validates the Dockerfile; image not pushed or persisted) | `docker-build` |
| `container-smoke-test` | `docker build` → `docker run` → poll `GET /health/ready` via curl (20 attempts / 2s) → `docker rm -f` cleanup (`if: always()`) | `e2e` (exercises the real running app over HTTP) |
| `trivy` | `docker build` → `aquasecurity/trivy-action` gate on fixable CRITICAL/HIGH (`exit-code: 1`) → full SARIF report (`if: always()`) → `github/codeql-action/upload-sarif@v4` | `trivy` |

All jobs run independently in parallel — no `needs:` dependencies between them. `docker-build`,
`container-smoke-test`, and `trivy` each run their own `docker build` rather than sharing an
image via artifacts, matching ThePlayground's pattern (keeps jobs fully independent, trades some
CI minutes for isolation).

## Explicitly out of scope

- `lint` / `lint-css` (ESLint/stylelint) and `lighthouse` (Lighthouse CI): these are JS/SPA-specific
  and BookWheel has no corresponding tooling or config. Adding `dotnet format` linting or a
  performance budget tool would be new scope beyond "restructure CI like ThePlayground's" and is
  not requested by issue #17.
- The unrelated, already-in-flight `Dockerfile` `APP_VERSION` bump on this branch is not part of
  this issue.
- `docker-release.yml` (the release-publish workflow) is untouched; this design only covers
  CI-on-push/PR.

## Simplifications vs. today's `dotnet.yml`

- Today's workflow runs the full `dotnet test` suite, then *also* reruns two named filtered
  subsets ("Security focused regression tests", "Smoke tests") as separate steps — executing
  those tests twice. The new `unit-tests` job runs the full suite once; the redundant filtered
  reruns are dropped.

## Triggers, permissions, versioning

- Triggers: `push` and `pull_request` on `main` (unchanged from today).
- Permissions: workflow-level `contents: read`; the `trivy` job additionally needs
  `security-events: write` to upload its SARIF report to the Security tab.
- Versioning: today's `dotnet.yml` hardcodes `APP_VERSION: 1.3.1-ci.${{ github.run_number }}+${{
  github.sha }}`, duplicating the version already recorded in `BookWheel.csproj`'s
  `InformationalVersion` default (`1.3.1-local`) — the two drift unless someone remembers to bump
  both. Instead, jobs that need `APP_VERSION` (`build`, `docker-build`, `container-smoke-test`,
  `trivy`) derive the base version dynamically from the csproj:
  `dotnet msbuild BookWheel/BookWheel.csproj -getProperty:InformationalVersion`, strip the
  trailing `-local` (or any `-suffix`), then append `-ci.${{ github.run_number }}+${{ github.sha
  }}`. `BookWheel.csproj` becomes the single source of truth for the app's base version; bumping
  it there is enough, with no CI file edit required.

## File changes

- Delete `.github/workflows/dotnet.yml`
- Write `.github/workflows/ci.yml` (an empty untracked stub already exists at this path and will
  be filled in)
- `.github/workflows/docker-release.yml` is untouched
