using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookWheel.Tests;

public sealed class BookWheelConcurrencyTests : IClassFixture<BookWheelWebAppFactory>, IAsyncLifetime
{
    private readonly BookWheelWebAppFactory _factory;

    public BookWheelConcurrencyTests(BookWheelWebAppFactory factory)
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
    public async Task Parallel_Spin_Requests_All_Return_Valid_Book()
    {
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "test-admin",
            password = "test-password",
            email = "test-setup@example.com"
        });

        foreach (var title in new[] { "Book Alpha", "Book Beta", "Book Gamma", "Book Delta", "Book Epsilon" })
        {
            var resp = await client.PostAsJsonAsync("/api/books", new { title });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        const int parallelCount = 8;
        var tasks = Enumerable.Range(0, parallelCount)
            .Select(_ => client.PostAsync("/api/books/spin", content: null))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var title = doc.RootElement.GetProperty("selected").GetProperty("title").GetString();
            Assert.NotNull(title);
            Assert.NotEmpty(title);
        }
    }
}
