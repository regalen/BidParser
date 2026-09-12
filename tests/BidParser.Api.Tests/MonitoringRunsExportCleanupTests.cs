namespace BidParser.Api.Tests;

using System.Net;
using System.Data.Common;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BidParser.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BidParser.Api.Options;

internal sealed class TrackedMemoryStream : MemoryStream
{
    public bool IsDisposed { get; private set; }
    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
    public override ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return base.DisposeAsync();
    }
}

internal sealed class TestExportEnvironment : IExportEnvironment
{
    private readonly string _connectionString;

    private TestExportEnvironment(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static TestExportEnvironment FromFixture(ApiTestFixture fixture)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new TestExportEnvironment(db.Database.GetConnectionString()!);
    }

    public TrackedMemoryStream TempStream { get; } = new();

    public bool ThrowExceptionOnLimit { get; set; }
    public bool BlockQueryUntilCancellation { get; set; }

    public TaskCompletionSource StreamCreatedGate { get; } = new();

    private int _worksheetRowLimit = 1_048_576;
    public int WorksheetRowLimit
    {
        get
        {
            if (ThrowExceptionOnLimit) throw new InvalidOperationException("Simulated exception after stream creation");
            return _worksheetRowLimit;
        }
        set => _worksheetRowLimit = value;
    }

    public AppDbContext CreateExportContext()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connectionString);
        if (BlockQueryUntilCancellation)
        {
            builder.AddInterceptors(new CancellationBlockingCommandInterceptor());
        }
        return new AppDbContext(builder.Options);
    }

    public Stream CreateTempStream()
    {
        StreamCreatedGate.TrySetResult();
        return TempStream;
    }
}

internal sealed class CancellationBlockingCommandInterceptor : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return result;
    }
}

public sealed class MonitoringRunsExportCleanupTests
{
    [Fact]
    public async Task Default_export_environment_uses_non_retrying_context_and_delete_on_close_file()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        var options = fixture.Factory.Services.GetRequiredService<AppOptions>();
        var environment = new DefaultExportEnvironment(options);

        await using (var context = environment.CreateExportContext())
        {
            context.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeFalse();
        }

        string path;
        await using (var stream = environment.CreateTempStream())
        {
            var file = stream.Should().BeOfType<FileStream>().Subject;
            path = file.Name;
            Path.GetExtension(path).Should().Be(".xlsx");
            File.Exists(path).Should().BeTrue();
        }

        File.Exists(path).Should().BeFalse();
    }

    private async Task SeedDataAsync(ApiTestFixture fixture)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.ParseJobs.AnyAsync()) return;
        var user = await db.Users.FirstAsync();

        db.ParseJobs.Add(new ParseJob
        {
            UserId = user.Id,
            Vendor = "Nutanix",
            ParserSlug = "nutanix",
            CrmTemplate = "Foreign Uplift",
            SourceFilename = "src.pdf",
            SourcePath = "src.pdf",
            OutputPath = "out.xlsx",
            FxRate = 1m,
            Margin = 0m,
            ComputedTotal = 100m,
            TotalsMatch = true,
            CreatedAt = DateTime.UtcNow,
            ImportType = ImportType.Manual
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Export_Cleanup_OnSuccess()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        var env = TestExportEnvironment.FromFixture(fixture);
        var client = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IExportEnvironment>(env);
            });
        }).CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedDataAsync(fixture);

        var res = await client.GetAsync("/api/monitoring/runs/export?range=all");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        await res.Content.ReadAsByteArrayAsync();
        res.Content.Dispose();
        await Task.Delay(100);

        env.TempStream.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Export_Cleanup_OnException()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        var env = TestExportEnvironment.FromFixture(fixture);
        env.ThrowExceptionOnLimit = true;
        var client = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IExportEnvironment>(env);
            });
        }).CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedDataAsync(fixture);

        var res = await client.GetAsync("/api/monitoring/runs/export?range=all");
        res.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        await Task.Delay(100);
        env.TempStream.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Export_Cleanup_OnCancellation()
    {
        using var fixture = await ApiTestFixture.CreateAsync();
        var env = TestExportEnvironment.FromFixture(fixture);
        env.BlockQueryUntilCancellation = true;
        var client = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IExportEnvironment>(env);
            });
        }).CreateClient();
        await ApiTestFixture.UnlockAdminAsync(client);
        await SeedDataAsync(fixture);

        using var cts = new CancellationTokenSource();
        var task = client.GetAsync("/api/monitoring/runs/export?range=all", cts.Token);

        await env.StreamCreatedGate.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Delay(100);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<Exception>(() => task);

        await Task.Delay(200);
        env.TempStream.IsDisposed.Should().BeTrue();
    }
}
