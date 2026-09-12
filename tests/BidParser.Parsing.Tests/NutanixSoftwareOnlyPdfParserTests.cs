using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixSoftwareOnlyPdfParserTests
{
    [Fact]
    public void Extracts_expected_line_items_and_totals()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100002.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100002");
        result.Metadata.QuotedTotal.Should().Be(1625358.51m);
        result.Validation.ComputedTotal.Should().Be(1625358.51m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("SW-NCM-STR-PR", 60, 383m, 101.11m, 2096),
                ("TERM-MONTHS", 60, 0m, 0m, 60),
                ("SW-NCI-PRO-PR", 60, 2275m, 600.60m, 864),
                ("TERM-MONTHS", 60, 0m, 0m, 60),
                ("SW-NCI-PRO-PR", 60, 2275m, 600.60m, 1232),
                ("TERM-MONTHS", 60, 0m, 0m, 60),
                ("SW-NCI-E-PRO-PR", 60, 3455m, 912.12m, 145),
                ("TERM-MONTHS", 60, 0m, 0m, 60),
                ("SW-NCM-E-STR-PR", 60, 583m, 153.91m, 145),
                ("TERM-MONTHS", 60, 0m, 0m, 60));

        result.LineItems
            .Where(item => item.Vpn == "TERM-MONTHS")
            .Select(item => item.Description)
            .Should()
            .AllBe("Term in months");
    }

    [Fact]
    public void Extracts_extended_layout_single_line_item()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100005.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100005");
        result.Metadata.QuotedTotal.Should().Be(206169.60m);
        result.Validation.ComputedTotal.Should().Be(206169.60m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty, item.StartDate))
            .Should()
            .Equal(
                ("SW-NDB-PR", (int?)12, (decimal?)1092m, 644.28m, 320, (DateOnly?)new DateOnly(2026, 7, 13)),
                ("TERM-MONTHS", 12, 0m, 0m, 12, null));

        result.LineItems
            .Where(item => item.Vpn == "TERM-MONTHS")
            .Select(item => item.Description)
            .Should()
            .AllBe("Term in months");
    }

    [Fact]
    public void Extracts_extended_layout_with_selected_start_date_and_wrapped_skus()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100006.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100006");
        result.Metadata.QuotedTotal.Should().Be(320562.54m);
        result.Validation.ComputedTotal.Should().Be(320562.54m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty, item.StartDate))
            .Should()
            .Equal(
                ("SW-NDB-PR", (int?)36, (decimal?)3275m, 545.83m, 288, (DateOnly?)new DateOnly(2026, 7, 31)),
                ("TERM-MONTHS", 36, 0m, 0m, 36, null),
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

        result.LineItems
            .Where(item => item.Vpn == "TERM-MONTHS")
            .Select(item => item.Description)
            .Should()
            .AllBe("Term in months");
    }

    [Fact]
    public void Extracts_inline_usd_layout_without_term_currency_bleed()
    {
        // XQ-9100012 renders "USD <amount>" inline in the price columns; the List "USD" label sits
        // just left of the Term/List column boundary, so it lands in the Term cell ("36 USD").
        // DecimalCleaner must strip that currency noise instead of throwing. Term-less service/
        // education rows (where only "USD" bleeds in) must resolve to a null Term, not a failure.
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_software_only_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100012.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100012");
        result.Metadata.QuotedTotal.Should().Be(101320.13m);
        result.Validation.ComputedTotal.Should().Be(101320.13m);
        result.Validation.Matches.Should().BeTrue();
        result.LineItems
            .Select(item => (item.Vpn, item.Term, item.Msrp, item.Cost, item.Qty))
            .Should()
            .Equal(
                ("SW-NCI-ULT-PR", (int?)36, (decimal?)1724m, 448.24m, 128),
                ("TERM-MONTHS", 36, 0m, 0m, 36),
                ("SW-NCM-STR-PR", 36, 230m, 59.80m, 128),
                ("TERM-MONTHS", 36, 0m, 0m, 36),
                ("CNS-INF-A-SVC-DEP-ONP-AHV", null, 1885.71m, 1131.43m, 7),
                ("EDU-C-ADM5-NTC", null, 3990m, 2394m, 4),
                ("EDU-C-ADM5-PVT-PK", null, 28875m, 18795m, 1),
                ("EDU-ONSITE-FEE", null, 0m, 0m, 1));
    }

}
