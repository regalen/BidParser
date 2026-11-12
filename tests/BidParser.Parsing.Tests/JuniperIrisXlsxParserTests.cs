using BidParser.Application.Output;
using BidParser.Application.Parsing;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using BidParser.Parsing.Juniper.IrisXlsx;
using BidParser.Parsing.Registry;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class JuniperIrisXlsxParserTests
{
    private readonly JuniperIrisXlsxParser parser = new();
    private static string Fixture => TestSample.Path("Juniper_Iris_Synthetic.xlsx");

    [Fact]
    public void Parses_msrp_without_inventing_cost_or_bid_metadata()
    {
        var result = parser.Parse(Fixture);
        result.LineItems.Select(i => (i.Vpn, i.Qty, i.Msrp, i.Cost)).Should().Equal(
            ("S-SRX340-A1-1", 2, 410m, 0m), ("S-SRX300-A1-1", 1, 280m, 0m));
        result.LineItems[0].Raw["Unit Price"].Should().Be("410");
        result.Metadata.BidNumber.Should().BeNull();
        result.Metadata.BidRevision.Should().BeNull();
        result.Metadata.Currency.Should().Be("USD");
        result.Validation.ComputedTotal.Should().Be(1100m);
        result.Validation.QuotedTotal.Should().Be(1100m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void Anchors_survive_shifted_reordered_columns_and_blank_rows()
    {
        using var source = new XLWorkbook(Fixture);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Different name");
        var original = source.Worksheet(1);
        // Reverse all source columns and shift the whole table, including total label/value.
        for (var row = 1; row <= 4; row++)
        for (var col = 1; col <= 10; col++)
            sheet.Cell(row + 5, 15 - col).Value = original.Cell(row, col).Value;
        sheet.Row(7).InsertRowsAbove(1);
        sheet.Cell(1, 1).Value = "Unrelated heading";
        WithWorkbook(workbook, path => parser.Parse(path).Validation.ComputedTotal.Should().Be(1100m));
    }

    [Theory]
    [InlineData("Manufacturer", "Cisco", "extract")]
    [InlineData("Price List", "Juniper Price List (AUD)", "currency")]
    [InlineData("Discount", "5", "extract")]
    [InlineData("Discount", "-1", "extract")]
    [InlineData("Quantity", "1.5", "extract")]
    [InlineData("Quantity", "0", "extract")]
    [InlineData("Unit Price", "-1", "extract")]
    [InlineData("Unit Price", "bad", "extract")]
    [InlineData("Description", "", "extract")]
    [InlineData("Part Number", "", "extract")]
    public void Rejects_invalid_source_rows(string header, string value, string stage)
    {
        using var wb = new XLWorkbook(Fixture);
        var cell = wb.Worksheet(1).Row(1).CellsUsed().Single(c => c.GetString() == header);
        wb.Worksheet(1).Cell(2, cell.Address.ColumnNumber).Value = value;
        WithWorkbook(wb, path => ((Action)(() => parser.Parse(path))).Should().Throw<ParseError>()
            .Which.Stage.Should().Be(stage));
    }

    [Theory]
    [InlineData(1100.01, true)]
    [InlineData(1100.02, false)]
    public void Validates_msrp_against_quote_total_with_cent_tolerance(double total, bool matches)
    {
        using var wb = new XLWorkbook(Fixture);
        wb.Worksheet(1).Cell(4, 9).Value = total;
        WithWorkbook(wb, path => parser.Parse(path).Validation.Matches.Should().Be(matches));
    }

    [Theory]
    [InlineData(true, "detect")]
    [InlineData(false, "extract")]
    public void Missing_header_or_total_has_correct_error_stage(bool header, string stage)
    {
        using var wb = new XLWorkbook(Fixture);
        wb.Worksheet(1).Cell(header ? 1 : 4, header ? 2 : 3).Clear();
        WithWorkbook(wb, path => ((Action)(() => parser.Parse(path))).Should().Throw<ParseError>()
            .Which.Stage.Should().Be(stage));
    }

    [Fact]
    public async Task Writer_populates_foreign_cost_from_im_and_preserves_msrp_validation()
    {
        var quote = await new QuoteParseService(new ParserRegistry(), new SourceFormatInspector())
            .ParseAsync(new QuoteParseRequest(Fixture, Vendors.Juniper, parser.Slug));
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try
        {
            var output = await new WorkbookWriteService().WriteAsync(
                new WorkbookWriteRequest(quote, path, Margin: 5m, FxRate: 1.5m, ImPercent: 30.25m));
            output.CrmTemplate.Should().Be(CrmTemplates.ForeignUplift);
            output.OutputFilename.Should().Be("Juniper_Iris_Synthetic_ForeignUplift.xlsx");
            using var wb = new XLWorkbook(path);
            var sheet = wb.Worksheet(CrmTemplates.ForeignUplift);
            sheet.Cell(3, 21).GetDouble().Should().Be(410);
            sheet.Cell(3, 20).GetDouble().Should().Be(285.98);
            sheet.Cell(4, 20).GetDouble().Should().Be(195.30);
            sheet.Cell(3, 22).GetDouble().Should().Be(1.5);
            sheet.Cell(3, 11).GetDouble().Should().Be(5);
            sheet.Cell(3, 19).GetString().Should().Be("USD");
            foreach (var col in new[] { 8, 9, 24 }) sheet.Cell(3, col).IsEmpty().Should().BeTrue();
            sheet.Cell(5, 2).GetString().Should().Be("*");
            quote.Result.LineItems.Select(item => item.Cost).Should().OnlyContain(cost => cost == 0m);
            quote.Result.Validation.ComputedTotal.Should().Be(1100m);
            quote.Result.Validation.Matches.Should().BeTrue();
            WorkbookComparer.AssertEqual(path, Path.Combine(TestSample.Root, "samples", "outputs",
                "Juniper_Iris_Synthetic_ForeignUplift.xlsx"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("-1", false)]
    [InlineData("1.234", false)]
    [InlineData("0", true)]
    [InlineData("130.25", true)]
    public async Task Shared_writer_enforces_im_precision_and_allows_values_over_100(string? input, bool valid)
    {
        var service = new QuoteParseService(new ParserRegistry(), new SourceFormatInspector());
        var quote = await service.ParseAsync(new QuoteParseRequest(Fixture, Vendors.Juniper, parser.Slug));
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try
        {
            var writer = new WorkbookWriteService();
            var request = new WorkbookWriteRequest(quote, path, ImPercent: input is null ? null : decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture));
            if (valid)
            {
                var output = await writer.WriteAsync(request);
                output.OutputFilename.Should().Be("Juniper_Iris_Synthetic_ForeignUplift.xlsx");
            }
            else await ((Func<Task>)(async () => await writer.WriteAsync(request))).Should().ThrowAsync<ParseInputException>();
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("427", "15", "362.95")]
    [InlineData("427", "27.53", "309.45")]
    [InlineData("1", "12.5", "0.88")]
    [InlineData("427", "0", "427")]
    [InlineData("427", "100", "0.0001")]
    [InlineData("0", "15", "0.0001")]
    [InlineData("427", "130.25", "-129.17")]
    public async Task Output_discounts_each_unit_rounds_to_cents_and_uses_zero_sentinel(
        string msrp, string discount, string expected)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var quote = await new QuoteParseService(new ParserRegistry(), new SourceFormatInspector())
            .ParseAsync(new QuoteParseRequest(Fixture, Vendors.Juniper, parser.Slug));
        quote = quote with
        {
            Result = quote.Result with
            {
                LineItems = [quote.Result.LineItems[0] with { Msrp = decimal.Parse(msrp, culture) }]
            }
        };
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try
        {
            await new WorkbookWriteService().WriteAsync(new WorkbookWriteRequest(
                quote, path, ImPercent: decimal.Parse(discount, culture)));
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(CrmTemplates.ForeignUplift);
            sheet.Cell(3, 20).GetValue<decimal>().Should().Be(decimal.Parse(expected, culture));
            sheet.Cell(3, 6).GetDouble().Should().Be(2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Rewriting_with_a_different_im_recalculates_cost_and_rejects_old_template()
    {
        var quote = await new QuoteParseService(new ParserRegistry(), new SourceFormatInspector())
            .ParseAsync(new QuoteParseRequest(Fixture, Vendors.Juniper, parser.Slug));
        var writer = new WorkbookWriteService();
        ((Action)(() => writer.Describe(quote, CrmTemplates.ForeignPercentOffWithUplift, false)))
            .Should().Throw<ParseInputException>();
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try
        {
            foreach (var (discount, expected) in new[] { (15m, 348.50m), (27.53m, 297.13m) })
            {
                await writer.WriteAsync(new WorkbookWriteRequest(quote, path, ImPercent: discount));
                using var workbook = new XLWorkbook(path);
                workbook.Worksheet(CrmTemplates.ForeignUplift).Cell(3, 20).GetValue<decimal>()
                    .Should().Be(expected);
            }
            quote.Result.LineItems[0].Cost.Should().Be(0m);
            quote.Result.Validation.ComputedTotal.Should().Be(1100m);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(ParserSlugs.JuniperCpqXlsx, "Juniper_CPQ_900001.xlsx")]
    [InlineData(ParserSlugs.JuniperIndirectXlsx, "Juniper_Indirect_900001.xlsx")]
    public async Task Removed_foreign_no_calculation_template_is_rejected(string slug, string filename)
    {
        var service = new QuoteParseService(new ParserRegistry(), new SourceFormatInspector());
        var quote = await service.ParseAsync(new QuoteParseRequest(TestSample.Path(filename), Vendors.Juniper, slug));
        var writer = new WorkbookWriteService();
        ((Action)(() => writer.Describe(quote, CrmTemplates.ForeignNoCalculation, false)))
            .Should().Throw<ParseInputException>();
    }

    [Fact]
    public void Duplicate_parts_are_preserved_in_source_order()
    {
        using var workbook = new XLWorkbook(Fixture);
        workbook.Worksheet(1).Cell(3, 2).Value = "S-SRX340-A1-1";
        WithWorkbook(workbook, path => parser.Parse(path).LineItems.Select(item => item.Vpn)
            .Should().Equal("S-SRX340-A1-1", "S-SRX340-A1-1"));
    }

    [Fact]
    public void Percent_formatted_zero_discount_is_supported()
    {
        using var workbook = new XLWorkbook(Fixture);
        workbook.Worksheet(1).Cell(2, 8).Style.NumberFormat.Format = "0%";
        WithWorkbook(workbook, path => parser.Parse(path).Validation.Matches.Should().BeTrue());
    }

    private static void WithWorkbook(XLWorkbook workbook, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try { workbook.SaveAs(path); action(path); }
        finally { File.Delete(path); }
    }
}
