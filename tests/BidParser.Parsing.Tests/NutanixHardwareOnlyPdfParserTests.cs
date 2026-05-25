using BidParser.Parsing.Nutanix.HardwareOnlyPdf;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixHardwareOnlyPdfParserTests
{
    [Theory]
    // "Page N of M" footers must be skipped, however the words bucket into cells.
    [InlineData(true, "Page 5 of 7")]                       // whole footer in one cell
    [InlineData(true, "Page 10 of 12")]                     // multi-digit page numbers
    [InlineData(true, "Page 5", "of 7")]                    // split across adjacent columns
    [InlineData(true, "Page 5 of 7", "29/01/2026")]         // footer decorated with a date
    [InlineData(true, "Confidential", "Page 5 of 7")]       // footer decorated with a note
    // Rows that merely look footer-ish must NOT be skipped.
    [InlineData(false, "Page")]                             // bare "Page" continuation text
    [InlineData(false, "Page 5 of")]                        // no trailing page count
    [InlineData(false, "Bundle of 4 drives", "2")]          // "of" inside a description
    [InlineData(false, "NX-1175S-G10-6517P-CM", "60", "USD 4,019.99", "1")] // real line item
    [InlineData(false)]                                     // empty row
    public void IsPageFooterRow_detects_page_footers(bool expected, params string[] cellValues)
    {
        var cells = cellValues
            .Select((value, index) => (Key: $"c{index}", Value: value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        NutanixHardwareOnlyPdfParser.IsPageFooterRow(cells).Should().Be(expected);
    }

    [Fact]
    public void Extracts_expected_quote_d_line_items_and_totals()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_hardware_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4108785.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-4108785");
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
                ("Support-Term", 60, 0m, 0m, 60),
                ("C-TPM-2.0-U-C-CM", null, 77.89m, 62.31m, 1),
                ("Platform Integration", 0, 4003.51m, 0m, 1));
    }

    [Fact]
    public void Excludes_quote_c_rows()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_hardware_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-4108785.pdf"));

        result.LineItems.Should().HaveCount(11);
        var leadItem = result.LineItems[0];
        leadItem.Vpn.Should().Be("NX-1175S-G10-6517P-CM");
        leadItem.Cost.Should().Be(20017.57m, "Quote D has the reseller-facing price, while Quote C has 5903.72");
    }

    [Fact]
    public void Parses_multi_page_quote_d_powerhouse_museum()
    {
        // Quote D spans pages 4-5 of 7; the "Page 5 of 7" footer falls in the
        // Term (Months) column range and previously caused FormatException: 'Page'.
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == "nutanix_hardware_only_pdf");
        const string file = "XQ-4175235-PowerHouse NX 2-1670234_Logicalis_25-05-Powerhouse Museum.pdf";

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", file));

        result.Metadata.QuotedTotal.Should().Be(247510.94m);
        result.Validation.Matches.Should().BeTrue();

        // No page-footer contamination — "Page" must never appear as a VPN or term.
        result.LineItems.Should().NotContain(item =>
            item.Vpn.Equals("Page", StringComparison.OrdinalIgnoreCase),
            "page footer text must not be mistaken for a Product Code");

        // Two identical NX bundles (qty 3 each) + two Platform Integration rows.
        var nx = result.LineItems.Where(i => i.Vpn == "NX-1175S-G10-6724P-CM").ToList();
        nx.Should().HaveCount(2);
        nx.Should().AllSatisfy(item =>
        {
            item.Msrp.Should().Be(38994.60m);
            item.Cost.Should().Be(31195.67m);
            item.Qty.Should().Be(3);
            item.Term.Should().BeNull();
        });

        var platform = result.LineItems.Where(i => i.Vpn == "Platform Integration").ToList();
        platform.Should().HaveCount(2);
        platform.Should().AllSatisfy(item =>
        {
            item.Cost.Should().Be(18717.40m);
            item.Qty.Should().Be(1);
            item.Term.Should().Be(0);
        });
    }

    [Fact]
    public void Parses_multi_page_quote_d_worksafe_victoria()
    {
        // Quote D spans pages 7-10 of 12 with three page footers inside the table.
        // Net Unit Price for NX-8150-G10-6728P-CM wraps: "USD" on the anchor row,
        // "169,690.39" on the continuation row — previously silently produced Cost=0
        // once the page-footer crash was removed.
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == "nutanix_hardware_only_pdf");
        const string file = "XQ-4175219-Worksafe TAC - NX - 29012026-1615307-Worksafe Victoria.pdf";

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", file));

        result.Metadata.QuotedTotal.Should().Be(3601962.18m);
        result.Validation.Matches.Should().BeTrue();

        // Currency fusion must correctly recover the wrapped net price.
        var nx6728 = result.LineItems.Where(i => i.Vpn == "NX-8150-G10-6728P-CM").ToList();
        nx6728.Should().NotBeEmpty();
        nx6728.Should().AllSatisfy(item =>
            item.Cost.Should().NotBe(0m, "FuseCurrencyTokens must join the wrapped 'USD 169,690.39'"));
        nx6728.Should().AllSatisfy(item => item.Cost.Should().Be(169690.39m));

        // No page-footer contamination.
        result.LineItems.Should().NotContain(item =>
            item.Vpn.Equals("Page", StringComparison.OrdinalIgnoreCase));
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
