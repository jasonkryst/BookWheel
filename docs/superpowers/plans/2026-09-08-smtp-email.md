# SMTP Email Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add SMTP-based email delivery for password-reset links and forgotten-username lookups, make `Email` a unique, required-for-new-accounts field on `users`, and build the plumbing (`IEmailSender`/`AccountEmailService`) so future email types are cheap to add.

**Architecture:** A new `EmailOptions`-configured `SmtpEmailSender` (MailKit) implements a minimal `IEmailSender` interface; `AccountEmailService` sits on top with one method per email type (password-reset, forgotten-username) and is injected into `AuthService`. `AuthService.CreatePasswordResetLinkAsync` (the existing admin-triggered flow) gains an emailing side effect that both it and two new public self-service endpoints (`POST /api/auth/password-reset/request`, `POST /api/auth/forgot-username`) share; both new endpoints are rate-limited and always return the same generic response regardless of whether the account/email exists, to prevent enumeration. `Email` becomes a new nullable-but-unique `citext` column on `users` (both the `JsonCredentialRepository` legacy backend and the active `PostgresCredentialRepository`), required on `POST /api/auth/setup` (via a new `SetupAccountRequest`, split out of the login-shared `LoginRequest`) and `POST /api/users`, optional/backfillable via `PUT /api/users/{id}`.

**Tech Stack:** ASP.NET Core 10 (C#), EF Core 9 + Npgsql (PostgreSQL), MailKit (new dependency, SMTP client), xUnit + Testcontainers, vanilla JS frontend (no framework), i18n via a hand-rolled catalog in `js/i18n.js`.

**Spec:** `docs/superpowers/specs/2026-09-08-smtp-email-design.md`

## Global Constraints

- `ICredentialRepository` is implemented by both `PostgresCredentialRepository` (active) and `JsonCredentialRepository` (legacy, still compiled) — any interface signature change must be applied to both, and `PostgresMigrationService`'s one-time JSON→Postgres copy step must carry the new field too.
- `Email` is globally **unique** (citext unique index on Postgres + an app-level duplicate check on both backends, mirroring how `Username` uniqueness already works) but **nullable** — Postgres unique indexes never compare two `NULL`s as equal, so multiple legacy accounts may have `Email IS NULL` simultaneously without violating the constraint.
- Every new API-facing validation error message (used via `[Required(ErrorMessage = "...")]`, `[EmailAddress(ErrorMessage = "...")]`, or thrown as `InvalidOperationException` and passed through `_errors.Localize(ex.Message)`) must be added to `ApiMessageLocalizer.KeysByEnglishMessage` and to all three `BookWheel/Resources/SharedErrors*.resx` files (`en` base, `es`, `pl`) — see `InvalidBookInfoProvider` for the exact shape to copy. `BookWheel.Tests/Services/ApiMessageLocalizerTests.cs` already has a data-driven theory test (`Localize_HasNonEmptyTranslation_ForEverySupportedCulture`) that iterates every cataloged key and fails if an `es`/`pl` translation is missing or identical to the English text — no new test code is needed for this, but the resx entries must be real translations, not copies of the English string.
- `IEmailSender.SendAsync` must never throw — `SmtpEmailSender` logs and swallows every failure (unconfigured host, connection failure, auth failure, send failure). Nothing about email delivery success/failure may ever be observable from an HTTP response.
- `POST /api/auth/password-reset/request` and `POST /api/auth/forgot-username` must always return the same generic `200` body regardless of whether the account/email exists, is disabled, or was rate-limited. Never branch the HTTP response on any of those conditions.
- Setting up an account (`POST /api/auth/setup`) must NOT require an email is not a login-time concept — `LoginRequest` (still used by `POST /api/auth/login`) is untouched; a new `SetupAccountRequest` carries the required `Email` for setup only.
- Version: bump `BookWheel/BookWheel.csproj`'s `InformationalVersion` from `2.18.0` to `3.0.0` (Task 8) — a **major** bump, not minor, because email becomes a required field on every new-account creation path (a behavior change to an existing public contract), on top of adding new unauthenticated endpoints.

---

### Task 1: Email subsystem foundation — EmailOptions, IEmailSender, SmtpEmailSender

**Files:**
- Modify: `BookWheel/BookWheel.csproj` (add MailKit package reference)
- Create: `BookWheel/Models/EmailOptions.cs`
- Create: `BookWheel/Services/IEmailSender.cs`
- Create: `BookWheel/Services/SmtpEmailSender.cs`
- Modify: `BookWheel/Program.cs` (DI registration)
- Modify: `BookWheel/appsettings.json` (add `Smtp` section)
- Modify: `docker-compose.yml` (pass through `Smtp__*` env vars)
- Test: `BookWheel.Tests/Services/SmtpEmailSenderTests.cs` (new)

**Interfaces:**
- Produces: `EmailOptions { string Host; int Port; string Username; string Password; bool EnableSsl; string FromAddress; string FromName; }`, section name `"Smtp"`. `IEmailSender.SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default)` — Task 2's `AccountEmailService` depends on this interface.

- [ ] **Step 1: Add the MailKit package reference**

Run: `dotnet add BookWheel/BookWheel.csproj package MailKit`

Expected: adds a `<PackageReference Include="MailKit" Version="X.Y.Z" />` line to `BookWheel/BookWheel.csproj` (the exact resolved version depends on what's current — note the version that lands in the diff). Run `dotnet build BookWheel/BookWheel.csproj` afterward to confirm it restores and builds cleanly with no version-conflict warnings (this project pins `Microsoft.EntityFrameworkCore*` to exact versions specifically because of a past transitive-version conflict — MailKit/MimeKit have no such dependency on EF Core, so no conflict is expected, but confirm the build is clean before moving on).

- [ ] **Step 2: Add `EmailOptions`**

Create `BookWheel/Models/EmailOptions.cs`:

```csharp
namespace BookWheel.Models;

public sealed class EmailOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Book Wheel";
}
```

- [ ] **Step 3: Add `IEmailSender`**

Create `BookWheel/Services/IEmailSender.cs`:

```csharp
namespace BookWheel.Services;

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write the failing tests for `SmtpEmailSender`**

Create `BookWheel.Tests/Services/SmtpEmailSenderTests.cs`:

```csharp
using BookWheel.Models;
using BookWheel.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookWheel.Tests.Services;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_When_Host_Not_Configured_Does_Not_Throw()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions()), NullLogger<SmtpEmailSender>.Instance);

        var exception = await Record.ExceptionAsync(() => sender.SendAsync("someone@example.com", "Subject", "Body"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_When_Connection_Fails_Does_Not_Throw()
    {
        // Port 1 on loopback has no listener in any normal environment, so this
        // deterministically exercises the real MailKit connect-failure path
        // (connection refused) without needing a live or fake SMTP server.
        var options = new EmailOptions
        {
            Host = "127.0.0.1",
            Port = 1,
            FromAddress = "noreply@bookwheel.example",
            FromName = "Book Wheel"
        };
        var sender = new SmtpEmailSender(Options.Create(options), NullLogger<SmtpEmailSender>.Instance);

        var exception = await Record.ExceptionAsync(() => sender.SendAsync("someone@example.com", "Subject", "Body"));

        Assert.Null(exception);
    }
}
```

- [ ] **Step 5: Run the new tests to verify they fail (the class doesn't exist yet)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~SmtpEmailSenderTests"`
Expected: FAIL to compile — `SmtpEmailSender` doesn't exist yet.

- [ ] **Step 6: Implement `SmtpEmailSender`**

Create `BookWheel/Services/SmtpEmailSender.cs`:

```csharp
using BookWheel.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BookWheel.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogWarning("Email send skipped because Smtp:Host is not configured. Intended recipient {ToAddress}.", toAddress);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
            message.To.Add(MailboxAddress.Parse(toAddress));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = plainTextBody };

            using var client = new SmtpClient();
            var secureSocketOptions = _options.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_options.Host, _options.Port, secureSocketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            // Deliberately broad: an SMTP failure (bad config, network outage,
            // auth failure, rejected recipient) must never propagate into a
            // caller's HTTP response — see Global Constraints. Logged, not rethrown.
            _logger.LogError(ex, "Failed to send email to {ToAddress}.", toAddress);
        }
    }
}
```

- [ ] **Step 7: Run the new tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~SmtpEmailSenderTests"`
Expected: PASS (both tests).

- [ ] **Step 8: Register `EmailOptions` and `IEmailSender` in `Program.cs`**

In `BookWheel/Program.cs`, add to the `Configure<...>` block (right after the existing `Configure<BookMetadataOptions>` line):

```csharp
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
```

Add near the other singleton service registrations (right after `builder.Services.AddSingleton<AppMetricsService>();`):

```csharp
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
```

- [ ] **Step 9: Add the `Smtp` section to `appsettings.json`**

In `BookWheel/appsettings.json`, add a new top-level section (after `"Analytics"`):

```json
  "Smtp": {
    "Host": "",
    "Port": 587,
    "Username": "",
    "Password": "",
    "EnableSsl": true,
    "FromAddress": "",
    "FromName": "Book Wheel"
  }
```

- [ ] **Step 10: Pass SMTP env vars through in `docker-compose.yml`**

In `docker-compose.yml`, find the `bookwheel` app service's `environment:` block (where `BookMetadata__GoogleBooks__ApiKey: "${GOOGLE_BOOKS_API_KEY:-}"` lives) and add, right after it:

```yaml
      # Set SMTP_HOST/SMTP_PORT/SMTP_USERNAME/SMTP_PASSWORD/SMTP_FROM_ADDRESS in
      # your own .env file to enable outbound email (password reset / forgotten
      # username links) — see README's "Email (SMTP)" section. Email delivery is
      # silently skipped (logged only) when SMTP_HOST is unset.
      Smtp__Host: "${SMTP_HOST:-}"
      Smtp__Port: "${SMTP_PORT:-587}"
      Smtp__Username: "${SMTP_USERNAME:-}"
      Smtp__Password: "${SMTP_PASSWORD:-}"
      Smtp__FromAddress: "${SMTP_FROM_ADDRESS:-}"
```

- [ ] **Step 11: Build and run the full test suite**

Run: `dotnet build BookWheel.slnx`
Expected: builds successfully.

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: all existing tests still pass, plus the two new `SmtpEmailSenderTests`.

- [ ] **Step 12: Commit**

```bash
git add BookWheel/BookWheel.csproj BookWheel/Models/EmailOptions.cs BookWheel/Services/IEmailSender.cs BookWheel/Services/SmtpEmailSender.cs BookWheel/Program.cs BookWheel/appsettings.json docker-compose.yml BookWheel.Tests/Services/SmtpEmailSenderTests.cs
git commit -m "Add SMTP email sending foundation: EmailOptions, IEmailSender, SmtpEmailSender (GH #115)"
```

---

### Task 2: AccountEmailService and FakeEmailSender test double

**Files:**
- Create: `BookWheel/Services/AccountEmailService.cs`
- Create: `BookWheel.Tests/Services/FakeEmailSender.cs`
- Modify: `BookWheel/Program.cs` (DI registration)
- Test: `BookWheel.Tests/Services/AccountEmailServiceTests.cs` (new)

**Interfaces:**
- Consumes: `IEmailSender` (Task 1).
- Produces: `AccountEmailService` with `Task SendPasswordResetEmailAsync(string toAddress, string username, string resetLink, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)` and `Task SendForgottenUsernameEmailAsync(string toAddress, string username, CancellationToken cancellationToken = default)` — Task 4's `AuthService` calls both directly. `FakeEmailSender : IEmailSender` with a public `List<(string ToAddress, string Subject, string Body)> SentEmails` — Task 6 reuses this in `BookWheelWebAppFactory` to assert on emails sent during integration tests.

- [ ] **Step 1: Add the `FakeEmailSender` test double**

Create `BookWheel.Tests/Services/FakeEmailSender.cs`:

```csharp
using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string ToAddress, string Subject, string Body)> SentEmails { get; } = [];

    public Task SendAsync(string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default)
    {
        SentEmails.Add((toAddress, subject, plainTextBody));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Write the failing tests for `AccountEmailService`**

Create `BookWheel.Tests/Services/AccountEmailServiceTests.cs`:

```csharp
using BookWheel.Services;

namespace BookWheel.Tests.Services;

public sealed class AccountEmailServiceTests
{
    [Fact]
    public async Task SendPasswordResetEmailAsync_Sends_Email_With_Link_And_Expiry()
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);
        var expiresAt = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        await service.SendPasswordResetEmailAsync("reader@example.com", "reader-one", "https://bookwheel.example/?resetToken=abc123", expiresAt);

        var sent = Assert.Single(fakeSender.SentEmails);
        Assert.Equal("reader@example.com", sent.ToAddress);
        Assert.Contains("reader-one", sent.Body, StringComparison.Ordinal);
        Assert.Contains("https://bookwheel.example/?resetToken=abc123", sent.Body, StringComparison.Ordinal);
        Assert.Contains("2026-09-09", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendForgottenUsernameEmailAsync_Sends_Email_With_Username()
    {
        var fakeSender = new FakeEmailSender();
        var service = new AccountEmailService(fakeSender);

        await service.SendForgottenUsernameEmailAsync("reader@example.com", "reader-one");

        var sent = Assert.Single(fakeSender.SentEmails);
        Assert.Equal("reader@example.com", sent.ToAddress);
        Assert.Contains("reader-one", sent.Body, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~AccountEmailServiceTests"`
Expected: FAIL to compile — `AccountEmailService` doesn't exist yet.

- [ ] **Step 4: Implement `AccountEmailService`**

Create `BookWheel/Services/AccountEmailService.cs`:

```csharp
namespace BookWheel.Services;

public sealed class AccountEmailService
{
    private readonly IEmailSender _emailSender;

    public AccountEmailService(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public Task SendPasswordResetEmailAsync(string toAddress, string username, string resetLink, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        const string subject = "Reset your Book Wheel password";
        var body =
            $"Hello {username},\n\n" +
            "A password reset was requested for your Book Wheel account. Use the link below to set a new password:\n\n" +
            $"{resetLink}\n\n" +
            $"This link expires at {expiresAtUtc:u}.\n\n" +
            "If you did not request this, you can safely ignore this email.";

        return _emailSender.SendAsync(toAddress, subject, body, cancellationToken);
    }

    public Task SendForgottenUsernameEmailAsync(string toAddress, string username, CancellationToken cancellationToken = default)
    {
        const string subject = "Your Book Wheel username";
        var body =
            "Hello,\n\n" +
            "You (or someone using this email address) requested a reminder of your Book Wheel username.\n\n" +
            $"Your username is: {username}\n\n" +
            "If you did not request this, you can safely ignore this email.";

        return _emailSender.SendAsync(toAddress, subject, body, cancellationToken);
    }
}
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~AccountEmailServiceTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Register `AccountEmailService` in `Program.cs`**

In `BookWheel/Program.cs`, add right after the `IEmailSender` registration from Task 1:

```csharp
builder.Services.AddSingleton<AccountEmailService>();
```

- [ ] **Step 7: Build and run the full test suite**

Run: `dotnet build BookWheel.slnx && dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: builds and all tests pass.

- [ ] **Step 8: Commit**

```bash
git add BookWheel/Services/AccountEmailService.cs BookWheel.Tests/Services/FakeEmailSender.cs BookWheel/Program.cs BookWheel.Tests/Services/AccountEmailServiceTests.cs
git commit -m "Add AccountEmailService and FakeEmailSender test double (GH #115)"
```

---

### Task 3: Credential repository — Email field, uniqueness, and lookups (both backends)

**Files:**
- Modify: `BookWheel/Models/CredentialRecord.cs`
- Modify: `BookWheel/Models/UserAccountSummary.cs`
- Modify: `BookWheel/Storage/ICredentialRepository.cs`
- Modify: `BookWheel/Storage/JsonCredentialRepository.cs`
- Modify: `BookWheel/Storage/Postgres/Entities/UserEntity.cs`
- Modify: `BookWheel/Storage/Postgres/BookWheelDbContext.cs`
- Modify: `BookWheel/Storage/Postgres/PostgresCredentialRepository.cs`
- Modify: `BookWheel/Services/PostgresMigrationService.cs` (carry `Email` through the legacy-JSON-to-Postgres copy)
- Create (via `dotnet ef migrations add`): `BookWheel/Migrations/<timestamp>_AddUserEmail.cs` and `.Designer.cs`
- Modify (auto-generated): `BookWheel/Migrations/BookWheelDbContextModelSnapshot.cs`
- Modify: `BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs` (fix compile-breaking call sites, add new coverage)
- Modify: `BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs` (same)
- Modify: `BookWheel.Tests/Services/PostgresMigrationServiceTests.cs` (fix compile-breaking call sites)

**Interfaces:**
- Produces: `CredentialRecord.Email` (`string?`), `UserAccountSummary.Email` (`string?`). `ICredentialRepository.CreateInitialAccountAsync(string username, string password, string email)`, `.CreateUserAsync(string username, bool isAdmin, string email)`, `.UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked, string? email)` (the 6-arg overload's signature changes to 7-arg; the 3-arg `UpdateUserAsync(Guid, string, bool)` overload is untouched), `.FindByUsernameAsync(string username)` returning `Task<CredentialRecord?>`, `.FindUsernameByEmailAsync(string email)` returning `Task<string?>`. Task 4's `AuthService` and Task 5's controllers call all of these.

- [ ] **Step 1: Add `Email` to the model classes**

In `BookWheel/Models/CredentialRecord.cs`, add after `Username`:

```csharp
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
```

In `BookWheel/Models/UserAccountSummary.cs`, add after `Username`:

```csharp
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
```

- [ ] **Step 2: Update `ICredentialRepository`**

Replace the full contents of `BookWheel/Storage/ICredentialRepository.cs`:

```csharp
using BookWheel.Models;

namespace BookWheel.Storage;

public interface ICredentialRepository
{
    Task<bool> HasAccountAsync();
    Task<CredentialRecord> CreateInitialAccountAsync(string username, string password, string email);
    Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password);
    Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync();
    Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin, string email);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked, string? email);
    Task<UserAccountSummary> DeleteUserAsync(Guid userId);
    Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId);
    Task<string> SetPasswordAsync(Guid userId, string newPassword);
    Task<string?> GetUsernameAsync(Guid userId);
    Task<CredentialRecord?> FindByUsernameAsync(string username);
    Task<string?> FindUsernameByEmailAsync(string email);
}
```

- [ ] **Step 3: Build to see every broken call site (expected, tracked below)**

Run: `dotnet build BookWheel.slnx`
Expected: FAILS with compile errors in `BookWheel/Storage/JsonCredentialRepository.cs`, `BookWheel/Storage/Postgres/PostgresCredentialRepository.cs`, `BookWheel/Controllers/UsersController.cs`, `BookWheel/Services/AuthService.cs`, `BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs`, `BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs`, and `BookWheel.Tests/Services/PostgresMigrationServiceTests.cs` — every one of these is fixed in this task or Task 4/5. Do not fix `UsersController.cs`/`AuthService.cs` yet; they're Task 4/5's responsibility. Confirm the errors are all "missing argument" / "no overload takes N arguments" style, nothing else.

- [ ] **Step 4: Update `JsonCredentialRepository`**

In `BookWheel/Storage/JsonCredentialRepository.cs`, replace `CreateInitialAccountAsync`:

```csharp
    public async Task<CredentialRecord> CreateInitialAccountAsync(string username, string password, string email)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            if (users.Count > 0)
            {
                throw new InvalidOperationException("An account already exists.");
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Username and password are required.");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Email is required.");
            }

            var normalizedUsername = username.Trim();

            var record = new CredentialRecord
            {
                UserId = Guid.NewGuid(),
                Username = normalizedUsername,
                PasswordHash = PasswordHasher.HashPassword(normalizedUsername, password),
                IsAdmin = true,
                Email = email.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            users.Add(record);
            await WriteUsersUnsafeAsync(users);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }
```

Replace `CreateUserAsync`:

```csharp
    public async Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin, string email)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            if (users.Count == 0)
            {
                throw new InvalidOperationException("Create the initial account first.");
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("Username is required.");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Email is required.");
            }

            var normalizedUsername = username.Trim();
            if (users.Any(user => string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var normalizedEmail = email.Trim();
            if (users.Any(user => user.Email is not null && string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Email already exists.");
            }

            var record = new CredentialRecord
            {
                UserId = Guid.NewGuid(),
                Username = normalizedUsername,
                PasswordHash = PasswordHasher.HashPassword(normalizedUsername, GenerateTemporaryPassword()),
                IsAdmin = isAdmin,
                Email = normalizedEmail,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            users.Add(record);
            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }
```

Replace the 6-arg `UpdateUserAsync` (leave the 3-arg overload above it untouched):

```csharp
    public async Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked, string? email)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var record = users.FirstOrDefault(user => user.UserId == userId)
                ?? throw new InvalidOperationException("User not found.");

            var normalizedUsername = username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                throw new InvalidOperationException("Username is required.");
            }

            var duplicateUser = users.FirstOrDefault(user =>
                user.UserId != userId && string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (duplicateUser is not null)
            {
                throw new InvalidOperationException("Username already exists.");
            }

            var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            if (normalizedEmail is not null)
            {
                var duplicateEmail = users.FirstOrDefault(user =>
                    user.UserId != userId && user.Email is not null && string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));

                if (duplicateEmail is not null)
                {
                    throw new InvalidOperationException("Email already exists.");
                }
            }

            if (!isAdmin)
            {
                var adminCount = users.Count(user => user.IsAdmin);
                if (record.IsAdmin && adminCount <= 1)
                {
                    throw new InvalidOperationException("At least one administrator account is required.");
                }
            }

            record.Username = normalizedUsername;
            record.IsAdmin = isAdmin;
            record.IsDisabled = isDisabled;
            record.ForcePasswordReset = forcePasswordReset;
            record.IsLocked = isLocked;
            record.LockedUntilUtc = isLocked ? DateTimeOffset.UtcNow.AddHours(12) : null;
            record.Email = normalizedEmail;

            await WriteUsersUnsafeAsync(users);
            return ToSummary(record);
        }
        finally
        {
            _gate.Release();
        }
    }
```

Add the two new lookup methods (right after `GetUsernameAsync`):

```csharp
    public async Task<CredentialRecord?> FindByUsernameAsync(string username)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var normalizedUsername = username.Trim();
            return users.FirstOrDefault(user => string.Equals(user.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> FindUsernameByEmailAsync(string email)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUsersUnsafeAsync();
            var normalizedEmail = email.Trim();
            return users.FirstOrDefault(user =>
                !user.IsDisabled &&
                user.Email is not null &&
                string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))?.Username;
        }
        finally
        {
            _gate.Release();
        }
    }
```

In `ToSummary`, add the mapping:

```csharp
    private static UserAccountSummary ToSummary(CredentialRecord record)
    {
        return new UserAccountSummary
        {
            UserId = record.UserId,
            Username = record.Username,
            Email = record.Email,
            IsAdmin = record.IsAdmin,
            IsDisabled = record.IsDisabled,
            ForcePasswordReset = record.ForcePasswordReset,
            IsLocked = record.IsLocked,
            LockedUntilUtc = record.LockedUntilUtc,
            CreatedAtUtc = record.CreatedAtUtc
        };
    }
```

- [ ] **Step 5: Update `UserEntity` and `BookWheelDbContext`**

In `BookWheel/Storage/Postgres/Entities/UserEntity.cs`, add after `Username`:

```csharp
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
```

In `BookWheel/Storage/Postgres/BookWheelDbContext.cs`, in the `UserEntity` configuration block, add after `entity.Property(u => u.PasswordHash).IsRequired();`:

```csharp
            entity.Property(u => u.Email).HasColumnType("citext");
            entity.HasIndex(u => u.Email).IsUnique();
```

- [ ] **Step 6: Update `PostgresCredentialRepository`**

In `BookWheel/Storage/Postgres/PostgresCredentialRepository.cs`, replace `CreateInitialAccountAsync`:

```csharp
    public async Task<CredentialRecord> CreateInitialAccountAsync(string username, string password, string email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (await context.Users.AnyAsync())
        {
            throw new InvalidOperationException("An account already exists.");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Username and password are required.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email is required.");
        }

        var normalizedUsername = username.Trim();
        var entity = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            PasswordHash = PasswordHasher.HashPassword(normalizedUsername, password),
            IsAdmin = true,
            Email = email.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Users.Add(entity);
        await context.SaveChangesAsync();
        return ToRecord(entity);
    }
```

Replace `CreateUserAsync`:

```csharp
    public async Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin, string email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (!await context.Users.AnyAsync())
        {
            throw new InvalidOperationException("Create the initial account first.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email is required.");
        }

        var normalizedUsername = username.Trim();
        if (await context.Users.AnyAsync(u => u.Username == normalizedUsername))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var normalizedEmail = email.Trim();
        if (await context.Users.AnyAsync(u => u.Email == normalizedEmail))
        {
            throw new InvalidOperationException("Email already exists.");
        }

        var entity = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            PasswordHash = PasswordHasher.HashPassword(normalizedUsername, GenerateTemporaryPassword()),
            IsAdmin = isAdmin,
            Email = normalizedEmail,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Users.Add(entity);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException(IsEmailConstraintViolation(ex) ? "Email already exists." : "Username already exists.");
        }

        return ToSummary(entity);
    }
```

Replace the two `UpdateUserAsync` public overloads and `UpdateUserCoreAsync`:

```csharp
    public Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin)
    {
        return UpdateUserCoreAsync(userId, username, isAdmin, isDisabled: null, forcePasswordReset: null, isLocked: null, email: null);
    }

    public Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked, string? email)
    {
        return UpdateUserCoreAsync(userId, username, isAdmin, isDisabled, forcePasswordReset, isLocked, email);
    }

    private async Task<UserAccountSummary> UpdateUserCoreAsync(Guid userId, string username, bool isAdmin, bool? isDisabled, bool? forcePasswordReset, bool? isLocked, string? email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        var normalizedUsername = username.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new InvalidOperationException("Username is required.");
        }

        var duplicateExists = await context.Users.AnyAsync(u => u.Id != userId && u.Username == normalizedUsername);
        if (duplicateExists)
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (normalizedEmail is not null)
        {
            var duplicateEmailExists = await context.Users.AnyAsync(u => u.Id != userId && u.Email == normalizedEmail);
            if (duplicateEmailExists)
            {
                throw new InvalidOperationException("Email already exists.");
            }
        }

        if (!isAdmin)
        {
            var adminCount = await context.Users.CountAsync(u => u.IsAdmin);
            if (entity.IsAdmin && adminCount <= 1)
            {
                throw new InvalidOperationException("At least one administrator account is required.");
            }
        }

        entity.Username = normalizedUsername;
        entity.IsAdmin = isAdmin;
        entity.Email = normalizedEmail;

        if (isDisabled.HasValue)
        {
            entity.IsDisabled = isDisabled.Value;
        }

        if (forcePasswordReset.HasValue)
        {
            entity.ForcePasswordReset = forcePasswordReset.Value;
        }

        if (isLocked.HasValue)
        {
            entity.IsLocked = isLocked.Value;
            entity.LockedUntilUtc = isLocked.Value ? DateTimeOffset.UtcNow.AddHours(12) : null;
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException(IsEmailConstraintViolation(ex) ? "Email already exists." : "Username already exists.");
        }

        return ToSummary(entity);
    }
```

Add the new lookup methods and the constraint-name helper (right after `GetUsernameAsync`, and right after `IsUniqueViolation`):

```csharp
    public async Task<CredentialRecord?> FindByUsernameAsync(string username)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var normalizedUsername = username.Trim();
        var entity = await context.Users.FirstOrDefaultAsync(u => u.Username == normalizedUsername);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<string?> FindUsernameByEmailAsync(string email)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var normalizedEmail = email.Trim();
        return await context.Users
            .Where(u => !u.IsDisabled && u.Email == normalizedEmail)
            .Select(u => u.Username)
            .FirstOrDefaultAsync();
    }
```

```csharp
    private static bool IsEmailConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException postgresException &&
            postgresException.ConstraintName is not null &&
            postgresException.ConstraintName.Contains("Email", StringComparison.OrdinalIgnoreCase);
    }
```

In `ToRecord` and `ToSummary`, add the mapping (both currently list `Username = entity.Username,` — add `Email = entity.Email,` right after it in both):

```csharp
        Username = entity.Username,
        Email = entity.Email,
```

- [ ] **Step 7: Carry `Email` through the legacy JSON→Postgres migration copy**

In `BookWheel/Services/PostgresMigrationService.cs`, in the `foreach (var user in users)` loop's `new UserEntity { ... }` initializer, add `Email = user.Email,` right after `Username = user.Username,`.

- [ ] **Step 8: Fix compile-breaking call sites in `JsonCredentialRepositoryTests.cs`**

In `BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs`, every `CreateInitialAccountAsync("admin-one", "correct-password")` call is textually identical (12 occurrences) — each test method gets its own fresh isolated repository (see the constructor's per-test `Guid.NewGuid()`-named temp directory), so reusing the same email literal everywhere is safe. Replace **all occurrences** of:

```csharp
_repository.CreateInitialAccountAsync("admin-one", "correct-password")
```

with:

```csharp
_repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com")
```

Similarly, replace **all occurrences** of:

```csharp
_repository.CreateUserAsync("reader-one", isAdmin: false)
```

with:

```csharp
_repository.CreateUserAsync("reader-one", isAdmin: false, email: "reader-one@example.com")
```

(this affects the 3 identical occurrences at the standalone `CreateUserAsync_Adds_NonAdmin_User`, the first call inside `CreateUserAsync_With_Duplicate_Username_Throws`, and `DeleteUserAsync_Removes_NonFirst_User` — all safe since each is either its own test or the first of a pair).

Then fix the one remaining, textually distinct call — the second call inside `CreateUserAsync_With_Duplicate_Username_Throws` — by hand, giving it a **different** email so the test unambiguously exercises username-collision detection rather than email-collision:

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("Reader-One", isAdmin: false));
```

becomes:

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("Reader-One", isAdmin: false, email: "reader-one-2@example.com"));
```

- [ ] **Step 9: Add new JSON repository test coverage**

In `BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs`, add these tests (near `CreateUserAsync_With_Duplicate_Username_Throws`):

```csharp
    [Fact]
    public async Task CreateUserAsync_With_Duplicate_Email_Throws()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");
        await _repository.CreateUserAsync("reader-one", isAdmin: false, email: "shared@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("reader-two", isAdmin: false, email: "Shared@example.com"));
    }

    [Fact]
    public async Task CreateUserAsync_Allows_Multiple_Accounts_With_No_Email()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");

        var readerOne = await _repository.CreateUserAsync("reader-one", isAdmin: false, email: "reader-one@example.com");
        var readerTwo = await _repository.CreateUserAsync("reader-two", isAdmin: false, email: "reader-two@example.com");
        var updated = await _repository.UpdateUserAsync(readerOne.UserId, "reader-one", isAdmin: false, isDisabled: false, forcePasswordReset: false, isLocked: false, email: null);
        var updatedTwo = await _repository.UpdateUserAsync(readerTwo.UserId, "reader-two", isAdmin: false, isDisabled: false, forcePasswordReset: false, isLocked: false, email: null);

        Assert.Null(updated.Email);
        Assert.Null(updatedTwo.Email);
    }

    [Fact]
    public async Task FindByUsernameAsync_Returns_Record_With_Email()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");

        var found = await _repository.FindByUsernameAsync("admin-one");

        Assert.NotNull(found);
        Assert.Equal("admin-one@example.com", found!.Email);
    }

    [Fact]
    public async Task FindByUsernameAsync_Returns_Null_For_Unknown_Username()
    {
        var found = await _repository.FindByUsernameAsync("nobody");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindUsernameByEmailAsync_Returns_Matching_Username()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");

        var username = await _repository.FindUsernameByEmailAsync("Admin-One@example.com");

        Assert.Equal("admin-one", username);
    }

    [Fact]
    public async Task FindUsernameByEmailAsync_Returns_Null_For_Unknown_Email()
    {
        var username = await _repository.FindUsernameByEmailAsync("nobody@example.com");

        Assert.Null(username);
    }

    [Fact]
    public async Task FindUsernameByEmailAsync_Returns_Null_For_Disabled_Account()
    {
        var admin = await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");
        var reader = await _repository.CreateUserAsync("reader-one", isAdmin: false, email: "reader-one@example.com");
        await _repository.UpdateUserAsync(reader.UserId, "reader-one", isAdmin: false, isDisabled: true, forcePasswordReset: false, isLocked: false, email: "reader-one@example.com");

        var username = await _repository.FindUsernameByEmailAsync("reader-one@example.com");

        Assert.Null(username);
    }
```

- [ ] **Step 10: Fix compile-breaking call sites in `PostgresCredentialRepositoryTests.cs`**

Apply the same mechanical fix to `BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs`. Replace **all occurrences** of:

```csharp
_repository.CreateInitialAccountAsync("admin-one", "correct-password")
```

with:

```csharp
_repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com")
```

Fix the one distinct-case occurrence by hand:

```csharp
        await _repository.CreateInitialAccountAsync("Admin-One", "correct-password");
```

becomes:

```csharp
        await _repository.CreateInitialAccountAsync("Admin-One", "correct-password", "admin-one@example.com");
```

Replace **all occurrences** of:

```csharp
_repository.CreateUserAsync("reader-one", isAdmin: false)
```

with:

```csharp
_repository.CreateUserAsync("reader-one", isAdmin: false, email: "reader-one@example.com")
```

(this affects `CreateUserAsync_Adds_NonAdmin_User`, the first call in `CreateUserAsync_With_Duplicate_Username_Throws`, and `DeleteUserAsync_Removes_NonFirst_User`). Fix the remaining distinct call by hand:

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("Reader-One", isAdmin: false));
```

becomes:

```csharp
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("Reader-One", isAdmin: false, email: "reader-one-2@example.com"));
```

- [ ] **Step 11: Add new Postgres repository test coverage**

In `BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs`, add these tests (near `CreateUserAsync_With_Duplicate_Username_Throws`):

```csharp
    [Fact]
    public async Task CreateUserAsync_With_Duplicate_Email_Throws()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");
        await _repository.CreateUserAsync("reader-one", isAdmin: false, email: "shared@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.CreateUserAsync("reader-two", isAdmin: false, email: "Shared@example.com"));
    }

    [Fact]
    public async Task CreateUserAsync_Allows_Multiple_Accounts_With_No_Email()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");

        var readerOne = await _repository.CreateUserAsync("reader-one", isAdmin: false, email: "reader-one@example.com");
        var readerTwo = await _repository.CreateUserAsync("reader-two", isAdmin: false, email: "reader-two@example.com");
        var updated = await _repository.UpdateUserAsync(readerOne.UserId, "reader-one", isAdmin: false, isDisabled: false, forcePasswordReset: false, isLocked: false, email: null);
        var updatedTwo = await _repository.UpdateUserAsync(readerTwo.UserId, "reader-two", isAdmin: false, isDisabled: false, forcePasswordReset: false, isLocked: false, email: null);

        Assert.Null(updated.Email);
        Assert.Null(updatedTwo.Email);
    }

    [Fact]
    public async Task FindByUsernameAsync_Returns_Record_With_Email()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");

        var found = await _repository.FindByUsernameAsync("admin-one");

        Assert.NotNull(found);
        Assert.Equal("admin-one@example.com", found!.Email);
    }

    [Fact]
    public async Task FindByUsernameAsync_Returns_Null_For_Unknown_Username()
    {
        var found = await _repository.FindByUsernameAsync("nobody");

        Assert.Null(found);
    }

    [Fact]
    public async Task FindUsernameByEmailAsync_Returns_Matching_Username_Case_Insensitively()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");

        var username = await _repository.FindUsernameByEmailAsync("Admin-One@example.com");

        Assert.Equal("admin-one", username);
    }

    [Fact]
    public async Task FindUsernameByEmailAsync_Returns_Null_For_Disabled_Account()
    {
        await _repository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com");
        var reader = await _repository.CreateUserAsync("reader-one", isAdmin: false, email: "reader-one@example.com");
        await _repository.UpdateUserAsync(reader.UserId, "reader-one", isAdmin: false, isDisabled: true, forcePasswordReset: false, isLocked: false, email: "reader-one@example.com");

        var username = await _repository.FindUsernameByEmailAsync("reader-one@example.com");

        Assert.Null(username);
    }
```

- [ ] **Step 12: Fix compile-breaking call sites in `PostgresMigrationServiceTests.cs`**

In `BookWheel.Tests/Services/PostgresMigrationServiceTests.cs`, replace **both** occurrences of:

```csharp
_jsonCredentialRepository.CreateInitialAccountAsync("admin-one", "correct-password")
```

with:

```csharp
_jsonCredentialRepository.CreateInitialAccountAsync("admin-one", "correct-password", "admin-one@example.com")
```

And in `RunAsync_Copies_Users_Books_And_Tokens_Into_Postgres`, add an assertion that `Email` survived the copy — right after the existing `Assert.True(migratedUser.IsAdmin);` line, add:

```csharp
        Assert.Equal("admin-one@example.com", migratedUser.Email);
```

- [ ] **Step 13: Generate the EF Core migration**

Run: `dotnet ef migrations add AddUserEmail --project BookWheel --startup-project BookWheel`

Expected: creates `BookWheel/Migrations/<timestamp>_AddUserEmail.cs` and `.Designer.cs`, and updates `BookWheel/Migrations/BookWheelDbContextModelSnapshot.cs`. Open the generated `<timestamp>_AddUserEmail.cs` and verify its `Up()` method contains:

- `AddColumn<string>("Email", "users", type: "citext", nullable: true)`
- `CreateIndex("IX_users_Email", "users", "Email", unique: true)`

This is a single nullable `ADD COLUMN` plus a unique index on an all-`NULL` new column — safe and zero-downtime (matches `docs/audits/database.md` Finding 5's guidance for additive nullable columns; a unique index over all-`NULL` values costs nothing to create since there are no duplicates to find).

- [ ] **Step 14: Build and run the full test suite**

Run: `dotnet build BookWheel.slnx`
Expected: still fails — `UsersController.cs` and `AuthService.cs` are not yet updated (Task 4/5). Confirm the ONLY remaining errors are in those two files (both are `CreateUserAsync`/`CreateInitialAccountAsync`/`UpdateUserAsync` call-site errors, e.g. `Program.cs:69` and `AuthService.cs:43`), and that every file this task touched now builds clean in isolation — the fastest way to confirm is `dotnet build BookWheel.Tests/BookWheel.Tests.csproj` in isolation, which will fail because `BookWheel.Tests` references `BookWheel` and pulls in the same errors; that's expected, not a new problem introduced by tests.

- [ ] **Step 15: Commit**

```bash
git add BookWheel/Models/CredentialRecord.cs BookWheel/Models/UserAccountSummary.cs BookWheel/Storage/ICredentialRepository.cs BookWheel/Storage/JsonCredentialRepository.cs BookWheel/Storage/Postgres/Entities/UserEntity.cs BookWheel/Storage/Postgres/BookWheelDbContext.cs BookWheel/Storage/Postgres/PostgresCredentialRepository.cs BookWheel/Services/PostgresMigrationService.cs BookWheel/Migrations/ BookWheel.Tests/Storage/JsonCredentialRepositoryTests.cs BookWheel.Tests/Storage/Postgres/PostgresCredentialRepositoryTests.cs BookWheel.Tests/Services/PostgresMigrationServiceTests.cs
git commit -m "Add unique Email column and lookups to the credential repository, both backends (GH #115)"
```

(This commit intentionally leaves the solution non-building — `UsersController.cs`/`AuthService.cs` are fixed in the very next task. If your workflow requires every commit to build, squash Tasks 3-5 together instead of committing here; this plan keeps them separate for reviewability.)

---

### Task 4: AuthService — required email, self-service password-reset and forgot-username, rate limiting

**Files:**
- Modify: `BookWheel/Services/AuthService.cs`

**Interfaces:**
- Consumes: `ICredentialRepository.CreateInitialAccountAsync(username, password, email)`, `.FindByUsernameAsync`, `.FindUsernameByEmailAsync` (Task 3); `AccountEmailService` (Task 2).
- Produces: `AuthService.CreateAccountAsync(string username, string password, string email)` (signature change), `AuthService.CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl, CancellationToken cancellationToken = default)` (now also emails), `AuthService.RequestPasswordResetAsync(string username, string appBaseUrl, CancellationToken cancellationToken = default)`, `AuthService.RequestForgottenUsernameAsync(string email, CancellationToken cancellationToken = default)` — Task 5's `AuthController` calls the last two directly.

- [ ] **Step 1: Add the `AccountEmailService` dependency and rate-limit state**

In `BookWheel/Services/AuthService.cs`, add a private nested class right after `FailedLoginRecord`:

```csharp
    private sealed class RateLimitRecord
    {
        public int Count { get; set; }
        public DateTimeOffset WindowStartUtc { get; set; }
    }
```

Add two new fields right after `_failedLogins`:

```csharp
    private readonly ConcurrentDictionary<string, RateLimitRecord> _passwordResetRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RateLimitRecord> _forgottenUsernameRequests = new(StringComparer.OrdinalIgnoreCase);
```

Add the `AccountEmailService` field and thread it through the constructor:

```csharp
    private readonly AccountEmailService _accountEmailService;
```

```csharp
    public AuthService(ICredentialRepository credentialRepository, IPasswordResetTokenRepository resetTokenRepository, IOptions<SecurityOptions> securityOptions, AccountEmailService accountEmailService)
    {
        _credentialRepository = credentialRepository;
        _resetTokenRepository = resetTokenRepository;
        _securityOptions = securityOptions.Value;
        _accountEmailService = accountEmailService;
    }
```

- [ ] **Step 2: Thread `email` through `CreateAccountAsync`**

Replace:

```csharp
    public async Task<AuthenticatedUser> CreateAccountAsync(string username, string password)
    {
        var user = await _credentialRepository.CreateInitialAccountAsync(username, password);
        return ToAuthenticatedUser(user);
    }
```

with:

```csharp
    public async Task<AuthenticatedUser> CreateAccountAsync(string username, string password, string email)
    {
        var user = await _credentialRepository.CreateInitialAccountAsync(username, password, email);
        return ToAuthenticatedUser(user);
    }
```

- [ ] **Step 3: Make `CreatePasswordResetLinkAsync` email the link when the target has an address on file**

Replace:

```csharp
    public async Task<(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username)> CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl)
    {
        var user = await _credentialRepository.MarkForPasswordResetAsync(userId);
        var (rawToken, expiresAtUtc) = await _resetTokenRepository.CreateAsync(userId);

        var trimmedBaseUrl = appBaseUrl.TrimEnd('/');
        var resetLink = $"{trimmedBaseUrl}/?resetToken={Uri.EscapeDataString(rawToken)}";
        return (resetLink, expiresAtUtc, user.Username);
    }
```

with:

```csharp
    public async Task<(string ResetLink, DateTimeOffset ExpiresAtUtc, string Username)> CreatePasswordResetLinkAsync(Guid userId, string appBaseUrl, CancellationToken cancellationToken = default)
    {
        var user = await _credentialRepository.MarkForPasswordResetAsync(userId);
        var (rawToken, expiresAtUtc) = await _resetTokenRepository.CreateAsync(userId);

        var trimmedBaseUrl = appBaseUrl.TrimEnd('/');
        var resetLink = $"{trimmedBaseUrl}/?resetToken={Uri.EscapeDataString(rawToken)}";

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            await _accountEmailService.SendPasswordResetEmailAsync(user.Email, user.Username, resetLink, expiresAtUtc, cancellationToken);
        }

        return (resetLink, expiresAtUtc, user.Username);
    }
```

(The `cancellationToken` parameter defaults to `default`, so `UsersController.CreatePasswordResetLink`'s existing call site — `_authService.CreatePasswordResetLinkAsync(id, appBaseUrl)` — keeps compiling unchanged and now also emails as a side effect.)

- [ ] **Step 4: Add the rate-limit helper and the two self-service methods**

Add right after `ValidateCredentialsAsync`:

```csharp
    private static bool TryConsumeRateLimit(ConcurrentDictionary<string, RateLimitRecord> store, string key)
    {
        const int maxRequestsPerWindow = 3;
        var window = TimeSpan.FromMinutes(15);
        var now = DateTimeOffset.UtcNow;
        var normalizedKey = key.Trim();

        var record = store.AddOrUpdate(
            normalizedKey,
            _ => new RateLimitRecord { Count = 1, WindowStartUtc = now },
            (_, existing) =>
            {
                if (now - existing.WindowStartUtc > window)
                {
                    existing.WindowStartUtc = now;
                    existing.Count = 1;
                }
                else
                {
                    existing.Count += 1;
                }

                return existing;
            });

        return record.Count <= maxRequestsPerWindow;
    }

    public async Task RequestPasswordResetAsync(string username, string appBaseUrl, CancellationToken cancellationToken = default)
    {
        if (!TryConsumeRateLimit(_passwordResetRequests, username))
        {
            return;
        }

        var account = await _credentialRepository.FindByUsernameAsync(username);
        if (account is null || account.IsDisabled || string.IsNullOrWhiteSpace(account.Email))
        {
            return;
        }

        await CreatePasswordResetLinkAsync(account.UserId, appBaseUrl, cancellationToken);
    }

    public async Task RequestForgottenUsernameAsync(string email, CancellationToken cancellationToken = default)
    {
        if (!TryConsumeRateLimit(_forgottenUsernameRequests, email))
        {
            return;
        }

        var username = await _credentialRepository.FindUsernameByEmailAsync(email);
        if (username is null)
        {
            return;
        }

        await _accountEmailService.SendForgottenUsernameEmailAsync(email, username, cancellationToken);
    }
```

- [ ] **Step 5: Build (still expected to fail — `Program.cs`/`AuthController.cs` are Task 5)**

Run: `dotnet build BookWheel.slnx`
Expected: fails only in `BookWheel/Controllers/AuthController.cs` (`Setup` still calls the old 2-arg `CreateAccountAsync`) and wherever `AuthService` is constructed without the new `AccountEmailService` argument — there is no explicit `new AuthService(...)` outside DI, so the only remaining error should be `AuthController.cs`'s `Setup` action. Confirm that's the only error before moving on.

- [ ] **Step 6: Commit**

```bash
git add BookWheel/Services/AuthService.cs
git commit -m "Add self-service password-reset and forgot-username flows with rate limiting to AuthService (GH #115)"
```

---

### Task 5: Controllers — required email on setup/create-user, new self-service endpoints, localization

**Files:**
- Create: `BookWheel/Models/SetupAccountRequest.cs`
- Modify: `BookWheel/Models/CreateUserRequest.cs`
- Modify: `BookWheel/Models/UpdateUserAccountRequest.cs`
- Create: `BookWheel/Models/RequestPasswordResetRequest.cs`
- Create: `BookWheel/Models/ForgotUsernameRequest.cs`
- Modify: `BookWheel/Controllers/AuthController.cs`
- Modify: `BookWheel/Controllers/UsersController.cs`
- Modify: `BookWheel/Services/ApiMessageLocalizer.cs`
- Modify: `BookWheel/Resources/SharedErrors.resx`, `SharedErrors.es.resx`, `SharedErrors.pl.resx`

**Interfaces:**
- Produces: `SetupAccountRequest { Username, Password, Email }`, `RequestPasswordResetRequest { Username }`, `ForgotUsernameRequest { Email }`. `POST /api/auth/password-reset/request` and `POST /api/auth/forgot-username` — Task 6's tests and Task 7's frontend call these directly. `CreateUserRequest.Email` (required), `UpdateUserAccountRequest.Email` (optional).

- [ ] **Step 1: Add `SetupAccountRequest`**

Create `BookWheel/Models/SetupAccountRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class SetupAccountRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 64 characters.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Add `Email` to `CreateUserRequest` (required)**

Replace the full contents of `BookWheel/Models/CreateUserRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class CreateUserRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 64 characters.")]
    public string Username { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Add `Email` to `UpdateUserAccountRequest` (optional)**

Replace the full contents of `BookWheel/Models/UpdateUserAccountRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class UpdateUserAccountRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 64 characters.")]
    public string Username { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public bool ForcePasswordReset { get; set; }
    public bool IsLocked { get; set; }

    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string? Email { get; set; }
}
```

- [ ] **Step 4: Add the two new request models**

Create `BookWheel/Models/RequestPasswordResetRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class RequestPasswordResetRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Username must be between 1 and 64 characters.")]
    public string Username { get; set; } = string.Empty;
}
```

Create `BookWheel/Models/ForgotUsernameRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace BookWheel.Models;

public sealed class ForgotUsernameRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be a valid email address.")]
    public string Email { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Update `ApiMessageLocalizer` and all three resx files**

In `BookWheel/Services/ApiMessageLocalizer.cs`, add three entries to `KeysByEnglishMessage` (order doesn't matter; add near the other `Required`/`AlreadyExists` entries):

```csharp
		["Email is required."] = "EmailRequired",
		["Email must be a valid email address."] = "InvalidEmailFormat",
		["Email already exists."] = "EmailAlreadyExists",
```

In `BookWheel/Resources/SharedErrors.resx`, add (anywhere before `</root>`):

```xml
  <data name="EmailRequired" xml:space="preserve">
    <value>Email is required.</value>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>Email must be a valid email address.</value>
  </data>
  <data name="EmailAlreadyExists" xml:space="preserve">
    <value>Email already exists.</value>
  </data>
```

In `BookWheel/Resources/SharedErrors.es.resx`, add:

```xml
  <data name="EmailRequired" xml:space="preserve">
    <value>El correo electrónico es obligatorio.</value>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>El correo electrónico debe ser una dirección válida.</value>
  </data>
  <data name="EmailAlreadyExists" xml:space="preserve">
    <value>El correo electrónico ya existe.</value>
  </data>
```

In `BookWheel/Resources/SharedErrors.pl.resx`, add:

```xml
  <data name="EmailRequired" xml:space="preserve">
    <value>Adres e-mail jest wymagany.</value>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>Adres e-mail musi być prawidłowym adresem.</value>
  </data>
  <data name="EmailAlreadyExists" xml:space="preserve">
    <value>Adres e-mail już istnieje.</value>
  </data>
```

- [ ] **Step 6: Update `AuthController`**

In `BookWheel/Controllers/AuthController.cs`, change the `Setup` action's signature and body:

```csharp
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupAccountRequest request)
```

(keep the existing body exactly as-is except the `CreateAccountAsync` call, which becomes:)

```csharp
        var user = await _authService.CreateAccountAsync(request.Username, request.Password, request.Email);
```

Add the two new actions (right after `CompletePasswordReset`, before `ValidatePasswordResetToken`):

```csharp
    [HttpPost("password-reset/request")]
    public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest request)
    {
        var appBaseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        await _authService.RequestPasswordResetAsync(request.Username, appBaseUrl, HttpContext.RequestAborted);
        _logger.LogInformation(
            "Password reset requested. Username {Username} from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
            request.Username,
            GetClientIp(),
            GetRequestPath(),
            GetRequestId(),
            GetUserAgent());
        return Ok(new { message = "If that account exists and has an email on file, a reset link has been sent." });
    }

    [HttpPost("forgot-username")]
    public async Task<IActionResult> ForgotUsername([FromBody] ForgotUsernameRequest request)
    {
        await _authService.RequestForgottenUsernameAsync(request.Email, HttpContext.RequestAborted);
        _logger.LogInformation(
            "Forgotten-username requested from {ClientIp} path {Path} request {RequestId} user agent {UserAgent}",
            GetClientIp(),
            GetRequestPath(),
            GetRequestId(),
            GetUserAgent());
        return Ok(new { message = "If that email is on file, we've sent the associated username." });
    }
```

These two success messages are deliberately **not** passed through `_errors.Localize(...)` — this controller's existing convention is that only error responses (`Conflict`/`Unauthorized`/`BadRequest`/`StatusCode(4xx/5xx, ...)`) get localized via `ApiMessageLocalizer`; `Ok(...)` success messages elsewhere in this file (`"Account created."`, `"Logged in."`, `"Logged out."`, `"Password updated."`) are hardcoded English, since the frontend never actually displays the server's `message` field for success cases — it shows its own `i18n.js`-driven toast text instead (see Task 7, which wires `t('auth.forgotPasswordSentMessage')`/`t('auth.forgotUsernameSentMessage')` for exactly this). No `ApiMessageLocalizer`/resx changes are needed for these two strings.

- [ ] **Step 7: Update `UsersController`**

In `BookWheel/Controllers/UsersController.cs`, change the `CreateUser` action's repository call:

```csharp
            var user = await _credentialRepository.CreateUserAsync(request.Username, request.IsAdmin, request.Email);
```

Change the `UpdateUser` action's repository call:

```csharp
            var user = await _credentialRepository.UpdateUserAsync(id, request.Username, request.IsAdmin, request.IsDisabled, request.ForcePasswordReset, request.IsLocked, request.Email);
```

- [ ] **Step 8: Build**

Run: `dotnet build BookWheel.slnx`
Expected: builds successfully — this is the first point since Task 3 where the whole solution compiles again.

- [ ] **Step 9: Run the full test suite (expected to still show failures — fixed in Task 6)**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: builds and runs, but many tests in `BookWheelApiTests.cs`, `BookWheelBrowserWorkflowTests.cs`, `BookWheelSmokeTests.cs`, and `PreferencesControllerTests.cs` FAIL at runtime with `400 Bad Request` — every inline `/api/auth/setup` call in those files is now missing the required `email` field. This is expected and is exactly what Task 6 fixes. Confirm the failures are all assertion failures on setup/login flows (not new compile errors, not unrelated regressions) before moving on — e.g. spot-check that `SmtpEmailSenderTests`, `AccountEmailServiceTests`, `ApiMessageLocalizerTests`, and the Task 3 repository tests all still pass cleanly.

- [ ] **Step 10: Commit**

```bash
git add BookWheel/Models/SetupAccountRequest.cs BookWheel/Models/CreateUserRequest.cs BookWheel/Models/UpdateUserAccountRequest.cs BookWheel/Models/RequestPasswordResetRequest.cs BookWheel/Models/ForgotUsernameRequest.cs BookWheel/Controllers/AuthController.cs BookWheel/Controllers/UsersController.cs BookWheel/Services/ApiMessageLocalizer.cs BookWheel/Resources/SharedErrors.resx BookWheel/Resources/SharedErrors.es.resx BookWheel/Resources/SharedErrors.pl.resx
git commit -m "Require email on setup/create-user; add password-reset/request and forgot-username endpoints (GH #115)"
```

---

### Task 6: Fix the existing integration-test ripple and add new HTTP-level coverage

**Files:**
- Modify: `BookWheel.Tests/BookWheelWebAppFactory.cs` (swap `IEmailSender` for `FakeEmailSender`, expose it, clear it on reset)
- Modify: `BookWheel.Tests/BookWheelApiTests.cs` (bulk fix + 2 manual fixes + new tests)
- Modify: `BookWheel.Tests/BookWheelBrowserWorkflowTests.cs` (bulk fix)
- Modify: `BookWheel.Tests/BookWheelSmokeTests.cs` (bulk fix)
- Modify: `BookWheel.Tests/Controllers/PreferencesControllerTests.cs` (bulk fix)

**Interfaces:**
- Consumes: `FakeEmailSender` (Task 2), the endpoints from Task 5.
- Produces: `BookWheelWebAppFactory.FakeEmailSender` (`FakeEmailSender`, public property) — any test in the suite can now assert on `factory.FakeEmailSender.SentEmails` after making a request.

- [ ] **Step 1: Wire `FakeEmailSender` into `BookWheelWebAppFactory`**

In `BookWheel.Tests/BookWheelWebAppFactory.cs`, add a field and property (near `_loggerProvider`/`LoggerProvider`):

```csharp
    private readonly FakeEmailSender _fakeEmailSender = new();

    public FakeEmailSender FakeEmailSender => _fakeEmailSender;
```

In `ConfigureWebHost`'s `ConfigureServices` block, add alongside the existing `BookMetadataLookupDispatcher` override:

```csharp
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_fakeEmailSender);
```

In `ResetAsync()`, clear the fake sender's captured emails so state doesn't leak between test methods sharing this factory (add right after the `TRUNCATE TABLE` call):

```csharp
        _fakeEmailSender.SentEmails.Clear();
```

No new `using` directives are needed — the file already has `using BookWheel.Services;` (covers `IEmailSender`) and `using BookWheel.Tests.Services;` (covers `FakeEmailSender`).

- [ ] **Step 2: Verify the regex-based bulk fix, then apply it**

Run this PowerShell script from the repository root. It has already been verified (dry-run, read-only) to match exactly 69 setup-call bodies across these 4 files with no cross-call spanning:

```powershell
$files = @(
  "BookWheel.Tests/BookWheelApiTests.cs",
  "BookWheel.Tests/BookWheelBrowserWorkflowTests.cs",
  "BookWheel.Tests/BookWheelSmokeTests.cs",
  "BookWheel.Tests/Controllers/PreferencesControllerTests.cs"
)
$pattern = '(?s)("/api/auth/setup",\s*new\s*\{.*?password\s*=\s*"[^"]*")(\s*\}\s*\))'
$replacement = "`$1,`r`n            email = ""test-setup@example.com""`$2"
foreach ($f in $files) {
    $content = Get-Content -Raw -LiteralPath $f
    $matchCount = [regex]::Matches($content, $pattern).Count
    $new = [regex]::Replace($content, $pattern, $replacement)
    Set-Content -LiteralPath $f -Value $new -NoNewline
    Write-Output "$f : replaced $matchCount occurrence(s)"
}
```

Expected output:

```
BookWheel.Tests/BookWheelApiTests.cs : replaced 66 occurrence(s)
BookWheel.Tests/BookWheelBrowserWorkflowTests.cs : replaced 1 occurrence(s)
BookWheel.Tests/BookWheelSmokeTests.cs : replaced 1 occurrence(s)
BookWheel.Tests/Controllers/PreferencesControllerTests.cs : replaced 1 occurrence(s)
```

Every test class that calls `/api/auth/setup` resets its database (`ResetAsync()`/a fresh temp directory) before each test method, so reusing the same literal email across all 69 insertions is safe — no test creates two accounts that both need this literal, since accounts created via `POST /api/users` (not `/api/auth/setup`) are handled separately below.

Run `git diff --stat` and confirm exactly these 4 files changed, then spot-check one instance in each file (e.g. `git diff BookWheel.Tests/BookWheelSmokeTests.cs`) to confirm the inserted line reads `email = "test-setup@example.com"` in valid context.

- [ ] **Step 3: Fix the two `POST /api/users` call sites that don't go through the regex (different endpoint, not matched by the setup-only pattern)**

In `BookWheel.Tests/BookWheelApiTests.cs`, the shared `CreateUserAsync` test helper (used by 14 different tests) needs an email in its request body. Change:

```csharp
    private static async Task<(Guid UserId, string SetupLink)> CreateUserAsync(HttpClient client, string username, bool isAdmin = false)
    {
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username,
            isAdmin
        });
```

to:

```csharp
    private static async Task<(Guid UserId, string SetupLink)> CreateUserAsync(HttpClient client, string username, bool isAdmin = false)
    {
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username,
            isAdmin,
            email = $"{username}@example.com"
        });
```

Separately, the one standalone inline `/api/users` call in `Non_Admin_Cannot_Manage_Users` (which does not use the helper, since it expects a `403 Forbidden` result) also needs a valid email — without one, `[ApiController]`'s automatic model validation would short-circuit with `400` before the controller's authorization check ever runs, which would silently break what this test is actually trying to verify. Change:

```csharp
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-two",
            isAdmin = false
        });
```

to:

```csharp
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-two",
            isAdmin = false,
            email = "reader-two@example.com"
        });
```

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet build BookWheel.slnx && dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: all previously-failing tests now pass again. If any individual test still fails, inspect it — it's likely a setup call with a shape the regex didn't anticipate (e.g. a line-wrapped variant); fix it by hand following the same pattern (add `email = "test-setup@example.com"` to the request body) rather than re-running the bulk script.

- [ ] **Step 5: Add new integration tests for the email feature, in `BookWheelApiTests.cs`**

Add these tests (a good location is right after the existing `Admin_Can_Update_Other_User_Account`-style tests, anywhere in the class):

```csharp
    [Fact]
    public async Task Setup_Without_Email_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_Without_Email_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var response = await client.PostAsJsonAsync("/api/users", new { username = "reader-one", isAdmin = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_With_Duplicate_Email_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "shared@example.com" });

        var response = await client.PostAsJsonAsync("/api/users", new { username = "reader-one", isAdmin = false, email = "shared@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_With_Email_Already_Used_By_Another_Account_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new { username = "reader-one", isAdmin = false, email = "reader-one@example.com" });
        using var createUserDoc = await ReadJsonAsync(createUserResponse);
        var readerId = createUserDoc.RootElement.GetProperty("userId").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/users/{readerId}", new
        {
            username = "reader-one",
            isAdmin = false,
            isDisabled = false,
            forcePasswordReset = false,
            isLocked = false,
            email = "admin@example.com"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PasswordResetRequest_For_Unknown_Username_Returns_Generic_Ok_And_Sends_No_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { username = "does-not-exist" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.FakeEmailSender.SentEmails);
    }

    [Fact]
    public async Task PasswordResetRequest_For_Known_Username_With_Email_Sends_Reset_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var response = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { username = "test-admin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Equal("admin@example.com", sent.ToAddress);
    }

    [Fact]
    public async Task PasswordResetRequest_Beyond_RateLimit_Still_Returns_Ok_But_Stops_Sending()
    {
        // Uses its own username ("rate-limit-admin"), distinct from every other
        // test's "test-admin"/etc. — AuthService's rate-limit dictionaries live on
        // the singleton AuthService shared across this whole test class (ResetAsync
        // only truncates the DB and clears FakeEmailSender; it does not, and should
        // not, reach into AuthService's in-memory rate-limit state), so reusing a
        // username another test also sends password-reset requests for would make
        // this test's pass/fail depend on unspecified test execution order.
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "rate-limit-admin", password = "test-password", email = "rate-limit-admin@example.com" });

        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/password-reset/request", new { username = "rate-limit-admin" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(3, factory.FakeEmailSender.SentEmails.Count);
    }

    [Fact]
    public async Task ForgotUsername_For_Known_Email_Sends_Username_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var response = await client.PostAsJsonAsync("/api/auth/forgot-username", new { email = "admin@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Contains("test-admin", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForgotUsername_For_Unknown_Email_Returns_Generic_Ok_And_Sends_No_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/forgot-username", new { email = "nobody@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.FakeEmailSender.SentEmails);
    }

    [Fact]
    public async Task Admin_Generated_Reset_Link_Also_Sends_Email_When_Target_Has_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new { username = "reader-one", isAdmin = false, email = "reader-one@example.com" });
        using var createUserDoc = await ReadJsonAsync(createUserResponse);
        var readerId = createUserDoc.RootElement.GetProperty("userId").GetGuid();

        var response = await client.PostAsync($"/api/users/{readerId}/password-reset-link", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Equal("reader-one@example.com", sent.ToAddress);
    }
```

- [ ] **Step 6: Run the new tests**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj --filter "FullyQualifiedName~PasswordResetRequest|FullyQualifiedName~ForgotUsername|FullyQualifiedName~Setup_Without_Email|FullyQualifiedName~CreateUser_Without_Email|FullyQualifiedName~CreateUser_With_Duplicate_Email|FullyQualifiedName~UpdateUser_With_Email_Already_Used|FullyQualifiedName~Admin_Generated_Reset_Link_Also_Sends_Email"`
Expected: PASS (all 11 new tests).

- [ ] **Step 7: Run the entire test suite one more time**

Run: `dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: PASS, in full — this is the first fully-green run since Task 3 started.

- [ ] **Step 8: Commit**

```bash
git add BookWheel.Tests/BookWheelWebAppFactory.cs BookWheel.Tests/BookWheelApiTests.cs BookWheel.Tests/BookWheelBrowserWorkflowTests.cs BookWheel.Tests/BookWheelSmokeTests.cs BookWheel.Tests/Controllers/PreferencesControllerTests.cs
git commit -m "Fix existing tests for required setup email; add integration coverage for email feature (GH #115)"
```

---

### Task 7: Frontend — email fields, forgot-password/forgot-username UI

**Files:**
- Modify: `BookWheel/wwwroot/index.html`
- Modify: `BookWheel/wwwroot/js/app.js`
- Modify: `BookWheel/wwwroot/js/i18n.js`
- Modify: `BookWheel/wwwroot/css/site.css`
- Test: `BookWheel.Tests/BookWheelFrontendTests.cs` (new assertions)

**Interfaces:**
- Consumes: `POST /api/auth/setup` (now requires `email`), `POST /api/users` (now requires `email`), `POST /api/auth/password-reset/request`, `POST /api/auth/forgot-username` (Task 5).

- [ ] **Step 1: Add the email input to the login/setup form**

In `BookWheel/wwwroot/index.html`, inside `#loginForm`, add a new email field right after the username label and before the password label:

```html
        <label id="setupEmailLabel" class="hidden">
          <span data-i18n="auth.emailLabel">Email</span>
          <input id="setupEmail" type="email" autocomplete="email" aria-describedby="loginError" />
        </label>
```

(placed between the closing `</label>` of the username field and the opening `<label>` of the password field). It starts hidden — `app.js` shows it and marks it `required` only in setup mode, since `/api/auth/login` must never require an email.

- [ ] **Step 2: Add the forgot-password/forgot-username links and their inline forms**

In `BookWheel/wwwroot/index.html`, right after `loginForm`'s closing `</form>` tag (and before `resetPasswordForm`), add:

```html
      <div id="authHelperLinks" class="stack">
        <button type="button" id="forgotPasswordLinkBtn" class="link-btn" data-i18n="auth.forgotPasswordLink">Forgot password?</button>
        <button type="button" id="forgotUsernameLinkBtn" class="link-btn" data-i18n="auth.forgotUsernameLink">Forgot username?</button>
      </div>

      <form id="forgotPasswordForm" class="stack hidden">
        <label>
          <span data-i18n="auth.usernameLabel">Username</span>
          <input id="forgotPasswordUsername" autocomplete="username" required />
        </label>
        <div class="actions">
          <button type="submit" data-i18n="auth.forgotPasswordSubmit">Send reset link</button>
          <button type="button" id="cancelForgotPasswordBtn" class="secondary" data-i18n="common.cancel">Cancel</button>
        </div>
        <p id="forgotPasswordMessage" class="message" aria-live="polite"></p>
      </form>

      <form id="forgotUsernameForm" class="stack hidden">
        <label>
          <span data-i18n="auth.emailLabel">Email</span>
          <input id="forgotUsernameEmail" type="email" autocomplete="email" required />
        </label>
        <div class="actions">
          <button type="submit" data-i18n="auth.forgotUsernameSubmit">Send username</button>
          <button type="button" id="cancelForgotUsernameBtn" class="secondary" data-i18n="common.cancel">Cancel</button>
        </div>
        <p id="forgotUsernameMessage" class="message" aria-live="polite"></p>
      </form>
```

- [ ] **Step 3: Add the email input to the admin create-user form**

In `BookWheel/wwwroot/index.html`, inside `#createUserForm`, add right after the username label and before the admin checkbox label:

```html
            <label>
              <span data-i18n="users.emailLabel">Email</span>
              <input id="createUserEmail" type="email" autocomplete="email" placeholder="reader-01@example.com" required />
            </label>
```

- [ ] **Step 4: Add a small `.link-btn` style**

In `BookWheel/wwwroot/css/site.css`, add right after the `.message` rule:

```css
.link-btn {
  background: none;
  color: var(--accent);
  font-weight: 600;
  text-decoration: underline;
  padding: 0;
  align-self: flex-start;
}
```

- [ ] **Step 5: Add the new i18n keys (all three locales)**

In `BookWheel/wwwroot/js/i18n.js`, in the `en` locale's `auth` block, add right after `passwordLabel: 'Password',`:

```javascript
        emailLabel: 'Email',
```

and right after `credentialsRequiredError: 'Username and password are required.',`:

```javascript
        emailRequiredError: 'Email is required.',
        forgotPasswordLink: 'Forgot password?',
        forgotUsernameLink: 'Forgot username?',
        forgotPasswordSubmit: 'Send reset link',
        forgotUsernameSubmit: 'Send username',
        forgotPasswordSendingBusy: 'Sending...',
        forgotUsernameSendingBusy: 'Sending...',
        forgotPasswordSentMessage: 'If that account exists and has an email on file, a reset link has been sent.',
        forgotUsernameSentMessage: "If that email is on file, we've sent the associated username.",
```

In the `es` locale's `auth` block (same relative position, 250 lines further down), add:

```javascript
        emailLabel: 'Correo electrónico',
```

and:

```javascript
        emailRequiredError: 'El correo electrónico es obligatorio.',
        forgotPasswordLink: '¿Olvidaste tu contraseña?',
        forgotUsernameLink: '¿Olvidaste tu nombre de usuario?',
        forgotPasswordSubmit: 'Enviar enlace de restablecimiento',
        forgotUsernameSubmit: 'Enviar nombre de usuario',
        forgotPasswordSendingBusy: 'Enviando...',
        forgotUsernameSendingBusy: 'Enviando...',
        forgotPasswordSentMessage: 'Si esa cuenta existe y tiene un correo electrónico registrado, se ha enviado un enlace de restablecimiento.',
        forgotUsernameSentMessage: 'Si ese correo electrónico está registrado, hemos enviado el nombre de usuario asociado.',
```

In the `pl` locale's `auth` block, add:

```javascript
        emailLabel: 'Adres e-mail',
```

and:

```javascript
        emailRequiredError: 'Adres e-mail jest wymagany.',
        forgotPasswordLink: 'Nie pamiętasz hasła?',
        forgotUsernameLink: 'Nie pamiętasz nazwy użytkownika?',
        forgotPasswordSubmit: 'Wyślij link resetujący',
        forgotUsernameSubmit: 'Wyślij nazwę użytkownika',
        forgotPasswordSendingBusy: 'Wysyłanie...',
        forgotUsernameSendingBusy: 'Wysyłanie...',
        forgotPasswordSentMessage: 'Jeśli takie konto istnieje i ma zarejestrowany adres e-mail, wysłano link do resetowania hasła.',
        forgotUsernameSentMessage: 'Jeśli ten adres e-mail jest zarejestrowany, wysłaliśmy powiązaną nazwę użytkownika.',
```

Now add `emailRequiredError` to the `users` namespace too (all three locales, right after `usernameRequiredError`), since the create-user form needs its own copy of the message:

`en` (line ~178): `emailRequiredError: 'Email is required.',`
`es` (line ~428): `emailRequiredError: 'El correo electrónico es obligatorio.',`
`pl` (line ~678): `emailRequiredError: 'Adres e-mail jest wymagany.',`

Also add an `emailLabel` key to the `users` namespace (all three locales, right after `administratorLabel`), for the create-user form's field label — reuse the same English/Spanish/Polish text as `auth.emailLabel`:

`en`: `emailLabel: 'Email',`
`es`: `emailLabel: 'Correo electrónico',`
`pl`: `emailLabel: 'Adres e-mail',`

(Step 3's `createUserForm` email label already uses `data-i18n="users.emailLabel"`, matching this — every other string in that panel reads from the `users` namespace too.)

- [ ] **Step 6: Wire up the setup email field and the two forgot-* forms in `app.js`**

In `BookWheel/wwwroot/js/app.js`, add new `const` element references right after `const passwordInput = document.getElementById('password');`:

```javascript
const setupEmailLabel = document.getElementById('setupEmailLabel');
const setupEmailInput = document.getElementById('setupEmail');
const authHelperLinks = document.getElementById('authHelperLinks');
const forgotPasswordLinkBtn = document.getElementById('forgotPasswordLinkBtn');
const forgotUsernameLinkBtn = document.getElementById('forgotUsernameLinkBtn');
const forgotPasswordForm = document.getElementById('forgotPasswordForm');
const forgotPasswordUsername = document.getElementById('forgotPasswordUsername');
const forgotPasswordMessage = document.getElementById('forgotPasswordMessage');
const cancelForgotPasswordBtn = document.getElementById('cancelForgotPasswordBtn');
const forgotUsernameForm = document.getElementById('forgotUsernameForm');
const forgotUsernameEmail = document.getElementById('forgotUsernameEmail');
const forgotUsernameMessage = document.getElementById('forgotUsernameMessage');
const cancelForgotUsernameBtn = document.getElementById('cancelForgotUsernameBtn');
```

Add a new `const` right after `const createUserIsAdmin = document.getElementById('createUserIsAdmin');`:

```javascript
const createUserEmail = document.getElementById('createUserEmail');
```

In `setAuthMode`, show/require the email field only in setup mode. Replace:

```javascript
  if (mode === 'setup') {
    authTitle.textContent = t('auth.setupTitle');
    authMessage.textContent = t('auth.setupSubtitle');
    authSubmitBtn.textContent = t('auth.setupSubmit');
    return;
  }

  authTitle.textContent = t('auth.loginTitle');
  authMessage.textContent = t('auth.loginSubtitle');
  authSubmitBtn.textContent = t('auth.loginSubmit');
```

with:

```javascript
  if (mode === 'setup') {
    authTitle.textContent = t('auth.setupTitle');
    authMessage.textContent = t('auth.setupSubtitle');
    authSubmitBtn.textContent = t('auth.setupSubmit');
    setupEmailLabel.classList.remove('hidden');
    setupEmailInput.setAttribute('required', 'required');
    authHelperLinks.classList.add('hidden');
    return;
  }

  authTitle.textContent = t('auth.loginTitle');
  authMessage.textContent = t('auth.loginSubtitle');
  authSubmitBtn.textContent = t('auth.loginSubmit');
  setupEmailLabel.classList.add('hidden');
  setupEmailInput.removeAttribute('required');
  authHelperLinks.classList.remove('hidden');
```

In `resetAuthForm`, clear the email field too. Replace:

```javascript
function resetAuthForm() {
  usernameInput.value = '';
  passwordInput.value = '';
  usernameInput.setAttribute('aria-invalid', 'false');
  passwordInput.setAttribute('aria-invalid', 'false');
  loginError.textContent = '';
}
```

with:

```javascript
function resetAuthForm() {
  usernameInput.value = '';
  passwordInput.value = '';
  setupEmailInput.value = '';
  usernameInput.setAttribute('aria-invalid', 'false');
  passwordInput.setAttribute('aria-invalid', 'false');
  loginError.textContent = '';
}
```

In the `loginForm` submit handler, validate and send the email only in setup mode. Replace:

```javascript
  const username = usernameInput.value.trim();
  const password = passwordInput.value;

  if (!username || !password) {
    usernameInput.setAttribute('aria-invalid', 'true');
    passwordInput.setAttribute('aria-invalid', 'true');
    loginError.textContent = t('auth.credentialsRequiredError');
    return;
  }

  usernameInput.setAttribute('aria-invalid', 'false');
  passwordInput.setAttribute('aria-invalid', 'false');
```

with:

```javascript
  const username = usernameInput.value.trim();
  const password = passwordInput.value;
  const email = setupEmailInput.value.trim();

  if (!username || !password) {
    usernameInput.setAttribute('aria-invalid', 'true');
    passwordInput.setAttribute('aria-invalid', 'true');
    loginError.textContent = t('auth.credentialsRequiredError');
    return;
  }

  if (authMode === 'setup' && !email) {
    setupEmailInput.setAttribute('aria-invalid', 'true');
    loginError.textContent = t('auth.emailRequiredError');
    return;
  }

  usernameInput.setAttribute('aria-invalid', 'false');
  passwordInput.setAttribute('aria-invalid', 'false');
  setupEmailInput.setAttribute('aria-invalid', 'false');
```

and replace the request body:

```javascript
    const authResult = await requestJson(authMode === 'setup' ? '/api/auth/setup' : '/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({
        username,
        password
      })
    });
```

with:

```javascript
    const authResult = await requestJson(authMode === 'setup' ? '/api/auth/setup' : '/api/auth/login', {
      method: 'POST',
      body: JSON.stringify(
        authMode === 'setup'
          ? { username, password, email }
          : { username, password }
      )
    });
```

Add the forgot-password/forgot-username link and form handlers at the end of the file (near the other top-level `addEventListener` calls, e.g. right after the `loginForm.addEventListener('submit', ...)` block ends):

```javascript
forgotPasswordLinkBtn.addEventListener('click', () => {
  forgotPasswordMessage.textContent = '';
  forgotPasswordUsername.value = '';
  loginForm.classList.add('hidden');
  authHelperLinks.classList.add('hidden');
  forgotPasswordForm.classList.remove('hidden');
});

cancelForgotPasswordBtn.addEventListener('click', () => {
  forgotPasswordForm.classList.add('hidden');
  loginForm.classList.remove('hidden');
  authHelperLinks.classList.remove('hidden');
});

forgotPasswordForm.addEventListener('submit', async event => {
  event.preventDefault();
  const { t } = window.BookWheelI18n;
  const username = forgotPasswordUsername.value.trim();
  if (!username) {
    return;
  }

  const submitBtn = forgotPasswordForm.querySelector('button[type="submit"]');
  setButtonBusy(submitBtn, true, t('auth.forgotPasswordSendingBusy'), t('auth.forgotPasswordSubmit'));
  try {
    await requestJson('/api/auth/password-reset/request', {
      method: 'POST',
      body: JSON.stringify({ username })
    });
    forgotPasswordMessage.textContent = t('auth.forgotPasswordSentMessage');
  } finally {
    setButtonBusy(submitBtn, false, t('auth.forgotPasswordSendingBusy'), t('auth.forgotPasswordSubmit'));
  }
});

forgotUsernameLinkBtn.addEventListener('click', () => {
  forgotUsernameMessage.textContent = '';
  forgotUsernameEmail.value = '';
  loginForm.classList.add('hidden');
  authHelperLinks.classList.add('hidden');
  forgotUsernameForm.classList.remove('hidden');
});

cancelForgotUsernameBtn.addEventListener('click', () => {
  forgotUsernameForm.classList.add('hidden');
  loginForm.classList.remove('hidden');
  authHelperLinks.classList.remove('hidden');
});

forgotUsernameForm.addEventListener('submit', async event => {
  event.preventDefault();
  const { t } = window.BookWheelI18n;
  const email = forgotUsernameEmail.value.trim();
  if (!email) {
    return;
  }

  const submitBtn = forgotUsernameForm.querySelector('button[type="submit"]');
  setButtonBusy(submitBtn, true, t('auth.forgotUsernameSendingBusy'), t('auth.forgotUsernameSubmit'));
  try {
    await requestJson('/api/auth/forgot-username', {
      method: 'POST',
      body: JSON.stringify({ email })
    });
    forgotUsernameMessage.textContent = t('auth.forgotUsernameSentMessage');
  } finally {
    setButtonBusy(submitBtn, false, t('auth.forgotUsernameSendingBusy'), t('auth.forgotUsernameSubmit'));
  }
});
```

(`requestJson`'s existing error handling already surfaces network failures; since both endpoints always return `200`, there's no error branch to add — the generic message is shown unconditionally once the request completes. `setButtonBusy` is the existing helper already used by `createUserForm`'s submit handler.)

- [ ] **Step 7: Wire the create-user email field**

In the `createUserForm` submit handler, replace:

```javascript
    const username = createUserUsername.value.trim();

    if (!username) {
      createUserUsername.setAttribute('aria-invalid', 'true');
      userManagementError.textContent = t('users.usernameRequiredError');
      return;
    }

    createUserUsername.setAttribute('aria-invalid', 'false');
```

with:

```javascript
    const username = createUserUsername.value.trim();
    const email = createUserEmail.value.trim();

    if (!username) {
      createUserUsername.setAttribute('aria-invalid', 'true');
      userManagementError.textContent = t('users.usernameRequiredError');
      return;
    }

    if (!email) {
      createUserEmail.setAttribute('aria-invalid', 'true');
      userManagementError.textContent = t('users.emailRequiredError');
      return;
    }

    createUserUsername.setAttribute('aria-invalid', 'false');
    createUserEmail.setAttribute('aria-invalid', 'false');
```

and replace the request body plus the disable/enable pairs around it:

```javascript
    const createUserSubmitButton = createUserForm.querySelector('button[type="submit"]');
    setButtonBusy(createUserSubmitButton, true, t('common.creating'), t('users.createUserBtn'));
    createUserUsername.disabled = true;
    createUserIsAdmin.disabled = true;

    try {
      const result = await requestJson('/api/users', {
        method: 'POST',
        body: JSON.stringify({
          username,
          isAdmin: createUserIsAdmin.checked
        })
      });
```

with:

```javascript
    const createUserSubmitButton = createUserForm.querySelector('button[type="submit"]');
    setButtonBusy(createUserSubmitButton, true, t('common.creating'), t('users.createUserBtn'));
    createUserUsername.disabled = true;
    createUserEmail.disabled = true;
    createUserIsAdmin.disabled = true;

    try {
      const result = await requestJson('/api/users', {
        method: 'POST',
        body: JSON.stringify({
          username,
          isAdmin: createUserIsAdmin.checked,
          email
        })
      });
```

and in the `finally` block, replace:

```javascript
    } finally {
      setButtonBusy(createUserSubmitButton, false, t('common.creating'), t('users.createUserBtn'));
      createUserUsername.disabled = false;
      createUserIsAdmin.disabled = false;
    }
```

with:

```javascript
    } finally {
      setButtonBusy(createUserSubmitButton, false, t('common.creating'), t('users.createUserBtn'));
      createUserUsername.disabled = false;
      createUserEmail.disabled = false;
      createUserIsAdmin.disabled = false;
    }
```

- [ ] **Step 8: Show each user's email in the admin user-row editor**

In the `renderUsers` (or equivalently-named user-list rendering) function in `app.js`, right after the block that builds `usernameLabel`/`username`:

```javascript
    const username = document.createElement('input');
    username.className = 'user-input';
    username.value = user.username;
    username.maxLength = 64;

    const usernameLabel = document.createElement('label');
    usernameLabel.textContent = t('auth.usernameLabel');
    usernameLabel.appendChild(username);
```

add a parallel read-only-by-default email display (editable, since admins may need to backfill it for legacy accounts):

```javascript
    const email = document.createElement('input');
    email.type = 'email';
    email.className = 'user-input';
    email.value = user.email || '';

    const emailLabel = document.createElement('label');
    emailLabel.textContent = t('users.emailLabel');
    emailLabel.appendChild(email);
```

Add `email` to `editGrid.append(...)`. Replace:

```javascript
    const editGrid = document.createElement('div');
    editGrid.className = 'user-edit-grid';
    editGrid.append(usernameLabel, adminLabel, disabledLabel, forceResetLabel, lockLabel);
```

with:

```javascript
    const editGrid = document.createElement('div');
    editGrid.className = 'user-edit-grid';
    editGrid.append(usernameLabel, emailLabel, adminLabel, disabledLabel, forceResetLabel, lockLabel);
```

Add `email` to the dirty-check and disable/enable logic. In `evaluateDirty`, replace:

```javascript
      const hasChanges =
        username.value.trim() !== user.username ||
        adminCheckbox.checked !== Boolean(user.isAdmin) ||
        disabledCheckbox.checked !== Boolean(user.isDisabled) ||
        forceResetCheckbox.checked !== Boolean(user.forcePasswordReset) ||
        lockCheckbox.checked !== Boolean(user.isLocked);
```

with:

```javascript
      const hasChanges =
        username.value.trim() !== user.username ||
        email.value.trim() !== (user.email || '') ||
        adminCheckbox.checked !== Boolean(user.isAdmin) ||
        disabledCheckbox.checked !== Boolean(user.isDisabled) ||
        forceResetCheckbox.checked !== Boolean(user.forcePasswordReset) ||
        lockCheckbox.checked !== Boolean(user.isLocked);
```

Add `email.addEventListener('input', evaluateDirty);` right after `username.addEventListener('input', evaluateDirty);`, and add `email.disabled = true;`/`email.disabled = pending;` alongside every other field's `.disabled` toggle in the `isCurrentUser || isFirstUser` block and in `togglePendingState`.

In the `saveButton` click handler, include `email` in the PUT body. Replace:

```javascript
        await requestJson(`/api/users/${user.userId}`, {
          method: 'PUT',
          body: JSON.stringify({
            username: trimmedUsername,
            isAdmin: adminCheckbox.checked,
            isDisabled: disabledCheckbox.checked,
            forcePasswordReset: forceResetCheckbox.checked,
            isLocked: lockCheckbox.checked
          })
        });
```

with:

```javascript
        await requestJson(`/api/users/${user.userId}`, {
          method: 'PUT',
          body: JSON.stringify({
            username: trimmedUsername,
            isAdmin: adminCheckbox.checked,
            isDisabled: disabledCheckbox.checked,
            forcePasswordReset: forceResetCheckbox.checked,
            isLocked: lockCheckbox.checked,
            email: email.value.trim() || null
          })
        });
```

- [ ] **Step 9: Add frontend regression tests**

In `BookWheel.Tests/BookWheelFrontendTests.cs`, add a new test near the other markup/i18n checks:

```csharp
    [Fact]
    public async Task Home_Page_Should_Include_Setup_Email_Field_And_Forgot_Links()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("id=\"setupEmail\"", html, StringComparison.Ordinal);
        Assert.Contains("data-i18n=\"auth.emailLabel\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"forgotPasswordLinkBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"forgotUsernameLinkBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"createUserEmail\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Frontend_I18n_Should_Include_Forgot_Password_And_Username_Strings_In_All_Locales()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/i18n.js");
        var script = await response.Content.ReadAsStringAsync();

        Assert.Contains("forgotPasswordLink: 'Forgot password?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotPasswordLink: '¿Olvidaste tu contraseña?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotPasswordLink: 'Nie pamiętasz hasła?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotUsernameLink: 'Forgot username?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotUsernameLink: '¿Olvidaste tu nombre de usuario?'", script, StringComparison.Ordinal);
        Assert.Contains("forgotUsernameLink: 'Nie pamiętasz nazwy użytkownika?'", script, StringComparison.Ordinal);
    }
```

- [ ] **Step 10: Build, run all tests**

Run: `dotnet build BookWheel.slnx && dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: all tests pass, including the two new frontend tests.

- [ ] **Step 11: Manually verify in a browser**

Run the app locally (see README's "Running locally" section for the exact command) and walk through: first-run setup now requires an email; logging in afterward still requires only username/password; "Forgot password?"/"Forgot username?" links show inline forms and a generic confirmation message on submit; the admin create-user form requires an email; the admin user-row editor shows and can edit each user's email.

- [ ] **Step 12: Commit**

```bash
git add BookWheel/wwwroot/index.html BookWheel/wwwroot/js/app.js BookWheel/wwwroot/js/i18n.js BookWheel/wwwroot/css/site.css BookWheel.Tests/BookWheelFrontendTests.cs
git commit -m "Add setup/create-user email fields and forgot-password/forgot-username UI (GH #115)"
```

---

### Task 8: Documentation and version bump

**Files:**
- Modify: `README.md`
- Modify: `docs/audits/database.md`
- Modify: `BookWheel/BookWheel.csproj`

**Interfaces:** None (documentation/metadata only).

- [ ] **Step 1: Update `README.md`'s Features list**

In `README.md`, add a new bullet to the Features section right after the existing `- Administrator-generated password reset links (24-hour expiry) instead of direct password setting` bullet:

```markdown
- Email-based account recovery: users can request a password reset link (`POST /api/auth/password-reset/request`, by username) or a reminder of their username (`POST /api/auth/forgot-username`, by email) from the login screen; both always return the same generic confirmation regardless of whether the account/email exists, to prevent account enumeration. Email is now a required, admin-set, globally-unique field on every account created via setup or by an administrator (existing accounts created before this feature keep working with no email until an administrator backfills one). Outbound email is sent via SMTP — see "Email (SMTP)" below for configuration. (GH #115)
```

- [ ] **Step 2: Update the API Overview section**

In `README.md`'s "API Overview" section, add to the Auth endpoints list (after `GET /api/auth/me`):

```markdown
- `POST /api/auth/password-reset/request` — self-service; body `{ username }`; always returns `200` with a generic message
- `POST /api/auth/forgot-username` — self-service; body `{ email }`; always returns `200` with a generic message
```

Update the `POST /api/users` behavior note. Replace:

```markdown
`POST /api/users` behavior:

- Request body accepts `username` and `isAdmin` only
- Administrators do not provide a password when creating a user
- Response includes `setupLink` and `setupLinkExpiresAtUtc` for secure account setup sharing
```

with:

```markdown
`POST /api/users` behavior:

- Request body accepts `username`, `isAdmin`, and `email` (required)
- Administrators do not provide a password when creating a user
- Response includes `setupLink` and `setupLinkExpiresAtUtc` for secure account setup sharing
- `email` must be unique across all accounts (case-insensitive); `PUT /api/users/{id}` can also set/clear/change an existing account's email
```

- [ ] **Step 3: Add an "Email (SMTP)" configuration section**

In `README.md`, add a new `##` section right after the existing `## Analytics` section (which ends right before `## Progressive Web App` at line 90) — insert the new section between them, mirroring `## Analytics`'s own structure (a short standalone config-reference section, same heading level):

```markdown
## Email (SMTP)

Password-reset and forgotten-username emails are sent via SMTP, configured through `appsettings.json`'s `Smtp` section or environment variable overrides — there is no in-app settings UI for this, matching how the Google Books API key is configured:

- `Smtp:Host` / `Smtp__Host` / `SMTP_HOST` — SMTP server hostname. Leave empty to disable email sending entirely (delivery is silently skipped and logged, never surfaced as an error to the caller).
- `Smtp:Port` / `Smtp__Port` / `SMTP_PORT` — defaults to `587`.
- `Smtp:Username` / `Smtp__Username` / `SMTP_USERNAME` and `Smtp:Password` / `Smtp__Password` / `SMTP_PASSWORD` — SMTP auth credentials; leave both empty to connect without authentication.
- `Smtp:EnableSsl` / `Smtp__EnableSsl` — `true` (default) uses STARTTLS; `false` connects unencrypted.
- `Smtp:FromAddress` / `Smtp__FromAddress` / `SMTP_FROM_ADDRESS` and `Smtp:FromName` / `Smtp__FromName` — the sender address/name on outgoing emails.

In `docker-compose.yml`, set `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, and `SMTP_FROM_ADDRESS` in your own `.env` file.
```

- [ ] **Step 4: Update `docs/audits/database.md`**

In `docs/audits/database.md`, in Finding 3 ("`citext` used correctly for `users.Username`"), add a short addendum noting the new column follows the same pattern:

```markdown
**Update (GH #115):** `users.Email` (added by the `AddUserEmail` migration) follows this exact same pattern — nullable `citext`, with a unique index (`IX_users_Email`). Unlike `Username`, `Email` is nullable (legacy accounts predating this feature have none), but Postgres unique indexes never treat two `NULL`s as a collision, so multiple such accounts coexist without violating uniqueness.
```

In Finding 9 ("Username uniqueness — enforced at the DB level, not just app code"), add:

```markdown
**Update (GH #115):** The same two-layer pattern (DB unique index + app-level `DbUpdateException`/`UniqueViolation` handling, translated to a friendly `InvalidOperationException`) now also covers `users.Email` in `PostgresCredentialRepository.CreateUserAsync`/`UpdateUserCoreAsync`.
```

- [ ] **Step 5: Bump the version**

In `BookWheel/BookWheel.csproj`, change:

```xml
    <InformationalVersion Condition="'$(InformationalVersion)' == ''">2.18.0</InformationalVersion>
```

to:

```xml
    <InformationalVersion Condition="'$(InformationalVersion)' == ''">3.0.0</InformationalVersion>
```

- [ ] **Step 6: Build and run the full test suite one final time**

Run: `dotnet build BookWheel.slnx && dotnet test BookWheel.Tests/BookWheel.Tests.csproj`
Expected: builds and all tests pass.

- [ ] **Step 7: Commit**

```bash
git add README.md docs/audits/database.md BookWheel/BookWheel.csproj
git commit -m "Document SMTP email feature and bump version to 3.0.0 (GH #115)"
```
