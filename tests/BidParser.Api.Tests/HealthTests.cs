using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
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

    [Fact]
    public async Task HealthzIncludesSecurityHeadersForHttps()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            using var environment = new ScopedEnvironment(new Dictionary<string, string>
            {
                ["DATABASE_URL"] = $"sqlite:///{Path.Combine(tempDir, "db.sqlite")}",
                ["UPLOAD_DIR"] = Path.Combine(tempDir, "files"),
                ["FORWARDED_ALLOW_IPS"] = "127.0.0.1"
            });
            using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
            using var client = factory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/healthz");
            request.Headers.Add("X-Test-Remote-Ip", "127.0.0.1");
            request.Headers.Add("X-Forwarded-Proto", "https");

            using var response = await client.SendAsync(request);

            response.IsSuccessStatusCode.Should().BeTrue();
            response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
            response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
            response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
            response.Headers.GetValues("Strict-Transport-Security").Should().ContainSingle().Which.Should().Be("max-age=31536000");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
