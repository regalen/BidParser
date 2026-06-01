using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

public sealed class ParserRegistryTests
{
    [Fact]
    public void Registry_is_explicit_and_ordered()
    {
        new ParserRegistry().Parsers
            .Select(parser => parser.Slug)
            .Should()
            .Equal(
                "nutanix_software_only_pdf",
                "nutanix_software_only_xlsx",
                "nutanix_renewal_pdf",
                "nutanix_hardware_only_pdf",
                "nutanix_hardware_only_xlsx",
                "hp_bid_xlsx",
                "hp_global_bid_xlsx",
                "hp_oneconfig_xlsx",
                "lenovo_brda_dcg_pdf",
                "lenovo_brda_dcg_xlsx",
                "zebra_price_concession_pdf",
                "zebra_price_concession_xls");
    }
}
