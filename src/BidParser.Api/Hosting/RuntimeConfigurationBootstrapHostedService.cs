using System.Text.Json;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Entities;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Api.Hosting;

/// <summary>Inserts missing runtime configuration documents after migrations without replacing administrator edits.</summary>
public sealed class RuntimeConfigurationBootstrapHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await db.RuntimeConfigs.AsNoTracking()
                .Select(config => config.Key)
                .ToListAsync(cancellationToken);

            if (!existing.Contains(RuntimeConfigKeys.GuidanceMessages, StringComparer.Ordinal))
            {
                db.RuntimeConfigs.Add(new RuntimeConfig
                {
                    Key = RuntimeConfigKeys.GuidanceMessages,
                    JsonPayload = GuidanceJson()
                });
            }

            if (!existing.Contains(RuntimeConfigKeys.VendorDefaults, StringComparer.Ordinal))
            {
                db.RuntimeConfigs.Add(new RuntimeConfig
                {
                    Key = RuntimeConfigKeys.VendorDefaults,
                    JsonPayload = "[\n  {\n    \"vendors\": [\"Zebra\"],\n    \"onCostPct\": 2.85\n  }\n]"
                });
            }

            if (!db.ChangeTracker.HasChanges()) return;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // Another application instance may have inserted one or both singleton keys
                // after our read. Clear the failed tracked inserts and re-evaluate missing keys.
                db.ChangeTracker.Clear();
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string GuidanceJson() => JsonSerializer.Serialize(new[]
    {
        Message(new[] { ParserSlugs.NutanixSoftwareOnlyPdf, ParserSlugs.NutanixSoftwareOnlyXlsx, ParserSlugs.NutanixHardwareOnlyPdf, ParserSlugs.NutanixHardwareOnlyXlsx, ParserSlugs.HpOneConfigXlsx, ParserSlugs.LenovoLbpeIsgXls, ParserSlugs.LenovoLbpiIsgPdf, ParserSlugs.LenovoLbpiIdgPdf }, "Standard"),
        Message(new[] { ParserSlugs.NutanixRenewalPdf, ParserSlugs.NutanixRenewalXlsx, ParserSlugs.DellAposJson }, "Start End Date"),
        Message(new[] { ParserSlugs.HpBidXlsx, ParserSlugs.HpGlobalBidXlsx, ParserSlugs.HpeBidXlsx, ParserSlugs.ZebraPcrPdf, ParserSlugs.ZebraPcrXls, ParserSlugs.DellCtoJson }, "Hardware SOH"),
        Message(new[] { ParserSlugs.CiscoCcwQuoteXls }, "Hardware SOH Disc %")
    }, new JsonSerializerOptions { WriteIndented = true });

    private static object Message(IReadOnlyList<string> fileTypes, string reportType) => new
    {
        fileTypes,
        html = $"<p>When sending this quote to the customer, use the <strong>{reportType}</strong> report type.</p>"
    };
}
