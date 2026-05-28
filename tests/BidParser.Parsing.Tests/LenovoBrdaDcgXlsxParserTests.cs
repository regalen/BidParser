using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class LenovoBrdaDcgXlsxParserTests
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

    private static BidParser.Domain.Models.ParseResult Parse()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoBrdaDcgXlsx);
        return parser.Parse(Path.Combine(RepoRoot(), "samples", "inputs", "BRDAD010458440.xls"));
    }

    [Fact]
    public void Metadata_IsCorrect()
    {
        var result = Parse();

        result.Metadata.QuoteNumber.Should().Be("BRDAD010458440");
        result.Metadata.Supplier.Should().Be(Vendors.Lenovo);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(103542.60m);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.LenovoBrdaDcgXlsx);
    }

    [Fact]
    public void Validation_Matches()
    {
        var result = Parse();

        result.Validation.QuotedTotal.Should().Be(103542.60m);
        result.Validation.ComputedTotal.Should().Be(103542.60m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void TotalItemCount_8Parents_54Children_62Total()
    {
        var result = Parse();

        result.LineItems.Should().HaveCount(62);
        // Parent rows have integer LineSequence (no '.'); children carry 'N.NN'.
        var parents = result.LineItems.Where(item => !item.LineSequence!.Contains('.')).ToList();
        var children = result.LineItems.Where(item => item.LineSequence!.Contains('.')).ToList();
        parents.Should().HaveCount(8);
        children.Should().HaveCount(54);
    }

    [Fact]
    public void ParentsHaveRealCosts()
    {
        var result = Parse();
        var parents = result.LineItems.Where(item => !item.LineSequence!.Contains('.')).ToList();

        parents[0].Vpn.Should().Be("7D7ACTO1WW");
        parents[0].Cost.Should().Be(36399.96m);
        parents[0].Qty.Should().Be(1);
        parents[0].LineSequence.Should().Be("1");
        parents[0].Description.Should().Be("256GB ThinkSystem ST650 V3 3yr Base Warranty");

        parents[1].Vpn.Should().Be("7S0XCTO5WW");
        parents[1].Cost.Should().Be(322.14m);
        parents[1].LineSequence.Should().Be("2");

        parents[2].Vpn.Should().Be("5PS7C00099");
        parents[2].Cost.Should().Be(236.31m);

        parents[3].Vpn.Should().Be("5WS7C00090");
        parents[3].Cost.Should().Be(6880.39m);

        parents[4].Vpn.Should().Be("7D7ACTO1WW");
        parents[4].Cost.Should().Be(49742.85m);
        parents[4].LineSequence.Should().Be("5");
        parents[4].Description.Should().Be("512GB ThinkSystem ST650 V3 3yr Base Warranty");

        parents[7].Vpn.Should().Be("5WS7C00090");
        parents[7].Cost.Should().Be(9402.50m);
        parents[7].LineSequence.Should().Be("8");
    }

    [Fact]
    public void ChildrenCarrySentinelZeroCost()
    {
        var result = Parse();
        var children = result.LineItems.Where(item => item.LineSequence!.Contains('.')).ToList();

        children.Should().OnlyContain(item => item.Cost == 0m);

        var firstChild = result.LineItems.Single(item => item.LineSequence == "1.01");
        firstChild.Vpn.Should().Be("BNW0");
        firstChild.Description.Should().Be("ThinkSystem ST650 V3 - 2.5\" Chassis Base");
        firstChild.Qty.Should().Be(1);

        var rdimm = result.LineItems.Single(item => item.LineSequence == "5.06");
        rdimm.Vpn.Should().Be("BNF9");
        rdimm.Qty.Should().Be(8);
    }

    [Fact]
    public void ZeroPricedConfigInstructionTreatedAsChild()
    {
        // '5374CM1' has D = 0.0 explicitly populated in the workbook. It must be
        // classified as a CHILD (sentinel zero), not promoted to a PARENT just
        // because the price cell is non-blank.
        var result = Parse();
        var rows = result.LineItems.Where(item => item.Vpn == "5374CM1").ToList();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(item => item.LineSequence!.Contains('.'));
        rows.Should().OnlyContain(item => item.Cost == 0m);
    }

    [Fact]
    public void SubtotalAndFeatureCodeHeaderRowsAreSkipped()
    {
        var result = Parse();

        result.LineItems.Should().NotContain(item =>
            string.Equals(item.Description, "Subtotal", StringComparison.OrdinalIgnoreCase));
        result.LineItems.Should().NotContain(item =>
            string.Equals(item.Vpn, "Feature Code", StringComparison.OrdinalIgnoreCase));
        result.LineItems.Should().NotContain(item =>
            item.Vpn.StartsWith("Set from Configurator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParserExposesBothCrmTemplates()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.LenovoBrdaDcgXlsx);
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        parser.AcceptedMime.Should().Be("application/vnd.ms-excel");
        parser.Vendor.Should().Be(Vendors.Lenovo);
        parser.DisplayName.Should().Be("BRDA DCG (XLSX)");
    }
}
