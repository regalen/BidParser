namespace BidParser.Api.Tests;

using BidParser.Api.Auth;
using BidParser.Domain.Abstractions;
using BidParser.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

internal sealed class CustomTestFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly ScopedEnvironment _env;

    private CustomTestFixture(string tempDir, ScopedEnvironment env, WebApplicationFactory<Program> factory)
    {
        _tempDir = tempDir;
        _env = env;
        Factory = factory;
        UploadDir = Path.Combine(tempDir, "files");
    }

    public WebApplicationFactory<Program> Factory { get; }
    public string UploadDir { get; }

    public static async Task<CustomTestFixture> CreateAsync(
        IParserRegistry? registry = null,
        int maxUploadMb = 10,
        string? forwardedAllowIps = null,
        string? environmentName = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"bidparser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var envValues = new Dictionary<string, string>
        {
            ["DATABASE_URL"] = $"sqlite:///{Path.Combine(tempDir, "db.sqlite")}",
            ["UPLOAD_DIR"] = Path.Combine(tempDir, "files"),
            ["SESSION_SECRET"] = $"test-{Guid.NewGuid():N}",
            ["ADMIN_USERNAME"] = "admin",
            ["ADMIN_PASSWORD"] = "changeme",
            ["RATE_LIMIT_AUTH_PER_MIN"] = "5",
            ["MAX_UPLOAD_MB"] = maxUploadMb.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (forwardedAllowIps is not null)
        {
            envValues["FORWARDED_ALLOW_IPS"] = forwardedAllowIps;
        }

        var env = new ScopedEnvironment(envValues);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (environmentName is not null)
            {
                builder.UseEnvironment(environmentName);
            }

            if (registry is not null)
            {
                builder.ConfigureServices(services =>
                {
                    var d = services.SingleOrDefault(s => s.ServiceType == typeof(IParserRegistry));
                    if (d is not null) services.Remove(d);
                    services.AddSingleton<IParserRegistry>(registry);
                });
            }
        });

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<AuthRateLimiter>().Clear();

        return new CustomTestFixture(tempDir, env, factory);
    }

    public void Dispose()
    {
        Factory.Dispose();
        _env.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

internal sealed class TestRegistry : IParserRegistry
{
    public TestRegistry(params IParser[] parsers) => Parsers = parsers;
    public IReadOnlyList<IParser> Parsers { get; }
}

internal sealed class TestParser : IParser
{
    private readonly Func<string, ParseResult> _parse;

    public TestParser(string slug, string vendor, string acceptedMime, Func<string, ParseResult> parse)
    {
        Slug = slug;
        DisplayName = slug;
        Vendor = vendor;
        AcceptedMime = acceptedMime;
        _parse = parse;
    }

    public string Slug { get; }
    public string DisplayName { get; }
    public string Vendor { get; }
    public string AcceptedMime { get; }
    public string CrmTemplate { get; } = "Foreign Uplift";

    public ParseResult Parse(string path) => _parse(path);
}
