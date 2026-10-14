# Book Wheel

[![CI](https://github.com/jasonkryst/BookWheel/actions/workflows/ci.yml/badge.svg)](https://github.com/jasonkryst/BookWheel/actions/workflows/ci.yml)
[![Copilot](https://github.com/jasonkryst/BookWheel/actions/workflows/agents/copilot-pull-request-reviewer/badge.svg)](https://github.com/jasonkryst/BookWheel/actions/workflows/agents/copilot-pull-request-reviewer)
[![Docker Release](https://github.com/jasonkryst/BookWheel/actions/workflows/docker-release.yml/badge.svg)](https://github.com/jasonkryst/BookWheel/actions/workflows/docker-release.yml)

Book Wheel is a self-hosted .NET 10 web app for managing a list of books and
spinning a wheel to pick a title at random. Scan or look up a book by ISBN or
title, track spin history and stats, and install it as an offline-capable PWA
— in English, Spanish, or Polish.

This solution is split into separate application and test projects:

- `BookWheel/` — the ASP.NET Core web application (API + static frontend)
- `BookWheel.Tests/` — integration tests
- `BookWheel.slnx` — ties both projects together

## Quick Start

Requires Docker Desktop (or Docker Engine) and, for anything beyond a quick
local try, a `.env` file next to `docker-compose.yml` overriding the dev-only
default Postgres passwords.

```bash
docker-compose up --build
```

Open `http://localhost:32700` and create the first account — it's
automatically made an administrator. See the [Self-Hosting Guide](docs/self-hosting.md)
for building/running without Compose, the full environment variable
reference, reverse-proxy setup, backups, and upgrades.

## Documentation

- [User Guide](docs/user-guide.md) — features, internationalization, themes, PWA install, import/export
- [Admin Guide](docs/admin-guide.md) — first-run setup, managing users, email delivery, legacy data migration
- [Self-Hosting Guide](docs/self-hosting.md) — Docker/Compose, environment variables, reverse proxies, backups, upgrades, troubleshooting
- [Architecture and Contributing](docs/architecture.md) — solution structure, API reference, testing, CI
- [CHANGELOG.md](CHANGELOG.md) — notable changes by version, with GitHub issue references
- [SECURITY_AUDIT_REPORT.md](SECURITY_AUDIT_REPORT.md) — latest audit summary and remediation priorities
- [IMPROVEMENT_ROADMAP.md](IMPROVEMENT_ROADMAP.md) — forward-looking roadmap

## License

[MIT](LICENSE) — see the `LICENSE` file for the full text.
