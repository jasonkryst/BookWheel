using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookWheel.Tests.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BookWheel.Tests;

public sealed class BookWheelApiTests
{
    [Fact]
    public async Task Status_Endpoint_Reports_Setup_Required_When_No_Account_Exists()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.True(doc.RootElement.GetProperty("setupRequired").GetBoolean());
    }

    [Fact]
    public async Task Login_Before_Setup_Returns_Conflict()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "test-admin",
            password = "test-password"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Failed_Login_Is_Recorded_As_Structured_Warning_Log()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheelTests/1.0");

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "test-admin",
            password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var logEntry = factory.LoggerProvider.Entries.LastOrDefault(entry =>
            entry.Category == "BookWheel.Controllers.AuthController" &&
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            entry.Message.Contains("Login failed", StringComparison.Ordinal));

        Assert.NotNull(logEntry);
        Assert.Equal("test-admin", logEntry!.State["Username"]);
        Assert.False(logEntry.State.ContainsKey("Password"));
        Assert.Equal("/api/auth/login", logEntry.State["Path"]);
        Assert.True(logEntry.State.ContainsKey("RequestId"));
        Assert.Equal("BookWheelTests/1.0", logEntry.State["UserAgent"]);
        Assert.DoesNotContain("wrong-password", logEntry.Message, StringComparison.Ordinal);

        var logFilePath = Directory.GetFiles(factory.LogDirectoryPath, "bookwheel-*.jsonl")
            .OrderBy(path => path)
            .LastOrDefault();

        Assert.NotNull(logFilePath);

        var logLines = await File.ReadAllLinesAsync(logFilePath!);
        var persistedEntry = logLines
            .Select(line => JsonDocument.Parse(line))
            .FirstOrDefault(document =>
                document.RootElement.GetProperty("Category").GetString() == "BookWheel.Controllers.AuthController" &&
                document.RootElement.GetProperty("Level").GetString() == "Warning" &&
                document.RootElement.GetProperty("Message").GetString()?.Contains("Login failed", StringComparison.Ordinal) == true);

        Assert.NotNull(persistedEntry);
        Assert.Equal("test-admin", persistedEntry!.RootElement.GetProperty("Properties").GetProperty("Username").GetString());
        Assert.Equal("/api/auth/login", persistedEntry.RootElement.GetProperty("Properties").GetProperty("Path").GetString());
        Assert.Equal("BookWheelTests/1.0", persistedEntry.RootElement.GetProperty("Properties").GetProperty("UserAgent").GetString());
        Assert.False(persistedEntry.RootElement.GetProperty("Properties").TryGetProperty("Password", out _));
    }

    [Fact]
    public async Task Login_WithBadCredentials_ReturnsSpanishMessage_WhenAcceptLanguageIsSpanish()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "test-admin", password = "wrong-password" })
        };
        request.Headers.Add("Accept-Language", "es");

        var response = await client.SendAsync(request);
        using var doc = await ReadJsonAsync(response);
        var message = doc.RootElement.GetProperty("message").GetString();

        // Positive: the Spanish translation is returned for Accept-Language: es.
        Assert.Equal("Nombre de usuario o contraseña incorrectos.", message);

        // Negative: the raw English string is not leaking through for a non-English request.
        Assert.NotEqual("Invalid username or password.", message);
    }

    [Fact]
    public async Task Setup_Creates_Account_And_Logs_The_User_In()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
    public async Task Non_Admin_User_Cannot_Access_User_Management_Endpoints()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
            isAdmin = false
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
    public async Task Password_Reset_Link_Can_Be_Generated_And_Used_Once()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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

        var oldPasswordLogin = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "reader-one",
            password = "reader-pass-1"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "reader-one",
            password = "reader-pass-2"
        });
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }

    [Fact]
    public async Task Admin_Cannot_Delete_First_Account()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        var deletedUserLogin = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "reader-one",
            password = "reader-pass-1"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, deletedUserLogin.StatusCode);
    }

    [Fact]
    public async Task Books_Are_Isolated_Per_User()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BookWheelTests/1.0");

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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

        var rateLimitLog = factory.LoggerProvider.Entries.LastOrDefault(entry =>
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        await AddBookAsync(client, "Metrics Book");
        var spinResponse = await client.PostAsync("/api/books/spin", content: null);
        Assert.Equal(HttpStatusCode.OK, spinResponse.StatusCode);

        await client.PostAsync("/api/auth/logout", content: null);
        var failedLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "test-admin",
            password = "wrong-password"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, failedLoginResponse.StatusCode);

        await LoginAsync(client);
        var metricsResponse = await client.GetAsync("/api/metrics");
        Assert.Equal(HttpStatusCode.OK, metricsResponse.StatusCode);

        using var metricsDoc = await ReadJsonAsync(metricsResponse);
        Assert.True(metricsDoc.RootElement.GetProperty("loginFailureCount").GetInt64() >= 1);
        Assert.True(metricsDoc.RootElement.GetProperty("successfulLoginCount").GetInt64() >= 1);
        Assert.True(metricsDoc.RootElement.GetProperty("spinCount").GetInt64() >= 1);
        Assert.True(metricsDoc.RootElement.GetProperty("totalBookCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Non_Admin_User_Cannot_Access_Metrics_Endpoint()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        await client.PostAsync("/api/auth/logout", content: null);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "test-admin",
            password = "test-password"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var booksResponse = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, booksResponse.StatusCode);
    }

    [Fact]
    public async Task Spin_Does_Not_Remove_Selected_Book_From_Active_List()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
    public async Task Update_Then_Remove_Book_Works()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/books/spin-history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Spin_History_Endpoint_Is_Empty_Before_Any_Spins()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
    public async Task Export_Includes_Isbn_Author_And_CoverUrl_For_Tagged_Books()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/books/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Import_Adds_New_Books_And_Reports_Counts()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
    public async Task Import_Skips_Match_Against_A_SoftDeleted_Book_Without_Restoring_It()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        await LoginAsync(client);
        var bookId = await AddBookAsync(client, "Deleted On Purpose");
        await client.DeleteAsync($"/api/books/{bookId}");

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new
        {
            books = new[] { new { title = "Deleted On Purpose" } }
        });

        using var doc = await ReadJsonAsync(importResponse);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());

        using var activeDoc = await GetBooksDocumentAsync(client);
        Assert.Empty(activeDoc.RootElement.GetProperty("activeBooks").EnumerateArray());
    }

    [Fact]
    public async Task Import_Dedupes_Duplicates_Within_The_Same_Batch()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        await LoginAsync(client);

        var importResponse = await client.PostAsJsonAsync("/api/books/import", new { books = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, importResponse.StatusCode);
    }

    [Fact]
    public async Task Import_Endpoint_Requires_Authentication()
    {
        using var factory = new BookWheelWebAppFactory();
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        Assert.Contains(titleErrors, message => string.Equals(message, "The Title field is required.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_Book_With_Isbn_Author_And_CoverUrl_Persists_Metadata()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
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
    public async Task Add_Book_With_Invalid_Isbn_Returns_BadRequest()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
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
    public async Task Lookup_By_Isbn_Returns_Metadata_From_The_Lookup_Service()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
        await LoginAsync(client);

        var response = await client.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(FakeBookMetadataLookupService.KnownIsbnTitle, doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(FakeBookMetadataLookupService.KnownIsbnAuthor, doc.RootElement.GetProperty("author").GetString());
        Assert.Equal(FakeBookMetadataLookupService.KnownIsbnCoverUrl, doc.RootElement.GetProperty("coverUrl").GetString());
    }

    [Fact]
    public async Task Lookup_By_Title_Returns_A_Single_Item_Results_Array_When_Unambiguous()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
        await LoginAsync(client);

        var response = await client.GetAsync("/api/books/lookup?title=SomeTitleThatWillNeverMatchAnything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal("No book metadata found for that title.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Lookup_With_Invalid_Isbn_Returns_BadRequest()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
        await LoginAsync(client);

        var response = await client.GetAsync("/api/books/lookup?isbn=not-a-real-isbn");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_Without_Isbn_Or_Title_Returns_BadRequest()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new { username = "test-admin", password = "test-password" });
        await LoginAsync(client);

        var response = await client.GetAsync("/api/books/lookup");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal("Provide an ISBN or a title to look up.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Lookup_Requires_Authentication()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/books/lookup?isbn={FakeBookMetadataLookupService.KnownIsbn}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Version_Endpoint_Returns_NonEmpty_Version_String()
    {
        using var factory = new BookWheelWebAppFactory();
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
        using var factory = new BookWheelWebAppFactory();
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        var liveResponse = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var readyResponse = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
    }

    [Fact]
    public async Task Disabled_User_Cannot_Log_In()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
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
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "reader-one",
            password = "reader-pass-1"
        });

        Assert.Equal(HttpStatusCode.Locked, loginResponse.StatusCode);
    }

    private static async Task LoginAsync(HttpClient client, string username = "test-admin", string password = "test-password")
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<(Guid UserId, string SetupLink)> CreateUserAsync(HttpClient client, string username, bool isAdmin = false)
    {
        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username,
            isAdmin
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