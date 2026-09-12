using BidParser.Domain.Constants;
using BidParser.Output;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class OutputNamingTests
{
    [Fact]
    public void BidScoped_WholeQuote_UsesBidAndRevision()
    {
        var name = OutputNaming.OutputFilename("source.xlsx", CrmTemplates.NoCalculation, "XQ-9100002", "1", OutputNameStyle.BidScoped);
        name.Should().Be("XQ-9100002_1_NoCalculation.xlsx");
    }

    [Fact]
    public void BidScoped_WholeQuote_FallsBackToSourceStemWhenBidMetadataAbsent()
    {
        var name = OutputNaming.OutputFilename("my_source_file.xlsx", CrmTemplates.Uplift, null, null, OutputNameStyle.BidScoped);
        name.Should().Be("my_source_file_Uplift.xlsx");
    }

    [Fact]
    public void BidScoped_SplitArchive_UsesBidAndRevision()
    {
        var archive = OutputNaming.OutputArchiveFilename("source.xlsx", CrmTemplates.NoCalculation, "XQ-9100002", "1", OutputNameStyle.BidScoped);
        archive.Should().Be("XQ-9100002_1_NoCalculation.zip");
    }

    [Fact]
    public void BidScoped_SplitEntry_IncludesStemAndSolution()
    {
        var entry = OutputNaming.OutputFilename("source.xlsx", CrmTemplates.NoCalculation, "SID-101", "XQ-9100002", "1", OutputNameStyle.BidScoped);
        entry.Should().Be("XQ-9100002_1_SID-101_NoCalculation.xlsx");
    }

    [Fact]
    public void SolutionScoped_WholeQuote_UsesBidNumberWithoutRevision()
    {
        var name = OutputNaming.OutputFilename("CH9000000002.xlsx", CrmTemplates.NoCalculation, "CH9000000002", "1", OutputNameStyle.SolutionScoped);
        name.Should().Be("CH9000000002_NoCalculation.xlsx");
    }

    [Fact]
    public void SolutionScoped_WholeQuote_FallsBackToSourceStemWhenBidNumberAbsent()
    {
        var name = OutputNaming.OutputFilename("CH9000000002.xlsx", CrmTemplates.NoCalculation, null, null, OutputNameStyle.SolutionScoped);
        name.Should().Be("CH9000000002_NoCalculation.xlsx");
    }

    [Fact]
    public void SolutionScoped_Archive_UsesBidNumberWithoutRevision()
    {
        var archive = OutputNaming.OutputArchiveFilename("CH9000000002.xlsx", CrmTemplates.NoCalculation, "CH9000000002", "1", OutputNameStyle.SolutionScoped);
        archive.Should().Be("CH9000000002_NoCalculation.zip");
    }

    [Fact]
    public void SolutionScoped_Archive_FallsBackToSourceStemWhenBidNumberAbsent()
    {
        var archive = OutputNaming.OutputArchiveFilename("CH9000000002.xlsx", CrmTemplates.NoCalculation, null, null, OutputNameStyle.SolutionScoped);
        archive.Should().Be("CH9000000002_NoCalculation.zip");
    }

    [Fact]
    public void SolutionScoped_SplitEntry_HasNoStemAndConvertsWhitespace()
    {
        var entry = OutputNaming.OutputFilename("CH9000000002.xlsx", CrmTemplates.NoCalculation, "1073 3013 5309", "CH9000000002", "1", OutputNameStyle.SolutionScoped);
        entry.Should().Be("1073_3013_5309_NoCalculation.xlsx");
    }

    [Fact]
    public void SolutionScoped_SplitEntry_FallsBackToNoSolutionIdWhenBlank()
    {
        var entry = OutputNaming.OutputFilename("CH9000000002.xlsx", CrmTemplates.NoCalculation, null, "CH9000000002", "1", OutputNameStyle.SolutionScoped);
        entry.Should().Be("NoSolutionID_NoCalculation.xlsx");

        var emptyEntry = OutputNaming.OutputFilename("CH9000000002.xlsx", CrmTemplates.NoCalculation, "   ", "CH9000000002", "1", OutputNameStyle.SolutionScoped);
        emptyEntry.Should().Be("NoSolutionID_NoCalculation.xlsx");
    }
}
