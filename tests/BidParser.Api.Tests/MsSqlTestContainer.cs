using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace BidParser.Api.Tests;

internal static class MsSqlTestContainer
{
    private static readonly Lazy<Task<MsSqlContainer>> Container = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(3);

    public static async Task<string> GetConnectionStringAsync(string databaseName)
    {
        var container = await Container.Value;
        var builder = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = databaseName
        };
        return builder.ConnectionString;
    }

    private static async Task<MsSqlContainer> StartAsync()
    {
        // The package default waits by exec'ing sqlcmd inside the container. Under CPU
        // contention (parsing and API assemblies running in parallel) that exec can be
        // reset by the container runtime while SQL Server is still booting, and the
        // failure is fatal — the wait strategy does not retry, so this Lazy faults and
        // every test in the assembly fails. Wait on the log line instead, then confirm
        // readiness over TDS with our own retry loop.
        // SQL Server 2025's current Linux image intermittently aborts during bootstrap under
        // rootless Podman with errno 11. The application supports SQL Server 2022, and pinning
        // this test dependency keeps integration tests reproducible instead of tracking `latest`.
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("SQL Server is now ready for client connections"))
            .Build();

        await container.StartAsync();
        await WaitForConnectionAsync(container);
        return container;
    }

    private static async Task WaitForConnectionAsync(MsSqlContainer container)
    {
        var connectionString = container.GetConnectionString();
        var deadline = Stopwatch.StartNew();
        Exception? last = null;

        while (deadline.Elapsed < ReadyTimeout)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException(
            $"SQL Server container was not accepting connections after {ReadyTimeout}.", last);
    }
}
