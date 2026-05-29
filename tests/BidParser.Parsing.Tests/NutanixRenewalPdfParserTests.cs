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

    [Fact]
    public void Extracts_expected_line_items_and_totals_for_platform_column_variant()
    {
        // XQ-4029825 is a Renewal variant with an extra "Platform" column between No and
        // Product Code. Hardware rows carry a platform value (e.g. NX-8035N-G8-HY) which
        // must appear as Description "Platform: <value>"; software rows leave Description null.
        // Product Code wraps across two lines (RSW-NCI- / ULT-PR) and must be joined without
        // a separator. Totals must match: 5 rows, USD 392,190.58.
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4029825.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4029825");
        result.Metadata.QuotedTotal.Should().Be(392190.58m);
        result.Validation.ComputedTotal.Should().Be(392190.58m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems.Should().HaveCount(5);

        // Software-only rows: no Platform value → Description null
        result.LineItems[0].Vpn.Should().Be("RSW-NCI-ULT-PR");
        result.LineItems[0].Description.Should().BeNull();
        result.LineItems[0].SerialNumber.Should().Be("25SW000437991,LIC-02543011");
        result.LineItems[0].StartDate.Should().Be(new DateOnly(2026, 8, 16));
        result.LineItems[0].EndDate.Should().Be(new DateOnly(2029, 12, 31));
        result.LineItems[0].Msrp.Should().Be(1943.00m);
        result.LineItems[0].Cost.Should().Be(354.77m);
        result.LineItems[0].Qty.Should().Be(448);

        result.LineItems[1].Vpn.Should().Be("RSW-NCI-ULT-PR");
        result.LineItems[1].Description.Should().BeNull();
        result.LineItems[1].SerialNumber.Should().Be("25SW000437992,LIC-02543012");
        result.LineItems[1].Cost.Should().Be(601.52m);
        result.LineItems[1].Qty.Should().Be(192);

        result.LineItems[2].Vpn.Should().Be("RSW-NCI-PRO-PR");
        result.LineItems[2].Description.Should().BeNull();
        result.LineItems[2].SerialNumber.Should().Be("22SW000262928,LIC-01461229");
        result.LineItems[2].Cost.Should().Be(889.43m);
        result.LineItems[2].Qty.Should().Be(128);

        // Hardware rows: Platform present → Description = "Platform: NX-8035N-G8-HY"
        result.LineItems[3].Vpn.Should().Be("RS-HW-PRD-MY");
        result.LineItems[3].Description.Should().Be("Platform: NX-8035N-G8-HY");
        result.LineItems[3].SerialNumber.Should().Be("22SH3G410326");
        result.LineItems[3].StartDate.Should().Be(new DateOnly(2026, 11, 3));
        result.LineItems[3].EndDate.Should().Be(new DateOnly(2029, 7, 31));
        result.LineItems[3].Msrp.Should().Be(2676.24m);
        result.LineItems[3].Cost.Should().Be(1957.37m);
        result.LineItems[3].Qty.Should().Be(1);

        result.LineItems[4].Vpn.Should().Be("RS-HW-PRD-MY");
        result.LineItems[4].Description.Should().Be("Platform: NX-8035N-G8-HY");
        result.LineItems[4].SerialNumber.Should().Be("22SH3G410327");
        result.LineItems[4].Cost.Should().Be(1957.37m);
        result.LineItems[4].Qty.Should().Be(1);
    }

    [Fact]
    public void Extracts_expected_line_items_for_platform_column_variant_with_reversed_net_header()
    {
        // XQ-4183034 is a Platform-column variant where the "Net Unit Price" header renders
        // with "Net" to the RIGHT of "Unit" and "Price" (reversed from XQ-4029825). The
        // original code used the "Net" word X0 as the column boundary, which placed it at
        // 471.7 — right of the actual amount data at 460.4, causing a FormatException.
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4183034.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4183034");
        result.Metadata.QuotedTotal.Should().Be(122442.85m);
        result.Validation.ComputedTotal.Should().Be(122442.85m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems.Should().HaveCount(13);

        // Software rows: no Platform value → Description null
        result.LineItems[0].Vpn.Should().Be("RSW-NCI-PRO-PR");
        result.LineItems[0].Description.Should().BeNull();
        result.LineItems[0].SerialNumber.Should().Be("25SW000398208,LIC-02510506");
        result.LineItems[0].StartDate.Should().Be(new DateOnly(2026, 5, 31));
        result.LineItems[0].EndDate.Should().Be(new DateOnly(2027, 12, 31));
        result.LineItems[0].Msrp.Should().Be(723.00m);
        result.LineItems[0].Cost.Should().Be(326.31m);
        result.LineItems[0].Qty.Should().Be(128);

        // Hardware rows: Platform present → Description populated; Msrp == Cost (0% discount)
        result.LineItems[4].Vpn.Should().Be("RS-HW-PRD-ST");
        result.LineItems[4].Description.Should().Be("Platform: NX-1175S-G7-HY");
        result.LineItems[4].SerialNumber.Should().Be("21SM5E080333");
        result.LineItems[4].StartDate.Should().Be(new DateOnly(2026, 5, 31));
        result.LineItems[4].EndDate.Should().Be(new DateOnly(2027, 6, 30));
        result.LineItems[4].Msrp.Should().Be(257.19m);
        result.LineItems[4].Cost.Should().Be(257.19m);
        result.LineItems[4].Qty.Should().Be(1);
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
