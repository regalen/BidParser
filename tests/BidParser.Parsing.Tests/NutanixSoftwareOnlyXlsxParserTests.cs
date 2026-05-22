using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixSoftwareOnlyXlsxParserTests
{
    [Fact]
    public void Extracts_expected_line_items_and_totals()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_xlsx");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4076249.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4076249");
        result.Metadata.QuotedTotal.Should().Be(1625358.51m);
        result.Validation.ComputedTotal.Should().Be(1625358.51m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("SW-NCM-STR-PR", 60, 383m, 101.11m, 2096),
                ("Term-Months", 60, 0m, 0m, 60),
                ("SW-NCI-PRO-PR", 60, 2275m, 600.60m, 864),
                ("Term-Months", 60, 0m, 0m, 60),
                ("SW-NCI-PRO-PR", 60, 2275m, 600.60m, 1232),
                ("Term-Months", 60, 0m, 0m, 60),
                ("SW-NCI-E-PRO-PR", 60, 3455m, 912.12m, 145),
                ("Term-Months", 60, 0m, 0m, 60),
                ("SW-NCM-E-STR-PR", 60, 583m, 153.91m, 145),
                ("Term-Months", 60, 0m, 0m, 60));

        result.LineItems
            .Where(item => item.Vpn == "Term-Months")
            .Select(item => item.Description)
            .Should()
            .AllBe("Term in months");
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
