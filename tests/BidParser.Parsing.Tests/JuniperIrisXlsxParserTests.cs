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
            var linePricing = quote.Result.LineItems
                .Select((item, idx) => new LinePricingEntry(idx, item.Vpn, Margin: 5m, ImPercent: 30.25m))
                .ToList();
            var output = await new WorkbookWriteService().WriteAsync(
                new WorkbookWriteRequest(quote, path, FxRate: 1.5m, LinePricing: linePricing));
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
    [InlineData("100", true)]
    [InlineData("100.01", false)]
    [InlineData("130.25", false)]
    public async Task Shared_writer_enforces_im_precision_and_range_0_to_100(string? input, bool valid)
    {
        var service = new QuoteParseService(new ParserRegistry(), new SourceFormatInspector());
        var quote = await service.ParseAsync(new QuoteParseRequest(Fixture, Vendors.Juniper, parser.Slug));
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try
        {
            var writer = new WorkbookWriteService();
            decimal? im = input is null ? null : decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
            var linePricing = quote.Result.LineItems
                .Select((item, idx) => new LinePricingEntry(idx, item.Vpn, Margin: 5m, ImPercent: im))
                .ToList();
            var request = new WorkbookWriteRequest(quote, path, LinePricing: linePricing);
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
    public async Task Output_discounts_each_unit_rounds_to_cents_and_uses_zero_sentinel(
        string msrp, string discount, string expected)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var quote = await new QuoteParseService(new ParserRegistry(), new SourceFormatInspector())
            .ParseAsync(new QuoteParseRequest(Fixture, Vendors.Juniper, parser.Slug));
        var item0 = quote.Result.LineItems[0] with { Msrp = decimal.Parse(msrp, culture) };
        quote = quote with
        {
            Result = quote.Result with
            {
                LineItems = [item0]
            }
        };
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try
        {
            var im = decimal.Parse(discount, culture);
            await new WorkbookWriteService().WriteAsync(new WorkbookWriteRequest(
                quote, path, LinePricing: [new LinePricingEntry(0, item0.Vpn, Margin: 5m, ImPercent: im)]));
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
                var linePricing = quote.Result.LineItems
                    .Select((item, idx) => new LinePricingEntry(idx, item.Vpn, Margin: 5m, ImPercent: discount))
                    .ToList();
                await writer.WriteAsync(new WorkbookWriteRequest(quote, path, LinePricing: linePricing));
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

    [Fact]
    public void Fixture_line_numbers_become_parent_sequences()
    {
        parser.Parse(Fixture).LineItems.Select(item => item.LineSequence).Should().Equal("1", "2");
    }

    [Fact]
    public void Included_components_are_zero_price_lines_and_line_numbers_flatten_to_two_levels()
    {
        using var workbook = BundledWorkbook();
        WithWorkbook(workbook, path =>
        {
            var result = parser.Parse(path);
            result.LineItems.Select(item => (item.LineSequence, item.Vpn, item.Qty, item.Msrp, item.Cost)).Should().Equal(
                ("1", "SYN-AP-01", 2, 100m, 0m),
                ("1.01", "SYN-BRKT-01", 2, 0m, 0m),      // 1.1   incl.
                ("1.02", "SYN-SUB-01", 2, 50m, 0m),      // 1.2   priced child
                ("1.03", "SYN-SUB-BND-A", 2, 0m, 0m),    // 1.2.1 incl., third level flattened
                ("1.04", "SYN-SUB-BND-B", 2, 0m, 0m),    // 1.2.2 "INCL" with a stray price list
                ("2", "SYN-RTR-01", 1, 10m, 0m),
                ("2.01", "SYN-CBL-01", 1, 0m, 0m),       // 2.1   blank Unit Price and Price List
                ("2.02", "SYN-SVC-01", 1, 5m, 0m));      // 2.1.1 priced third level
            result.LineItems[1].Raw["Unit Price"].Should().Be("incl.");
            result.Validation.ComputedTotal.Should().Be(315m);
            result.Validation.QuotedTotal.Should().Be(315m);
            result.Validation.Matches.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(new[] { "1.0", "1.1", "2.1", "2.2" }, new[] { "1", "1.01", "2", "2.01" })]
    [InlineData(new[] { "1", "1.1", "1.1.1", "1.2", "2" }, new[] { "1", "1.01", "1.02", "1.03", "2" })]
    [InlineData(new[] { "1.0", "", "1.1" }, new[] { "1", "2", "3" })]
    [InlineData(new[] { "01.0", "1.1", "1.0.0" }, new[] { "1", "1.01", "2" })]
    public void Line_number_edge_cases_never_attach_a_child_to_the_wrong_parent(string[] lineNumbers, string[] expected)
    {
        using var workbook = PricedLinesWorkbook(lineNumbers);
        WithWorkbook(workbook, path => parser.Parse(path).LineItems.Select(item => item.LineSequence)
            .Should().Equal(expected));
    }

    [Fact]
    public void Ten_or_more_children_keep_two_digit_ordering()
    {
        var lineNumbers = new[] { "1.0" }.Concat(Enumerable.Range(1, 10).Select(i => $"1.{i}")).ToArray();
        using var workbook = PricedLinesWorkbook(lineNumbers);
        WithWorkbook(workbook, path => parser.Parse(path).LineItems.Select(item => item.LineSequence)
            .Should().EndWith(["1.09", "1.10"]));
    }

    [Fact]
    public void Blank_unit_price_with_a_price_list_is_still_rejected()
    {
        using var workbook = new XLWorkbook(Fixture);
        workbook.Worksheet(1).Cell(2, 5).Clear();
        WithWorkbook(workbook, path => ((Action)(() => parser.Parse(path))).Should().Throw<ParseError>()
            .Which.Stage.Should().Be("extract"));
    }

    [Fact]
    public async Task Writer_emits_included_lines_with_sentinel_prices_and_needs_no_im_for_them()
    {
        using var source = BundledWorkbook();
        var input = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        var output = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try
        {
            source.SaveAs(input);
            var quote = await new QuoteParseService(new ParserRegistry(), new SourceFormatInspector())
                .ParseAsync(new QuoteParseRequest(input, Vendors.Juniper, parser.Slug));
            var linePricing = quote.Result.LineItems
                .Select((item, idx) => new LinePricingEntry(idx, item.Vpn, Margin: 5m,
                    ImPercent: item.Msrp is > 0m ? 20m : null))
                .ToList();
            await new WorkbookWriteService().WriteAsync(
                new WorkbookWriteRequest(quote, output, FxRate: 1.5m, LinePricing: linePricing));

            using var workbook = new XLWorkbook(output);
            var sheet = workbook.Worksheet(CrmTemplates.ForeignUplift);
            Enumerable.Range(3, 8).Select(row => sheet.Cell(row, 1).GetString())
                .Should().Equal("1", "1.01", "1.02", "1.03", "1.04", "2", "2.01", "2.02");
            foreach (var row in new[] { 4, 6, 7, 9 })
            {
                sheet.Cell(row, 20).GetValue<decimal>().Should().Be(0.0001m); // TemplateLayout.ZeroPriceSentinel
                sheet.Cell(row, 21).GetValue<decimal>().Should().Be(0.0001m); // TemplateLayout.ZeroPriceSentinel
            }
            sheet.Cell(3, 20).GetValue<decimal>().Should().Be(80m);
            sheet.Cell(3, 21).GetValue<decimal>().Should().Be(100m);
            sheet.Cell(5, 20).GetValue<decimal>().Should().Be(40m);
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    /// <summary>One priced synthetic line (10 USD × 1) per Line#, plus a matching Quote Total.</summary>
    private static XLWorkbook PricedLinesWorkbook(IReadOnlyList<string> lineNumbers)
        => IrisWorkbook(
            lineNumbers.Select((line, i) => new object?[]
            {
                "01 - Group", line, $"SYN-PART-{i:D2}", "Synthetic part", "Juniper Networks",
                10, 1, 10, 0, 10, "Juniper Price List for APAC (USD)"
            }).ToArray(),
            10 * lineNumbers.Count);

    /// <summary>
    /// Fully synthetic Iris BOM with included components at the second and third level, a priced
    /// third-level row, an "incl." row with a stray price list, and a row with neither a Unit
    /// Price nor a Price List. Quote Total counts priced rows only: 200 + 100 + 10 + 5 = 315.
    /// </summary>
    private static XLWorkbook BundledWorkbook()
    {
        const string usd = "Juniper Price List for APAC (USD)";
        object?[][] rows =
        [
            ["01 - Group A", "1.0", "SYN-AP-01", "Synthetic access point", "Juniper Networks", 100, 2, 200, 0, 200, usd],
            ["01 - Group A", "1.1", "SYN-BRKT-01", "INCLUDED: Synthetic bracket", "Juniper Networks", "incl.", 2, null, null, null, null],
            ["01 - Group A", "1.2", "SYN-SUB-01", "Synthetic subscription", "Juniper Networks", 50, 2, 100, 0, 100, usd],
            ["01 - Group A", "1.2.1", "SYN-SUB-BND-A", "INCLUDED: Synthetic bundled service A", "Juniper Networks", "incl.", 2, null, null, null, null],
            ["01 - Group A", "1.2.2", "SYN-SUB-BND-B", "INCLUDED: Synthetic bundled service B", "Juniper Networks", "INCL", 2, null, null, null, usd],
            ["02 - Group B", "2.0", "SYN-RTR-01", "Synthetic router", "Juniper Networks", 10, 1, 10, 0, 10, usd],
            ["02 - Group B", "2.1", "SYN-CBL-01", "INCLUDED: Synthetic cable", "Juniper Networks", null, 1, null, null, null, null],
            ["02 - Group B", "2.1.1", "SYN-SVC-01", "Synthetic support", "Juniper Networks", 5, 1, 5, 0, 5, usd],
        ];
        return IrisWorkbook(rows, 315);
    }

    private static XLWorkbook IrisWorkbook(object?[][] rows, int quoteTotal)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        string[] headers = ["Design Group", "Line#", "Part Number", "Description", "Manufacturer",
            "Unit Price", "Quantity", "Sub Total", "Discount", "Total", "Price List"];
        for (var col = 0; col < headers.Length; col++) sheet.Cell(1, col + 1).Value = headers[col];
        for (var r = 0; r < rows.Length; r++)
        for (var c = 0; c < rows[r].Length; c++)
        {
            var value = rows[r][c];
            var cell = sheet.Cell(r + 2, c + 1);
            if (value is int number) cell.Value = number;
            else if (value is string text) cell.Value = text;
        }
        sheet.Cell(rows.Length + 2, 3).Value = "Quote Total";
        sheet.Cell(rows.Length + 2, 10).Value = quoteTotal;
        return workbook;
    }

    private static void WithWorkbook(XLWorkbook workbook, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"iris-{Guid.NewGuid():N}.xlsx");
        try { workbook.SaveAs(path); action(path); }
        finally { File.Delete(path); }
    }
}
