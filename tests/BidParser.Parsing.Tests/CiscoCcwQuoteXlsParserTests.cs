using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cisco.CcwQuoteXls;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class CiscoCcwQuoteXlsParserTests
{

    private static BidParser.Domain.Models.ParseResult Parse()
    {
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.CiscoCcwQuoteXls);
        return parser.Parse(Path.Combine(TestSample.Root, "samples", "inputs", "Quote_9400000001.xls"));
    }

    [Fact]
    public void Metadata_IsCorrect()
    {
        var result = Parse();

        result.Metadata.QuoteNumber.Should().Be("9400000001");
        result.Metadata.Supplier.Should().Be(Vendors.Cisco);
        result.Metadata.Currency.Should().Be("AUD");
        result.Metadata.QuotedTotal.Should().BeNull();
        result.Metadata.ParserSlug.Should().Be(ParserSlugs.CiscoCcwQuoteXls);
    }

    [Fact]
    public void Validation_IsNeutral()
    {
        var result = Parse();

        result.Validation.Matches.Should().BeTrue();
        result.Validation.Difference.Should().Be(0m);
        result.Validation.QuotedTotal.Should().BeNull();
        result.Validation.ComputedTotal.Should().Be(716644.04m);
    }

    [Fact]
    public void LineCount_102Lines_12Parents_90Children()
    {
        var result = Parse();

        result.LineItems.Should().HaveCount(102);
        var parents = result.LineItems.Where(i => !i.LineSequence!.Contains('.')).ToList();
        var children = result.LineItems.Where(i => i.LineSequence!.Contains('.')).ToList();

        parents.Should().HaveCount(12);
        children.Should().HaveCount(90);
    }

    [Fact]
    public void LineSequence_FlattensThreeLevels()
    {
        var result = Parse();
        var itemBySeq = result.LineItems.ToDictionary(i => i.LineSequence!);

        itemBySeq["2.01"].Vpn.Should().Be("CON-SNT-C920CX92");
        itemBySeq["2.03"].Vpn.Should().Be("C9200CX-DNAE8-5Y");
        itemBySeq["3.18"].Vpn.Should().Be("NETWORK-PNP-LIC");
    }

    [Fact]
    public void ContinuationRowsFoldIntoComments()
    {
        var result = Parse();
        var line1 = result.LineItems.Single(i => i.LineSequence == "1");
        var line101 = result.LineItems.Single(i => i.LineSequence == "1.01");

        line1.Comments.Should().StartWith("Requested Start Date : 24-Jun-2026| Requested For : 60.00 Months");
        line1.Comments.Should().EndWith("Included Item/Support: No");

        line101.Comments.Should().Be("Included Item/Support: No");
    }

    [Fact]
    public void PricingComesFromDurationInclusiveMsrpAndUnitNetPrice()
    {
        var result = Parse();
        var itemBySeq = result.LineItems.ToDictionary(i => i.LineSequence!);

        var line102 = itemBySeq["1.02"];
        line102.Msrp.Should().Be(4635.49m);
        line102.Cost.Should().Be(24.04m);

        var line2 = itemBySeq["2"];
        line2.Msrp.Should().Be(9241.86m);
        line2.Cost.Should().Be(1929.92m);

        var line301 = itemBySeq["3.01"];
        line301.Msrp.Should().Be(16708.40m); // rounded artefact
    }

    [Fact]
    public void NonBreakingSpacesNormalised()
    {
        var result = Parse();
        var line102 = result.LineItems.Single(i => i.LineSequence == "1.02");

        line102.Description.Should().Be("Cisco Switching Essentials Tier 1, Medium");
    }

    [Fact]
    public void RawCapturesSourceValuesNotDerivedOnes()
    {
        var result = Parse();

        // Raw is provenance: it must hold what the workbook said, not what we computed from it.
        var support = result.LineItems.Single(i => i.LineSequence == "2.01");
        support.Raw["#"].Should().Be("2.0.1", "Raw keeps Cisco's own 3-level number, not the flattened one");
        support.Raw["Part Number"].Should().Be("CON-SNT-C920CX92");

        // The unrounded source value is exactly what you would want when investigating the
        // rounding, so it must survive into Raw.
        var artefact = result.LineItems.Single(i => i.LineSequence == "3.01");
        artefact.Msrp.Should().Be(16708.40m);
        artefact.Raw["Unit List Price (With Duration)"].Should().Be("16708.399999999998");

        var subscription = result.LineItems.Single(i => i.LineSequence == "1");
        subscription.Raw["Continuation"].Should().StartWith("Requested Start Date : 24-Jun-2026");
    }

    [Fact]
    public void NonAudQuoteIsRejected()
    {
        // Patch the standalone "AUD" BIFF8 shared-string entry (2-byte length, 1-byte flags,
        // then the characters) to "USD" — equal length, so every record offset is preserved.
        var bytes = File.ReadAllBytes(Path.Combine(TestSample.Root, "samples", "inputs", "Quote_9400000001.xls"));
        var marker = new byte[] { 0x03, 0x00, 0x00, (byte)'A', (byte)'U', (byte)'D' };
        var at = IndexOf(bytes, marker);
        at.Should().BeGreaterThan(-1, "the fixture should contain a standalone 'AUD' string record");
        bytes[at + 3] = (byte)'U';
        bytes[at + 4] = (byte)'S';

        var temp = Path.Combine(Path.GetTempPath(), $"cisco-usd-{Guid.NewGuid():N}.xls");
        try
        {
            File.WriteAllBytes(temp, bytes);
            var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.CiscoCcwQuoteXls);

            var act = () => parser.Parse(temp);

            act.Should().Throw<ParseError>()
                .Which.Stage.Should().Be("currency");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public void HtmlDisguisedXlsIsAWrongFileTypeNotAnUnhandledException()
    {
        // Zebra exports styled HTML under a .xls name and magic-byte validation deliberately
        // allows it through, so it reaches this parser. It must read as a wrong-file-type
        // selection, not leak ExcelDataReader's HeaderException as an unhandled failure.
        var parser = new ParserRegistry().Parsers.Single(p => p.Slug == ParserSlugs.CiscoCcwQuoteXls);
        var zebra = Path.Combine(TestSample.Root, "samples", "inputs", "Zebra_PC_97000001.xls");

        var act = () => parser.Parse(zebra);

        act.Should().Throw<ParseError>()
            .Which.Stage.Should().Be("detect");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    [Fact]
    public void ParserExposesSingleNoCalculationTemplate()
    {
        IParser parser = new CiscoCcwQuoteXlsParser();

        parser.Slug.Should().Be(ParserSlugs.CiscoCcwQuoteXls);
        parser.DisplayName.Should().Be("CCW Quote (XLS)");
        parser.AcceptedMime.Should().Be("application/vnd.ms-excel");
        parser.Vendor.Should().Be(Vendors.Cisco);
        parser.CrmTemplate.Should().Be(CrmTemplates.NoCalculation);
        parser.AvailableTemplates.Should().Equal(CrmTemplates.NoCalculation);
        parser.Detect(Path.Combine(TestSample.Root, "samples", "inputs", "Quote_9400000001.xls")).Should().Be(0.0);
    }
}
