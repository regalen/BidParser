using BidParser.Output;
using BidParser.Parsing.Nutanix.RenewalPdf;
using BidParser.Parsing.Pdf;
using BidParser.Parsing.Registry;
using ClosedXML.Excel;
using FluentAssertions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixRenewalPdfParserTests
{
    [Fact]
    public void Keeps_unpriced_renewal_lines_with_zero_prices_and_matching_total()
    {
        var directory = Directory.CreateTempSubdirectory("bidparser-renewal-");
        try
        {
            var inputPath = Path.Combine(directory.FullName, "synthetic-renewal.pdf");
            var outputPath = Path.Combine(directory.FullName, "synthetic-renewal-output.xlsx");
            var pdf = new PdfDocumentBuilder();
            var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
            var page = pdf.AddPage(650, 600);
            void Add(string value, double x, double y) =>
                page.AddText(value, 7, new PdfPoint(x, y), font);

            Add("No", 10, 500);
            Add("Product", 40, 500);
            Add("Code", 75, 500);
            Add("Serial", 175, 500);
            Add("Number", 198, 500);
            Add("Start", 260, 500);
            Add("End", 310, 500);
            Add("Adjusted", 350, 500);
            Add("List", 385, 500);
            Add("Discount", 410, 500);
            Add("Net", 455, 500);
            Add("Unit", 473, 500);
            Add("Price", 490, 500);
            Add("Qty", 515, 500);
            Add("Total", 550, 500);

            void AddItem(string number, string vpn, string serial, double y, string qty,
                string? msrp = null, string? cost = null, string? total = null)
            {
                Add(number, 10, y);
                Add(vpn, 40, y);
                Add(serial, 175, y);
                Add("11/29/2026", 260, y);
                Add("11/28/2027", 310, y);
                if (msrp is not null) Add($"USD {msrp}", 350, y);
                if (cost is not null) Add($"USD {cost}", 455, y);
                Add(qty, 515, y);
                if (total is not null) Add($"USD {total}", 550, y);
            }

            AddItem("1", "R-SYN-PARENT-PRD", "SYN-SERIAL-001", 470, "1", "80.00", "25.00", "25.00");
            AddItem("2", "R-SYN-CHILD-A", "SYN-LIC-001", 445, "30");
            AddItem("3", "R-SYN-CHILD-B", "SYN-LIC-002", 420, "36");
            Add("TOTAL:", 500, 390);
            Add("USD 25.00", 550, 390);
            File.WriteAllBytes(inputPath, pdf.Build());

            var parser = new NutanixRenewalPdfParser();
            var result = parser.Parse(inputPath);

            result.LineItems.Select(item => (item.Vpn, item.SerialNumber, item.Qty, item.Msrp, item.Cost))
                .Should().Equal(
                    ("R-SYN-PARENT-PRD", "SYN-SERIAL-001", 1, 80m, 25m),
                    ("R-SYN-CHILD-A", "SYN-LIC-001", 30, 0m, 0m),
                    ("R-SYN-CHILD-B", "SYN-LIC-002", 36, 0m, 0m));
            result.LineItems.Should().OnlyContain(item =>
                item.StartDate == new DateOnly(2026, 11, 29)
                && item.EndDate == new DateOnly(2027, 11, 28));
            result.Validation.ComputedTotal.Should().Be(25m);
            result.Metadata.QuotedTotal.Should().Be(25m);
            result.Validation.Matches.Should().BeTrue();

            CrmWriter.Write(result.LineItems, outputPath, parser.CrmTemplate,
                new CrmWriterOptions(parser.Vendor.ToUpperInvariant()));
            using var workbook = new XLWorkbook(outputPath);
            var sheet = workbook.Worksheet(parser.CrmTemplate);
            sheet.Cell(4, 6).GetValue<int>().Should().Be(30);
            sheet.Cell(5, 6).GetValue<int>().Should().Be(36);
            foreach (var row in new[] { 4, 5 })
            {
                sheet.Cell(row, 20).GetValue<decimal>().Should().Be(0.0001m);
                sheet.Cell(row, 21).GetValue<decimal>().Should().Be(0.0001m);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Keeps_right_aligned_total_out_of_quantity_when_it_starts_before_its_header()
    {
        static PdfWord Word(string text, double left, double right, double top) =>
            new(text, left, right, top, top + 8, 0, 600);

        var header = Word("No", 10, 20, 100);
        PdfWord[] words =
        [
            header,
            Word("Product", 40, 70, 100),
            Word("Serial", 170, 195, 100),
            Word("Start", 255, 275, 100),
            Word("End", 305, 320, 100),
            Word("Adjusted", 348, 378, 100),
            Word("List", 380, 395, 100),
            Word("Discount", 410, 440, 100),
            Word("Net", 455, 468, 100),
            Word("Unit", 469, 482, 100),
            Word("Price", 483, 497, 100),
            Word("Qty", 498.39, 508.78, 100),
            Word("Total", 534.36, 548.71, 100),
            Word("240", 498.57, 508.59, 130),
            Word("USD", 520, 532, 130),
            Word("48,271.60", 534.28, 561, 130)
        ];

        var fused = PdfTableHelpers.FuseCurrencyTokens(words);
        var columns = NutanixRenewalPdfParser.BuildColumns(fused, header);
        var row = PdfTableHelpers.RowsBetween(fused, header.Top + 6, 0, columns).Single();

        row.Cells["Qty"].Should().Be("240");
        row.Cells["Total Net Price"].Should().Be("USD 48,271.60");
    }

    [Fact]
    public void Extracts_expected_line_items_and_totals()
    {
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100004.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100004");
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
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100007.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100007");
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
        // XQ-9100001 is a Renewal variant with an extra "Platform" column between No and
        // Product Code. Hardware rows carry a platform value (e.g. NX-8035N-G8-HY) which
        // must appear as Description "Platform: <value>"; software rows leave Description null.
        // Product Code wraps across two lines (RSW-NCI- / ULT-PR) and must be joined without
        // a separator. Totals must match: 5 rows, USD 392,190.58.
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100001.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100001");
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
        // XQ-9100011 is a Platform-column variant where the "Net Unit Price" header renders
        // with "Net" to the RIGHT of "Unit" and "Price" (reversed from XQ-9100001). The
        // original code used the "Net" word X0 as the column boundary, which placed it at
        // 471.7 — right of the actual amount data at 460.4, causing a FormatException.
        var root = TestSample.Root;
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == "nutanix_renewal_pdf");

        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "XQ-9100011.pdf"));

        result.Metadata.QuoteNumber.Should().Be("XQ-9100011");
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

}
