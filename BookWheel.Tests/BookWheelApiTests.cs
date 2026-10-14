using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookWheel.Storage;
using BookWheel.Tests.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests;

public sealed class BookWheelApiTests : IClassFixture<BookWheelWebAppFactory>, IAsyncLifetime
{
    private readonly BookWheelWebAppFactory _factory;

    public BookWheelApiTests(BookWheelWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Status_Endpoint_Reports_Setup_Required_When_No_Account_Exists()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.True(doc.RootElement.GetProperty("setupRequired").GetBoolean());
    }

    [Fact]
    public async Task Login_Cookie_Secure_Flag_Matches_Request_Scheme()
    {
        // Disable automatic cookie handling so the raw Set-Cookie header is
        // visible in the response — this is required to inspect cookie attributes.
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "cookie-secure-admin",
            password = "test-password",
            email = "cookie-test@example.com",
        });

        var counter = System.Threading.Interlocked.Increment(ref _loginRateLimitIpCounter);
        var syntheticIp = $"10.{(counter >> 16) & 0xFF}.{(counter >> 8) & 0xFF}.{counter & 0xFF}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "cookie-secure-admin", password = "test-password" })
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", syntheticIp);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault();
        Assert.NotNull(setCookie);

        // The test host runs over plain HTTP; Request.IsHttps = false, so the
        // Secure attribute must not be present.  The old code forced Secure = true
        // in non-Development environments regardless of scheme, which broke
        // plain-HTTP/LAN deployments (GH #136).
        Assert.DoesNotContain("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Before_Setup_Returns_Conflict()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await PostLoginAsync(client, "test-admin", "test-password");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Failed_Login_Is_Recorded_As_Structured_Warning_Log()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        // A username unique to this test (rather than the ubiquitous "test-admin"
        // default) so the log-entry lookups below can't match a different test's
        // failed-login attempt sharing this class's now-shared LoggerProvider/log file.
        const string username = "warninglog-admin";

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username,
            password = "test-password",
            email = "test-setup@example.com"
        });

        var response = await PostLoginAsync(client, username, "wrong-password", userAgent: "BookWheelTests/1.0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var logEntry = factory.LoggerProvider.Entries.LastOrDefault(entry =>
            entry.Category == "BookWheel.Controllers.AuthController" &&
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            entry.Message.Contains("Login failed", StringComparison.Ordinal) &&
            entry.State.TryGetValue("Username", out var loggedUsername) && Equals(loggedUsername, username));

        Assert.NotNull(logEntry);
        Assert.Equal(username, logEntry!.State["Username"]);
        Assert.False(logEntry.State.ContainsKey("Password"));
        Assert.Equal("/api/auth/login", logEntry.State["Path"]);
        Assert.True(logEntry.State.ContainsKey("RequestId"));
        Assert.Equal("BookWheelTests/1.0", logEntry.State["UserAgent"]);
        Assert.DoesNotContain("wrong-password", logEntry.Message, StringComparison.Ordinal);

        // Search every rolled log file, not just one: the provider rolls
        // bookwheel-<date>.jsonl -> bookwheel-<date>-1.jsonl -> -2 ... once a file exceeds
        // MaxFileSizeBytes, and a late-running test's entry can land in a rolled file.
        var logFilePaths = Directory.GetFiles(factory.LogDirectoryPath, "bookwheel-*.jsonl");
        Assert.NotEmpty(logFilePaths);

        var logLines = new List<string>();
        foreach (var path in logFilePaths)
        {
            logLines.AddRange(await File.ReadAllLinesAsync(path));
        }

        var persistedEntry = logLines
            .Select(line => JsonDocument.Parse(line))
            .FirstOrDefault(document =>
                document.RootElement.GetProperty("Category").GetString() == "BookWheel.Controllers.AuthController" &&
                document.RootElement.GetProperty("Level").GetString() == "Warning" &&
                document.RootElement.GetProperty("Message").GetString()?.Contains("Login failed", StringComparison.Ordinal) == true &&
                document.RootElement.GetProperty("Properties").GetProperty("Username").GetString() == username);

        Assert.NotNull(persistedEntry);
        Assert.Equal(username, persistedEntry!.RootElement.GetProperty("Properties").GetProperty("Username").GetString());
        Assert.Equal("/api/auth/login", persistedEntry.RootElement.GetProperty("Properties").GetProperty("Path").GetString());
        Assert.Equal("BookWheelTests/1.0", persistedEntry.RootElement.GetProperty("Properties").GetProperty("UserAgent").GetString());
        Assert.False(persistedEntry.RootElement.GetProperty("Properties").TryGetProperty("Password", out _));
    }

    [Fact]
    public async Task Login_WithBadCredentials_ReturnsSpanishMessage_WhenAcceptLanguageIsSpanish()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "test-admin", password = "wrong-password" })
        };
        request.Headers.Add("Accept-Language", "es");
        // Distinct synthetic IP so this test's single attempt doesn't collide with the
        // dedicated rate-limit tests' shared "unknown" bucket (see LoginAsync).
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "10.99.0.3");

        var response = await client.SendAsync(request);
        using var doc = await ReadJsonAsync(response);
        var message = doc.RootElement.GetProperty("message").GetString();

        // Positive: the Spanish translation is returned for Accept-Language: es.
        Assert.Equal("Nombre de usuario o contraseña incorrectos.", message);

        // Negative: the raw English string is not leaking through for a non-English request.
        Assert.NotEqual("Invalid username or password.", message);
    }

    [Fact]
    public async Task AddBook_WithMissingTitle_ReturnsSpanishValidationMessage_WhenAcceptLanguageIsSpanish()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/books")
        {
            Content = JsonContent.Create(new { title = "" })
        };
        request.Headers.Add("Accept-Language", "es");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var titleErrors = doc.RootElement.GetProperty("errors").GetProperty("Title")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains("El título del libro es obligatorio.", titleErrors);
        Assert.DoesNotContain(titleErrors, msg => msg != null && msg.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddBook_WithOverlongIsbn_ReturnsLocalizedLengthMessage()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var response = await client.PostAsJsonAsync("/api/books", new
        {
            title = "Valid Title",
            isbn = new string('9', 21)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var isbnErrors = doc.RootElement.GetProperty("errors").GetProperty("Isbn")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains("ISBN must be 20 characters or fewer.", isbnErrors);
    }

    [Fact]
    public async Task Setup_Creates_Account_And_Logs_The_User_In()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        using (var meDoc = await ReadJsonAsync(meResponse))
        {
            Assert.True(meDoc.RootElement.GetProperty("authenticated").GetBoolean());
            Assert.True(meDoc.RootElement.GetProperty("isAdmin").GetBoolean());
            Assert.Equal("test-admin", meDoc.RootElement.GetProperty("username").GetString());
        }

        var booksResponse = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, booksResponse.StatusCode);
    }

    [Fact]
    public async Task First_User_Is_Admin_And_Can_Create_Other_Users()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        Assert.False(string.IsNullOrWhiteSpace(readerSetupLink));

        using var listResponse = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var usersDoc = await ReadJsonAsync(listResponse);
        var users = usersDoc.RootElement.GetProperty("users").EnumerateArray().ToList();

        Assert.Contains(users, user => user.GetProperty("username").GetString() == "test-admin" && user.GetProperty("isAdmin").GetBoolean());
        Assert.Contains(users, user => user.GetProperty("username").GetString() == "reader-one" && !user.GetProperty("isAdmin").GetBoolean());
        Assert.Contains(users, user => user.GetProperty("username").GetString() == "reader-one" && user.GetProperty("forcePasswordReset").GetBoolean());
        Assert.All(users, user => Assert.False(user.TryGetProperty("passwordHash", out _)));
    }

    [Fact]
    public async Task GetUsers_Reports_Null_LastLogin_And_Zero_Books_For_A_Fresh_User()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "test-setup@example.com" });

        using var listResponse = await client.GetAsync("/api/users");
        using var usersDoc = await ReadJsonAsync(listResponse);
        var admin = usersDoc.RootElement.GetProperty("users").EnumerateArray()
            .Single(user => user.GetProperty("username").GetString() == "test-admin");

        Assert.Equal(JsonValueKind.Null, admin.GetProperty("lastLoginAtUtc").ValueKind);
        Assert.Equal(0, admin.GetProperty("bookCount").GetInt32());
    }

    [Fact]
    public async Task GetUsers_Reports_LastLogin_And_Book_Count_Including_Soft_Deleted_Books()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "test-setup@example.com" });
        await client.PostAsJsonAsync("/api/books", new { title = "Active Book" });
        var secondBookResponse = await client.PostAsJsonAsync("/api/books", new { title = "Removed Book" });
        using var secondBookDoc = await ReadJsonAsync(secondBookResponse);
        var removedBookId = secondBookDoc.RootElement.GetProperty("id").GetGuid();
        await client.DeleteAsync($"/api/books/{removedBookId}");

        await client.PostAsync("/api/auth/logout", content: null);
        var beforeLogin = DateTimeOffset.UtcNow;
        await LoginAsync(client, "test-admin", "test-password");

        using var listResponse = await client.GetAsync("/api/users");
        using var usersDoc = await ReadJsonAsync(listResponse);
        var admin = usersDoc.RootElement.GetProperty("users").EnumerateArray()
            .Single(user => user.GetProperty("username").GetString() == "test-admin");

        // Soft-deleted books still count here, matching what DELETE /api/users/{id}
        // actually removes (BookWheelApiTests.DeleteUser_Removes_All_Of_Their_Books
        // covers that count via IgnoreQueryFilters()).
        Assert.Equal(2, admin.GetProperty("bookCount").GetInt32());
        var lastLoginAtUtc = admin.GetProperty("lastLoginAtUtc").GetDateTimeOffset();
        Assert.True(lastLoginAtUtc >= beforeLogin);
    }

    [Fact]
    public async Task GetUsers_Book_Count_Matches_Books_Actually_Removed_On_Delete()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "test-setup@example.com" });
        var (readerId, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");
        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");
        await client.PostAsJsonAsync("/api/books", new { title = "Reader Book One" });
        var secondResponse = await client.PostAsJsonAsync("/api/books", new { title = "Reader Book Two" });
        using var secondDoc = await ReadJsonAsync(secondResponse);
        await client.DeleteAsync($"/api/books/{secondDoc.RootElement.GetProperty("id").GetGuid()}");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "test-admin", "test-password");

        using var beforeListResponse = await client.GetAsync("/api/users");
        using var beforeUsersDoc = await ReadJsonAsync(beforeListResponse);
        var readerBefore = beforeUsersDoc.RootElement.GetProperty("users").EnumerateArray()
            .Single(user => user.GetProperty("username").GetString() == "reader-one");
        var reportedBookCount = readerBefore.GetProperty("bookCount").GetInt32();

        var deleteResponse = await client.DeleteAsync($"/api/users/{readerId}");
        using var deleteDoc = await ReadJsonAsync(deleteResponse);

        Assert.Equal(2, reportedBookCount);
        Assert.Equal(reportedBookCount, deleteDoc.RootElement.GetProperty("removedBooks").GetInt32());
    }

    [Fact]
    public async Task System_ConfigStatus_Is_Forbidden_For_NonAdmin_And_Returns_Booleans_For_Admin()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var adminResponse = await client.GetAsync("/api/system/config-status");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        using var doc = await ReadJsonAsync(adminResponse);
        var root = doc.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.True(root.GetProperty("baseUrlConfigured").GetBoolean());
        Assert.False(root.GetProperty("smtpConfigured").GetBoolean());
        Assert.False(root.GetProperty("googleBooksKeyConfigured").GetBoolean());
        Assert.True(root.TryGetProperty("proxyTrustConfigured", out var proxy) && proxy.ValueKind is JsonValueKind.True or JsonValueKind.False);

        var (_, setupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, setupLink, "reader-pass-1");
        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");

        var readerResponse = await client.GetAsync("/api/system/config-status");
        Assert.Equal(HttpStatusCode.Forbidden, readerResponse.StatusCode);
        var readerPost = await client.PostAsync("/api/system/test-email", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, readerPost.StatusCode);
    }

    [Fact]
    public async Task System_TestEmail_Sends_To_Admin_Address()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin-test@example.com" });
        factory.FakeEmailSender.SentEmails.Clear();

        var response = await client.PostAsync("/api/system/test-email", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Equal("admin-test@example.com", sent.ToAddress);
    }

    [Fact]
    public async Task System_TestEmail_Returns_BadRequest_When_Admin_Has_No_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin-noemail@example.com" });
        var repo = factory.Services.GetRequiredService<ICredentialRepository>();
        var admin = await repo.FindByUsernameAsync("test-admin");
        Assert.NotNull(admin);
        await repo.UpdateUserAsync(admin!.UserId, admin.Username, true, false, false, false, null);
        factory.FakeEmailSender.SentEmails.Clear();

        var response = await client.PostAsync("/api/system/test-email", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.FakeEmailSender.SentEmails);
    }

    [Fact]
    public async Task Non_Admin_User_Cannot_Access_User_Management_Endpoints()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (createdUserId, createdUserSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, createdUserSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");

        var listUsersResponse = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, listUsersResponse.StatusCode);

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-two",
            isAdmin = false,
            email = "reader-two@example.com"
        });

        Assert.Equal(HttpStatusCode.Forbidden, createUserResponse.StatusCode);

        var createResetLinkResponse = await client.PostAsync($"/api/users/{createdUserId}/password-reset-link", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, createResetLinkResponse.StatusCode);

        var deleteUserResponse = await client.DeleteAsync($"/api/users/{createdUserId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteUserResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Update_Other_User_Account()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (createdUserId, _) = await CreateUserAsync(client, "reader-one");

        var updateResponse = await client.PutAsJsonAsync($"/api/users/{createdUserId}", new
        {
            username = "reader-prime",
            isAdmin = true
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var updatedUserDoc = await ReadJsonAsync(updateResponse);
        Assert.True(updatedUserDoc.RootElement.GetProperty("isAdmin").GetBoolean());
        Assert.Equal("reader-prime", updatedUserDoc.RootElement.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Admin_Can_Update_Own_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "original@example.com"
        });
        using var setupDoc = await ReadJsonAsync(setupResponse);
        var adminId = setupDoc.RootElement.GetProperty("user").GetProperty("userId").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/users/{adminId}", new
        {
            username = "test-admin",
            isAdmin = true,
            isDisabled = false,
            forcePasswordReset = false,
            isLocked = false,
            email = "updated@example.com"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updatedDoc = await ReadJsonAsync(updateResponse);
        Assert.Equal("updated@example.com", updatedDoc.RootElement.GetProperty("email").GetString());

        var meResponse = await client.GetAsync("/api/auth/me");
        using var meDoc = await ReadJsonAsync(meResponse);
        Assert.Equal("updated@example.com", meDoc.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Admin_Self_Update_Rejects_Changes_To_Other_Fields()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "original@example.com"
        });
        using var setupDoc = await ReadJsonAsync(setupResponse);
        var adminId = setupDoc.RootElement.GetProperty("user").GetProperty("userId").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/users/{adminId}", new
        {
            username = "renamed-admin",
            isAdmin = true,
            isDisabled = false,
            forcePasswordReset = false,
            isLocked = false,
            email = "updated@example.com"
        });

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Setup_Login_And_Me_Responses_Include_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "admin@example.com"
        });
        using var setupDoc = await ReadJsonAsync(setupResponse);
        Assert.Equal("admin@example.com", setupDoc.RootElement.GetProperty("user").GetProperty("email").GetString());

        await client.PostAsync("/api/auth/logout", content: null);
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { username = "test-admin", password = "test-password" });
        using var loginDoc = await ReadJsonAsync(loginResponse);
        Assert.Equal("admin@example.com", loginDoc.RootElement.GetProperty("user").GetProperty("email").GetString());

        var meResponse = await client.GetAsync("/api/auth/me");
        using var meDoc = await ReadJsonAsync(meResponse);
        Assert.Equal("admin@example.com", meDoc.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_Success_Stamps_LastLoginAtUtc()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        var repo = factory.Services.GetRequiredService<ICredentialRepository>();
        var admin = await repo.FindByUsernameAsync("test-admin");
        Assert.NotNull(admin);
        Assert.Null(admin!.LastLoginAtUtc);

        var beforeLogin = DateTimeOffset.UtcNow;
        await client.PostAsJsonAsync("/api/auth/login", new { username = "test-admin", password = "test-password" });

        var reloaded = await repo.FindByIdAsync(admin.UserId);
        Assert.NotNull(reloaded!.LastLoginAtUtc);
        Assert.True(reloaded.LastLoginAtUtc >= beforeLogin);
    }

    [Fact]
    public async Task Login_Failure_Does_Not_Stamp_LastLoginAtUtc()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        var repo = factory.Services.GetRequiredService<ICredentialRepository>();
        var admin = await repo.FindByUsernameAsync("test-admin");
        Assert.NotNull(admin);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "test-admin", password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var reloaded = await repo.FindByIdAsync(admin!.UserId);
        Assert.Null(reloaded!.LastLoginAtUtc);
    }

    [Fact]
    public async Task Login_Attempt_On_Disabled_Account_Does_Not_Stamp_LastLoginAtUtc()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        var repo = factory.Services.GetRequiredService<ICredentialRepository>();
        var admin = await repo.FindByUsernameAsync("test-admin");
        Assert.NotNull(admin);
        await repo.UpdateUserAsync(admin!.UserId, admin.Username, isAdmin: true, isDisabled: true, forcePasswordReset: false, isLocked: false, email: null);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "test-admin", password = "test-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var reloaded = await repo.FindByIdAsync(admin.UserId);
        Assert.Null(reloaded!.LastLoginAtUtc);
    }

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

        var response = await PostRateLimitedAuthEndpointAsync(client, "/api/auth/password-reset/request", new { username = "does-not-exist" }, NextSyntheticIp());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.FakeEmailSender.SentEmails);
    }

    [Fact]
    public async Task PasswordResetRequest_For_Known_Username_With_Email_Sends_Reset_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var response = await PostRateLimitedAuthEndpointAsync(client, "/api/auth/password-reset/request", new { username = "test-admin" }, NextSyntheticIp());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Equal("admin@example.com", sent.ToAddress);
    }

    [Fact]
    public async Task PasswordResetRequest_Does_Not_Force_Password_Reset_On_Account()
    {
        // Self-service "I forgot my password" must never itself invalidate the
        // account's current password before the caller has even opened the email
        // (that's a DoS: anyone could force-lock any account just by knowing its
        // username). Only completing the reset with a valid token should do that.
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var response = await PostRateLimitedAuthEndpointAsync(client, "/api/auth/password-reset/request", new { username = "test-admin" }, NextSyntheticIp());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(factory.FakeEmailSender.SentEmails);

        await client.PostAsync("/api/auth/logout", content: null);
        var loginResponse = await PostLoginAsync(client, "test-admin", "test-password");

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task PasswordResetRequest_For_Locked_Account_Returns_Generic_Ok_And_Sends_No_Email()
    {
        // A locked-out account must not leak a working reset link to whoever
        // requests one — that would let an attacker bypass an administrator's
        // deliberate lock just by asking.
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var (readerUserId, readerSetupLink) = await CreateUserAsync(client, "locked-reader");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        var lockResponse = await client.PutAsJsonAsync($"/api/users/{readerUserId}", new
        {
            username = "locked-reader",
            isAdmin = false,
            isDisabled = false,
            forcePasswordReset = false,
            isLocked = true,
            email = "locked-reader@example.com"
        });
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        // Creating the account above already sent its own setup-link email (see
        // Admin_Generated_Reset_Link_Also_Sends_Email_When_Target_Has_Email) — clear
        // that unrelated send so this assertion isolates the self-service request
        // under test, not account creation.
        factory.FakeEmailSender.SentEmails.Clear();

        var response = await PostRateLimitedAuthEndpointAsync(client, "/api/auth/password-reset/request", new { username = "locked-reader" }, NextSyntheticIp());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.FakeEmailSender.SentEmails);
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

        // All 5 calls deliberately share one synthetic IP: this test targets
        // AuthService's own per-username limiter (3 per 15 minutes), which requires
        // looking like a single repeated caller. The per-IP limiter added alongside
        // it in Program.cs permits 5/minute, comfortably above this loop's 5 calls.
        var syntheticIp = NextSyntheticIp();
        for (var i = 0; i < 5; i++)
        {
            var response = await PostRateLimitedAuthEndpointAsync(client, "/api/auth/password-reset/request", new { username = "rate-limit-admin" }, syntheticIp);
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

        var response = await PostRateLimitedAuthEndpointAsync(client, "/api/auth/forgot-username", new { email = "admin@example.com" }, NextSyntheticIp());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Contains("test-admin", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForgotUsername_For_Unknown_Email_Returns_Generic_Ok_And_Sends_No_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await PostRateLimitedAuthEndpointAsync(client, "/api/auth/forgot-username", new { email = "nobody@example.com" }, NextSyntheticIp());

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

        // Creating the user above already sent its own setup-link email (POST
        // /api/users emails a setup link whenever the new account has an email —
        // see UsersController.CreateUser / AuthService.CreatePasswordResetLinkAsync).
        // Clear that unrelated send so this assertion isolates the side effect of
        // the admin-triggered reset-link endpoint under test, not account creation.
        factory.FakeEmailSender.SentEmails.Clear();

        var response = await client.PostAsync($"/api/users/{readerId}/password-reset-link", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Equal("reader-one@example.com", sent.ToAddress);
    }

    [Fact]
    public async Task Password_Reset_Link_Can_Be_Generated_And_Used_Once()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (createdUserId, _) = await CreateUserAsync(client, "reader-one");

        var resetLinkResponse = await client.PostAsync($"/api/users/{createdUserId}/password-reset-link", content: null);
        Assert.Equal(HttpStatusCode.OK, resetLinkResponse.StatusCode);
        using var resetLinkDoc = await ReadJsonAsync(resetLinkResponse);
        var resetLink = resetLinkDoc.RootElement.GetProperty("resetLink").GetString();
        Assert.False(string.IsNullOrWhiteSpace(resetLink));
        Assert.Equal("reader-one", resetLinkDoc.RootElement.GetProperty("username").GetString());

        var token = ExtractResetToken(resetLink!);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var validateResponse = await client.PostAsJsonAsync("/api/auth/password-reset/validate", new
        {
            token
        });
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        using (var validateDoc = await ReadJsonAsync(validateResponse))
        {
            Assert.True(validateDoc.RootElement.GetProperty("isValid").GetBoolean());
            Assert.Equal("reader-one", validateDoc.RootElement.GetProperty("username").GetString());
        }

        await client.PostAsync("/api/auth/logout", content: null);

        var resetCompleteResponse = await client.PostAsJsonAsync("/api/auth/password-reset/complete", new
        {
            token,
            newPassword = "reader-pass-2"
        });
        Assert.Equal(HttpStatusCode.OK, resetCompleteResponse.StatusCode);

        var reusedResetResponse = await client.PostAsJsonAsync("/api/auth/password-reset/complete", new
        {
            token,
            newPassword = "reader-pass-3"
        });
        Assert.Equal(HttpStatusCode.BadRequest, reusedResetResponse.StatusCode);

        var validateAfterUseResponse = await client.PostAsJsonAsync("/api/auth/password-reset/validate", new
        {
            token
        });
        Assert.Equal(HttpStatusCode.BadRequest, validateAfterUseResponse.StatusCode);

        var oldPasswordLogin = await PostLoginAsync(client, "reader-one", "reader-pass-1");
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await PostLoginAsync(client, "reader-one", "reader-pass-2");
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }

    [Fact]
    public async Task Admin_Cannot_Delete_First_Account()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        using var meDoc = await ReadJsonAsync(meResponse);
        var firstUserId = meDoc.RootElement.GetProperty("userId").GetGuid();

        var deleteResponse = await client.DeleteAsync($"/api/users/{firstUserId}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);

        using var deleteDoc = await ReadJsonAsync(deleteResponse);
        Assert.Equal("Administrators can only remove other user accounts.", deleteDoc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Admin_Can_Delete_User_And_Their_Books()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (readerUserId, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");
        await AddBookAsync(client, "Reader One Book A");
        await AddBookAsync(client, "Reader One Book B");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client);

        var deleteResponse = await client.DeleteAsync($"/api/users/{readerUserId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var deleteDoc = await ReadJsonAsync(deleteResponse);
        Assert.Equal("reader-one", deleteDoc.RootElement.GetProperty("username").GetString());
        Assert.Equal(2, deleteDoc.RootElement.GetProperty("removedBooks").GetInt32());

        var booksPath = Path.Combine(factory.ContentRootPath, "App_Data", "books.json");
        var booksJson = await File.ReadAllTextAsync(booksPath);
        using var booksDoc = JsonDocument.Parse(booksJson);
        Assert.False(booksDoc.RootElement.TryGetProperty(readerUserId.ToString(), out _));

        await client.PostAsync("/api/auth/logout", content: null);
        var deletedUserLogin = await PostLoginAsync(client, "reader-one", "reader-pass-1");
        Assert.Equal(HttpStatusCode.Unauthorized, deletedUserLogin.StatusCode);
    }

    [Fact]
    public async Task Books_Are_Isolated_Per_User()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await AddBookAsync(client, "Admin Book");

        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");
        await AddBookAsync(client, "Reader Book");

        using (var readerBooks = await GetBooksDocumentAsync(client))
        {
            var readerTitles = readerBooks.RootElement
                .GetProperty("activeBooks")
                .EnumerateArray()
                .Select(book => book.GetProperty("title").GetString())
                .ToList();

            Assert.Contains("Reader Book", readerTitles);
            Assert.DoesNotContain("Admin Book", readerTitles);
        }

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client);

        using var adminBooks = await GetBooksDocumentAsync(client);
        var adminTitles = adminBooks.RootElement
            .GetProperty("activeBooks")
            .EnumerateArray()
            .Select(book => book.GetProperty("title").GetString())
            .ToList();

        Assert.Contains("Admin Book", adminTitles);
        Assert.DoesNotContain("Reader Book", adminTitles);
    }

    [Fact]
    public async Task Login_Is_Rate_Limited_After_Repeated_Failed_Attempts()
    {
        // Isolate on a dedicated host so this test's fixed-window login limiter starts
        // fresh at the first attempt. On the shared host the six attempts can straddle a
        // one-minute window boundary (the longer the suite runs, the likelier), which made
        // this test intermittently flaky. The derived host gets its own rate limiter but
        // still logs into the base factory's shared LoggerProvider, so the audit-log
        // assertion below is unaffected.
        using var factory = _factory.WithWebHostBuilder(_ => { });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheelTests/1.0");

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        HttpStatusCode lastStatus = HttpStatusCode.OK;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                username = "test-admin",
                password = "wrong-password"
            });

            lastStatus = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);

        var rateLimitLog = _factory.LoggerProvider.Entries.LastOrDefault(entry =>
            entry.Category == "RateLimitAudit" &&
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            entry.Message.Contains("Rate limit rejected", StringComparison.Ordinal));

        Assert.NotNull(rateLimitLog);
        Assert.Equal("/api/auth/login", rateLimitLog!.State["Path"]);
        Assert.True(rateLimitLog.State.ContainsKey("RequestId"));
        Assert.Equal("BookWheelTests/1.0", rateLimitLog.State["UserAgent"]);
    }

    [Fact]
    public async Task Login_Rate_Limiter_Uses_Forwarded_Client_Ip_When_Present()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        HttpStatusCode lastFirstIpStatus = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { username = "test-admin", password = "wrong-password" })
            };
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
            using var response = await client.SendAsync(request);
            lastFirstIpStatus = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastFirstIpStatus);

        using var secondIpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "test-admin", password = "wrong-password" })
        };
        secondIpRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.11");
        using var secondIpResponse = await client.SendAsync(secondIpRequest);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, secondIpResponse.StatusCode);
    }

    [Fact]
    public async Task Metrics_Endpoint_Provides_Structured_Operational_Counters()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await AddBookAsync(client, "Metrics Book");
        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        await client.PostAsync("/api/auth/logout", content: null);
        var failedLoginResponse = await PostLoginAsync(client, "test-admin", "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, failedLoginResponse.StatusCode);

        await LoginAsync(client);
        var metricsResponse = await client.GetAsync("/api/metrics");
        Assert.Equal(HttpStatusCode.OK, metricsResponse.StatusCode);

        using var metricsDoc = await ReadJsonAsync(metricsResponse);
        Assert.True(metricsDoc.RootElement.GetProperty("loginFailureCount").GetInt64() >= 1);
        Assert.True(metricsDoc.RootElement.GetProperty("successfulLoginCount").GetInt64() >= 1);
        Assert.True(metricsDoc.RootElement.GetProperty("spinsSinceRestart").GetInt64() >= 1);
        Assert.True(metricsDoc.RootElement.GetProperty("totalBookCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Non_Admin_User_Cannot_Access_Metrics_Endpoint()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (_, readerSetupLinkForMetrics) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLinkForMetrics, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");

        var metricsResponse = await client.GetAsync("/api/metrics");
        Assert.Equal(HttpStatusCode.Forbidden, metricsResponse.StatusCode);
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Allows_Accessing_Protected_Endpoints()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await client.PostAsync("/api/auth/logout", content: null);

        var loginResponse = await PostLoginAsync(client, "test-admin", "test-password");

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var booksResponse = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, booksResponse.StatusCode);
    }

    [Fact]
    public async Task Spin_Does_Not_Remove_Selected_Book_From_Active_List()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Book A");
        await AddBookAsync(client, "Book B");
        await AddBookAsync(client, "Book C");

        using var before = await GetBooksDocumentAsync(client);
        var beforeIds = GetBookIds(before, "activeBooks");

        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        using var spinDoc = await ReadJsonAsync(spinResponse);
        var selectedId = spinDoc.RootElement.GetProperty("selected").GetProperty("id").GetGuid();
        var activeIds = GetBookIds(spinDoc, "activeBooks");

        Assert.Equal(beforeIds.Count, activeIds.Count);
        Assert.Contains(selectedId, activeIds);
    }

    [Fact]
    public async Task Spin_Response_Includes_Author_And_CoverUrl_When_Present()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await client.PostAsJsonAsync("/api/books", new
        {
            title = "Effective Java",
            isbn = "978-0-13-468599-1",
            author = "Joshua Bloch",
            coverUrl = "https://covers.openlibrary.org/b/id/12345-L.jpg"
        });

        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        using var spinDoc = await ReadJsonAsync(spinResponse);
        var selected = spinDoc.RootElement.GetProperty("selected");
        Assert.Equal("Effective Java", selected.GetProperty("title").GetString());
        Assert.Equal("Joshua Bloch", selected.GetProperty("author").GetString());
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", selected.GetProperty("coverUrl").GetString());
    }

    [Fact]
    public async Task Spin_Response_Omits_Author_And_CoverUrl_When_Absent()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Untagged Book");

        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        using var spinDoc = await ReadJsonAsync(spinResponse);
        var selected = spinDoc.RootElement.GetProperty("selected");
        Assert.Equal("Untagged Book", selected.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, selected.GetProperty("author").ValueKind);
        Assert.Equal(JsonValueKind.Null, selected.GetProperty("coverUrl").ValueKind);
    }

    [Fact]
    public async Task Add_Book_Stores_AddedByScanner_Flag()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        // Positive: addedByScanner=true is round-tripped through the API.
        var scanResponse = await client.PostAsJsonAsync("/api/books", new { title = "Scanned Book", addedByScanner = true });
        Assert.Equal(HttpStatusCode.OK, scanResponse.StatusCode);
        using var scanDoc = await ReadJsonAsync(scanResponse);
        Assert.True(scanDoc.RootElement.GetProperty("addedByScanner").GetBoolean());

        // Negative: without the flag the field defaults to false.
        var manualResponse = await client.PostAsJsonAsync("/api/books", new { title = "Manual Book" });
        Assert.Equal(HttpStatusCode.OK, manualResponse.StatusCode);
        using var manualDoc = await ReadJsonAsync(manualResponse);
        Assert.False(manualDoc.RootElement.GetProperty("addedByScanner").GetBoolean());
    }

    [Fact]
    public async Task Update_Then_Remove_Book_Works()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Original Title");

        var updateResponse = await client.PutAsJsonAsync($"/api/books/{bookId}", new { title = "Updated Title" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using (var booksAfterUpdate = await GetBooksDocumentAsync(client))
        {
            var titles = booksAfterUpdate.RootElement
                .GetProperty("activeBooks")
                .EnumerateArray()
                .Select(book => book.GetProperty("title").GetString())
                .ToList();

            Assert.Contains("Updated Title", titles);
        }

        var removeResponse = await client.DeleteAsync($"/api/books/{bookId}");
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        using var booksAfterRemove = await GetBooksDocumentAsync(client);
        var idsAfterRemove = GetBookIds(booksAfterRemove, "activeBooks");
        Assert.DoesNotContain(bookId, idsAfterRemove);
    }

    [Fact]
    public async Task Removing_A_Book_Twice_Returns_NotFound()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Removed Twice");

        var firstRemove = await client.DeleteAsync($"/api/books/{bookId}");
        Assert.Equal(HttpStatusCode.OK, firstRemove.StatusCode);

        var secondRemove = await client.DeleteAsync($"/api/books/{bookId}");
        Assert.Equal(HttpStatusCode.NotFound, secondRemove.StatusCode);
    }

    [Fact]
    public async Task Updating_A_Removed_Book_Returns_NotFound()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Removed Book");
        await client.DeleteAsync($"/api/books/{bookId}");

        var updateResponse = await client.PutAsJsonAsync($"/api/books/{bookId}", new { title = "New Title" });

        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Removed_Book_Is_Never_Selected_By_Spin()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var keptBookId = await AddBookAsync(client, "Kept Book");
        var removedBookId = await AddBookAsync(client, "Removed Book");
        await client.DeleteAsync($"/api/books/{removedBookId}");

        for (var i = 0; i < 10; i++)
        {
            var spinResponse = await client.PostAsync("/api/books/spin", content: null);
            Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

            using var spinDoc = await ReadJsonAsync(spinResponse);
            var selectedId = spinDoc.RootElement.GetProperty("selected").GetProperty("id").GetGuid();
            Assert.Equal(keptBookId, selectedId);
        }
    }

    [Fact]
    public async Task Spin_History_Endpoint_Requires_Authentication()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/books/spin-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Spin_History_Endpoint_Is_Empty_Before_Any_Spins()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var response = await client.GetAsync("/api/books/spin-history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Empty(doc.RootElement.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Spin_Records_A_Spin_History_Entry_With_Book_And_Timestamp()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Spun Book");

        var beforeSpin = DateTimeOffset.UtcNow;
        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);
        var afterSpin = DateTimeOffset.UtcNow;

        var historyResponse = await client.GetAsync("/api/books/spin-history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        using var doc = await ReadJsonAsync(historyResponse);
        var entry = Assert.Single(doc.RootElement.GetProperty("history").EnumerateArray());
        Assert.Equal(bookId, entry.GetProperty("bookId").GetGuid());
        Assert.Equal("Spun Book", entry.GetProperty("title").GetString());
        var recordedAt = entry.GetProperty("selectedAtUtc").GetDateTimeOffset();
        Assert.InRange(recordedAt, beforeSpin.AddSeconds(-1), afterSpin.AddSeconds(1));
    }

    [Fact]
    public async Task Multiple_Spins_Are_Recorded_Newest_First()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Book A");
        await AddBookAsync(client, "Book B");

        await client.PostAsync("/api/books/spin", content: null);
        await client.PostAsync("/api/books/spin", content: null);
        await client.PostAsync("/api/books/spin", content: null);

        var historyResponse = await client.GetAsync("/api/books/spin-history");
        using var doc = await ReadJsonAsync(historyResponse);
        var entries = doc.RootElement.GetProperty("history").EnumerateArray().ToList();

        Assert.Equal(3, entries.Count);
        var timestamps = entries.Select(e => e.GetProperty("selectedAtUtc").GetDateTimeOffset()).ToList();
        var sortedDescending = timestamps.OrderByDescending(t => t).ToList();
        Assert.Equal(sortedDescending, timestamps);
    }

    [Fact]
    public async Task Spin_History_Is_Isolated_Per_User()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Admin Book");
        await client.PostAsync("/api/books/spin", content: null);

        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-history");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-history", "reader-pass-1");

        var readerHistoryResponse = await client.GetAsync("/api/books/spin-history");
        using var readerDoc = await ReadJsonAsync(readerHistoryResponse);
        Assert.Empty(readerDoc.RootElement.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Spin_History_Is_Paged_Server_Side_Newest_First_With_Total()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        for (var i = 1; i <= 5; i++)
        {
            await AddBookAsync(client, $"Paged Book {i}");
        }

        var spunTitles = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var spinResponse = await client.PostAsync("/api/books/spin", content: null);
            Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);
            using var spinDoc = await ReadJsonAsync(spinResponse);
            spunTitles.Add(spinDoc.RootElement.GetProperty("selected").GetProperty("title").GetString()!);
            await Task.Delay(5);
        }

        var expectedNewestFirst = Enumerable.Reverse(spunTitles).ToList();

        var page1Response = await client.GetAsync("/api/books/spin-history?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, page1Response.StatusCode);
        using var page1 = await ReadJsonAsync(page1Response);
        Assert.Equal(1, page1.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, page1.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, page1.RootElement.GetProperty("total").GetInt32());
        var page1Titles = page1.RootElement.GetProperty("history").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Equal(expectedNewestFirst.Take(2), page1Titles);

        var page3Response = await client.GetAsync("/api/books/spin-history?page=3&pageSize=2");
        using var page3 = await ReadJsonAsync(page3Response);
        Assert.Equal(5, page3.RootElement.GetProperty("total").GetInt32());
        var page3Titles = page3.RootElement.GetProperty("history").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Equal(expectedNewestFirst.Skip(4), page3Titles);

        var beyondResponse = await client.GetAsync("/api/books/spin-history?page=4&pageSize=2");
        using var beyond = await ReadJsonAsync(beyondResponse);
        Assert.Empty(beyond.RootElement.GetProperty("history").EnumerateArray());
        Assert.Equal(5, beyond.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Spin_History_Clamps_Page_And_PageSize()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Clamp Book");
        await client.PostAsync("/api/books/spin", content: null);

        var response = await client.GetAsync("/api/books/spin-history?page=0&pageSize=1000");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(100, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Single(doc.RootElement.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Spin_History_Keeps_Title_For_Soft_Deleted_Book()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Soon Removed Book");
        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/books/{bookId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var response = await client.GetAsync("/api/books/spin-history?page=1&pageSize=10");
        using var doc = await ReadJsonAsync(response);
        Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
        var entry = Assert.Single(doc.RootElement.GetProperty("history").EnumerateArray());
        Assert.Equal(bookId, entry.GetProperty("bookId").GetGuid());
        // Soft-deleted books are still joined (IgnoreQueryFilters), so the title is preserved.
        Assert.Equal("Soon Removed Book", entry.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Export_Includes_Isbn_Author_And_CoverUrl_For_Tagged_Books()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookWithDetailsAsync(client, "Effective Java", "9780134685991", "Joshua Bloch", "https://covers.openlibrary.org/b/id/12345-L.jpg");

        var response = await client.GetAsync("/api/books/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var book = Assert.Single(doc.RootElement.GetProperty("books").EnumerateArray());
        Assert.Equal("Effective Java", book.GetProperty("title").GetString());
        Assert.Equal("9780134685991", book.GetProperty("isbn").GetString());
        Assert.Equal("Joshua Bloch", book.GetProperty("author").GetString());
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", book.GetProperty("coverUrl").GetString());
        Assert.Equal(JsonValueKind.Null, book.GetProperty("deletedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Export_Includes_SoftDeleted_Books_With_DeletedAtUtc_Set()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Kept Book");
        var removedBookId = await AddBookAsync(client, "Removed Book");
        await client.DeleteAsync($"/api/books/{removedBookId}");

        var response = await client.GetAsync("/api/books/export");
        using var doc = await ReadJsonAsync(response);
        var books = doc.RootElement.GetProperty("books").EnumerateArray().ToList();

        Assert.Equal(2, books.Count);
        var removed = books.Single(book => book.GetProperty("title").GetString() == "Removed Book");
        Assert.NotEqual(JsonValueKind.Null, removed.GetProperty("deletedAtUtc").ValueKind);
        var kept = books.Single(book => book.GetProperty("title").GetString() == "Kept Book");
        Assert.Equal(JsonValueKind.Null, kept.GetProperty("deletedAtUtc").ValueKind);

        // The active-only endpoint is unaffected by export exposing deleted rows.
        using var activeDoc = await GetBooksDocumentAsync(client);
        var activeTitles = activeDoc.RootElement.GetProperty("activeBooks").EnumerateArray()
            .Select(book => book.GetProperty("title").GetString())
            .ToList();
        Assert.DoesNotContain("Removed Book", activeTitles);
    }

    [Fact]
    public async Task Export_Includes_SpinHistory_And_Account_Username()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Spun Book");
        await client.PostAsync("/api/books/spin", content: null);

        var response = await client.GetAsync("/api/books/export");
        using var doc = await ReadJsonAsync(response);

        Assert.Equal("test-admin", doc.RootElement.GetProperty("account").GetProperty("username").GetString());
        var historyEntry = Assert.Single(doc.RootElement.GetProperty("spinHistory").EnumerateArray());
        Assert.Equal("Spun Book", historyEntry.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Export_Endpoint_Requires_Authentication()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/books/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_Adds_New_Books_And_Reports_Counts()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[]
            {
                new { title = "Dune", isbn = (string?)"9780441013593", author = (string?)"Frank Herbert", coverUrl = (string?)null },
                new { title = "Foundation", isbn = (string?)null, author = (string?)null, coverUrl = (string?)null }
            }
        });

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(2, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("skipped").GetInt32());

        using var booksDoc = await GetBooksDocumentAsync(client);
        var dune = booksDoc.RootElement.GetProperty("activeBooks").EnumerateArray()
            .Single(book => book.GetProperty("title").GetString() == "Dune");
        Assert.Equal("9780441013593", dune.GetProperty("isbn").GetString());
        Assert.Equal("Frank Herbert", dune.GetProperty("author").GetString());
    }

    [Fact]
    public async Task Import_Skips_Case_Insensitive_Title_Duplicate()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookAsync(client, "Dune");

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "  DUNE  " } }
        });

        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task Import_Skips_Isbn_Duplicate_Even_When_Title_Differs()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookWithDetailsAsync(client, "Dune", "9780441013593", null, null);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "Dune (Reissue)", isbn = "9780441013593" } }
        });

        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task Import_Restores_Match_Against_A_SoftDeleted_Book_And_Skips_Active_Duplicates()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var removedId = await AddBookAsync(client, "Deleted On Purpose");
        await client.DeleteAsync($"/api/books/{removedId}");
        await AddBookAsync(client, "Still Here");

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "deleted on purpose" }, new { title = "Still Here" }, new { title = "Brand New" } }
        });

        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(1, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("restored").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());

        using var activeDoc = await GetBooksDocumentAsync(client);
        var ids = GetBookIds(activeDoc, "activeBooks");
        Assert.Contains(removedId, ids);
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public async Task Import_Replaces_Out_Of_Range_BookTypeId_With_Default_And_Reports_A_Warning()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new object[]
            {
                new { title = "Bad Type", bookTypeId = 99 },
                new { title = "Audio Type", bookTypeId = 3 },
                new { title = "No Type" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(3, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("warnings").GetInt32());

        using var booksDoc = await GetBooksDocumentAsync(client);
        var books = booksDoc.RootElement.GetProperty("activeBooks").EnumerateArray().ToList();
        int TypeOf(string title) => books.Single(b => b.GetProperty("title").GetString() == title).GetProperty("bookTypeId").GetInt32();
        Assert.Equal(1, TypeOf("Bad Type"));
        Assert.Equal(3, TypeOf("Audio Type"));
        Assert.Equal(1, TypeOf("No Type"));
    }

    [Fact]
    public async Task Import_Skips_Entries_Marked_Deleted_In_The_File()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var removedId = await AddBookAsync(client, "Removed Before Export");
        await AddBookAsync(client, "Kept At Export");
        await client.DeleteAsync($"/api/books/{removedId}");

        var exportResponse = await client.GetAsync("/api/books/export");
        var exportJson = await exportResponse.Content.ReadAsStringAsync();

        // Re-import the export into the same (reset) account; the removed book must stay removed.
        await factory.ResetAsync();
        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });
        await LoginAsync(client);

        using var exportDoc = JsonDocument.Parse(exportJson);
        Assert.Contains(exportDoc.RootElement.GetProperty("books").EnumerateArray(), b => b.TryGetProperty("deletedAtUtc", out var d) && d.ValueKind == JsonValueKind.String);

        var importResponse = await client.PostAsync("/api/books/import", new StringContent(
            "{\"books\":" + exportDoc.RootElement.GetProperty("books").GetRawText() + "}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(1, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());

        using var booksDoc = await GetBooksDocumentAsync(client);
        var titles = booksDoc.RootElement.GetProperty("activeBooks").EnumerateArray().Select(b => b.GetProperty("title").GetString()).ToList();
        Assert.Equal(new[] { "Kept At Export" }, titles);
    }

    [Fact]
    public async Task CreateUser_With_Email_Sends_An_Invite_Email_Not_A_Password_Reset_Email()
    {
        var factory = _factory;
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        factory.FakeEmailSender.SentEmails.Clear();

        var response = await client.PostAsJsonAsync("/api/users", new { username = "invitee", isAdmin = false, email = "invitee@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);
        Assert.True(doc.RootElement.GetProperty("inviteEmailQueued").GetBoolean());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("setupLink").GetString()));

        var sent = Assert.Single(factory.FakeEmailSender.SentEmails);
        Assert.Equal("invitee@example.com", sent.ToAddress);
        Assert.Equal("You've been invited to Book Wheel", sent.Subject);
        Assert.DoesNotContain("Reset your Book Wheel password", sent.Subject);
        Assert.Contains("test-admin invited you", sent.Body);
    }

    [Fact]
    public async Task Deleted_Endpoint_Returns_Only_The_Callers_Removed_Books()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var removedId = await AddBookAsync(client, "Removed Mine");
        await AddBookAsync(client, "Active Mine");
        await client.DeleteAsync($"/api/books/{removedId}");

        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-trash");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");
        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-trash", "reader-pass-1");
        var readerRemovedId = await AddBookAsync(client, "Removed Reader");
        await client.DeleteAsync($"/api/books/{readerRemovedId}");

        var readerResponse = await client.GetAsync("/api/books/deleted");
        Assert.Equal(HttpStatusCode.OK, readerResponse.StatusCode);
        using (var readerDoc = await ReadJsonAsync(readerResponse))
        {
            var readerBook = Assert.Single(readerDoc.RootElement.GetProperty("books").EnumerateArray());
            Assert.Equal("Removed Reader", readerBook.GetProperty("title").GetString());
        }

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client);
        var adminResponse = await client.GetAsync("/api/books/deleted");
        using var adminDoc = await ReadJsonAsync(adminResponse);
        var adminBook = Assert.Single(adminDoc.RootElement.GetProperty("books").EnumerateArray());
        Assert.Equal(removedId, adminBook.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Restore_Brings_A_Removed_Book_Back_And_Returns_NotFound_For_Unknown_Or_Foreign_Ids()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Restorable");
        await client.DeleteAsync($"/api/books/{bookId}");

        var restoreResponse = await client.PostAsync($"/api/books/{bookId}/restore", content: null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);

        using (var activeDoc = await GetBooksDocumentAsync(client))
        {
            Assert.Contains(bookId, GetBookIds(activeDoc, "activeBooks"));
        }

        var deletedResponse = await client.GetAsync("/api/books/deleted");
        using (var deletedDoc = await ReadJsonAsync(deletedResponse))
        {
            Assert.Empty(deletedDoc.RootElement.GetProperty("books").EnumerateArray());
        }

        var again = await client.PostAsync($"/api/books/{bookId}/restore", content: null);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        var unknown = await client.PostAsync($"/api/books/{Guid.NewGuid()}/restore", content: null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        await client.DeleteAsync($"/api/books/{bookId}");
        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-restore");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");
        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-restore", "reader-pass-1");
        var foreign = await client.PostAsync($"/api/books/{bookId}/restore", content: null);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Restored_Book_Retains_Its_Spin_History()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "History Keeper");
        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        await client.DeleteAsync($"/api/books/{bookId}");
        await client.PostAsync($"/api/books/{bookId}/restore", content: null);

        var historyResponse = await client.GetAsync("/api/books/spin-history");
        using var doc = await ReadJsonAsync(historyResponse);
        var entry = Assert.Single(doc.RootElement.GetProperty("history").EnumerateArray());
        Assert.Equal(bookId, entry.GetProperty("bookId").GetGuid());
    }

    [Fact]
    public async Task Add_Duplicate_Active_Title_Returns_Conflict_Unless_AllowDuplicate()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var originalId = await AddBookAsync(client, "Dune");

        var conflict = await client.PostAsJsonAsync("/api/books", new { title = "  dUNE " });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var doc = await ReadJsonAsync(conflict))
        {
            Assert.Equal("exact", doc.RootElement.GetProperty("reason").GetString());
            Assert.Equal(originalId, doc.RootElement.GetProperty("match").GetProperty("id").GetGuid());
        }

        var forced = await client.PostAsJsonAsync("/api/books", new { title = "Dune", allowDuplicate = true });
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);

        using var activeDoc = await GetBooksDocumentAsync(client);
        Assert.Equal(2, GetBookIds(activeDoc, "activeBooks").Count);
    }

    [Fact]
    public async Task Add_Duplicate_Isbn_Returns_Conflict()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        await AddBookWithDetailsAsync(client, "Dune", "9780441013593", null, null);

        var conflict = await client.PostAsJsonAsync("/api/books", new { title = "Dune (Reissue)", isbn = "9780441013593" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var doc = await ReadJsonAsync(conflict);
        Assert.Equal("exact", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Add_Title_Matching_A_Removed_Book_Returns_Removed_Conflict()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var removedId = await AddBookAsync(client, "Gone Book");
        await client.DeleteAsync($"/api/books/{removedId}");

        var conflict = await client.PostAsJsonAsync("/api/books", new { title = "gone book" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var doc = await ReadJsonAsync(conflict))
        {
            Assert.Equal("removed", doc.RootElement.GetProperty("reason").GetString());
            Assert.Equal(removedId, doc.RootElement.GetProperty("match").GetProperty("id").GetGuid());
        }

        var forced = await client.PostAsJsonAsync("/api/books", new { title = "gone book", allowDuplicate = true });
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
    }

    [Fact]
    public async Task Import_Dedupes_Duplicates_Within_The_Same_Batch()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "Dune" }, new { title = "dune" } }
        });

        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(1, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task Import_Tolerates_An_Invalid_Isbn_By_Dropping_It_Instead_Of_Failing()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "Bad Isbn Book", isbn = "not-a-real-isbn" } }
        });

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(1, doc.RootElement.GetProperty("added").GetInt32());

        using var booksDoc = await GetBooksDocumentAsync(client);
        var imported = Assert.Single(booksDoc.RootElement.GetProperty("activeBooks").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, imported.GetProperty("isbn").ValueKind);
    }

    [Fact]
    public async Task Import_Skips_Blank_Titles_Without_Counting_Or_Failing()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "   " }, new { title = "Valid Title" } }
        });

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(1, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task Import_With_Empty_Books_Array_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new { books = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, importResponse.StatusCode);
    }

    [Fact]
    public async Task Import_Endpoint_Requires_Authentication()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "Some Book" } }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_Only_Adds_Books_To_The_Importing_Users_Account()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);
        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-import");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-import", "reader-pass-1");
        await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "Reader's Import" } }
        });

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client);

        using var adminBooks = await GetBooksDocumentAsync(client);
        var adminTitles = adminBooks.RootElement.GetProperty("activeBooks").EnumerateArray()
            .Select(book => book.GetProperty("title").GetString())
            .ToList();
        Assert.DoesNotContain("Reader's Import", adminTitles);
    }

    [Fact]
    public async Task Add_Book_With_Whitespace_Title_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        await LoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/books", new
        {
            title = "   "
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var errors = doc.RootElement.GetProperty("errors");
        var titleErrors = errors.GetProperty("Title").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains(titleErrors, message => string.Equals(message, "Book title is required.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_Book_With_Isbn_Author_And_CoverUrl_Persists_Metadata()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/books", new
        {
            title = "Effective Java",
            isbn = "978-0-13-468599-1",
            author = "Joshua Bloch",
            coverUrl = "https://covers.openlibrary.org/b/id/12345-L.jpg"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal("9780134685991", doc.RootElement.GetProperty("isbn").GetString());
        Assert.Equal("Joshua Bloch", doc.RootElement.GetProperty("author").GetString());
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", doc.RootElement.GetProperty("coverUrl").GetString());
    }

    [Fact]
    public async Task Add_Book_Persists_Null_BookInfoProviderId_When_Not_Supplied()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/books", new { title = "Manually Entered Book" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.True(doc.RootElement.TryGetProperty("bookInfoProviderId", out var providerIdElement));
        Assert.Equal(JsonValueKind.Null, providerIdElement.ValueKind);
    }

    [Fact]
    public async Task Add_Book_Persists_Supplied_BookInfoProviderId()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/books", new { title = "Looked-Up Book", bookInfoProviderId = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(1, doc.RootElement.GetProperty("bookInfoProviderId").GetInt32());
    }

    [Fact]
    public async Task Add_Book_With_Invalid_Isbn_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.PostAsJsonAsync("/api/books", new
        {
            title = "Untitled",
            isbn = "not-a-real-isbn"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal("The provided ISBN is not valid.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Update_Book_Backfills_Isbn_Author_And_CoverUrl_On_An_Existing_Book()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Untagged Book");

        var updateResponse = await client.PutAsJsonAsync($"/api/books/{bookId}", new
        {
            title = "Untagged Book",
            isbn = "9780134685991",
            author = "Joshua Bloch",
            coverUrl = "https://covers.openlibrary.org/b/id/12345-L.jpg"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var doc = await ReadJsonAsync(updateResponse);
        Assert.Equal("9780134685991", doc.RootElement.GetProperty("isbn").GetString());
        Assert.Equal("Joshua Bloch", doc.RootElement.GetProperty("author").GetString());
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-L.jpg", doc.RootElement.GetProperty("coverUrl").GetString());
    }

    [Fact]
    public async Task Update_Book_With_Invalid_Isbn_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Untagged Book");

        var updateResponse = await client.PutAsJsonAsync($"/api/books/{bookId}", new
        {
            title = "Untagged Book",
            isbn = "1234567890"
        });

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Lookup_Is_Rate_Limited_Per_User_Across_Sessions()
    {
        using var limitedFactory = _factory.WithWebHostBuilder(b => b.UseSetting("Security:MetadataLookupRateLimitPerMinute", "3"));

        // Two separate sessions (two cookies) for the SAME user. The limit must apply to the
        // user, not the session, so minting extra sessions can't multiply the budget.
        using var sessionA = limitedFactory.CreateClient();
        using var sessionB = limitedFactory.CreateClient();

        await sessionA.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(sessionA);
        await LoginAsync(sessionB);

        // Three allowed across the user: two on session A, one on session B.
        for (var i = 0; i < 2; i++)
        {
            var ok = await sessionA.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var thirdOnOtherSession = await sessionB.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");
        Assert.Equal(HttpStatusCode.OK, thirdOnOtherSession.StatusCode);

        // The fourth lookup — on the second session — is still rejected, because both
        // sessions share the one per-user bucket.
        var limited = await sessionB.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // /api/books/info shares the same bucket.
        var info = await sessionA.GetAsync($"/api/books/info?isbn={FakeBookMetadataLookupService.KnownIsbn}");
        Assert.Equal(HttpStatusCode.TooManyRequests, info.StatusCode);
    }

    [Fact]
    public async Task Lookup_By_Isbn_Returns_Metadata_From_The_Lookup_Service()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(FakeBookMetadataLookupService.KnownIsbnTitle, doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(FakeBookMetadataLookupService.KnownIsbnAuthor, doc.RootElement.GetProperty("author").GetString());
        Assert.Equal(FakeBookMetadataLookupService.KnownIsbnCoverUrl, doc.RootElement.GetProperty("coverUrl").GetString());
    }

    [Fact]
    public async Task BookInfo_By_Isbn_Includes_Retailer_Links_With_No_Affiliate_Tags_By_Default()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.GetAsync($"/api/books/info?isbn={FakeBookMetadataLookupService.KnownIsbn}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await ReadJsonAsync(response);

        // GH #208: retailer links are returned as a configurable list, with no
        // affiliate tags configured by default (scaffolding only).
        var links = doc.RootElement.GetProperty("links").EnumerateArray().ToList();
        Assert.Contains(links, l => l.GetProperty("name").GetString() == "Google Books"
            && l.GetProperty("url").GetString() == $"https://books.google.com/books?vid=ISBN{FakeBookMetadataLookupService.KnownIsbn}");
        Assert.All(links, l => Assert.False(l.GetProperty("isAffiliate").GetBoolean()));
    }

    [Theory]
    [InlineData("en", "https://www.amazon.com/s?k=", "Barnes & Noble")]
    [InlineData("es", "https://www.amazon.es/s?k=", "Bookshop.org")]
    [InlineData("pl", "https://www.amazon.pl/s?k=", "Bookshop.org")]
    public async Task BookInfo_Retailer_Links_Follow_The_Request_Locale(string acceptLanguage, string expectedAmazonUrlPrefix, string expectedSecondRetailer)
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/books/info?isbn={FakeBookMetadataLookupService.KnownIsbn}");
        request.Headers.Add("Accept-Language", acceptLanguage);

        var response = await client.SendAsync(request);
        using var doc = await ReadJsonAsync(response);
        var links = doc.RootElement.GetProperty("links").EnumerateArray().ToList();

        // Positive: the regional Amazon domain for this locale is used, and
        // exactly one Amazon entry is present (not all locale variants at once).
        var amazonLinks = links.Where(l => l.GetProperty("name").GetString() == "Amazon").ToList();
        var amazon = Assert.Single(amazonLinks);
        Assert.StartsWith(expectedAmazonUrlPrefix, amazon.GetProperty("url").GetString());

        // Positive: Barnes & Noble (en) vs. Bookshop.org (es/pl) swap by locale.
        Assert.Contains(links, l => l.GetProperty("name").GetString() == expectedSecondRetailer);
    }

    [Fact]
    public async Task Lookup_By_Isbn_Uses_Preferred_Provider_Then_Falls_Back()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = (string?)null,
            analyticsConsentOptedOut = false,
            preferredBookInfoProviderId = 2
        });

        var response = await client.GetAsync($"/api/books/lookup?isbn={FakeGoogleBooksMetadataLookupService.KnownIsbn}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(FakeGoogleBooksMetadataLookupService.KnownIsbnTitle, doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("providerId").GetInt32());
    }

    [Fact]
    public async Task Lookup_By_Isbn_Falls_Back_To_Open_Library_When_Preferred_Google_Books_Has_No_Match()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        await client.PutAsJsonAsync("/api/preferences", new
        {
            theme = (string?)null,
            analyticsConsentOptedOut = false,
            preferredBookInfoProviderId = 2
        });

        // This ISBN is only known to the Open Library fake, not the Google Books fake,
        // so a preference for provider 2 must still fall back to provider 1's match.
        var response = await client.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(FakeBookMetadataLookupService.KnownIsbnTitle, doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("providerId").GetInt32());
    }

    [Fact]
    public async Task Lookup_By_Title_Returns_A_Single_Item_Results_Array_When_Unambiguous()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.GetAsync($"/api/books/lookup?title={Uri.EscapeDataString(FakeBookMetadataLookupService.KnownTitle)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var results = doc.RootElement.GetProperty("results");
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal(FakeBookMetadataLookupService.KnownTitleIsbn, results[0].GetProperty("isbn").GetString());
        Assert.Equal(FakeBookMetadataLookupService.KnownTitleAuthor, results[0].GetProperty("author").GetString());
    }

    [Fact]
    public async Task Lookup_By_Title_Returns_All_Candidates_When_Ambiguous()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.GetAsync($"/api/books/lookup?title={Uri.EscapeDataString(FakeBookMetadataLookupService.AmbiguousTitle)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var results = doc.RootElement.GetProperty("results");
        Assert.Equal(3, results.GetArrayLength());
        var authors = results.EnumerateArray().Select(r => r.GetProperty("author").GetString()).ToList();
        Assert.Contains(FakeBookMetadataLookupService.AmbiguousTitleFirstAuthor, authors);
        Assert.Contains(FakeBookMetadataLookupService.AmbiguousTitleSecondAuthor, authors);
        Assert.Contains(FakeBookMetadataLookupService.AmbiguousTitleThirdAuthor, authors);
    }

    [Fact]
    public async Task Lookup_With_No_Match_Returns_NotFound()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.GetAsync("/api/books/lookup?title=SomeTitleThatWillNeverMatchAnything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal("No book metadata found for that title.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Lookup_With_Invalid_Isbn_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.GetAsync("/api/books/lookup?isbn=not-a-real-isbn");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_Without_Isbn_Or_Title_Returns_BadRequest()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await LoginAsync(client);

        var response = await client.GetAsync("/api/books/lookup");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal("Provide an ISBN or a title to look up.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Lookup_Requires_Authentication()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Version_Endpoint_Returns_NonEmpty_Version_String()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var version = doc.RootElement.GetProperty("version").GetString();

        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public async Task Request_Correlation_Header_Is_Propagated()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "corr-test-123");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Contains("corr-test-123", values);
    }

    [Fact]
    public async Task Migration_Endpoints_Require_Administrator_When_Account_Exists()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (_, migrationReaderSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, migrationReaderSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");

        var statusResponse = await client.GetAsync("/api/system/migrations/status");
        Assert.Equal(HttpStatusCode.Forbidden, statusResponse.StatusCode);

        var runResponse = await client.PostAsync("/api/system/migrations/run", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, runResponse.StatusCode);
    }

    [Fact]
    public async Task Health_Endpoints_Report_Live_And_Ready()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var liveResponse = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var readyResponse = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    [Fact]
    public async Task Disabled_User_Cannot_Log_In()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (readerUserId, readerSetupLinkForDisable) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLinkForDisable, "reader-pass-1");

        var disableResponse = await client.PutAsJsonAsync($"/api/users/{readerUserId}", new
        {
            username = "reader-one",
            isAdmin = false,
            isDisabled = true,
            forcePasswordReset = false,
            isLocked = false
        });

        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        await client.PostAsync("/api/auth/logout", content: null);
        var loginResponse = await PostLoginAsync(client, "reader-one", "reader-pass-1");

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Force_Password_Reset_User_Cannot_Log_In()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (readerUserId, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        var forceResetResponse = await client.PutAsJsonAsync($"/api/users/{readerUserId}", new
        {
            username = "reader-one",
            isAdmin = false,
            isDisabled = false,
            forcePasswordReset = true,
            isLocked = false
        });

        Assert.Equal(HttpStatusCode.OK, forceResetResponse.StatusCode);

        await client.PostAsync("/api/auth/logout", content: null);
        var loginResponse = await PostLoginAsync(client, "reader-one", "reader-pass-1");

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_User_Removes_Books_And_Spin_History_Atomically()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var (readerUserId, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");
        await AddBookAsync(client, "Reader Book A");
        await AddBookAsync(client, "Reader Book B");
        await client.PostAsync("/api/books/spin", null);

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client);

        var deleteResponse = await client.DeleteAsync($"/api/users/{readerUserId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var deleteDoc = await ReadJsonAsync(deleteResponse);
        Assert.Equal("reader-one", deleteDoc.RootElement.GetProperty("username").GetString());
        Assert.Equal(2, deleteDoc.RootElement.GetProperty("removedBooks").GetInt32());

        // Confirm the user is actually gone from the user list
        var usersResponse = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);
        using var usersDoc = await ReadJsonAsync(usersResponse);
        var users = usersDoc.RootElement.GetProperty("users").EnumerateArray().ToList();
        Assert.DoesNotContain(users, u => u.GetProperty("userId").GetGuid() == readerUserId);
    }

    // ── Stats: positive cases ────────────────────────────────────────────────

    [Fact]
    public async Task Stats_Unauthenticated_Returns_Unauthorized()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stats_For_User_With_No_History_Returns_Zero_Totals()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });

        var response = await client.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(0, doc.RootElement.GetProperty("totalSpins").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("uniqueBooksSpun").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("neverSpunCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("topBooks").GetArrayLength());
    }

    [Fact]
    public async Task Stats_After_Spins_Returns_Correct_Counts()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        var bookIdA = await AddBookAsync(client, "Book A");
        var bookIdB = await AddBookAsync(client, "Book B");

        // Spin the wheel 3 times
        for (var i = 0; i < 3; i++)
        {
            var spinResponse = await client.PostAsync("/api/books/spin", null);
            Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);
        }

        var statsResponse = await client.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);

        using var doc = await ReadJsonAsync(statsResponse);
        Assert.Equal(3, doc.RootElement.GetProperty("totalSpins").GetInt32());
        Assert.True(doc.RootElement.GetProperty("uniqueBooksSpun").GetInt32() >= 1);
        Assert.True(doc.RootElement.GetProperty("topBooks").GetArrayLength() >= 1);
        Assert.NotNull(doc.RootElement.GetProperty("longestOnWheel").GetRawText());
        Assert.NotNull(doc.RootElement.GetProperty("shortestOnWheel").GetRawText());

        // Top books ordered by spin count descending
        var topBooks = doc.RootElement.GetProperty("topBooks").EnumerateArray().ToList();
        for (var i = 0; i + 1 < topBooks.Count; i++)
        {
            Assert.True(
                topBooks[i].GetProperty("spinCount").GetInt32() >= topBooks[i + 1].GetProperty("spinCount").GetInt32()
            );
        }
    }

    [Fact]
    public async Task Stats_NeverSpunCount_Reflects_Unselected_Books()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await AddBookAsync(client, "Book Alpha");
        await AddBookAsync(client, "Book Beta");

        // No spins — both books should appear as never-spun
        var response = await client.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(0, doc.RootElement.GetProperty("totalSpins").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("neverSpunCount").GetInt32());
    }

    [Fact]
    public async Task Stats_After_Book_Deleted_Preserves_Spin_Count()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        var bookId = await AddBookAsync(client, "Ephemeral Book");

        var spinResponse = await client.PostAsync("/api/books/spin", null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        // Delete the book
        await client.DeleteAsync($"/api/books/{bookId}");

        var statsResponse = await client.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);

        using var doc = await ReadJsonAsync(statsResponse);
        // Spin count must still be 1 even though the book is gone
        Assert.Equal(1, doc.RootElement.GetProperty("totalSpins").GetInt32());
        var topBooks = doc.RootElement.GetProperty("topBooks").EnumerateArray().ToList();
        Assert.Single(topBooks);
        Assert.Equal(1, topBooks[0].GetProperty("spinCount").GetInt32());
    }

    [Fact]
    public async Task Admin_Can_Access_Aggregate_Stats()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });

        var response = await client.GetAsync("/api/stats/aggregate");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.True(doc.RootElement.TryGetProperty("totalSpinsAllUsers", out _));
        Assert.True(doc.RootElement.TryGetProperty("totalUserCount", out _));
        Assert.True(doc.RootElement.TryGetProperty("activeUserCount", out _));
        Assert.True(doc.RootElement.TryGetProperty("topUsers", out _));
    }

    [Fact]
    public async Task Aggregate_Stats_Reflect_Multi_User_Totals()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await AddBookAsync(client, "Admin Book");
        await client.PostAsync("/api/books/spin", null);

        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");
        await AddBookAsync(client, "Reader Book");
        await client.PostAsync("/api/books/spin", null);
        await client.PostAsync("/api/books/spin", null);

        // Re-login as admin to access aggregate
        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client);

        var response = await client.GetAsync("/api/stats/aggregate");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(3, doc.RootElement.GetProperty("totalSpinsAllUsers").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("totalUserCount").GetInt32());
        // Both accounts logged in (or spun) within the last 30 days during this test.
        Assert.Equal(2, doc.RootElement.GetProperty("activeUserCount").GetInt32());
    }

    [Fact]
    public async Task Aggregate_Stats_ActiveUserCount_Excludes_Disabled_User_With_Recent_Activity()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });

        var (readerUserId, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");
        await AddBookAsync(client, "Reader Book");
        await client.PostAsync("/api/books/spin", null);

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client);
        var repo = factory.Services.GetRequiredService<ICredentialRepository>();
        await repo.UpdateUserAsync(readerUserId, "reader-one", isAdmin: false, isDisabled: true, forcePasswordReset: false, isLocked: false, email: null);

        var response = await client.GetAsync("/api/stats/aggregate");

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(2, doc.RootElement.GetProperty("totalUserCount").GetInt32());
        // reader-one logged in and spun moments ago but is now disabled, so only the admin counts as active.
        Assert.Equal(1, doc.RootElement.GetProperty("activeUserCount").GetInt32());
    }

    // ── Stats: negative cases ────────────────────────────────────────────────

    [Fact]
    public async Task Aggregate_Stats_Unauthenticated_Returns_Unauthorized()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/stats/aggregate");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_Admin_Cannot_Access_Aggregate_Stats()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });

        var (_, readerSetupLink) = await CreateUserAsync(client, "reader-one");
        await SetPasswordFromSetupLinkAsync(client, readerSetupLink, "reader-pass-1");

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");

        var response = await client.GetAsync("/api/stats/aggregate");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Stats_TopBooks_Percentage_Sums_To_One_Hundred()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password",
            email = "test-setup@example.com" });
        await AddBookAsync(client, "Book One");

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsync("/api/books/spin", null);
        }

        var response = await client.GetAsync("/api/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var topBooks = doc.RootElement.GetProperty("topBooks").EnumerateArray().ToList();
        Assert.Single(topBooks);
        Assert.Equal(100.0, topBooks[0].GetProperty("percentage").GetDouble());
    }

    // ── Admin self-modify guards ─────────────────────────────────────────────

    [Fact]
    public async Task Admin_Cannot_Update_Own_Non_Email_Fields()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var usersResponse = await client.GetAsync("/api/users");
        using var usersDoc = await ReadJsonAsync(usersResponse);
        var adminId = usersDoc.RootElement.GetProperty("users").EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == "test-admin")
            .GetProperty("userId").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/users/{adminId}", new
        {
            username = "test-admin",
            isAdmin = false,
            isDisabled = false,
            forcePasswordReset = false,
            isLocked = false,
            email = "test-setup@example.com"
        });

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_Can_Update_Own_Email_Only()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var usersResponse = await client.GetAsync("/api/users");
        using var usersDoc = await ReadJsonAsync(usersResponse);
        var adminUser = usersDoc.RootElement.GetProperty("users").EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == "test-admin");
        var adminId = adminUser.GetProperty("userId").GetGuid();

        var updateResponse = await client.PutAsJsonAsync($"/api/users/{adminId}", new
        {
            username = "test-admin",
            isAdmin = true,
            isDisabled = false,
            forcePasswordReset = false,
            isLocked = false,
            email = "new-email@example.com"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_Cannot_Generate_Own_Password_Reset_Link()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var usersResponse = await client.GetAsync("/api/users");
        using var usersDoc = await ReadJsonAsync(usersResponse);
        var adminId = usersDoc.RootElement.GetProperty("users").EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == "test-admin")
            .GetProperty("userId").GetGuid();

        var resetResponse = await client.PostAsync($"/api/users/{adminId}/password-reset-link", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resetResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_Cannot_Delete_Own_Account()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        var usersResponse = await client.GetAsync("/api/users");
        using var usersDoc = await ReadJsonAsync(usersResponse);
        var adminId = usersDoc.RootElement.GetProperty("users").EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == "test-admin")
            .GetProperty("userId").GetGuid();

        var deleteResponse = await client.DeleteAsync($"/api/users/{adminId}");

        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Disabling_A_User_Revokes_Their_Active_Session()
    {
        using var adminClient = _factory.CreateClient();
        using var readerClient = _factory.CreateClient();
        var readerId = await SetUpAdminAndLoggedInReaderAsync(adminClient, readerClient);

        var update = await UpdateUserStateAsync(adminClient, readerId, "reader-one", isAdmin: false, isDisabled: true, isLocked: false);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var after = await readerClient.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Locking_A_User_Revokes_Their_Active_Session()
    {
        using var adminClient = _factory.CreateClient();
        using var readerClient = _factory.CreateClient();
        var readerId = await SetUpAdminAndLoggedInReaderAsync(adminClient, readerClient);

        var update = await UpdateUserStateAsync(adminClient, readerId, "reader-one", isAdmin: false, isDisabled: false, isLocked: true);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var after = await readerClient.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Demoting_An_Admin_Revokes_Their_Existing_Admin_Session()
    {
        using var adminClient = _factory.CreateClient();
        using var secondAdminClient = _factory.CreateClient();
        await adminClient.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        var (secondAdminId, setupLink) = await CreateUserAsync(adminClient, "second-admin", isAdmin: true);
        await SetPasswordFromSetupLinkAsync(secondAdminClient, setupLink, "second-pass-1");
        await LoginAsync(secondAdminClient, "second-admin", "second-pass-1");

        var before = await secondAdminClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var update = await UpdateUserStateAsync(adminClient, secondAdminId, "second-admin", isAdmin: false, isDisabled: false, isLocked: false);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // The stale admin snapshot must be gone: the old cookie no longer works at all
        // (401), so it can never reach the admin-only endpoint.
        var after = await secondAdminClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Completing_A_Password_Reset_Revokes_The_Users_Other_Sessions()
    {
        using var adminClient = _factory.CreateClient();
        using var readerClient = _factory.CreateClient();
        using var anonymousClient = _factory.CreateClient();
        var readerId = await SetUpAdminAndLoggedInReaderAsync(adminClient, readerClient);

        // Mint the token straight from the service (not the admin endpoint) so the
        // reader's session is still live and completion is the only thing that can evict it.
        // Uses the token repository directly (the self-service "forgot password" path) rather than
        // CreatePasswordResetLinkAsync: the latter is the admin-forced flow, which flags the account
        // ForcePasswordReset and rotates its security stamp immediately, evicting the session itself.
        var tokenRepository = _factory.Services.GetRequiredService<BookWheel.Storage.IPasswordResetTokenRepository>();
        var (token, _) = await tokenRepository.CreateAsync(readerId);
        Assert.Equal(HttpStatusCode.OK, (await readerClient.GetAsync("/api/books")).StatusCode);

        var complete = await anonymousClient.PostAsJsonAsync("/api/auth/password-reset/complete", new { token, newPassword = "reader-pass-2" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var after = await readerClient.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Keeps_Current_Session_And_Revokes_Other_Sessions()
    {
        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();
        using var anonymousClient = _factory.CreateClient();
        await clientA.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        await LoginAsync(clientB);
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/books")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync("/api/books")).StatusCode);

        var change = await clientA.PostAsJsonAsync("/api/auth/password", new { currentPassword = "test-password", newPassword = "brand-new-pass-1" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // The current device was re-issued a cookie carrying the new security stamp.
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/books")).StatusCode);
        // The other session still holds the old stamp and is revoked.
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientB.GetAsync("/api/books")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PostLoginAsync(anonymousClient, "test-admin", "brand-new-pass-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostLoginAsync(anonymousClient, "test-admin", "test-password")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_With_Wrong_Current_Password_Returns_BadRequest_And_Leaves_Password_Unchanged()
    {
        using var client = _factory.CreateClient();
        using var anonymousClient = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var change = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "not-the-password", newPassword = "brand-new-pass-1" });

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        using var doc = await ReadJsonAsync(change);
        Assert.Equal("The current password is incorrect.", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, (await PostLoginAsync(anonymousClient, "test-admin", "test-password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/books")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Locks_Out_After_Repeated_Wrong_Current_Password()
    {
        // A stolen session must not be able to brute-force the current password here:
        // wrong-current attempts count toward the same lockout as login.
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Security:UsernameLockoutThreshold", "2"));
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "nope", newPassword = "brand-new-pass-1" });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        // Now locked out — even the correct current password is rejected until it expires.
        var locked = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "test-password", newPassword = "brand-new-pass-1" });
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        using var doc = await ReadJsonAsync(locked);
        Assert.Equal("Too many failed attempts. Please try again later.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ChangePassword_To_The_Same_Password_Returns_BadRequest()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var change = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "test-password", newPassword = "test-password" });

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        using var doc = await ReadJsonAsync(change);
        Assert.Equal("The new password must be different from your current password.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ChangePassword_With_Too_Short_New_Password_Returns_BadRequest()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });

        var change = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "test-password", newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Without_A_Session_Returns_Unauthorized()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        using var anonymousClient = _factory.CreateClient();

        var change = await anonymousClient.PostAsJsonAsync("/api/auth/password", new { currentPassword = "test-password", newPassword = "brand-new-pass-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, change.StatusCode);
    }

    [Fact]
    public async Task Admin_Editing_Only_Own_Email_Keeps_Their_Session()
    {
        using var client = _factory.CreateClient();
        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "original@example.com" });
        using var setupDoc = await ReadJsonAsync(setupResponse);
        var adminId = setupDoc.RootElement.GetProperty("user").GetProperty("userId").GetGuid();

        var update = await client.PutAsJsonAsync($"/api/users/{adminId}", new
        {
            username = "test-admin",
            isAdmin = true,
            isDisabled = false,
            forcePasswordReset = false,
            isLocked = false,
            email = "changed@example.com"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var after = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Login_Cookie_Is_Secure_When_Forwarded_Proto_Is_Https()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "proto-admin", password = "test-password", email = "proto@example.com" });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "proto-admin", password = "test-password" })
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", NextSyntheticIp());
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("BookWheel.Auth=", StringComparison.Ordinal));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Cookie_Is_Secure_When_AlwaysSecureCookie_Is_Enabled()
    {
        using var derivedFactory = _factory.WithWebHostBuilder(b => b.UseSetting("Security:AlwaysSecureCookie", "true"));
        using var client = derivedFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        await client.PostAsJsonAsync("/api/auth/setup", new { username = "always-secure-admin", password = "test-password", email = "always@example.com" });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "always-secure-admin", password = "test-password" })
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", NextSyntheticIp());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("BookWheel.Auth=", StringComparison.Ordinal));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> SetUpAdminAndLoggedInReaderAsync(HttpClient adminClient, HttpClient readerClient)
    {
        await adminClient.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password", email = "admin@example.com" });
        var (readerId, setupLink) = await CreateUserAsync(adminClient, "reader-one");
        await SetPasswordFromSetupLinkAsync(readerClient, setupLink, "reader-pass-1");
        await LoginAsync(readerClient, "reader-one", "reader-pass-1");
        Assert.Equal(HttpStatusCode.OK, (await readerClient.GetAsync("/api/books")).StatusCode);
        return readerId;
    }

    private static Task<HttpResponseMessage> UpdateUserStateAsync(HttpClient adminClient, Guid userId, string username, bool isAdmin, bool isDisabled, bool isLocked)
    {
        return adminClient.PutAsJsonAsync($"/api/users/{userId}", new
        {
            username,
            isAdmin,
            isDisabled,
            forcePasswordReset = false,
            isLocked,
            email = $"{username}@example.com"
        });
    }

    private static int _loginRateLimitIpCounter;

    // Tests in this class now share one WebApplicationFactory host (and therefore one
    // in-process login rate limiter) instead of getting a fresh host per test. Every
    // direct POST to /api/auth/login in this file (except the two tests that
    // deliberately exercise the limiter — Login_Is_Rate_Limited_After_Repeated_Failed_Attempts
    // and Login_Rate_Limiter_Uses_Forwarded_Client_Ip_When_Present, which build their own
    // requests and intentionally share one consistent bucket across their own attempts)
    // goes through this helper so it gets a distinct synthetic client IP and doesn't
    // accumulate against the same bucket as every other test, tripping stray 429s.
    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string username, string password, string? userAgent = null)
    {
        var counter = System.Threading.Interlocked.Increment(ref _loginRateLimitIpCounter);
        var syntheticIp = $"10.{(counter >> 16) & 0xFF}.{(counter >> 8) & 0xFF}.{counter & 0xFF}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username, password })
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", syntheticIp);

        // Set explicitly on the message itself, rather than relying on the client's
        // DefaultRequestHeaders to merge in, so callers that need to assert on this
        // request's own User-Agent (e.g. in a captured log entry) get a guaranteed value.
        if (userAgent is not null)
        {
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        return await client.SendAsync(request);
    }

    private static int _authRateLimitIpCounter;

    // Program.cs now partitions the password-reset-request and forgot-username rate
    // limiters per-IP (in addition to AuthService's existing per-username/per-email
    // limiter), same as login. Tests in this class share one WebApplicationFactory
    // host (and therefore one in-process limiter), so every call to either endpoint
    // needs its own distinct synthetic IP to avoid tripping a 429 as a side effect of
    // unrelated tests, unless a test is deliberately exercising the limiter itself
    // (in which case it reuses one IP across its own calls, e.g.
    // PasswordResetRequest_Beyond_RateLimit_Still_Returns_Ok_But_Stops_Sending).
    private static string NextSyntheticIp()
    {
        var counter = System.Threading.Interlocked.Increment(ref _authRateLimitIpCounter);
        return $"10.{200 + ((counter >> 16) & 0x3F)}.{(counter >> 8) & 0xFF}.{counter & 0xFF}";
    }

    private static async Task<HttpResponseMessage> PostRateLimitedAuthEndpointAsync(HttpClient client, string path, object body, string syntheticIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", syntheticIp);

        return await client.SendAsync(request);
    }

    private static async Task LoginAsync(HttpClient client, string username = "test-admin", string password = "test-password")
    {
        var response = await PostLoginAsync(client, username, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<(Guid UserId, string SetupLink)> CreateUserAsync(HttpClient client, string username, bool isAdmin = false)
    {
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username,
            isAdmin,
            email = $"{username}@example.com"
        });

        Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);

        using var createUserDoc = await ReadJsonAsync(createUserResponse);
        var userId = createUserDoc.RootElement.GetProperty("userId").GetGuid();
        var setupLink = createUserDoc.RootElement.GetProperty("setupLink").GetString() ?? string.Empty;

        return (userId, setupLink);
    }

    private static async Task SetPasswordFromSetupLinkAsync(HttpClient client, string setupLink, string newPassword)
    {
        var token = ExtractResetToken(setupLink);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var completeResponse = await client.PostAsJsonAsync("/api/auth/password-reset/complete", new
        {
            token,
            newPassword
        });

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
    }

    [Fact]
    public async Task Get_Books_Orders_By_Created_Time_And_Populates_LastSpunAtUtc()
    {
        var factory = _factory;
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        // Titles are deliberately not alphabetical, so created-order is distinguishable.
        var titles = new[] { "Zeta Created First", "Alpha Created Second", "Mid Created Third" };
        foreach (var title in titles)
        {
            await AddBookAsync(client, title);
        }

        using var beforeSpin = await GetBooksDocumentAsync(client);
        var beforeBooks = beforeSpin.RootElement.GetProperty("activeBooks").EnumerateArray().ToList();
        Assert.Equal(titles, beforeBooks.Select(b => b.GetProperty("title").GetString()).ToArray());
        Assert.All(beforeBooks, b => Assert.Equal(JsonValueKind.Null, b.GetProperty("lastSpunAtUtc").ValueKind));

        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);
        using var spinDoc = await ReadJsonAsync(spinResponse);
        var selectedId = spinDoc.RootElement.GetProperty("selected").GetProperty("id").GetGuid();

        using var afterSpin = await GetBooksDocumentAsync(client);
        var afterBooks = afterSpin.RootElement.GetProperty("activeBooks").EnumerateArray().ToList();
        Assert.Equal(titles, afterBooks.Select(b => b.GetProperty("title").GetString()).ToArray());

        var spun = afterBooks.Where(b => b.GetProperty("lastSpunAtUtc").ValueKind != JsonValueKind.Null).ToList();
        var unspun = afterBooks.Where(b => b.GetProperty("lastSpunAtUtc").ValueKind == JsonValueKind.Null).ToList();
        Assert.Single(spun);
        Assert.Equal(selectedId, spun[0].GetProperty("id").GetGuid());
        Assert.Equal(2, unspun.Count);
    }

    private static async Task<Guid> AddBookAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/books", new { title });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddBookWithDetailsAsync(HttpClient client, string title, string? isbn, string? author, string? coverUrl)
    {
        var response = await client.PostAsJsonAsync("/api/books", new { title, isbn, author, coverUrl });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> GetBooksDocumentAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static List<Guid> GetBookIds(JsonDocument document, string propertyName)
    {
        return document.RootElement
            .GetProperty(propertyName)
            .EnumerateArray()
            .Select(book => book.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string ExtractResetToken(string resetLink)
    {
        var uri = new Uri(resetLink);
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "resetToken", StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return string.Empty;
    }

}