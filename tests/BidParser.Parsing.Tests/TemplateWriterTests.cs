using BidParser.Output;
using BidParser.Parsing.Registry;
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
