using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

        var booksResponse = await client.GetAsync("/api/books");
        Assert.Equal(HttpStatusCode.OK, booksResponse.StatusCode);
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

    private static async Task LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "test-admin",
            password = "test-password"
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
}