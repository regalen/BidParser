using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using BidParser.Parsing.Juniper.CpqXlsx;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class JuniperCpqXlsxParserTests
{
    private readonly JuniperCpqXlsxParser _parser = new();

    [Fact]
    public void Parser_exposes_expected_identity_and_capabilities()
    {
        _parser.Slug.Should().Be(ParserSlugs.JuniperCpqXlsx);
        _parser.DisplayName.Should().Be("CPQ (XLSX)");
        _parser.Vendor.Should().Be(Vendors.Juniper);
        ((IParser)_parser).OutputVendorName.Should().Be(Vendors.Juniper);
        _parser.AcceptedMime.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        _parser.CrmTemplate.Should().Be(CrmTemplates.ForeignUplift);
        _parser.AvailableTemplates.Should().Equal(CrmTemplates.ForeignUplift, CrmTemplates.ForeignNoCalculation);
        _parser.TermRendersAsComment.Should().BeTrue();
        ((IParser)_parser).SupportsOnCost.Should().BeFalse();
        ((IParser)_parser).SupportsSolutionIdSplit.Should().BeFalse();
        ((IParser)_parser).SupportsSubComponentDetail.Should().BeFalse();
    }

    [Fact]
    public void Parses_synthetic_cpq_fixture_and_excludes_structural_option_summary()
    {
        var result = _parser.Parse(TestSample.Path("Juniper_CPQ_900001.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("CPQ-900001");
        result.Metadata.BidNumber.Should().Be("CPQ-900001");
        result.Metadata.BidRevision.Should().Be("1");
        result.Metadata.Supplier.Should().Be(Vendors.Juniper);
        result.Metadata.Currency.Should().Be("USD");
        result.Metadata.QuotedTotal.Should().Be(52200.00m);
        result.Metadata.SourceFilename.Should().Be("Juniper_CPQ_900001.xlsx");
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.JuniperCpqXlsx);

        result.Validation.ComputedTotal.Should().Be(52200.00m);
        result.Validation.QuotedTotal.Should().Be(52200.00m);
        result.Validation.Matches.Should().BeTrue();
        result.Validation.Difference.Should().Be(0m);
        result.Validation.Warnings.Should().BeEmpty();

        result.LineItems.Should().HaveCount(9);
        result.LineItems.Should().OnlyContain(item =>
            item.Term == null
            && item.LineSequence == null
            && item.Comments == null
            && item.SerialNumber == null
            && item.StartDate == null
            && item.EndDate == null
            && item.SolutionId == null);

        result.LineItems.Select(item => (item.Vpn, item.Qty, item.Cost, item.Msrp)).Should().Equal(
            ("EX4400-48MP", 8, 3250.00m, 6500.00m),
            ("S-EX-P-C3-5-SD", 8, 1200.00m, 2400.00m),
            ("S-EX-FBT-P", 8, 750.00m, 1500.00m),
            ("CBL-EX-PWR-C13-C14", 12, 25.00m, 50.00m),
            ("JPSU-1600-C-AC-AFO", 4, 625.00m, 1250.00m),
            ("EX4400-EM-4Y", 4, 900.00m, 1800.00m),
            ("SFPP-10G-SR-C", 8, 375.00m, 750.00m),
            ("JNP-100G-DAC-3M", 2, 225.00m, 450.00m),
            ("JNP-100G-DAC-1M", 6, 125.00m, 250.00m));

        result.LineItems[0].Description.Should().Be(
            "12x1/2.5/5/10G, 36x1/2.5G Multigigabit switch with 2x100G uplink/stacking ports. 90W POE. MACsec AES256. Optional extension module:4x10G or 4x25G");
        result.LineItems[0].Raw.Should().ContainKey("SKU")
            .And.ContainKey("Description")
            .And.ContainKey("List Price Per")
            .And.ContainKey("Quantity")
            .And.ContainKey("Extended Net Price");
    }

    [Fact]
    public void Excludes_structural_summaries_by_content_and_preserves_duplicate_product_skus()
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 5, startCol: 1);
            // Product line 1 with SKU-DUP
            WriteRow(sheet, 6, 1, "SKU-DUP", "First switch line", "", "1000", "2", "1800");
            // Structural option summary in the middle of the table with a different SKU
            WriteRow(sheet, 7, 1, "BUNDLE-OPT-1", "", "", "", "4", "4200");
            // Product line 2 with the same SKU as the structural summary
            WriteRow(sheet, 8, 1, "BUNDLE-OPT-1", "Bundle base hardware", "36", "500", "4", "1600");
            // Product line 3 repeating SKU-DUP
            WriteRow(sheet, 9, 1, "SKU-DUP", "Second switch line", "12", "1000", "2", "1600");
            // Totals row
            sheet.Cell(10, 5).Value = "Totals:";
            sheet.Cell(10, 6).Value = "5000";
        });

        var result = _parser.Parse(temp.FilePath);

        result.Validation.Matches.Should().BeTrue();
        result.Validation.ComputedTotal.Should().Be(5000m);
        result.LineItems.Should().HaveCount(3);
        result.LineItems.Select(i => (i.Vpn, i.Description, i.Qty, i.Cost, i.Msrp, i.Term)).Should().Equal(
            ("SKU-DUP", "First switch line", 2, 900m, 1000m, null),
            ("BUNDLE-OPT-1", "Bundle base hardware", 4, 400m, 500m, 36),
            ("SKU-DUP", "Second switch line", 2, 800m, 1000m, 12));
    }

    [Fact]
    public void Extracts_anchor_based_table_when_sheet_is_renamed_shifted_and_columns_are_reordered()
    {
        using var temp = CreateWorkbook(workbook =>
        {
            var cover = workbook.AddWorksheet("Cover Notes");
            cover.Cell(2, 2).Value = "Overview sheet with no CPQ table";

            var quoteSheet = workbook.AddWorksheet("Custom Renamed BOM");
            quoteSheet.Cell(3, 4).Value = "Surrounding banner text before metadata";
            quoteSheet.Cell(6, 5).Value = "Transaction #:";
            quoteSheet.Cell(6, 7).Value = "CPQ-SHIFTED-77";

            // Reordered columns starting at column C (3) on row 14
            quoteSheet.Cell(14, 3).Value = "Quantity";
            quoteSheet.Cell(14, 4).Value = "Extended Net Price";
            quoteSheet.Cell(14, 5).Value = "SKU";
            quoteSheet.Cell(14, 6).Value = "Service Duration";
            quoteSheet.Cell(14, 7).Value = "List Price Per";
            quoteSheet.Cell(14, 8).Value = "Description";

            // Product row 1 (with non-terminating/unrounded unit price: 100 / 4 = 25.00, and 100.00 / 3 = 33.3333...)
            quoteSheet.Cell(16, 3).Value = "3";
            quoteSheet.Cell(16, 4).Value = "100.00";
            quoteSheet.Cell(16, 5).Value = "EX-SHIFT-01";
            quoteSheet.Cell(16, 6).Value = "24";
            quoteSheet.Cell(16, 7).Value = "50.00";
            quoteSheet.Cell(16, 8).Value = "Shifted switch item";

            // Genuine zero price row
            quoteSheet.Cell(17, 3).Value = "1";
            quoteSheet.Cell(17, 4).Value = "0.00";
            quoteSheet.Cell(17, 5).Value = "EX-FREE-01";
            quoteSheet.Cell(17, 6).Value = "";
            quoteSheet.Cell(17, 7).Value = "0.00";
            quoteSheet.Cell(17, 8).Value = "Included accessory";

            // Totals row anchored in column C, reading Extended Net Price from mapped column D (4)
            quoteSheet.Cell(19, 3).Value = "Totals:";
            quoteSheet.Cell(19, 4).Value = "100.00";
            quoteSheet.Cell(21, 5).Value = "Trailing footer text after Totals:";
        });

        _parser.Detect(temp.FilePath).Should().Be(0.9);
        var result = _parser.Parse(temp.FilePath);

        result.Metadata.BidNumber.Should().Be("CPQ-SHIFTED-77");
        result.Metadata.BidRevision.Should().Be("1");
        result.Metadata.QuoteNumber.Should().Be("CPQ-SHIFTED-77");
        result.Validation.Matches.Should().BeTrue();
        result.Validation.ComputedTotal.Should().Be(100.00m);
        result.LineItems.Should().HaveCount(2);
        result.LineItems[0].Vpn.Should().Be("EX-SHIFT-01");
        result.LineItems[0].Term.Should().Be(24);
        result.LineItems[0].Cost.Should().Be(100.00m / 3m);
        result.LineItems[1].Vpn.Should().Be("EX-FREE-01");
        result.LineItems[1].Cost.Should().Be(0m);
        result.LineItems[1].Msrp.Should().Be(0m);
    }

    [Fact]
    public void Missing_transaction_number_degrades_to_null_bid_metadata_and_filename_quote_number()
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 2, startCol: 1);
            WriteRow(sheet, 3, 1, "EX4400-48MP", "Switch line", "", "1000", "1", "800");
            sheet.Cell(4, 5).Value = "Totals:";
            sheet.Cell(4, 6).Value = "800";
        });

        var result = _parser.Parse(temp.FilePath);

        result.Metadata.BidNumber.Should().BeNull();
        result.Metadata.BidRevision.Should().BeNull();
        result.Metadata.QuoteNumber.Should().Be(Path.GetFileNameWithoutExtension(temp.FilePath));
        result.LineItems.Should().ContainSingle();
    }

    [Fact]
    public void Missing_totals_row_throws_extract_parse_error()
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 2, startCol: 1);
            WriteRow(sheet, 3, 1, "EX4400-48MP", "Switch line", "", "1000", "1", "800");
        });

        var act = () => _parser.Parse(temp.FilePath);

        act.Should().Throw<ParseError>()
            .Where(error => error.Stage == "extract");
    }

    [Theory]
    [InlineData("", "Valid description", "", "100", "1", "80")]
    [InlineData("EX-1", "", "", "100", "1", "80")]
    [InlineData("EX-1", "Valid description", "", "", "1", "80")]
    [InlineData("EX-1", "Valid description", "", "100", "0", "80")]
    [InlineData("EX-1", "Valid description", "", "100", "-2", "80")]
    [InlineData("EX-1", "Valid description", "", "100", "1.5", "80")]
    [InlineData("EX-1", "Valid description", "", "100", "1", "not-a-number")]
    [InlineData("EX-1", "Valid description", "0", "100", "1", "80")]
    [InlineData("EX-1", "Valid description", "12.5", "100", "1", "80")]
    public void Malformed_product_row_throws_extract_parse_error(
        string sku,
        string description,
        string duration,
        string listPrice,
        string qty,
        string extendedNet)
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 2, startCol: 1);
            WriteRow(sheet, 3, 1, sku, description, duration, listPrice, qty, extendedNet);
            sheet.Cell(4, 5).Value = "Totals:";
            sheet.Cell(4, 6).Value = "80";
        });

        var act = () => _parser.Parse(temp.FilePath);

        act.Should().Throw<ParseError>()
            .Where(error => error.Stage == "extract");
    }

    [Fact]
    public void Missing_required_header_throws_detect_parse_error()
    {
        using var temp = CreateWorkbook(sheet =>
        {
            sheet.Cell(2, 1).Value = "SKU";
            sheet.Cell(2, 2).Value = "Description";
            sheet.Cell(2, 3).Value = "Service Duration";
            sheet.Cell(2, 4).Value = "Quantity";
            sheet.Cell(2, 5).Value = "Extended Net Price";
        });

        _parser.Detect(temp.FilePath).Should().Be(0.0);
        var act = () => _parser.Parse(temp.FilePath);

        act.Should().Throw<ParseError>()
            .Where(error => error.Stage == "detect");
    }

    private static void WriteStandardHeader(IXLWorksheet sheet, int headerRow, int startCol)
    {
        sheet.Cell(headerRow, startCol).Value = "SKU";
        sheet.Cell(headerRow, startCol + 1).Value = "Description";
        sheet.Cell(headerRow, startCol + 2).Value = "Service Duration";
        sheet.Cell(headerRow, startCol + 3).Value = "List Price Per";
        sheet.Cell(headerRow, startCol + 4).Value = "Quantity";
        sheet.Cell(headerRow, startCol + 5).Value = "Extended Net Price";
    }

    private static void WriteRow(
        IXLWorksheet sheet,
        int row,
        int startCol,
        string sku,
        string description,
        string duration,
        string listPrice,
        string qty,
        string extendedNet)
    {
        sheet.Cell(row, startCol).Value = sku;
        sheet.Cell(row, startCol + 1).Value = description;
        sheet.Cell(row, startCol + 2).Value = duration;
        sheet.Cell(row, startCol + 3).Value = listPrice;
        sheet.Cell(row, startCol + 4).Value = qty;
        sheet.Cell(row, startCol + 5).Value = extendedNet;
    }

    private static TempWorkbook CreateWorkbook(Action<IXLWorksheet> configure) =>
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            configure(sheet);
        });

    private static TempWorkbook CreateWorkbook(Action<XLWorkbook> configure)
    {
        var path = Path.Combine(Path.GetTempPath(), $"juniper-cpq-test-{Guid.NewGuid():N}.xlsx");
        using (var workbook = new XLWorkbook())
        {
            configure(workbook);
            workbook.SaveAs(path);
        }

        return new TempWorkbook(path);
    }

    private sealed class TempWorkbook(string filePath) : IDisposable
    {
        public string FilePath { get; } = filePath;

        public void Dispose()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }
}
