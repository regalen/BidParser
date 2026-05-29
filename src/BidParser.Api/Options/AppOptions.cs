using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace BidParser.Api.Options;

public sealed class AppOptions
{
    public string ConnectionString { get; init; } = DefaultConnectionString();
    public string UploadDir { get; init; } = Path.Combine(DefaultDataDir(), "files");
    public string SessionSecret { get; init; } = "dev-only-change-me";
    public int SessionLifetimeHours { get; init; } = 12;
    public string AdminUsername { get; init; } = "admin";
    public string AdminPassword { get; init; } = "changeme";
    public int RetentionDays { get; init; } = 90;
    public int RateLimitAuthPerMin { get; init; } = 5;
    public int MaxUploadMb { get; init; } = 10;
    public string ForwardedAllowIps { get; init; } = "127.0.0.1,::1";

    public long MaxUploadBytes => (long)MaxUploadMb * 1024 * 1024;
    public string DataProtectionKeysDir => Path.Combine(DataDir, "dp-keys");
    private string DataDir => Path.GetDirectoryName(Path.GetFullPath(UploadDir)) ?? DefaultDataDir();

    public IReadOnlyList<IPAddress> ForwardedAllowIpAddresses =>
        ForwardedAllowIps
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => IPAddress.TryParse(value, out var parsed) ? parsed : null)
            .OfType<IPAddress>()
            .ToArray();

    public static AppOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var forwardedAllowIps = ReadString(configuration, "FORWARDED_ALLOW_IPS", "*");
        if (forwardedAllowIps == "*")
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "FORWARDED_ALLOW_IPS must be an explicit comma-separated proxy IP allowlist in production.");
            }

            forwardedAllowIps = "127.0.0.1,::1";
        }

        return new AppOptions
        {
            ConnectionString = ReadString(configuration, "DB_CONNECTION_STRING", DefaultConnectionString()),
            UploadDir = ReadString(configuration, "UPLOAD_DIR", Path.Combine(DefaultDataDir(), "files")),
            SessionSecret = ReadString(configuration, "SESSION_SECRET", "dev-only-change-me"),
            SessionLifetimeHours = ReadInt(configuration, "SESSION_LIFETIME_HOURS", 12),
            AdminUsername = ReadString(configuration, "ADMIN_USERNAME", "admin"),
            AdminPassword = ReadString(configuration, "ADMIN_PASSWORD", "changeme"),
            RetentionDays = ReadInt(configuration, "RETENTION_DAYS", 90),
            RateLimitAuthPerMin = ReadInt(configuration, "RATE_LIMIT_AUTH_PER_MIN", 5),
            MaxUploadMb = ReadInt(configuration, "MAX_UPLOAD_MB", 10),
            ForwardedAllowIps = forwardedAllowIps
        };
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(UploadDir);
        Directory.CreateDirectory(DataProtectionKeysDir);
    }

    private static string ReadString(IConfiguration configuration, string key, string fallback)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback)
    {
        return int.TryParse(configuration[key], out var value) ? value : fallback;
    }

    private static string DefaultConnectionString()
    {
        return "Server=localhost,1433;Database=bidparser;User Id=sa;Password=Dev_Password_123;Encrypt=True;TrustServerCertificate=True";
    }

    private static string DefaultDataDir()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}
