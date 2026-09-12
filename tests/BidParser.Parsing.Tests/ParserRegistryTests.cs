using BidParser.Domain.Constants;
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
                "nutanix_renewal_xlsx",
                "nutanix_hardware_only_pdf",
                "nutanix_hardware_only_xlsx",
                "hp_bid_xlsx",
                "hp_global_bid_xlsx",
                "hp_oneconfig_xlsx",
                "hp_services_xlsx",
                "hpe_bid_xlsx",
                "lenovo_lbpe_isg_xls",
                "lenovo_lbpi_isg_pdf",
                "lenovo_lbpi_idg_pdf",
                "zebra_pcr_pdf",
                "zebra_pcr_xls",
                "dell_cto_json",
                "dell_apos_json",
                "cisco_ccw_quote_xls",
                "datalogic_quote_pdf",
                "epson_quote_pdf",
                "strike_quote_pdf");
    }

    [Fact]
    public void Auto_detect_templates_are_supported_by_every_parser_for_the_vendor()
    {
        var parsers = new ParserRegistry().Parsers;

        foreach (var auto in AutoDetectTypes.All)
        {
            parsers.Where(parser => parser.Vendor == auto.Vendor)
                .Should().OnlyContain(parser => parser.AvailableTemplates.Contains(auto.CrmTemplate));
        }
    }

    [Fact]
    public void Auto_detect_split_support_is_backed_by_every_parser_for_the_vendor()
    {
        var parsers = new ParserRegistry().Parsers;

        // The synthetic Auto entry advertises the split checkbox before the concrete parser is
        // resolved, so it may only claim support the whole vendor can honour.
        foreach (var auto in AutoDetectTypes.All.Where(auto => auto.SupportsSolutionIdSplit))
        {
            parsers.Where(parser => parser.Vendor == auto.Vendor)
                .Should().OnlyContain(parser => parser.SupportsSolutionIdSplit);
        }
    }

    [Fact]
    public void Auto_detect_on_cost_support_is_backed_by_every_parser_for_the_vendor()
    {
        var parsers = new ParserRegistry().Parsers;
        foreach (var auto in AutoDetectTypes.All.Where(auto => auto.SupportsOnCost))
            parsers.Where(parser => parser.Vendor == auto.Vendor).Should().OnlyContain(parser => parser.SupportsOnCost);
    }

    [Fact]
    public void Dell_parsers_offer_only_no_calculation()
    {
        var dellParsers = new ParserRegistry().Parsers
            .Where(parser => parser.Vendor == Vendors.Dell);

        dellParsers.Should().HaveCount(2)
            .And.OnlyContain(parser =>
                parser.AvailableTemplates.Count == 1 &&
                parser.AvailableTemplates[0] == CrmTemplates.NoCalculation);
    }

    [Fact]
    public void Accepted_mimes_are_valid_and_contain_accepted_mime()
    {
        var parsers = new ParserRegistry().Parsers;

        foreach (var parser in parsers)
        {
            parser.AcceptedMimes.Should().NotBeNullOrEmpty();
            parser.AcceptedMimes.Should().Contain(parser.AcceptedMime);
            parser.AcceptedMimes.Should().OnlyHaveUniqueItems();
            parser.AcceptedMimes.Should().NotContain(m => string.IsNullOrWhiteSpace(m));
        }
    }
}
