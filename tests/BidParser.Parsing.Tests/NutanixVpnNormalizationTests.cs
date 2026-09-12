using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class NutanixVpnNormalizationTests
{
    public static TheoryData<string, string> NutanixFormats => new()
    {
        { ParserSlugs.NutanixSoftwareOnlyPdf, "XQ-9100002.pdf" },
        { ParserSlugs.NutanixSoftwareOnlyXlsx, "XQ-9100002.xlsx" },
        { ParserSlugs.NutanixRenewalPdf, "XQ-9100004.pdf" },
        { ParserSlugs.NutanixRenewalXlsx, "XQ-9100010.xlsx" },
        { ParserSlugs.NutanixHardwareOnlyPdf, "XQ-9100003.pdf" },
        { ParserSlugs.NutanixHardwareOnlyXlsx, "XQ-9100003.xlsx" }
    };

    [Theory]
    [MemberData(nameof(NutanixFormats))]
    public void Emits_uppercase_vpns_for_every_nutanix_format(string slug, string inputName)
    {
        var parser = new ParserRegistry().Parsers.Single(candidate => candidate.Slug == slug);
        var result = parser.Parse(Path.Combine(TestSample.Root, "samples", "inputs", inputName));

        result.LineItems.Should().OnlyContain(
            item => item.Vpn == item.Vpn.ToUpperInvariant(),
            "Nutanix VPNs must match SAP's uppercase identifiers");
    }

}
