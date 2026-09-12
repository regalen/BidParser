using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class DellCtoJsonParserTests
{

    private static string SamplePath(string filename) =>
        Path.Combine(TestSample.Root, "samples", "inputs", filename);

    [Fact]
    public void Metadata_IsCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));

        result.Metadata.QuoteNumber.Should().Be("9000000000002");
        result.Metadata.Supplier.Should().Be(Vendors.Dell);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(45108.52m);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.DellCtoJson);
    }

    [Fact]
    public void Validation_Matches()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().Be(45108.52m);
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void TotalItemCount_IsFortyFive()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));

        result.LineItems.Should().HaveCount(45);
    }

    [Fact]
    public void Parent_HasCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));
        var parent = result.LineItems[0];

        parent.LineSequence.Should().Be("1");
        parent.Vpn.Should().Be("210-BNMR");
        parent.Description.Should().Be("R470 - Smart Selection Flexi [PROMO_R470_1]");
        parent.Qty.Should().Be(2);
        parent.Msrp.Should().Be(64763.66m);
        parent.Cost.Should().Be(21307.24m);
    }

    [Fact]
    public void FirstChild_HasCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));
        var child = result.LineItems[1];

        child.LineSequence.Should().Be("1.01");
        child.Vpn.Should().Be("210-BNMR");
        child.Description.Should().Be("PowerEdge R470 Server, Enterprise");
        child.Qty.Should().Be(1);
        child.Msrp.Should().Be(0m);
        child.Cost.Should().Be(0m);
    }

    [Fact]
    public void Parent_UsesProductDescriptionWhenCustomProductNameIsBlank()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("""
            {
              "quoteNumber": 123,
              "quoteVersion": 1,
              "items": [{
                "baseSkuNumber": "BASE-SKU",
                "customProductName": "   ",
                "productDescription": "Fallback description",
                "quantity": 1,
                "unitListPriceIncludingShipping": 100,
                "unitSalesPriceIncludingShipping": 50,
                "skus": [{ "skuNumber": "CHILD-SKU", "description": "Child description", "quantity": 1, "unitSalesPrice": 50 }]
              }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.LineItems[0].Description.Should().Be("Fallback description");
        result.LineItems[1].Description.Should().Be("Child description");
    }

    [Fact]
    public void SecondParent_HasCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));
        var parent2 = result.LineItems[44];

        parent2.LineSequence.Should().Be("2");
        parent2.Vpn.Should().Be("634-CVFM");
        parent2.Description.Should().Be("Windows Server 2025,Standard, ROK,16CORE (for Distributor sale only),Customer Kit - [G7XD0ZN]");
        parent2.Qty.Should().Be(2);
        parent2.Msrp.Should().Be(4601.85m);
        parent2.Cost.Should().Be(1247.02m);
    }

    [Fact]
    public void SecondParent_HasNoRedundantChild()
    {
        // 634-CVFM's only child restates the parent exactly — same SKU number, same 4601.85 list,
        // same 1247.02 sale — so it is dropped and the parent stands alone.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));
        result.LineItems.Should().HaveCount(45);
        result.LineItems.Should().NotContain(i => i.LineSequence != null && i.LineSequence.StartsWith("2.", StringComparison.Ordinal));
    }

    [Fact]
    public void Parents_HaveEstimatedDeliveryComment()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));

        result.LineItems[0].Comments.Should().Be("Est. delivery on 14-Jan-2027 if purchased today");
        result.LineItems[44].Comments.Should().Be("Est. delivery on 28-Jul-2026 if purchased today");
    }

    [Fact]
    public void Children_HaveNoComment()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));

        var children = result.LineItems.Where(i => i.LineSequence != null && i.LineSequence.Contains('.'));
        children.Should().OnlyContain(i => i.Comments == null);
    }

    [Fact]
    public void RebateIneligibleParent_AddsCommentAndSuccessWarning()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("""
            {
              "quoteNumber": 123,
              "quoteVersion": 1,
              "salesPrice": 50,
              "items": [{
                "baseSkuNumber": "BASE-SKU",
                "productDescription": "Base description",
                "isRebateEligible": false,
                "quantity": 1,
                "unitListPriceIncludingShipping": 100,
                "unitSalesPriceIncludingShipping": 50,
                "shipments": [{
                  "estimatedDeliveryDateRange": { "max": "2027-01-14T23:01:00+00:00" }
                }],
                "skus": [{ "skuNumber": "CHILD-SKU", "description": "Child description", "quantity": 1, "unitSalesPrice": 50 }]
              }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.HasRebateIneligibleItems.Should().BeTrue();
        result.LineItems[0].Comments.Should().Be("Est. delivery on 14-Jan-2027 if purchased today | Not Dell Rebate Eligible");
        result.LineItems[1].Comments.Should().BeNull();
    }

    [Fact]
    public void RebateIneligibleParent_WithoutShipments_CommentsAreTheWarningOnly()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("""
            {
              "quoteNumber": 123,
              "quoteVersion": 1,
              "salesPrice": 50,
              "items": [{
                "baseSkuNumber": "BASE-SKU",
                "productDescription": "Base description",
                "isRebateEligible": false,
                "quantity": 1,
                "unitListPriceIncludingShipping": 100,
                "unitSalesPriceIncludingShipping": 50,
                "skus": [{ "skuNumber": "CHILD-SKU", "description": "Child description", "quantity": 1, "unitSalesPrice": 50 }]
              }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.HasRebateIneligibleItems.Should().BeTrue();
        result.LineItems[0].Comments.Should().Be("Not Dell Rebate Eligible");
    }

    [Fact]
    public void RebateEligibleParent_AddsNoCommentAndNoWarning()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("""
            {
              "quoteNumber": 123,
              "quoteVersion": 1,
              "salesPrice": 50,
              "items": [{
                "baseSkuNumber": "BASE-SKU",
                "productDescription": "Base description",
                "isRebateEligible": true,
                "quantity": 1,
                "unitListPriceIncludingShipping": 100,
                "unitSalesPriceIncludingShipping": 50,
                "skus": [{ "skuNumber": "CHILD-SKU", "description": "Child description", "quantity": 1, "unitSalesPrice": 50 }]
              }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.HasRebateIneligibleItems.Should().BeFalse();
        result.LineItems[0].Comments.Should().BeNull();
    }

    [Fact]
    public void AllChildren_HaveZeroMsrpAndCost()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_CTO_Sample.json"));

        var children = result.LineItems.Where(i => i.LineSequence != null && i.LineSequence.Contains('.'));
        children.Should().HaveCount(43);
        children.Should().OnlyContain(i => i.Msrp == 0m && i.Cost == 0m);
    }

    [Fact]
    public void Peripherals_DefaultParse_EmitsEveryParentAndFilteredChildren()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_Peripherals_Sample.json"));

        result.LineItems.Should().HaveCount(29);
        result.LineItems.Count(i => i.LineSequence != null && !i.LineSequence.Contains('.')).Should().Be(23);
        result.LineItems.Count(i => i.LineSequence != null && i.LineSequence.Contains('.')).Should().Be(6);
    }

    [Fact]
    public void Peripherals_DefaultParse_PreservesExpectedVpnOrder()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_Peripherals_Sample.json"));

        result.LineItems
            .Where(i => i.LineSequence != null && !i.LineSequence.Contains('.'))
            .Select(i => i.Vpn)
            .Should().Equal(
            "210-25624",
            "460-BDMT",
            "210-25624",
            "580-AJNS",
            "210-25624",
            "580-ADKO",
            "210-25624",
            "570-AAJD",
            "210-BKMR",
            "210-25624",
            "482-BBEB",
            "210-25624",
            "482-BBDL",
            "210-25624",
            "722-BBBX",
            "210-25624",
            "722-BBBS",
            "210-BQYG",
            "210-BQMN",
            "210-BRMB",
            "210-BRKN",
            "210-BVPV",
            "210-BVPF");
    }

    [Fact]
    public void Peripherals_DefaultParse_KeepsFormerContainersAndAppliesNewSkuFilters()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_Peripherals_Sample.json"));

        // The former "container" placeholders are parents like any other and are no longer dropped.
        result.LineItems.Count(i => i.Vpn == "210-25624").Should().Be(8);

        // Every remaining child sits under one of the two docks: the Displays parents (9, 18, 19,
        // 22, 23) lose their whole block, and every other item's only child restated its parent.
        var children = result.LineItems.Where(i => i.LineSequence != null && i.LineSequence.Contains('.')).ToList();
        children.Select(i => i.LineSequence).Should().Equal(
            "20.01", "20.02", "20.03",
            "21.01", "21.02", "21.03");
        children.Should().NotContain(i => i.Vpn == "210-25624");
        children.Should().NotContain(i => i.Vpn == "210-BRMB");
        children.Should().NotContain(i => i.Vpn == "210-BRKN");
    }

    [Fact]
    public void Peripherals_DefaultParse_ReconcilesQuotedTotal()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(SamplePath("Dell_Peripherals_Sample.json"));

        result.Metadata.QuotedTotal.Should().Be(3501.04m);
        result.Validation.ComputedTotal.Should().Be(3501.04m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void Peripherals_IncludeSubComponentDetail_RestoresFullTree()
    {
        // Exercises the non-default parser overload; this option is deliberately not reachable from the API.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(
            SamplePath("Dell_Peripherals_Sample.json"),
            new ParseOptions { IncludeSubComponentDetail = true });

        result.LineItems.Should().HaveCount(67);
        result.LineItems.Should().Contain(i => i.Vpn == "210-25624");
    }

    [Fact]
    public void Cto_IncludeSubComponentDetail_RestoresFullFortySixLineTree()
    {
        // Exercises the non-default parser overload; this option is deliberately not reachable from the API.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var result = parser.Parse(
            SamplePath("Dell_CTO_Sample.json"),
            new ParseOptions { IncludeSubComponentDetail = true });

        result.LineItems.Should().HaveCount(46);
        result.LineItems.Should().ContainSingle(i => i.LineSequence == "2.01" && i.Vpn == "634-CVFM");
    }

    [Fact]
    public void ChildRestatingParentAtRealQuotePrices_IsDropped()
    {
        // The shape every Dell echo line actually has: the child repeats the parent's SKU number and
        // both of its prices unchanged. Prices are compared like for like, so list must equal list
        // (87.00) and sale must equal sale (29.32) — not list against sale.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("""
            {
              "quoteNumber": 12345,
              "quoteVersion": 1,
              "items": [{
                "baseSkuNumber": "460-BDMT",
                "productDescription": "Dell Pro 14-16 Plus EcoLoop Briefcase - CC5623 - SnP",
                "quantity": 1,
                "unitListPriceIncludingShipping": 87.00,
                "unitSalesPriceIncludingShipping": 29.32,
                "skus": [{
                  "skuNumber": "460-BDMT",
                  "description": "Dell EcoLoop Pro Briefcase - CC5623 - 3yr Ltd Warranty - SnP",
                  "quantity": 1,
                  "unitListPrice": 87.00,
                  "unitSalesPrice": 29.32
                }]
              }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.LineItems.Should().ContainSingle();
        result.LineItems[0].LineSequence.Should().Be("1");
        result.LineItems[0].Cost.Should().Be(29.32m);
    }

    [Fact]
    public void ChildMatchingOnlySomeParentAttributes_IsRetainedAndSequenceHasNoGap()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("""
            {
              "quoteNumber": 12345,
              "quoteVersion": 1,
              "items": [{
                "baseSkuNumber": "BASE-SKU",
                "productDescription": "Parent",
                "quantity": 1,
                "unitListPriceIncludingShipping": 100,
                "unitSalesPriceIncludingShipping": 40,
                "skus": [
                  { "skuNumber": "OTHER-SKU", "description": "Different SKU, matching prices",
                    "quantity": 1, "unitListPrice": 100, "unitSalesPrice": 40 },
                  { "skuNumber": "BASE-SKU", "description": "All three match - dropped",
                    "quantity": 1, "unitListPrice": 100, "unitSalesPrice": 40 },
                  { "skuNumber": "BASE-SKU", "description": "Same SKU, different sale price",
                    "quantity": 1, "unitListPrice": 100, "unitSalesPrice": 39 },
                  { "skuNumber": "BASE-SKU", "description": "Same SKU, prices absent" }
                ]
              }]
            }
            """);

        var result = parser.Parse(temp.Path);

        // The dropped child sits mid-block, so the survivors must close the gap rather than skip 1.02.
        result.LineItems.Select(i => i.LineSequence).Should().Equal("1", "1.01", "1.02", "1.03");
        result.LineItems.Skip(1).Select(i => i.Description).Should().Equal(
            "Different SKU, matching prices",
            "Same SKU, different sale price",
            "Same SKU, prices absent");
    }

    [Fact]
    public void MalformedJson_ThrowsDetectError()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("not json content");

        var act = () => parser.Parse(temp.Path);
        act.Should().Throw<ParseError>()
            .Where(e => e.Stage == "detect");
    }

    [Fact]
    public void JsonWithoutItems_ThrowsDetectError()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        using var temp = new TempFile("{\"quoteNumber\": 123}");

        var act = () => parser.Parse(temp.Path);
        act.Should().Throw<ParseError>()
            .Where(e => e.Stage == "detect");
    }

    [Fact]
    public void AposJson_ParsedAsCto_ThrowsDetectError()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);

        var act = () => parser.Parse(SamplePath("Dell_APOS_Sample.json"));
        act.Should().Throw<ParseError>()
            .Where(e => e.Stage == "detect");
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bidparser-dell-test-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, content);
        }

        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }
    }
}
