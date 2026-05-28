using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Registry;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class HpOneConfigXlsxParserTests
{
    private static string RepoRoot()
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

    private static string SamplePath(string filename) =>
        Path.Combine(RepoRoot(), "samples", "inputs", filename);

    [Fact]
    public void Metadata_IsCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));

        result.Metadata.QuoteNumber.Should().Be("55648855");
        result.Metadata.Supplier.Should().Be(Vendors.Hp);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.HpOneConfigXlsx);
    }

    [Fact]
    public void Validation_MatchesWithNullTotal()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().BeNull();
    }

    [Fact]
    public void TotalItemCount_IsParentPlusThirtyChildren()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));

        result.LineItems.Should().HaveCount(31);
    }

    [Fact]
    public void Parent_HasCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));
        var parent = result.LineItems[0];

        parent.LineSequence.Should().Be("1");
        parent.Vpn.Should().Be("55648855");
        parent.Description.Should().Be("HP EliteBook 6 G2i 14 AI");
        parent.Qty.Should().Be(1);
        parent.Msrp.Should().Be(6042.77m);
        parent.Cost.Should().Be(0m);
    }

    [Fact]
    public void FirstChild_HasCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));
        var child = result.LineItems[1];

        child.LineSequence.Should().Be("1.01");
        child.Vpn.Should().Be("C6SL9AV");
        child.Qty.Should().Be(1);
        child.Msrp.Should().Be(0m);
        child.Cost.Should().Be(0m);
    }

    [Fact]
    public void LastChild_HasSequence_1_30()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));

        result.LineItems[^1].LineSequence.Should().Be("1.30");
    }

    [Fact]
    public void AllChildren_HaveZeroMsrpAndCost()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));

        result.LineItems.Skip(1).Should().OnlyContain(i => i.Msrp == 0m && i.Cost == 0m);
    }

    [Fact]
    public void LineSequences_AreCorrectlyFormatted()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        var result = parser.Parse(SamplePath("55648855.xlsx"));

        result.LineItems[0].LineSequence.Should().Be("1");
        result.LineItems[1].LineSequence.Should().Be("1.01");
        result.LineItems[9].LineSequence.Should().Be("1.09");
        result.LineItems[10].LineSequence.Should().Be("1.10");
        result.LineItems[30].LineSequence.Should().Be("1.30");
    }

    [Fact]
    public void CrmTemplate_IsPercentOffWithUplift()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);

        parser.CrmTemplate.Should().Be(CrmTemplates.PercentOffWithUplift);
        parser.AvailableTemplates.Should().ContainSingle().Which.Should().Be(CrmTemplates.PercentOffWithUplift);
    }

    [Fact]
    public void MultiConfig_Workbook_RaisesParseError()
    {
        // Build a synthetic OneConfig file with two "Config ID" headers in the same column —
        // the multi-config guard must reject this.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);
        using var temp = new TempFile();

        using (var wb = new XLWorkbook())
        {
            var sheet = wb.AddWorksheet("Sheet1");
            // First Config block
            sheet.Cell(1, 2).Value = "Config ID";
            sheet.Cell(1, 3).Value = "Config Name";
            sheet.Cell(1, 4).Value = "KMAT";
            sheet.Cell(1, 5).Value = "Total Price";
            sheet.Cell(2, 2).Value = "11111111";
            sheet.Cell(2, 3).Value = "First Config";
            sheet.Cell(2, 5).Value = "100.00";

            // Components header for first Config
            sheet.Cell(4, 1).Value = "Component Category";
            sheet.Cell(4, 2).Value = "Part Number";
            sheet.Cell(4, 3).Value = "Description";
            sheet.Cell(4, 4).Value = "Quantity";
            sheet.Cell(4, 5).Value = "Price";
            sheet.Cell(5, 2).Value = "PN1";
            sheet.Cell(5, 3).Value = "Component 1";
            sheet.Cell(5, 4).Value = "1";

            // Second Config block in the SAME column as the first — should trip the guard
            sheet.Cell(7, 2).Value = "Config ID";
            sheet.Cell(7, 3).Value = "Config Name";
            sheet.Cell(7, 5).Value = "Total Price";
            sheet.Cell(8, 2).Value = "22222222";
            sheet.Cell(8, 3).Value = "Second Config";
            sheet.Cell(8, 5).Value = "200.00";

            wb.SaveAs(temp.Path);
        }

        var act = () => parser.Parse(temp.Path);
        act.Should().Throw<ParseError>()
            .Where(e => e.Stage == "config")
            .WithMessage("*More than one Config ID*");
    }

    [Fact]
    public void DescriptionContainingConfigIdText_DoesNotTripGuard()
    {
        // A *component description* that happens to contain the literal text "Config ID"
        // must NOT trip the multi-config guard, because the guard is scoped to the same
        // column as the original "Config ID" header (col B in the standard layout).
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);
        using var temp = new TempFile();

        using (var wb = new XLWorkbook())
        {
            var sheet = wb.AddWorksheet("Sheet1");
            sheet.Cell(1, 2).Value = "Config ID";
            sheet.Cell(1, 3).Value = "Config Name";
            sheet.Cell(1, 4).Value = "KMAT";
            sheet.Cell(1, 5).Value = "Total Price";
            sheet.Cell(2, 2).Value = "33333333";
            sheet.Cell(2, 3).Value = "Sole Config";
            sheet.Cell(2, 5).Value = "10.00";

            sheet.Cell(4, 1).Value = "Component Category";
            sheet.Cell(4, 2).Value = "Part Number";
            sheet.Cell(4, 3).Value = "Description";
            sheet.Cell(4, 4).Value = "Quantity";
            sheet.Cell(4, 5).Value = "Price";
            sheet.Cell(5, 2).Value = "PN1";
            // Description in column C contains the literal "Config ID" text — must be ignored.
            sheet.Cell(5, 3).Value = "Config ID label sticker";
            sheet.Cell(5, 4).Value = "1";

            wb.SaveAs(temp.Path);
        }

        var result = parser.Parse(temp.Path);
        result.LineItems.Should().HaveCount(2);
        result.LineItems[0].Vpn.Should().Be("33333333");
        result.LineItems[1].Vpn.Should().Be("PN1");
        result.LineItems[1].Description.Should().Be("Config ID label sticker");
    }

    [Fact]
    public void EmptyConfigId_RaisesParseError()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);
        using var temp = new TempFile();

        using (var wb = new XLWorkbook())
        {
            var sheet = wb.AddWorksheet("Sheet1");
            sheet.Cell(1, 2).Value = "Config ID";
            sheet.Cell(1, 3).Value = "Config Name";
            sheet.Cell(1, 5).Value = "Total Price";
            // Config ID cell intentionally empty
            sheet.Cell(2, 3).Value = "Has No ID";
            sheet.Cell(2, 5).Value = "50.00";

            wb.SaveAs(temp.Path);
        }

        var act = () => parser.Parse(temp.Path);
        act.Should().Throw<ParseError>()
            .Where(e => e.Stage == "config");
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bidparser-oneconfig-test-{Guid.NewGuid():N}.xlsx");
        }

        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }
    }
}
