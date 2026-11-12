using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using BidParser.Parsing.Juniper.IndirectXlsx;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class JuniperIndirectXlsxParserTests
{
    private readonly JuniperIndirectXlsxParser _parser = new();

    [Fact]
    public void Parser_exposes_expected_identity_and_capabilities()
    {
        _parser.Slug.Should().Be(ParserSlugs.JuniperIndirectXlsx);
        _parser.DisplayName.Should().Be("Indirect (XLSX)");
        _parser.Vendor.Should().Be(Vendors.Juniper);
        ((IParser)_parser).OutputVendorName.Should().Be(Vendors.Juniper);
        _parser.AcceptedMime.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        _parser.CrmTemplate.Should().Be(CrmTemplates.ForeignUplift);
        _parser.AvailableTemplates.Should().Equal(CrmTemplates.ForeignUplift);
        _parser.TermRendersAsComment.Should().BeTrue();
        ((IParser)_parser).SupportsOnCost.Should().BeFalse();
        ((IParser)_parser).SupportsSolutionIdSplit.Should().BeFalse();
        ((IParser)_parser).SupportsSubComponentDetail.Should().BeFalse();
    }

    [Fact]
    public void Parses_synthetic_indirect_fixture_with_repeated_skus_and_subscription_terms()
    {
        var result = _parser.Parse(TestSample.Path("Juniper_Indirect_900001.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("90000101");
        result.Metadata.BidNumber.Should().Be("90000101");
        result.Metadata.BidRevision.Should().Be("2");
        result.Metadata.Supplier.Should().Be(Vendors.Juniper);
        result.Metadata.Currency.Should().Be("USD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.SourceFilename.Should().Be("Juniper_Indirect_900001.xlsx");
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.JuniperIndirectXlsx);

        result.Validation.ComputedTotal.Should().Be(2105.00m);
        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.Matches.Should().BeTrue();
        result.Validation.Difference.Should().Be(0m);
        result.Validation.Warnings.Should().BeEmpty();

        result.LineItems.Should().HaveCount(6);
        result.LineItems.Should().OnlyContain(item =>
            item.LineSequence == null
            && item.Comments == null
            && item.SerialNumber == null
            && item.StartDate == null
            && item.EndDate == null
            && item.SolutionId == null);

        result.LineItems.Select(item => (item.Vpn, item.Qty, item.Cost, item.Msrp, item.Term)).Should().Equal(
            ("AP27-WW", 1, 550.00m, 1100.00m, null),
            ("SUB-1S-1Y", 1, 80.00m, 160.00m, 12),
            ("AP27-WW", 1, 525.00m, 1100.00m, null),
            ("SUB-1S-3Y", 1, 180.00m, 360.00m, 36),
            ("AP27-WW", 1, 500.00m, 1100.00m, null),
            ("SUB-1S-5Y", 1, 270.00m, 540.00m, 60));

        result.LineItems[0].Description.Should().Be(
            "High Performance MultiGigabit Wi-Fi 7 Access Point (2x2:2) with Bluetooth Low Energy, with built-in Internal Antenna -- outside US only; Universal Mounting Bracket is included");
        result.LineItems[0].Raw.Should().ContainKey("SKU")
            .And.ContainKey("SKU DESCRIPTION")
            .And.ContainKey("QUANTITY")
            .And.ContainKey("UNIT NET PRICE")
            .And.ContainKey("LIST PRICE $")
            .And.ContainKey("EXTENDED NET TOTAL")
            .And.ContainKey("CURRENCY");
    }

    [Fact]
    public void Extracts_anchor_based_table_when_sheet_is_renamed_shifted_and_columns_are_reordered()
    {
        using var temp = CreateWorkbook(workbook =>
        {
            var notes = workbook.AddWorksheet("Intro");
            notes.Cell(1, 1).Value = "Summary sheet without an Indirect table";

            var sheet = workbook.AddWorksheet("Custom Indirect Export");
            sheet.Cell(2, 2).Value = "Pre-table banner";

            // Reordered headers starting at row 7, column D (4)
            sheet.Cell(7, 4).Value = "CURRENCY";
            sheet.Cell(7, 5).Value = "EXTENDED NET TOTAL";
            sheet.Cell(7, 6).Value = "SERVICE DURATION";
            sheet.Cell(7, 7).Value = "UNIT NET PRICE";
            sheet.Cell(7, 8).Value = "LIST PRICE $";
            sheet.Cell(7, 9).Value = "QUANTITY";
            sheet.Cell(7, 10).Value = "SKU DESCRIPTION";
            sheet.Cell(7, 11).Value = "SKU";
            sheet.Cell(7, 12).Value = "CONFIG INSTANCE";
            sheet.Cell(7, 13).Value = "SHIP AND DEBIT ID";

            // Row 1: priced subscription
            sheet.Cell(9, 4).Value = "USD";
            sheet.Cell(9, 5).Value = "360.00";
            sheet.Cell(9, 6).Value = "36";
            sheet.Cell(9, 7).Value = "180.00";
            sheet.Cell(9, 8).Value = "360.00";
            sheet.Cell(9, 9).Value = "2";
            sheet.Cell(9, 10).Value = "3-year cloud subscription";
            sheet.Cell(9, 11).Value = "SUB-SHIFT-3Y";
            sheet.Cell(9, 12).Value = "4";
            sheet.Cell(9, 13).Value = "SD-70001";

            // Row 2: genuine zero price line
            sheet.Cell(10, 4).Value = "USD";
            sheet.Cell(10, 5).Value = "0.00";
            sheet.Cell(10, 6).Value = "";
            sheet.Cell(10, 7).Value = "0.00";
            sheet.Cell(10, 8).Value = "0.00";
            sheet.Cell(10, 9).Value = "1";
            sheet.Cell(10, 10).Value = "Included mounting kit";
            sheet.Cell(10, 11).Value = "BRKT-FREE-01";
        });

        _parser.Detect(temp.FilePath).Should().Be(0.9);
        var result = _parser.Parse(temp.FilePath);

        result.Metadata.BidNumber.Should().Be("SD-70001");
        result.Metadata.BidRevision.Should().Be("4");
        result.Metadata.QuoteNumber.Should().Be("SD-70001");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Validation.Matches.Should().BeTrue();
        result.Validation.ComputedTotal.Should().Be(360.00m);
        result.LineItems.Should().HaveCount(2);
        result.LineItems[0].Term.Should().Be(36);
        result.LineItems[1].Cost.Should().Be(0m);
        result.LineItems[1].Msrp.Should().Be(0m);
    }

    [Theory]
    [InlineData("AUD")]
    [InlineData("EUR")]
    [InlineData("")]
    public void Non_usd_or_blank_currency_throws_currency_parse_error(string currency)
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 1, startCol: 1);
            WriteRow(sheet, 2, 1, "AP27-WW", "Access point", "1", "500.00", "1000.00", "500.00", "", currency);
        });

        var act = () => _parser.Parse(temp.FilePath);

        act.Should().Throw<ParseError>()
            .Where(error => error.Stage == "currency");
    }

    [Fact]
    public void Per_row_discrepancy_produces_saveable_validation_mismatch_with_line_warning()
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 1, startCol: 1);
            WriteRow(sheet, 2, 1, "AP27-WW", "Access point", "2", "500.00", "1000.00", "1000.00", "", "USD");
            // Line 2 has Cost * Qty = 180.00, but EXTENDED NET TOTAL = 150.00 (difference +30.00)
            WriteRow(sheet, 3, 1, "SUB-1S-3Y", "3Y Subscription", "1", "180.00", "360.00", "150.00", "36", "USD");
        });

        var result = _parser.Parse(temp.FilePath);

        result.LineItems.Should().HaveCount(2);
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.ComputedTotal.Should().Be(1180.00m);
        result.Validation.Matches.Should().BeFalse();
        result.Validation.Difference.Should().Be(30.00m);
        result.Validation.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Line 2 (SUB-1S-3Y)")
            .And.Contain("180.00")
            .And.Contain("150.00");
    }

    [Fact]
    public void Offsetting_row_discrepancies_still_produce_validation_mismatch_and_both_line_warnings()
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 1, startCol: 1);
            // Line 1: Cost * Qty = 510.00 vs EXTENDED NET TOTAL = 500.00 (+10.00)
            WriteRow(sheet, 2, 1, "AP27-WW", "Access point", "1", "510.00", "1100.00", "500.00", "", "USD");
            // Line 2: Cost * Qty = 70.00 vs EXTENDED NET TOTAL = 80.00 (-10.00)
            WriteRow(sheet, 3, 1, "SUB-1S-1Y", "1Y Subscription", "1", "70.00", "160.00", "80.00", "12", "USD");
        });

        var result = _parser.Parse(temp.FilePath);

        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.ComputedTotal.Should().Be(580.00m);
        result.Validation.Difference.Should().Be(0.00m);
        result.Validation.Matches.Should().BeFalse("opposing row discrepancies must not cancel into a pass");
        result.Validation.Warnings.Should().HaveCount(2);
        result.Validation.Warnings[0].Should().Contain("Line 1 (AP27-WW)");
        result.Validation.Warnings[1].Should().Contain("Line 2 (SUB-1S-1Y)");
    }

    [Fact]
    public void Missing_metadata_columns_degrade_to_null_bid_fields_without_failing_parse()
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 1, startCol: 1, includeMetadataColumns: false);
            WriteRow(sheet, 2, 1, "AP27-WW", "Access point", "1", "500.00", "1000.00", "500.00", "", "USD");
        });

        var result = _parser.Parse(temp.FilePath);

        result.Metadata.BidNumber.Should().BeNull();
        result.Metadata.BidRevision.Should().BeNull();
        result.Metadata.QuoteNumber.Should().Be(Path.GetFileNameWithoutExtension(temp.FilePath));
        result.Validation.Matches.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Valid description", "1", "100", "200", "100", "", "USD")]
    [InlineData("AP27-WW", "", "1", "100", "200", "100", "", "USD")]
    [InlineData("AP27-WW", "Valid description", "0", "100", "200", "100", "", "USD")]
    [InlineData("AP27-WW", "Valid description", "-1", "100", "200", "100", "", "USD")]
    [InlineData("AP27-WW", "Valid description", "1.5", "100", "200", "100", "", "USD")]
    [InlineData("AP27-WW", "Valid description", "1", "", "200", "100", "", "USD")]
    [InlineData("AP27-WW", "Valid description", "1", "100", "", "100", "", "USD")]
    [InlineData("AP27-WW", "Valid description", "1", "100", "200", "", "", "USD")]
    [InlineData("AP27-WW", "Valid description", "1", "100", "200", "100", "0", "USD")]
    [InlineData("AP27-WW", "Valid description", "1", "100", "200", "100", "12.5", "USD")]
    public void Malformed_product_row_throws_extract_parse_error(
        string sku,
        string description,
        string qty,
        string unitNet,
        string listPrice,
        string extendedNet,
        string duration,
        string currency)
    {
        using var temp = CreateWorkbook(sheet =>
        {
            WriteStandardHeader(sheet, headerRow: 1, startCol: 1);
            WriteRow(sheet, 2, 1, sku, description, qty, unitNet, listPrice, extendedNet, duration, currency);
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
            sheet.Cell(1, 1).Value = "SKU";
            sheet.Cell(1, 2).Value = "SKU DESCRIPTION";
            sheet.Cell(1, 3).Value = "QUANTITY";
            sheet.Cell(1, 4).Value = "UNIT NET PRICE";
            sheet.Cell(1, 5).Value = "LIST PRICE $";
            sheet.Cell(1, 6).Value = "SERVICE DURATION";
            sheet.Cell(1, 7).Value = "CURRENCY";
            // EXTENDED NET TOTAL omitted
        });

        _parser.Detect(temp.FilePath).Should().Be(0.0);
        var act = () => _parser.Parse(temp.FilePath);

        act.Should().Throw<ParseError>()
            .Where(error => error.Stage == "detect");
    }

    private static void WriteStandardHeader(IXLWorksheet sheet, int headerRow, int startCol, bool includeMetadataColumns = true)
    {
        sheet.Cell(headerRow, startCol).Value = "SKU";
        sheet.Cell(headerRow, startCol + 1).Value = "SKU DESCRIPTION";
        sheet.Cell(headerRow, startCol + 2).Value = "QUANTITY";
        sheet.Cell(headerRow, startCol + 3).Value = "UNIT NET PRICE";
        sheet.Cell(headerRow, startCol + 4).Value = "LIST PRICE $";
        sheet.Cell(headerRow, startCol + 5).Value = "EXTENDED NET TOTAL";
        sheet.Cell(headerRow, startCol + 6).Value = "SERVICE DURATION";
        sheet.Cell(headerRow, startCol + 7).Value = "CURRENCY";
        if (includeMetadataColumns)
        {
            sheet.Cell(headerRow, startCol + 8).Value = "SHIP AND DEBIT ID";
            sheet.Cell(headerRow, startCol + 9).Value = "CONFIG INSTANCE";
        }
    }

    private static void WriteRow(
        IXLWorksheet sheet,
        int row,
        int startCol,
        string sku,
        string description,
        string qty,
        string unitNet,
        string listPrice,
        string extendedNet,
        string duration,
        string currency,
        string shipAndDebitId = "90000101",
        string configInstance = "1")
    {
        sheet.Cell(row, startCol).Value = sku;
        sheet.Cell(row, startCol + 1).Value = description;
        sheet.Cell(row, startCol + 2).Value = qty;
        sheet.Cell(row, startCol + 3).Value = unitNet;
        sheet.Cell(row, startCol + 4).Value = listPrice;
        sheet.Cell(row, startCol + 5).Value = extendedNet;
        sheet.Cell(row, startCol + 6).Value = duration;
        sheet.Cell(row, startCol + 7).Value = currency;
        sheet.Cell(row, startCol + 8).Value = shipAndDebitId;
        sheet.Cell(row, startCol + 9).Value = configInstance;
    }

    private static TempWorkbook CreateWorkbook(Action<IXLWorksheet> configure) =>
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("JNPR Indirect Quotation");
            configure(sheet);
        });

    private static TempWorkbook CreateWorkbook(Action<XLWorkbook> configure)
    {
        var path = Path.Combine(Path.GetTempPath(), $"juniper-indirect-test-{Guid.NewGuid():N}.xlsx");
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
