using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class LenovoLbpeIsgXlsParserTests
{
    private const string PrimarySample = "BRDAD019200001.xls";
    private const string FourConfigSample = "Bid_Platform_Bid_Request_Sample_03.xls";
    private const string StandaloneOptionsSample = "Bid_Platform_Bid_Request_Sample_01.xls";
    private const string StandaloneLicencesSample = "Bid_Platform_Bid_Request_Sample_02.xls";

    private const string XlsxSingleConfig = "Bid_Platform_Bid_Request_Sample_04.xlsx";
    private const string XlsxFourConfigWithStandalone = "Bid_Platform_Bid_Request_Sample_05.xlsx";
    private const string XlsxFourConfig = "Bid_Platform_Bid_Request_Sample_06.xlsx";
    private const string XlsxManualParts = "Bid_Platform_Bid_Request_Sample_07.xlsx";
    private const string XlsxTwoConfigWithStandalone = "Bid_Platform_Bid_Request_Sample_08.xlsx";

    private static readonly Dictionary<string, ParseResult> Results = [];
    private static readonly object ResultsLock = new();

    [Fact]
    public void MetadataAndParserSurface_AreCorrect()
    {
        var parser = Parser();
        var result = Parse(PrimarySample);

        parser.DisplayName.Should().Be("LBP-E ISG Quote (XLSX)");
        parser.Vendor.Should().Be(Vendors.LenovoIsg);
        parser.AcceptedMime.Should().Be("application/vnd.ms-excel");
        parser.AcceptedMimes.Should().Equal(
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation, CrmTemplates.Uplift);
        parser.SupportsSolutionIdSplit.Should().BeTrue();

        result.Metadata.QuoteNumber.Should().Be("BRDAD019200001");
        result.Metadata.Supplier.Should().Be(Vendors.LenovoIsg);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(103542.60m);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.LenovoLbpeIsgXls);
    }

    [Theory]
    [InlineData(PrimarySample)]
    [InlineData(FourConfigSample)]
    [InlineData(StandaloneOptionsSample)]
    [InlineData(StandaloneLicencesSample)]
    [InlineData(XlsxSingleConfig)]
    [InlineData(XlsxFourConfigWithStandalone)]
    [InlineData(XlsxFourConfig)]
    [InlineData(XlsxTwoConfigWithStandalone)]
    public void EveryLineCarriesItsSolutionIdWhileOnlyParentsCarryTheComment(string sample)
    {
        var result = Parse(sample);

        result.LineItems.Should().OnlyContain(item => item.SolutionId != null);
        Parents(result).Should().OnlyContain(parent => parent.Comments == $"Solution ID: {parent.SolutionId}");
        Children(result).Should().OnlyContain(child => child.Comments == null);
    }

    [Theory]
    // Legacy XLS fixtures
    [InlineData(PrimarySample, 2, 60, 62, "103542.60")]
    [InlineData(FourConfigSample, 4, 184, 188, "423640.27")]
    [InlineData(StandaloneOptionsSample, 13, 140, 153, "81882.90")]
    [InlineData(StandaloneLicencesSample, 5, 45, 50, "25430.84")]
    // XLSX fixtures
    [InlineData(XlsxSingleConfig, 1, 38, 39, "71380.91")]
    [InlineData(XlsxFourConfigWithStandalone, 34, 220, 254, "377368.03")]
    [InlineData(XlsxFourConfig, 4, 152, 156, "183932.90")]
    [InlineData(XlsxManualParts, 1, 0, 1, "74378.04")]
    [InlineData(XlsxTwoConfigWithStandalone, 3, 110, 113, "354578.02")]
    public void AllFixtures_ReconcileToQuotedTotal(
        string sample,
        int expectedParents,
        int expectedChildren,
        int expectedItems,
        string expectedTotalText)
    {
        var result = Parse(sample);
        var expectedTotal = decimal.Parse(expectedTotalText, System.Globalization.CultureInfo.InvariantCulture);

        Parents(result).Should().HaveCount(expectedParents);
        Children(result).Should().HaveCount(expectedChildren);
        result.LineItems.Should().HaveCount(expectedItems);
        result.Metadata.QuotedTotal.Should().Be(expectedTotal);
        result.Validation.QuotedTotal.Should().Be(expectedTotal);
        result.Validation.ComputedTotal.Should().Be(expectedTotal);
        result.Validation.Difference.Should().Be(0m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void PrimaryFixture_UsesConfigurationSubtotalsForTwoParents()
    {
        var parents = Parents(Parse(PrimarySample));

        parents[0].LineSequence.Should().Be("1");
        parents[0].Vpn.Should().Be("7D7ACTO1WW");
        parents[0].Description.Should().Be("256GB ThinkSystem ST650 V3 3yr Base Warranty");
        parents[0].Qty.Should().Be(1);
        parents[0].Cost.Should().Be(43838.80m);
        parents[0].Comments.Should().Be("Solution ID: SIDX02SDL3");

        parents[1].LineSequence.Should().Be("2");
        parents[1].Vpn.Should().Be("7D7ACTO1WW");
        parents[1].Description.Should().Be("512GB ThinkSystem ST650 V3 3yr Base Warranty");
        parents[1].Qty.Should().Be(1);
        parents[1].Cost.Should().Be(59703.80m);
        parents[1].Comments.Should().Be("Solution ID: SIDX02SDL3");

        parents[0].Raw["Subtotal (AUD) (per unit)"].Should().Be("43838.8");
        parents[1].Raw["Subtotal (AUD) (per unit)"].Should().Be("59703.8");
    }

    [Fact]
    public void PrimaryFixture_EmitsThirtyZeroCostChildrenPerConfiguration()
    {
        var result = Parse(PrimarySample);
        var children = Children(result);

        ChildrenOf(result, "1").Should().HaveCount(30);
        ChildrenOf(result, "2").Should().HaveCount(30);
        children.Should().OnlyContain(child => child.Cost == 0m && child.Comments == null);

        result.LineItems.Single(item => item.LineSequence == "1.01").Should().BeEquivalentTo(
            new { Vpn = "BNW0", Description = "ThinkSystem ST650 V3 - 2.5\" Chassis Base", Qty = 1 });
        result.LineItems.Single(item => item.LineSequence == "1.30").Vpn.Should().Be("5WS7C00090");
        result.LineItems.Single(item => item.LineSequence == "2.30").Vpn.Should().Be("5WS7C00090");

        children.Where(item => item.Vpn == "5374CM1").Should().HaveCount(2)
            .And.OnlyContain(item => item.Cost == 0m);
    }

    [Fact]
    public void FourConfigFixture_AssignsEachSolutionIdToItsConfiguration()
    {
        var result = Parse(FourConfigSample);
        var parents = Parents(result);

        parents.Select(parent => new { parent.Qty, parent.Cost, parent.Comments })
            .Should().Equal(
                new { Qty = 2, Cost = 90490.92m, Comments = (string?)"Solution ID: SIDX02YU2Q" },
                new { Qty = 2, Cost = 67014.31m, Comments = (string?)"Solution ID: SIDX02YU2R" },
                new { Qty = 1, Cost = 37114.01m, Comments = (string?)"Solution ID: SIDX02YU2S" },
                new { Qty = 2, Cost = 35757.90m, Comments = (string?)"Solution ID: SIDX02YU2T" });

        ChildrenOf(result, "1").Should().HaveCount(50);
        ChildrenOf(result, "2").Should().HaveCount(39);
        ChildrenOf(result, "3").Should().HaveCount(56);
        ChildrenOf(result, "4").Should().HaveCount(39);
    }

    [Fact]
    public void StandaloneOptionsFixture_ClosesBlockArithmeticallyAndPromotesPricedRows()
    {
        var result = Parse(StandaloneOptionsSample);
        var parents = Parents(result);

        parents.Should().HaveCount(13)
            .And.OnlyContain(parent => parent.Comments == "Solution ID: SIDX02Y26H");
        parents.Skip(2).Take(10).Select(parent => parent.Cost).Should().Equal(
            85.69m, 163.65m, 319.48m, 1065.43m, 2024.20m,
            2994.52m, 1332.92m, 2391.43m, 3463.15m, 1349.38m);

        foreach (var parent in parents.Skip(2).Take(10))
        {
            ChildrenOf(result, parent.LineSequence!).Should().BeEmpty();
        }

        var lastChild = ChildrenOf(result, "2").Last();
        lastChild.LineSequence.Should().Be("2.44");
        lastChild.Vpn.Should().Be("SCY0");
        lastChild.Cost.Should().Be(0m);
    }

    [Fact]
    public void StandaloneLicencesFixture_KeepsTrailingUnpricedRowAsLastChild()
    {
        var result = Parse(StandaloneLicencesSample);
        var parents = Parents(result);

        parents.Should().HaveCount(5)
            .And.OnlyContain(parent => parent.Comments == "Solution ID: SIDX02YC76");
        parents.Select(parent => parent.Cost).Should().Equal(
            19325.27m, 1349.38m, 336.82m, 1399.96m, 3019.41m);

        var lastChild = ChildrenOf(result, "1").Last();
        lastChild.LineSequence.Should().Be("1.45");
        lastChild.Vpn.Should().Be("SCY0");
        lastChild.Cost.Should().Be(0m);
    }

    [Fact]
    public void XlsxSingleConfig_EmitsOneConfigurationWithThirtyEightChildren()
    {
        var result = Parse(XlsxSingleConfig);
        var parents = Parents(result);
        var children = Children(result);

        parents.Should().ContainSingle();
        var parent = parents[0];
        parent.LineSequence.Should().Be("1");
        parent.Vpn.Should().Be("7D9CCTO1WW");
        parent.Description.Should().Be("Server ThinkSystem SR645 V3-3yr Base Warranty");
        parent.Qty.Should().Be(1);
        parent.Cost.Should().Be(71380.91m);
        parent.SolutionId.Should().Be("SIDTEST0001");
        parent.Comments.Should().Be("Solution ID: SIDTEST0001");

        children.Should().HaveCount(38)
            .And.OnlyContain(child => child.Cost == 0m && child.Comments == null && child.SolutionId == "SIDTEST0001");
        children.First().LineSequence.Should().Be("1.01");
        children.Last().LineSequence.Should().Be("1.38");
        children.Should().Contain(child => child.Description == "ThinkSystem SR645 V3/SR635 V3 Performance Heatsink (Neptune Air)");
        children.Should().Contain(child => child.Description == "ThinkSystem SR645 V3 MB");
        children.Should().Contain(child => child.Description == "SR645 V3 Laser service indicator");
        children.Should().Contain(child => child.Description == "ThinkSystem SR645 V3 Absolut-RoW RoT Module");
        children.Should().Contain(child => child.Description == "5Yr KYD Add-On SR645 V3");
        children.Should().Contain(child => child.Description == "5Yr Premier NBD Resp SR645 V3");
        result.LineItems.Should().NotContain(item => item.Description == "Sample Lenovo LBP-E Quote 1");
    }

    [Fact]
    public void XlsxFourConfigWithStandalone_ExercisesBlockReconciliationAndObservesAllRows()
    {
        var result = Parse(XlsxFourConfigWithStandalone);
        var parents = Parents(result);
        var children = Children(result);

        parents.Should().HaveCount(34)
            .And.OnlyContain(parent => parent.SolutionId == "SIDTEST0002");
        children.Should().HaveCount(220)
            .And.OnlyContain(child => child.Cost == 0m && child.Comments == null && child.SolutionId == "SIDTEST0002");
        result.LineItems.Should().HaveCount(254);
        result.Metadata.QuotedTotal.Should().Be(377368.03m);
    }

    [Fact]
    public void XlsxFourConfig_EmitsThirtyEightChildrenForEachConfiguration()
    {
        var result = Parse(XlsxFourConfig);
        var parents = Parents(result);

        parents.Should().HaveCount(4)
            .And.OnlyContain(parent => parent.SolutionId == "SIDTEST0003" && parent.Comments == "Solution ID: SIDTEST0003");

        ChildrenOf(result, "1").Should().HaveCount(38);
        ChildrenOf(result, "2").Should().HaveCount(38);
        ChildrenOf(result, "3").Should().HaveCount(38);
        ChildrenOf(result, "4").Should().HaveCount(38);
    }

    [Fact]
    public void XlsxManualParts_EmitsSingleStandaloneParentWithNoChildrenAndNullSolutionId()
    {
        var result = Parse(XlsxManualParts);
        var parents = Parents(result);
        var children = Children(result);

        children.Should().BeEmpty();
        parents.Should().ContainSingle();

        var parent = parents[0];
        parent.LineSequence.Should().Be("1");
        parent.Vpn.Should().Be("4X67A84824");
        parent.Description.Should().Be("ThinkSystem NVIDIA L4 24GB PCIe Gen4 Passive GPU");
        parent.Qty.Should().Be(12);
        parent.Cost.Should().Be(6198.17m);
        parent.SolutionId.Should().BeNull();
        parent.Comments.Should().BeNull();

        result.Metadata.QuotedTotal.Should().Be(74378.04m);
        result.Validation.ComputedTotal.Should().Be(74378.04m);
    }

    [Fact]
    public void XlsxTwoConfigWithStandalone_EmitsConfigurationsAndStandaloneDeploymentLine()
    {
        var result = Parse(XlsxTwoConfigWithStandalone);
        var parents = Parents(result);

        parents.Should().HaveCount(3);
        ChildrenOf(result, "1").Should().HaveCount(55);
        ChildrenOf(result, "2").Should().HaveCount(55);
        ChildrenOf(result, "3").Should().BeEmpty();

        var deployment = parents[2];
        deployment.LineSequence.Should().Be("3");
        deployment.Vpn.Should().Be("5MS7B00045");
        deployment.Qty.Should().Be(2);
        deployment.Cost.Should().Be(5370.35m);
        deployment.SolutionId.Should().Be("SIDTEST0005");
        deployment.Comments.Should().Be("Solution ID: SIDTEST0005");
    }

    [Fact]
    public void RawPriceKeys_ReflectActualSourceHeaderPerFormat()
    {
        var xlsResult = Parse(PrimarySample);
        var xlsxResult = Parse(XlsxSingleConfig);

        var xlsParent = Parents(xlsResult).First();
        xlsParent.Raw.Should().ContainKey("Adjusted Buy Price (AUD) (per unit)");
        xlsParent.Raw.Should().ContainKey("Adjusted Buy Price (AUD) (qty x unit price)");
        xlsParent.Raw.Should().NotContainKey("Estimated Price (AUD) (per unit)");

        var xlsxParent = Parents(xlsxResult).First();
        xlsxParent.Raw.Should().ContainKey("Estimated Price (AUD) (per unit)");
        xlsxParent.Raw.Should().ContainKey("Estimated Price (AUD) (qty x unit price)");
        xlsxParent.Raw.Should().NotContainKey("Adjusted Buy Price (AUD) (per unit)");
    }

    [Fact]
    public void StructuralRows_AreNotEmitted()
    {
        foreach (var sample in new[]
        {
            PrimarySample, FourConfigSample, StandaloneOptionsSample, StandaloneLicencesSample,
            XlsxSingleConfig, XlsxFourConfigWithStandalone, XlsxFourConfig, XlsxManualParts, XlsxTwoConfigWithStandalone
        })
        {
            Parse(sample).LineItems.Should().NotContain(item =>
                string.Equals(item.Description, "Subtotal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Vpn, "Feature Code", StringComparison.OrdinalIgnoreCase)
                || item.Vpn.StartsWith("Set from Configurator", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void HtmlDisguisedXls_IsRejectedAsWrongFileType()
    {
        var path = Path.Combine(TestSample.Root, "samples", "inputs", "Zebra_PC_97000001.xls");

        var error = FluentActions.Invoking(() => Parser().Parse(path))
            .Should().Throw<ParseError>()
            .Which;

        error.Stage.Should().Be("detect");
        error.Hint.Should().Be("File is not a supported Excel (.xls / .xlsx) workbook.");
    }

    private static BidParser.Domain.Abstractions.IParser Parser()
        => new ParserRegistry().Parsers.Single(parser => parser.Slug == ParserSlugs.LenovoLbpeIsgXls);

    private static ParseResult Parse(string sample)
    {
        lock (ResultsLock)
        {
            if (!Results.TryGetValue(sample, out var result))
            {
                result = Parser().Parse(Path.Combine(TestSample.Root, "samples", "inputs", sample));
                Results[sample] = result;
            }
            return result;
        }
    }

    private static List<LineItem> Parents(ParseResult result)
        => result.LineItems.Where(item => !item.LineSequence!.Contains('.')).ToList();

    private static List<LineItem> Children(ParseResult result)
        => result.LineItems.Where(item => item.LineSequence!.Contains('.')).ToList();

    private static List<LineItem> ChildrenOf(ParseResult result, string parentSequence)
        => result.LineItems
            .Where(item => item.LineSequence!.StartsWith($"{parentSequence}.", StringComparison.Ordinal))
            .ToList();

}
