using BidParser.Domain.Constants;
using BidParser.Output;
using BidParser.Parsing.Registry;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class TemplateWriterTests
{
    [Theory]
    [InlineData("nutanix_software_only_pdf", "XQ-4076249.pdf", "XQ-4076249_parsed.xlsx")]
    [InlineData("nutanix_software_only_pdf", "XQ-4157308.pdf", "XQ-4157308_parsed.xlsx")]
    [InlineData("nutanix_software_only_pdf", "XQ-4165884.pdf", "XQ-4165884_parsed.xlsx")]
    [InlineData("nutanix_software_only_xlsx", "XQ-4076249.xlsx", "XQ-4076249_parsed.xlsx")]
    [InlineData("nutanix_hardware_only_pdf", "XQ-4108785.pdf", "XQ-4108785_parsed.xlsx")]
    [InlineData("nutanix_hardware_only_xlsx", "XQ-4108785.xlsx", "XQ-4108785_parsed.xlsx")]
    [InlineData("nutanix_renewal_pdf", "XQ-4128926.pdf", "XQ-4128926_parsed.xlsx")]
    [InlineData("nutanix_renewal_pdf", "XQ-4029825.pdf", "XQ-4029825_parsed.xlsx")]
    [InlineData("nutanix_renewal_xlsx", "XQ-4176792.xlsx", "XQ-4176792_parsed.xlsx")]
    public void TemplateWriterMatchesGoldenWorkbookCells(string slug, string inputName, string expectedName)
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(parser => parser.Slug == slug);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        ForeignUpliftWriter.WriteForeignUplift(result.LineItems, actualPath, fxRate: 1.000m, margin: 5.00m, parserSlug: slug);

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── AnzGenericWriter (HP No Calculation / Uplift) ────────────────────────

    [Theory]
    [InlineData("Deals20260518T034809_HPI.xlsx", "Deals20260518T034809_HPI_NoCalculation_parsed.xlsx", false)]
    [InlineData("Deals20260518T034809_HPI.xlsx", "Deals20260518T034809_HPI_Uplift_parsed.xlsx",         true)]
    [InlineData("Deals20260518T043243_HPI.xlsx", "Deals20260518T043243_HPI_NoCalculation_parsed.xlsx", false)]
    [InlineData("Deals20260518T043243_HPI.xlsx", "Deals20260518T043243_HPI_Uplift_parsed.xlsx",         true)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells(string inputName, string expectedName, bool includeMargin)
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpBidXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", inputName));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var sheetName = includeMargin ? "Uplift" : "No Calculation";
        AnzGenericWriter.Write(result.LineItems, actualPath, sheetName, includeMargin, margin: 5.00m, vendorName: "HP");

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── AnzGenericWriter (Lenovo No Calculation) ─────────────────────────────

    [Fact]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_Lenovo()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoBrdaDcgPdf);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "BRDAS010260417V1.pdf"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "BRDAS010260417V1_parsed.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        AnzGenericWriter.Write(result.LineItems, actualPath, "No Calculation", includeMargin: false, margin: 0m, vendorName: "Lenovo");

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── AnzGenericWriter (Lenovo BRDA DCG XLSX, both templates) ──────────────

    [Theory]
    [InlineData("BRDAD010458440_NoCalculation_parsed.xlsx", false)]
    [InlineData("BRDAD010458440_Uplift_parsed.xlsx",        true)]
    public void AnzGenericWriterMatchesGoldenWorkbookCells_LenovoXlsx(string expectedName, bool includeMargin)
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoBrdaDcgXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "BRDAD010458440.xls"));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        var sheetName = includeMargin ? "Uplift" : "No Calculation";
        AnzGenericWriter.Write(result.LineItems, actualPath, sheetName, includeMargin, margin: 5.00m, vendorName: "LENOVO");

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    // ── PercentOffWithUpliftWriter (HP OneConfig XLSX) ───────────────────────

    [Fact]
    public void PercentOffWithUpliftWriter_MatchesGoldenWorkbookCells()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "55648855.xlsx"));
        using var tempDirectory = new TempDirectory();
        const string expectedName = "55648855_parsed.xlsx";
        var actualPath = Path.Combine(tempDirectory.Path, expectedName);

        PercentOffWithUpliftWriter.Write(result.LineItems, actualPath, margin: 5m, im: 30m, vendorName: "HP");

        WorkbookComparer.AssertEqual(actualPath, Path.Combine(root, "samples", "outputs", expectedName));
    }

    [Fact]
    public void PercentOffWithUpliftWriter_ParentMsrpIsReal_ChildrenAreSentinel()
    {
        var root = FindRepoRoot();
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.HpOneConfigXlsx);
        var result = parser.Parse(Path.Combine(root, "samples", "inputs", "55648855.xlsx"));
        using var tempDirectory = new TempDirectory();
        var actualPath = Path.Combine(tempDirectory.Path, "55648855_parsed.xlsx");

        PercentOffWithUpliftWriter.Write(result.LineItems, actualPath, margin: 5m, im: 30m, vendorName: "HP");

        using var workbook = new XLWorkbook(actualPath);
        var sheet = workbook.Worksheets.First();

        // Row 3 = parent: MSRP col H should be the real price
        sheet.Cell(3, 8).Value.GetNumber().Should().BeApproximately(6042.77, 0.001);

        // Row 4 = first child: MSRP col H should be the sentinel
        sheet.Cell(4, 8).Value.GetNumber().Should().BeApproximately(0.0001, 0.00001);

        // Col I (Cost) should be blank for all rows
        for (var row = 3; row <= 33; row++)
        {
            sheet.Cell(row, 9).Value.IsBlank.Should().BeTrue($"Cost col I should be blank on row {row}");
        }

        // Col K (Margin) = 5 for all rows
        for (var row = 3; row <= 33; row++)
        {
            sheet.Cell(row, 11).Value.GetNumber().Should().Be(5, $"Margin on row {row}");
        }

        // Col X (IM%) = 30 for all rows
        for (var row = 3; row <= 33; row++)
        {
            sheet.Cell(row, 24).Value.GetNumber().Should().Be(30, $"IM% on row {row}");
        }
    }

    // ── OutputNaming ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("XQ-4076249.pdf", "XQ-4076249_parsed.xlsx")]
    [InlineData("XQ-4076249.xlsx", "XQ-4076249_parsed.xlsx")]
    public void OutputFilenameUsesSourceStem(string sourceFilename, string expected)
    {
        OutputNaming.OutputFilename(sourceFilename).Should().Be(expected);
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bidparser-output-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
