using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthzReturnsOk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            using var environment = new ScopedEnvironment(new Dictionary<string, string>
            {
                ["DATABASE_URL"] = $"sqlite:///{Path.Combine(tempDir, "db.sqlite")}",
                ["UPLOAD_DIR"] = Path.Combine(tempDir, "files")
            });
            using var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/healthz");

            response.IsSuccessStatusCode.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
