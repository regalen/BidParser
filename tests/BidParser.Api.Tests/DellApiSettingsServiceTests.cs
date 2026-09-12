using BidParser.Infrastructure.Dell;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BidParser.Api.Tests;

public sealed class DellApiSettingsServiceTests
{
    private const string ClientSecret = "dell-super-secret-value";

    [Fact]
    public async Task Save_encrypts_secret_and_blank_update_preserves_it()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokenCache = new RecordingTokenCache();
        var service = CreateService(scope.ServiceProvider, db, tokenCache);

        await service.SaveAsync(ValidUpdate(ClientSecret), CancellationToken.None);
        tokenCache.Invalidations.Should().Be(1);

        var stored = await db.DellApiSettings.AsNoTracking().SingleAsync();
        stored.Id.Should().Be(1);
        stored.ClientSecretProtected.Should().NotBe(ClientSecret);
        stored.ClientSecretProtected.Should().NotContain(ClientSecret);
        stored.CreatedAt.Should().NotBe(default);
        stored.UpdatedAt.Should().NotBe(default);

        var config = await service.GetAsync(CancellationToken.None);
        config.Should().NotBeNull();
        config!.ClientSecret.Should().Be(ClientSecret);

        var originalCiphertext = stored.ClientSecretProtected;
        await service.SaveAsync(ValidUpdate(null) with { DefaultLocale = "en-nz" }, CancellationToken.None);
        tokenCache.Invalidations.Should().Be(2);

        var updated = await db.DellApiSettings.AsNoTracking().SingleAsync();
        updated.ClientSecretProtected.Should().Be(originalCiphertext);
        updated.DefaultLocale.Should().Be("en-nz");
        (await service.GetForAdminAsync(CancellationToken.None)).ClientSecretConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task First_save_requires_secret_and_valid_https_templates()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(scope.ServiceProvider, db);

        await AssertInvalidAsync(service, ValidUpdate(null), "Client secret is required");
        await AssertInvalidAsync(service, ValidUpdate(ClientSecret) with { TokenUrl = "http://example.test/token" }, "absolute HTTPS URL");
        await AssertInvalidAsync(service, ValidUpdate(ClientSecret) with { QuoteUrlTemplate = "file:///tmp/{quoteNumber}/{quoteVersion}/{locale}" }, "absolute HTTPS URL");
        await AssertInvalidAsync(service, ValidUpdate(ClientSecret) with { QuoteUrlTemplate = "https://example.test/{quoteNumber}/{locale}" }, "{quoteVersion}");
    }

    [Fact]
    public async Task Api_version_is_required_and_restricted_to_a_header_safe_token()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(scope.ServiceProvider, db);

        await AssertInvalidAsync(service, ValidUpdate(ClientSecret) with { ApiVersion = "  " }, "API version is required");
        await AssertInvalidAsync(service, ValidUpdate(ClientSecret) with { ApiVersion = "4.0\r\nX: 1" }, "letters, digits, dots, or hyphens");
        await AssertInvalidAsync(service, ValidUpdate(ClientSecret) with { ApiVersion = new string('4', 17) }, "16 characters or fewer");
        await AssertInvalidAsync(service, ValidUpdate(ClientSecret) with { ApiVersion = "4 0" }, "letters, digits, dots, or hyphens");

        await service.SaveAsync(ValidUpdate(ClientSecret) with { ApiVersion = "3.0" }, CancellationToken.None);

        var config = await service.GetAsync(CancellationToken.None);
        config!.ApiVersion.Should().Be("3.0");
    }

    [Fact]
    public async Task Undecryptable_secret_is_treated_as_not_configured()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = CreateService(scope.ServiceProvider, db);

        await service.SaveAsync(ValidUpdate(ClientSecret), CancellationToken.None);
        var stored = await db.DellApiSettings.SingleAsync();
        stored.ClientSecretProtected = "not-data-protection-ciphertext";
        await db.SaveChangesAsync();

        (await service.GetAsync(CancellationToken.None)).Should().BeNull();
        (await service.GetForAdminAsync(CancellationToken.None)).ClientSecretConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_first_save_collision_retries_as_update()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connectionString = scopedDb.Database.GetConnectionString();
        connectionString.Should().NotBeNullOrWhiteSpace();

        var competingOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var interceptor = new ConcurrentSingletonInsertInterceptor(competingOptions);
        var serviceOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new AppDbContext(serviceOptions);
        var tokenCache = new RecordingTokenCache();
        var service = CreateService(scope.ServiceProvider, db, tokenCache);
        var update = ValidUpdate(ClientSecret) with { DefaultLocale = "en-nz" };

        await service.SaveAsync(update, CancellationToken.None);

        interceptor.WasTriggered.Should().BeTrue();
        tokenCache.Invalidations.Should().Be(1);
        (await db.DellApiSettings.AsNoTracking().CountAsync()).Should().Be(1);
        var config = await service.GetAsync(CancellationToken.None);
        config.Should().NotBeNull();
        config!.ClientSecret.Should().Be(ClientSecret);
        config.DefaultLocale.Should().Be("en-nz");
    }

    private static DellApiSettingsService CreateService(
        IServiceProvider services,
        AppDbContext db,
        IDellAuthTokenCache? tokenCache = null) => new(
        db,
        services.GetRequiredService<IDataProtectionProvider>(),
        NullLogger<DellApiSettingsService>.Instance,
        tokenCache ?? new RecordingTokenCache());

    private static DellApiSettingsUpdate ValidUpdate(string? secret) => new(
        DellApiSettingsService.DefaultTokenUrl,
        "client-id",
        secret,
        DellApiSettingsService.DefaultQuoteUrlTemplate,
        DellApiSettingsService.InitialLocale,
        DellApiSettingsService.InitialClientIdHeader,
        DellApiSettingsService.InitialApiVersion,
        false);

    private static async Task AssertInvalidAsync(
        DellApiSettingsService service,
        DellApiSettingsUpdate update,
        string expectedDetail)
    {
        var act = () => service.SaveAsync(update, CancellationToken.None);
        (await act.Should().ThrowAsync<DellApiSettingsValidationException>())
            .Which.Detail.Should().Contain(expectedDetail);
    }

    private sealed class RecordingTokenCache : IDellAuthTokenCache
    {
        public int Invalidations { get; private set; }
        public void Invalidate() => Invalidations++;
    }

    private sealed class ConcurrentSingletonInsertInterceptor(
        DbContextOptions<AppDbContext> competingOptions) : SaveChangesInterceptor
    {
        private int _triggered;

        public bool WasTriggered => Volatile.Read(ref _triggered) != 0;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is AppDbContext context &&
                context.ChangeTracker.Entries<DellApiSettings>()
                    .Any(entry => entry.State == EntityState.Added) &&
                Interlocked.Exchange(ref _triggered, 1) == 0)
            {
                await using var competingDb = new AppDbContext(competingOptions);
                competingDb.DellApiSettings.Add(new DellApiSettings
                {
                    Id = 1,
                    TokenUrl = DellApiSettingsService.DefaultTokenUrl,
                    ClientId = "competing-client",
                    ClientSecretProtected = "competing-ciphertext",
                    QuoteUrlTemplate = DellApiSettingsService.DefaultQuoteUrlTemplate,
                    DefaultLocale = DellApiSettingsService.InitialLocale,
                    ClientIdHeader = DellApiSettingsService.InitialClientIdHeader,
                    ApiVersion = DellApiSettingsService.InitialApiVersion,
                    UseBasicAuthForToken = false,
                });
                await competingDb.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
