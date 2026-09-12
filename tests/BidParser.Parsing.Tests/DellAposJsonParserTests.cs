using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class DellAposJsonParserTests
{

    private static string SamplePath(string filename) =>
        Path.Combine(TestSample.Root, "samples", "inputs", filename);

    [Fact]
    public void Metadata_IsCorrect()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);

        var result = parser.Parse(SamplePath("Dell_APOS_Sample.json"));

        result.Metadata.QuoteNumber.Should().Be("9000000000001");
        result.Metadata.Supplier.Should().Be(Vendors.Dell);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().Be(5021.82m);
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.DellAposJson);
    }

    [Fact]
    public void Validation_Matches()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);

        var result = parser.Parse(SamplePath("Dell_APOS_Sample.json"));

        result.Validation.Matches.Should().BeTrue();
        result.Validation.QuotedTotal.Should().Be(5021.82m);
        result.Validation.Difference.Should().Be(0m);
    }

    [Fact]
    public void TotalItemCount_IsTwo()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);

        var result = parser.Parse(SamplePath("Dell_APOS_Sample.json"));

        result.LineItems.Should().HaveCount(2);
    }

    [Fact]
    public void FirstItem_HasCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);

        var result = parser.Parse(SamplePath("Dell_APOS_Sample.json"));
        var item1 = result.LineItems[0];

        item1.LineSequence.Should().Be("1");
        item1.Vpn.Should().Be("862-BMDP");
        item1.Description.Should().Be("ProSupport with 4-Hour Onsite Service Reinstate - PowerEdge Servers");
        item1.Qty.Should().Be(1);
        item1.Msrp.Should().Be(9469.0m);
        item1.Cost.Should().Be(4923.88m);
        item1.StartDate.Should().BeNull();
        item1.EndDate.Should().Be(new DateOnly(2027, 7, 20));
        item1.SerialNumber.Should().Be("TAG-SAMPLE1");
    }

    [Fact]
    public void SecondItem_HasCorrectFields()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);

        var result = parser.Parse(SamplePath("Dell_APOS_Sample.json"));
        var item2 = result.LineItems[1];

        item2.LineSequence.Should().Be("2");
        item2.Vpn.Should().Be("891-10514");
        item2.Description.Should().Be("CSS SVC, Reinstatement Fee,ENT LOW END,Technician,Qty1 only for EMC, Cloud Products, PE, F10, PC");
        item2.Qty.Should().Be(1);
        item2.Msrp.Should().Be(188.35m);
        item2.Cost.Should().Be(97.94m);
        item2.StartDate.Should().BeNull();
        item2.EndDate.Should().Be(new DateOnly(2027, 7, 20));
        item2.SerialNumber.Should().Be("TAG-SAMPLE1");
    }

    [Fact]
    public void Description_OmitsSuffixWhenLineOfBusinessIsMissingOrBlank()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);
        using var temp = new TempFile("""
            {
                "quoteNumber": 12345,
                "quoteVersion": 1,
                "currency": "AUD",
                "items": [{
                    "unitSalesPrice": 100,
                    "skus": [
                        {
                            "skuNumber": "SKU-BLANK",
                            "description": "Blank line of business",
                            "lineOfBusiness": "   ",
                            "quantity": 1,
                            "unitSalesPrice": 50,
                            "unitListPrice": 100,
                            "serviceTags": { "serviceTagNumber": "TAG-A" }
                        },
                        {
                            "skuNumber": "SKU-MISSING",
                            "description": "Missing line of business",
                            "quantity": 1,
                            "unitSalesPrice": 50,
                            "unitListPrice": 100,
                            "serviceTags": { "serviceTagNumber": "TAG-B" }
                        }
                    ]
                }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.LineItems[0].Description.Should().Be("Blank line of business");
        result.LineItems[1].Description.Should().Be("Missing line of business");
    }

    [Fact]
    public void Sort_OrdersByServiceTagNumber_Stable()
    {
        var json = """
            {
                "quoteNumber": 12345,
                "quoteVersion": 1,
                "currency": "AUD",
                "items": [
                    {
                        "unitSalesPrice": 300,
                        "skus": [
                            {
                                "skuNumber": "SKU-B1",
                                "description": "B1",
                                "quantity": 1,
                                "unitSalesPrice": 100,
                                "serviceTags": { "serviceTagNumber": "TAG-B" }
                            },
                            {
                                "skuNumber": "SKU-A1",
                                "description": "A1",
                                "quantity": 1,
                                "unitSalesPrice": 100,
                                "serviceTags": { "serviceTagNumber": "TAG-A" }
                            },
                            {
                                "skuNumber": "SKU-B2",
                                "description": "B2",
                                "quantity": 1,
                                "unitSalesPrice": 100,
                                "serviceTags": { "serviceTagNumber": "TAG-B" }
                            }
                        ]
                    }
                ]
            }
            """;

        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);
        using var temp = new TempFile(json);

        var result = parser.Parse(temp.Path);

        result.LineItems.Should().HaveCount(3);
        result.LineItems[0].SerialNumber.Should().Be("TAG-A");
        result.LineItems[0].Vpn.Should().Be("SKU-A1");
        result.LineItems[0].LineSequence.Should().Be("1");

        result.LineItems[1].SerialNumber.Should().Be("TAG-B");
        result.LineItems[1].Vpn.Should().Be("SKU-B1");
        result.LineItems[1].LineSequence.Should().Be("2");

        result.LineItems[2].SerialNumber.Should().Be("TAG-B");
        result.LineItems[2].Vpn.Should().Be("SKU-B2");
        result.LineItems[2].LineSequence.Should().Be("3");
    }

    [Fact]
    public void EveryChildSkuIsEmitted_EvenWhenItRestatesItsParentItem()
    {
        // The CTO parent/child restatement rules must never be applied here: APOS emits no parent
        // lines, so its SKUs carry the price and dropping one would break the salesPrice
        // reconciliation. The first SKU below echoes its item's base SKU and prices exactly.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);
        using var temp = new TempFile("""
            {
                "quoteNumber": 12345,
                "quoteVersion": 1,
                "currency": "AUD",
                "salesPrice": 125,
                "items": [{
                    "baseSkuNumber": "BASE-SKU",
                    "lineOfBusiness": "Displays",
                    "unitListPriceIncludingShipping": 200,
                    "unitSalesPriceIncludingShipping": 100,
                    "skus": [
                        {
                            "skuNumber": "BASE-SKU",
                            "description": "Restates the parent item exactly",
                            "quantity": 1,
                            "unitListPrice": 200,
                            "unitSalesPrice": 100,
                            "serviceTags": { "serviceTagNumber": "TAG-A" }
                        },
                        {
                            "skuNumber": "OTHER-SKU",
                            "description": "Sibling",
                            "quantity": 1,
                            "unitListPrice": 50,
                            "unitSalesPrice": 25,
                            "serviceTags": { "serviceTagNumber": "TAG-B" }
                        }
                    ]
                }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.LineItems.Select(item => item.Vpn).Should().Equal("BASE-SKU", "OTHER-SKU");
        result.Validation.ComputedTotal.Should().Be(125m);
        result.Validation.Matches.Should().BeTrue();
    }

    [Fact]
    public void EmptyMissingNullOrWhitespaceServiceTags_ClassifyAsCto()
    {
        var parsers = new ParserRegistry().Parsers;
        var cto = parsers.Single(p => p.Slug == ParserSlugs.DellCtoJson);
        var apos = parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);
        using var temp = new TempFile("""
            {
                "quoteNumber": 12345,
                "quoteVersion": 1,
                "items": [{
                    "baseSkuNumber": "BASE-SKU",
                    "quantity": 1,
                    "skus": [
                        { "skuNumber": "SKU-EMPTY", "serviceTags": { "serviceTagNumber": "" } },
                        { "skuNumber": "SKU-NULL", "serviceTags": { "serviceTagNumber": null } },
                        { "skuNumber": "SKU-MISSING", "serviceTags": {} },
                        { "skuNumber": "SKU-WHITESPACE", "serviceTags": { "serviceTagNumber": "   " } },
                        { "skuNumber": "SKU-NO-OBJECT" }
                    ]
                }]
            }
            """);

        cto.Detect(temp.Path).Should().BeGreaterThanOrEqualTo(0.7);
        apos.Detect(temp.Path).Should().BeLessThan(0.7);
        cto.Parse(temp.Path).Metadata.ParserSlug.Should().Be(ParserSlugs.DellCtoJson);
    }

    [Fact]
    public void Validation_Mismatches_When_SalesPrice_Differs_From_Line_Total()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);
        using var temp = new TempFile("""
            {
                "quoteNumber": 12345,
                "quoteVersion": 1,
                "currency": "AUD",
                "salesPrice": 999.99,
                "items": [{
                    "skus": [
                        {
                            "skuNumber": "SKU-A1",
                            "description": "A1",
                            "quantity": 1,
                            "unitSalesPrice": 50,
                            "serviceTags": { "serviceTagNumber": "TAG-A" }
                        }
                    ]
                }]
            }
            """);

        var result = parser.Parse(temp.Path);

        result.Validation.Matches.Should().BeFalse();
        result.Validation.ComputedTotal.Should().Be(50m);
        result.Validation.QuotedTotal.Should().Be(999.99m);
        result.Metadata.QuotedTotal.Should().Be(999.99m);
    }

    [Fact]
    public void MalformedJson_ThrowsDetectError()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);
        using var temp = new TempFile("not json content");

        var act = () => parser.Parse(temp.Path);
        act.Should().Throw<ParseError>()
            .Where(e => e.Stage == "detect");
    }

    [Fact]
    public void CtoJson_ParsedAsApos_ThrowsDetectError()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.DellAposJson);

        var act = () => parser.Parse(SamplePath("Dell_CTO_Sample.json"));
        act.Should().Throw<ParseError>()
            .Where(e => e.Stage == "detect");
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bidparser-dell-apos-test-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, content);
        }

        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }
    }
}
