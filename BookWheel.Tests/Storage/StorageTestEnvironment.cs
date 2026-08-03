using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace BookWheel.Tests.Storage;

internal static class StorageTestEnvironment
{
    public static IWebHostEnvironment Create(string contentRootPath)
    {
        var webRootPath = Path.Combine(contentRootPath, "wwwroot");
        Directory.CreateDirectory(webRootPath);

        return new TestWebHostEnvironment
        {
            ContentRootPath = contentRootPath,
            WebRootPath = webRootPath,
            EnvironmentName = "Testing",
            ApplicationName = "BookWheel.Tests",
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath),
            WebRootFileProvider = new PhysicalFileProvider(webRootPath)
        };
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
