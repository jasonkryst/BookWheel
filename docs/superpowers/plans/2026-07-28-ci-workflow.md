# CI Workflow Restructure (`ci.yml`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `.github/workflows/dotnet.yml` with `.github/workflows/ci.yml`, split into independent parallel jobs mirroring ThePlayground's CI pattern, with dynamic app-version resolution and new Trivy container image scanning.

**Architecture:** One workflow file, seven `runs-on: ubuntu-latest` jobs with no `needs:` dependencies between them, so GitHub Actions schedules them all in parallel. Jobs that need the app version (`build`, `docker-build`, `container-smoke-test`, `trivy`) each independently compute it from `BookWheel.csproj` rather than sharing a value across jobs — this duplication is intentional, it keeps every job fully self-contained.

**Tech Stack:** GitHub Actions, .NET 8 SDK, Docker, `gitleaks/gitleaks-action@v2`, `aquasecurity/trivy-action@v0.36.0`, `github/codeql-action/upload-sarif@v4`.

## Global Constraints

- Triggers: `push` and `pull_request` on branch `main` only.
- Workflow-level `permissions: contents: read`; the `trivy` job additionally sets `security-events: write` at the job level.
- App version is never hardcoded in the workflow. Every job that needs `APP_VERSION` derives the base version from `BookWheel.csproj`'s `InformationalVersion` MSBuild property via:
  ```bash
  BASE_VERSION=$(dotnet msbuild BookWheel/BookWheel.csproj -getProperty:InformationalVersion -nologo | tr -d '\r\n')
  BASE_VERSION="${BASE_VERSION%%-*}"
  if ! [[ "$BASE_VERSION" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]]; then
    echo "Refusing unexpected InformationalVersion format: $BASE_VERSION" >&2
    exit 1
  fi
  echo "APP_VERSION=${BASE_VERSION}-ci.${GITHUB_RUN_NUMBER}+${GITHUB_SHA}" >> "$GITHUB_ENV"
  ```
  (`${BASE_VERSION%%-*}` strips everything from the first `-` onward, e.g. `1.3.1-local` → `1.3.1`. `BookWheel.csproj` is repository content — reachable from a `pull_request` build — so `BASE_VERSION` is validated against a strict `major.minor[.patch[.build]]` pattern and refused otherwise, before it's ever written to `$GITHUB_ENV`; an unvalidated value with an embedded newline could inject extra environment variables into later steps. `GITHUB_RUN_NUMBER`/`GITHUB_SHA` are runner-provided env vars, used instead of `${{ github.run_number }}`/`${{ github.sha }}` template interpolation into the shell script as defense-in-depth.)
- `.github/workflows/docker-release.yml` is not touched by this plan.
- No `needs:` between jobs — every job independently checks out the repo and (where relevant) rebuilds the Docker image. Do not introduce cross-job artifact sharing.
- Spec: `docs/superpowers/specs/2026-07-28-ci-workflow-design.md`

---

### Task 1: Workflow header + `secret-scan` job

**Files:**
- Modify: `.github/workflows/ci.yml` (currently an empty tracked-as-untracked 0-byte stub — this task gives it its first real content)

**Interfaces:**
- Produces: the `name:`, `on:`, and top-level `permissions:` block that every subsequent task's job gets appended under `jobs:`.

- [ ] **Step 1: Write the workflow header and `secret-scan` job**

Write this as the entire contents of `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read

jobs:
  secret-scan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Secret scanning (gitleaks)
        uses: gitleaks/gitleaks-action@v2
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

- [ ] **Step 2: Validate YAML syntax**

Run:
```bash
py -m pip install --quiet pyyaml
py -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('OK')"
```
Expected: `OK` printed, no exception.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add ci.yml with secret-scan job"
```

---

### Task 2: `build` job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: the file ends with the `secret-scan` job body written in Task 1.
- Produces: the `build` job, and establishes the version-resolution snippet reused verbatim by Tasks 5, 6, and 7.

- [ ] **Step 1: Confirm the version-resolution command works locally**

Run (from the repo root):
```bash
BASE_VERSION=$(dotnet msbuild BookWheel/BookWheel.csproj -getProperty:InformationalVersion -nologo | tr -d '\r\n')
BASE_VERSION="${BASE_VERSION%%-*}"
if ! [[ "$BASE_VERSION" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]]; then
  echo "Refusing unexpected InformationalVersion format: $BASE_VERSION" >&2
  exit 1
fi
echo "Resolved: ${BASE_VERSION}-ci.999+deadbeef"
```
Expected: prints `Resolved: 1.3.1-ci.999+deadbeef` (base version matches whatever `BookWheel.csproj`'s `InformationalVersion` default currently is, with its `-local`/`-suffix` stripped and validated against `^[0-9]+(\.[0-9]+){0,3}$`).

- [ ] **Step 2: Confirm `dotnet build` works locally with an injected version**

```bash
dotnet restore
dotnet build --no-restore /p:InformationalVersion="1.3.1-ci.999+deadbeef"
```
Expected: `Build succeeded.`

- [ ] **Step 3: Append the `build` job**

Edit `.github/workflows/ci.yml`. Find:
```yaml
      - name: Secret scanning (gitleaks)
        uses: gitleaks/gitleaks-action@v2
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```
Replace with:
```yaml
      - name: Secret scanning (gitleaks)
        uses: gitleaks/gitleaks-action@v2
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Resolve app version
        run: |
          BASE_VERSION=$(dotnet msbuild BookWheel/BookWheel.csproj -getProperty:InformationalVersion -nologo | tr -d '\r\n')
          BASE_VERSION="${BASE_VERSION%%-*}"
          if ! [[ "$BASE_VERSION" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]]; then
            echo "Refusing unexpected InformationalVersion format: $BASE_VERSION" >&2
            exit 1
          fi
          echo "APP_VERSION=${BASE_VERSION}-ci.${GITHUB_RUN_NUMBER}+${GITHUB_SHA}" >> "$GITHUB_ENV"
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore /p:InformationalVersion="${{ env.APP_VERSION }}"
```

- [ ] **Step 4: Validate YAML syntax**

Run:
```bash
py -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('OK')"
```
Expected: `OK`.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add build job with dynamic app version"
```

---

### Task 3: `unit-tests` job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: file ends with the `build` job body from Task 2.
- Produces: the `unit-tests` job (single full `dotnet test` run, no duplicate filtered reruns).

- [ ] **Step 1: Confirm `dotnet test` passes locally**

```bash
dotnet restore
dotnet test --verbosity normal
```
Expected: all tests pass (`Passed!` summary, 0 failed).

- [ ] **Step 2: Append the `unit-tests` job**

Edit `.github/workflows/ci.yml`. Find:
```yaml
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore /p:InformationalVersion="${{ env.APP_VERSION }}"
```
Replace with:
```yaml
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore /p:InformationalVersion="${{ env.APP_VERSION }}"

  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Restore dependencies
        run: dotnet restore
      - name: Test
        run: dotnet test --verbosity normal
```

- [ ] **Step 3: Validate YAML syntax**

```bash
py -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('OK')"
```
Expected: `OK`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add unit-tests job"
```

---

### Task 4: `dependency-audit` job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: file ends with the `unit-tests` job body from Task 3.
- Produces: the `dependency-audit` job.

- [ ] **Step 1: Confirm the vulnerability scan commands work locally**

```bash
dotnet restore
dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive
dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive
```
Expected: both commands complete without error (reporting "no vulnerable packages" or listing findings — either way, exit code 0; `dotnet list --vulnerable` only exits non-zero on a malformed command, not on findings).

- [ ] **Step 2: Append the `dependency-audit` job**

Edit `.github/workflows/ci.yml`. Find:
```yaml
      - name: Restore dependencies
        run: dotnet restore
      - name: Test
        run: dotnet test --verbosity normal
```
Replace with:
```yaml
      - name: Restore dependencies
        run: dotnet restore
      - name: Test
        run: dotnet test --verbosity normal

  dependency-audit:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Restore dependencies
        run: dotnet restore
      - name: Security vulnerability scan
        run: |
          dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive
          dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive
```

- [ ] **Step 3: Validate YAML syntax**

```bash
py -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('OK')"
```
Expected: `OK`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add dependency-audit job"
```

---

### Task 5: `docker-build` job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: file ends with the `dependency-audit` job body from Task 4.
- Produces: the `docker-build` job. Establishes the `bookwheel:${{ github.sha }}` image tag convention reused by Task 6 and Task 7 (each job rebuilds under this same tag independently — it's local to each job's runner, not shared).

- [ ] **Step 1: Confirm the Docker build works locally**

```bash
docker build --build-arg APP_VERSION=1.3.1-ci.999+deadbeef -t bookwheel:local-test .
```
Expected: `Successfully tagged` (or BuildKit's equivalent final success line), exit code 0.

- [ ] **Step 2: Append the `docker-build` job**

Edit `.github/workflows/ci.yml`. Find:
```yaml
      - name: Security vulnerability scan
        run: |
          dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive
          dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive
```
Replace with:
```yaml
      - name: Security vulnerability scan
        run: |
          dotnet list BookWheel/BookWheel.csproj package --vulnerable --include-transitive
          dotnet list BookWheel.Tests/BookWheel.Tests.csproj package --vulnerable --include-transitive

  docker-build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Resolve app version
        run: |
          BASE_VERSION=$(dotnet msbuild BookWheel/BookWheel.csproj -getProperty:InformationalVersion -nologo | tr -d '\r\n')
          BASE_VERSION="${BASE_VERSION%%-*}"
          if ! [[ "$BASE_VERSION" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]]; then
            echo "Refusing unexpected InformationalVersion format: $BASE_VERSION" >&2
            exit 1
          fi
          echo "APP_VERSION=${BASE_VERSION}-ci.${GITHUB_RUN_NUMBER}+${GITHUB_SHA}" >> "$GITHUB_ENV"
      - name: Build Docker image
        run: docker build --build-arg APP_VERSION=${{ env.APP_VERSION }} -t bookwheel:${{ github.sha }} .
```

- [ ] **Step 3: Validate YAML syntax**

```bash
py -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('OK')"
```
Expected: `OK`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add docker-build job"
```

---

### Task 6: `container-smoke-test` job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: file ends with the `docker-build` job body from Task 5; reuses the same version-resolution snippet and `bookwheel:${{ github.sha }}` tag convention.
- Produces: the `container-smoke-test` job.

- [ ] **Step 1: Confirm the smoke-test steps work locally**

```bash
docker build --build-arg APP_VERSION=1.3.1-ci.999+deadbeef -t bookwheel:local-test .
docker run -d --name bookwheel-smoke-local -p 18080:8080 bookwheel:local-test
for i in {1..20}; do
  if curl -fsS http://127.0.0.1:18080/health/ready > /dev/null; then
    echo "ready"
    break
  fi
  sleep 2
done
docker rm -f bookwheel-smoke-local
```
Expected: prints `ready` within 20 attempts, container removed cleanly at the end.

- [ ] **Step 2: Append the `container-smoke-test` job**

Edit `.github/workflows/ci.yml`. Find:
```yaml
      - name: Build Docker image
        run: docker build --build-arg APP_VERSION=${{ env.APP_VERSION }} -t bookwheel:${{ github.sha }} .
```
Replace with:
```yaml
      - name: Build Docker image
        run: docker build --build-arg APP_VERSION=${{ env.APP_VERSION }} -t bookwheel:${{ github.sha }} .

  container-smoke-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Resolve app version
        run: |
          BASE_VERSION=$(dotnet msbuild BookWheel/BookWheel.csproj -getProperty:InformationalVersion -nologo | tr -d '\r\n')
          BASE_VERSION="${BASE_VERSION%%-*}"
          if ! [[ "$BASE_VERSION" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]]; then
            echo "Refusing unexpected InformationalVersion format: $BASE_VERSION" >&2
            exit 1
          fi
          echo "APP_VERSION=${BASE_VERSION}-ci.${GITHUB_RUN_NUMBER}+${GITHUB_SHA}" >> "$GITHUB_ENV"
      - name: Build Docker image
        run: docker build --build-arg APP_VERSION=${{ env.APP_VERSION }} -t bookwheel:${{ github.sha }} .
      - name: Container startup smoke verification
        run: |
          docker run -d --name bookwheel-smoke -p 18080:8080 bookwheel:${{ github.sha }}
          for i in {1..20}; do
            if curl -fsS http://127.0.0.1:18080/health/ready > /dev/null; then
              echo "ready"
              exit 0
            fi
            sleep 2
          done
          echo "Container failed readiness check"
          docker logs bookwheel-smoke
          exit 1
      - name: Cleanup smoke container
        if: always()
        run: docker rm -f bookwheel-smoke || true
```

**Note:** this appends a *second* `- name: Build Docker image` step under a different job — that's expected, it's `docker-build`'s step followed by `container-smoke-test`'s own copy. Use enough surrounding context (the whole job block, as shown) when applying the edit so you don't accidentally match the wrong occurrence.

- [ ] **Step 3: Validate YAML syntax**

```bash
py -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('OK')"
```
Expected: `OK`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add container-smoke-test job"
```

---

### Task 7: `trivy` job

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: file ends with the `container-smoke-test` job body from Task 6.
- Produces: the `trivy` job — the only job with a job-level `permissions:` override (`security-events: write`).

- [ ] **Step 1: Append the `trivy` job**

There is no local equivalent of `trivy` or the SARIF upload to test standalone (Trivy isn't installed locally and SARIF upload requires a real GitHub Actions run) — the Docker build step itself was already verified working in Task 5/6, so this task is YAML-syntax-verified only; full behavior is confirmed when the workflow runs on GitHub after Task 8's push.

Edit `.github/workflows/ci.yml`. Find the *last* occurrence of:
```yaml
      - name: Cleanup smoke container
        if: always()
        run: docker rm -f bookwheel-smoke || true
```
Replace with:
```yaml
      - name: Cleanup smoke container
        if: always()
        run: docker rm -f bookwheel-smoke || true

  trivy:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      security-events: write
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Resolve app version
        run: |
          BASE_VERSION=$(dotnet msbuild BookWheel/BookWheel.csproj -getProperty:InformationalVersion -nologo | tr -d '\r\n')
          BASE_VERSION="${BASE_VERSION%%-*}"
          if ! [[ "$BASE_VERSION" =~ ^[0-9]+(\.[0-9]+){0,3}$ ]]; then
            echo "Refusing unexpected InformationalVersion format: $BASE_VERSION" >&2
            exit 1
          fi
          echo "APP_VERSION=${BASE_VERSION}-ci.${GITHUB_RUN_NUMBER}+${GITHUB_SHA}" >> "$GITHUB_ENV"
      - name: Build Docker image
        run: docker build --build-arg APP_VERSION=${{ env.APP_VERSION }} -t bookwheel:${{ github.sha }} .
      - name: Vulnerability gate (fixable CRITICAL/HIGH findings)
        uses: aquasecurity/trivy-action@v0.36.0
        with:
          image-ref: bookwheel:${{ github.sha }}
          format: table
          severity: CRITICAL,HIGH
          ignore-unfixed: true
          exit-code: 1
      - name: Full vulnerability report (all severities, including unfixed)
        if: always()
        uses: aquasecurity/trivy-action@v0.36.0
        with:
          image-ref: bookwheel:${{ github.sha }}
          format: sarif
          output: trivy-results.sarif
      - name: Upload scan results to the Security tab
        if: always()
        uses: github/codeql-action/upload-sarif@v4
        with:
          sarif_file: trivy-results.sarif
```

- [ ] **Step 2: Validate YAML syntax**

```bash
py -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('OK')"
```
Expected: `OK`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add trivy container image scan job"
```

---

### Task 8: Remove `dotnet.yml`, final review

**Files:**
- Delete: `.github/workflows/dotnet.yml`
- Read (no changes): `.github/workflows/ci.yml`, `.github/workflows/docker-release.yml`

**Interfaces:**
- Consumes: the complete `ci.yml` produced by Tasks 1–7.

- [ ] **Step 1: Delete the superseded workflow**

```bash
git rm .github/workflows/dotnet.yml
```

- [ ] **Step 2: Re-validate full YAML syntax and job count**

```bash
py -c "
import yaml
doc = yaml.safe_load(open('.github/workflows/ci.yml'))
jobs = list(doc['jobs'].keys())
assert jobs == ['secret-scan', 'build', 'unit-tests', 'dependency-audit', 'docker-build', 'container-smoke-test', 'trivy'], jobs
print('OK, jobs:', jobs)
"
```
Expected: `OK, jobs: ['secret-scan', 'build', 'unit-tests', 'dependency-audit', 'docker-build', 'container-smoke-test', 'trivy']`.

- [ ] **Step 3: Confirm `docker-release.yml` is untouched**

```bash
git status .github/workflows/docker-release.yml
```
Expected: no output (clean, not modified) — this file must not appear in the diff for this branch's CI work.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/dotnet.yml
git commit -m "ci: remove dotnet.yml, superseded by ci.yml"
```

- [ ] **Step 5: Push and watch the real Actions run**

```bash
git push -u origin 17
```
Then open the repo's Actions tab (or `gh run watch` after the push triggers a run) and confirm all seven `ci.yml` jobs go green. This is the only way to validate the `secret-scan` and `trivy` jobs end-to-end, since gitleaks and Trivy aren't installed locally.

---

## Self-Review Notes

- **Spec coverage:** all seven jobs from the spec's table are covered (Tasks 1–7); dynamic versioning via `BookWheel.csproj` is covered (Global Constraints + Tasks 2/5/6/7); deleting `dotnet.yml` and leaving `docker-release.yml` untouched is covered (Task 8); no `needs:` dependencies anywhere (confirmed — no job in Tasks 1–7 declares `needs:`).
- **Placeholder scan:** none — every step has literal file contents or literal commands, no "TBD"/"similar to above".
- **Type/name consistency:** `APP_VERSION` env var name, `bookwheel:${{ github.sha }}` image tag, and job names (`secret-scan`, `build`, `unit-tests`, `dependency-audit`, `docker-build`, `container-smoke-test`, `trivy`) are identical everywhere they're referenced across tasks.
