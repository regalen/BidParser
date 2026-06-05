using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixRenewalXlsxParserTests
{
    [Fact]
    public void Extracts_expected_line_items_and_totals()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_xlsx");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4176792.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4176792");
        result.Metadata.QuotedTotal.Should().Be(68160.08m);
        result.Validation.ComputedTotal.Should().Be(68160.08m);
        result.Validation.Matches.Should().BeTrue();

        result.LineItems
            .Select(item => (item.Vpn, item.SerialNumber, item.StartDate, item.EndDate, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("RS-HW-PRD-ST", "21FM6K270093", new DateOnly(2026, 7, 12), new DateOnly(2027, 7, 11), 1107.36m, 803.30m, 1),
                ("RS-HW-PRD-ST", "21FM6K270094", new DateOnly(2026, 7, 12), new DateOnly(2027, 7, 11), 1107.36m, 803.30m, 1),
                ("RS-HW-PRD-ST", "21FM6K270091", new DateOnly(2026, 7, 12), new DateOnly(2027, 7, 11), 1107.36m, 803.30m, 1),
                ("RS-HW-PRD-ST", "21FM6K270092", new DateOnly(2026, 7, 12), new DateOnly(2027, 7, 11), 1107.36m, 803.30m, 1),
                ("RSW-NCI-PRO-PR", "26SW000487027,LIC-02574676", new DateOnly(2026, 7, 12), new DateOnly(2027, 7, 11), 455.00m, 225.51m, 288));
    }

    [Fact]
    public void Combines_product_description_and_platform_into_description()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_xlsx");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4176792.xlsx"));

        // Hardware row: Product Description + Platform combined.
        result.LineItems[0].Description.Should().Be(
            "24/7 Production Level Short Term HW Support Renewal for Nutanix HCI appliance (Platform: NX-3060-G7-AF)");

        // Software row: no Platform → bare Product Description.
        result.LineItems[4].Description.Should().Be(
            "Subscription Renewal, Nutanix Cloud Infrastructure (NCI) Pro Software License & Production Software Support Service for 1 CPU Core");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BidParser.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
