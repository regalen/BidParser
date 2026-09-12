using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixHardwareOnlyXlsxParserTests
{
    [Fact]
    public void Extracts_expected_quote_d_line_items_and_totals()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_hardware_only_xlsx");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100003.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100003");
        result.Metadata.QuotedTotal.Should().Be(22491.87m);
        result.Validation.ComputedTotal.Should().Be(22491.87m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("NX-1175S-G10-6517P-CM", null, 25021.99m, 20017.57m, 1),
                ("C-MEM-32GB-6400-CM", null, 0m, 0m, 4),
                ("C-HDD-12TB-ETBA-CM", null, 0m, 0m, 2),
                ("C-NVM-7.68TB-AB1A-CM", null, 0m, 0m, 2),
                ("C-HBA-3816-1N-C-CM", null, 0m, 0m, 1),
                ("C-NIC-25G4E1-CM", null, 0m, 0m, 1),
                ("C-PWR-4FC13C14A-CM", null, 0m, 0m, 2),
                ("S-HW-PRD", 60, 4019.99m, 2411.99m, 1),
                ("SUPPORT-TERM", 60, 0m, 0m, 60),
                ("C-TPM-2.0-U-C-CM", null, 77.89m, 62.31m, 1),
                ("PLATFORM INTEGRATION", 0, 4003.51m, 0m, 1));
    }

    [Fact]
    public void Excludes_non_quote_d_rows()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_hardware_only_xlsx");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100003.xlsx"));

        result.LineItems.Should().HaveCount(11);
        var leadItem = result.LineItems[0];
        leadItem.Vpn.Should().Be("NX-1175S-G10-6517P-CM");
        leadItem.Cost.Should().Be(20017.57m, "Quote D has the reseller-facing price, while other quote sections have different pricing");
    }

}
