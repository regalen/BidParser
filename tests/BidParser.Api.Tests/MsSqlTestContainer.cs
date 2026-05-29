using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace BidParser.Api.Tests;

internal static class MsSqlTestContainer
{
    private static readonly Lazy<Task<MsSqlContainer>> Container = new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

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
        var container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2025-latest")
            .Build();
        await container.StartAsync();
        return container;
    }
}
