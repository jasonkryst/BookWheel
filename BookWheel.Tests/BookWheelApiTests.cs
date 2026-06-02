using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Setup_Creates_Account_And_Logs_The_User_In()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();
        var credentialPath = Path.Combine(factory.ContentRootPath, "App_Data", "user.cred");

        var setupResponse = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        Assert.True(File.Exists(credentialPath));

        var protectedPayload = await File.ReadAllTextAsync(credentialPath);
        Assert.DoesNotContain("test-admin", protectedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("test-password", protectedPayload, StringComparison.Ordinal);

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

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });

        Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var usersDoc = await ReadJsonAsync(listResponse);
        var users = usersDoc.RootElement.GetProperty("users").EnumerateArray().ToList();

        Assert.Contains(users, user => user.GetProperty("username").GetString() == "test-admin" && user.GetProperty("isAdmin").GetBoolean());
        Assert.Contains(users, user => user.GetProperty("username").GetString() == "reader-one" && !user.GetProperty("isAdmin").GetBoolean());
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

        var createdUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });
        Assert.Equal(HttpStatusCode.OK, createdUserResponse.StatusCode);
        using var createdUserDoc = await ReadJsonAsync(createdUserResponse);
        var createdUserId = createdUserDoc.RootElement.GetProperty("userId").GetGuid();

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");

        var listUsersResponse = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, listUsersResponse.StatusCode);

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-two",
            password = "reader-pass-2",
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

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });
        Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);

        using var createUserDoc = await ReadJsonAsync(createUserResponse);
        var createdUserId = createUserDoc.RootElement.GetProperty("userId").GetGuid();

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

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });
        Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);
        using var createUserDoc = await ReadJsonAsync(createUserResponse);
        var createdUserId = createUserDoc.RootElement.GetProperty("userId").GetGuid();

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

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });
        Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);
        using var createUserDoc = await ReadJsonAsync(createUserResponse);
        var readerUserId = createUserDoc.RootElement.GetProperty("userId").GetGuid();

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

        await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });

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

        var createUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });
        Assert.Equal(HttpStatusCode.OK, createUserResponse.StatusCode);

        await client.PostAsync("/api/auth/logout", content: null);
        await LoginAsync(client, "reader-one", "reader-pass-1");

        var metricsResponse = await client.GetAsync("/api/metrics");
        Assert.Equal(HttpStatusCode.Forbidden, metricsResponse.StatusCode);
    }

    [Fact]
    public async Task Missing_Books_File_Is_Recreated_On_Read()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        var booksPath = Path.Combine(factory.ContentRootPath, "App_Data", "books.json");
        if (File.Exists(booksPath))
        {
            File.Delete(booksPath);
        }

        var response = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(booksPath));
    }

    [Fact]
    public async Task Corrupt_Books_File_Is_Quarantined_And_Returns_Server_Error()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        var appDataPath = Path.Combine(factory.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);
        var booksPath = Path.Combine(appDataPath, "books.json");
        await File.WriteAllTextAsync(booksPath, "{not-valid-json");

        var response = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var corruptDirectory = Path.Combine(appDataPath, "corrupt");
        Assert.Contains(Directory.GetFiles(corruptDirectory), path => Path.GetFileName(path).StartsWith("books.json-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Corrupt_Credential_File_Is_Quarantined_And_Login_Returns_Server_Error()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password"
        });

        await client.PostAsync("/api/auth/logout", content: null);

        var appDataPath = Path.Combine(factory.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);
        var credentialPath = Path.Combine(appDataPath, "user.cred");
        await File.WriteAllTextAsync(credentialPath, "definitely-not-protected-payload");

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "test-admin",
            password = "test-password"
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var corruptDirectory = Path.Combine(appDataPath, "corrupt");
        Assert.Contains(Directory.GetFiles(corruptDirectory), path => Path.GetFileName(path).StartsWith("user.cred-", StringComparison.Ordinal));
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
    public async Task Migration_Status_And_Run_Endpoints_Convert_Legacy_Payloads()
    {
        using var factory = new BookWheelWebAppFactory();
        using var client = factory.CreateClient();
        await SeedLegacyCredentialAndBooksPayloadsAsync(factory);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "legacy-admin",
            password = "legacy-password"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var statusResponse = await client.GetAsync("/api/system/migrations/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using (var statusDoc = await ReadJsonAsync(statusResponse))
        {
            Assert.True(statusDoc.RootElement.GetProperty("hasLegacyCredentialPayload").GetBoolean());
            Assert.True(statusDoc.RootElement.GetProperty("hasLegacyBooksPayload").GetBoolean());
            Assert.True(statusDoc.RootElement.GetProperty("requiresMigration").GetBoolean());
        }

        var runResponse = await client.PostAsync("/api/system/migrations/run", content: null);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

        using var runDoc = await ReadJsonAsync(runResponse);
        Assert.True(runDoc.RootElement.GetProperty("credentialPayloadMigrated").GetBoolean());
        Assert.True(runDoc.RootElement.GetProperty("booksPayloadMigrated").GetBoolean());
        Assert.Equal(1, runDoc.RootElement.GetProperty("credentialUsersAffected").GetInt32());
        Assert.Equal(2, runDoc.RootElement.GetProperty("booksAffected").GetInt32());
        var ownerUserId = runDoc.RootElement.GetProperty("booksOwnerUserId").GetGuid();

        var booksPath = Path.Combine(factory.ContentRootPath, "App_Data", "books.json");
        var booksJson = await File.ReadAllTextAsync(booksPath);
        using var booksDocument = JsonDocument.Parse(booksJson);
        var foundUsers = booksDocument.RootElement.TryGetProperty("Users", out var booksByUser)
            || booksDocument.RootElement.TryGetProperty("users", out booksByUser);
        Assert.True(foundUsers);
        Assert.True(booksByUser.TryGetProperty(ownerUserId.ToString(), out var migratedBooks));
        Assert.Equal(2, migratedBooks.GetArrayLength());

        var protectedCredentialPath = Path.Combine(factory.ContentRootPath, "App_Data", "user.cred");
        var protectedPayload = await File.ReadAllTextAsync(protectedCredentialPath);
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookWheel.Credentials.v1");
        var decryptedCredentialJson = protector.Unprotect(protectedPayload);
        using var credentialDocument = JsonDocument.Parse(decryptedCredentialJson);
        Assert.True(credentialDocument.RootElement.TryGetProperty("Users", out var users));
        Assert.Equal(1, users.GetArrayLength());
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

        await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });

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

        var createdUserResponse = await client.PostAsJsonAsync("/api/users", new
        {
            username = "reader-one",
            password = "reader-pass-1",
            isAdmin = false
        });

        Assert.Equal(HttpStatusCode.OK, createdUserResponse.StatusCode);
        using var createdUserDoc = await ReadJsonAsync(createdUserResponse);
        var readerUserId = createdUserDoc.RootElement.GetProperty("userId").GetGuid();

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

    private static async Task<Guid> AddBookAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/books", new { title });
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

    private static async Task SeedLegacyCredentialAndBooksPayloadsAsync(BookWheelWebAppFactory factory)
    {
        var dataDirectory = Path.Combine(factory.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);

        var hasher = new PasswordHasher<string>();
        var legacyCredential = new
        {
            Username = "legacy-admin",
            PasswordHash = hasher.HashPassword("legacy-admin", "legacy-password"),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var legacyCredentialJson = JsonSerializer.Serialize(legacyCredential);
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("BookWheel.Credentials.v1");
        var protectedCredentialPayload = protector.Protect(legacyCredentialJson);
        await File.WriteAllTextAsync(Path.Combine(dataDirectory, "user.cred"), protectedCredentialPayload);

        var legacyBooks = new[]
        {
            new { Id = Guid.NewGuid(), Title = "Legacy Book One" },
            new { Id = Guid.NewGuid(), Title = "Legacy Book Two" }
        };

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "books.json"),
            JsonSerializer.Serialize(legacyBooks, new JsonSerializerOptions { WriteIndented = true }));
    }
}