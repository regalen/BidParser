using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixSoftwareOnlyPdfParserTests
{
    [Fact]
    public void Extracts_expected_line_items_and_totals()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4076249.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4076249");
        result.Metadata.QuotedTotal.Should().Be(1625358.51m);
        result.Validation.ComputedTotal.Should().Be(1625358.51m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("SW-NCM-STR-PR", 60, 383m, 101.11m, 2096),
                ("SW-NCI-PRO-PR", 60, 2275m, 600.60m, 864),
                ("SW-NCI-PRO-PR", 60, 2275m, 600.60m, 1232),
                ("SW-NCI-E-PRO-PR", 60, 3455m, 912.12m, 145),
                ("SW-NCM-E-STR-PR", 60, 583m, 153.91m, 145));
    }

    [Fact]
    public void Extracts_extended_layout_single_line_item()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4157308.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4157308");
        result.Metadata.QuotedTotal.Should().Be(206169.60m);
        result.Validation.ComputedTotal.Should().Be(206169.60m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty, item.StartDate))
            .Should()
            .Equal(
                ("SW-NDB-PR", (int?)12, (decimal?)1092m, 644.28m, 320, (DateOnly?)new DateOnly(2026, 7, 13)));
    }

    [Fact]
    public void Extracts_extended_layout_with_selected_start_date_and_wrapped_skus()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4165884.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4165884");
        result.Metadata.QuotedTotal.Should().Be(320562.54m);
        result.Validation.ComputedTotal.Should().Be(320562.54m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty, item.StartDate))
            .Should()
            .Equal(
                ("SW-NDB-PR", (int?)36, (decimal?)3275m, 545.83m, 288, (DateOnly?)new DateOnly(2026, 7, 31)),
                ("FLEX-CST-CR", 12, 100m, 85m, 60, null),
                ("CNS-INF-A-WRK-DSGN-BAS-MS-SD-VIRT", null, 38105m, 34294.50m, 1, null),
                ("CNS-INF-A-SVC-DEP-ONP-AHV", null, 3440m, 3096m, 3, null),
                ("CNS-INF-A-SVC-DEP-ONP-AHV", null, 3440m, 3096m, 3, null),
                ("CNS-INF-A-SVC-DRD-LEAP", null, 9980m, 8982m, 1, null),
                ("CNS-INF-A-SVC-MIG-VMS-VIRT", null, 3745m, 3370.50m, 2, null),
                ("EDU-C-ADM5-PVT-PK", null, 28875m, 26355m, 1, null),
                ("EDU-ONSITE-FEE", null, 0m, 0m, 1, null),
                ("EDU-C-NDMA-INV", null, 2310m, 2079m, 1, null),
                ("PS-RES-IRE-CONS-QRTR-12MO", null, 68040m, 61236m, 1, null));
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
