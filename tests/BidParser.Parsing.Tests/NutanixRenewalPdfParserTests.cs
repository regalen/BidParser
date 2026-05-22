using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixRenewalPdfParserTests
{
    [Fact]
    public void Extracts_expected_line_items_and_totals()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4128926.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4128926");
        result.Metadata.QuotedTotal.Should().Be(60205.68m);
        result.Validation.ComputedTotal.Should().Be(60205.68m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.SerialNumber, item.StartDate, item.EndDate, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("RSW-NCM-STR-PR", "24SW000351227,LIC-02472987", new DateOnly(2026, 7, 13), new DateOnly(2027, 7, 12), 77m, 54.41m, 160),
                ("RSW-NCI-ULT-PR", "24SW000351236,LIC-02472996", new DateOnly(2026, 7, 13), new DateOnly(2027, 7, 12), 575m, 371.83m, 32),
                ("RSW-NCI-ULT-PR", "24SW000351221,LIC-02472983", new DateOnly(2026, 7, 13), new DateOnly(2027, 7, 12), 575m, 429.11m, 72),
                ("RSW-NCM-STR-PR", "24SW000351228,LIC-02472985", new DateOnly(2026, 7, 13), new DateOnly(2027, 7, 12), 77m, 54.41m, 160));
    }

    [Fact]
    public void Extracts_expected_line_items_and_totals_for_wrapped_currency_sample()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4166696.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4166696");
        result.Metadata.QuotedTotal.Should().Be(375636.00m);
        result.Validation.ComputedTotal.Should().Be(375636.00m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.SerialNumber, item.StartDate, item.EndDate, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("RSW-NCM-STR-PR", "25SW000430057,LIC-02537784", new DateOnly(2026, 6, 17), new DateOnly(2028, 12, 1), 189m, 54.64m, 80),
                ("RSW-NCI-PRO-PR", "25SW000430055,LIC-02537786", new DateOnly(2026, 6, 17), new DateOnly(2028, 12, 1), 1121m, 661.61m, 80),
                ("RSW-NCM-STR-PR", "25SW000430056,LIC-02537783", new DateOnly(2026, 10, 28), new DateOnly(2028, 12, 1), 161m, 40.20m, 400),
                ("RSW-NCI-PRO-PR", "25SW000430054,LIC-02537785", new DateOnly(2026, 10, 28), new DateOnly(2028, 12, 1), 955m, 755.64m, 400));
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
