using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Parsing.Registry;
using FluentAssertions;
using Xunit;

namespace BidParser.Parsing.Tests;

// Verifies that each parser's Detect() signature uniquely identifies its own format
// among the sibling formats of the same vendor + MIME — the candidate set used to
// suggest the correct file type when a user picks the wrong one.
public sealed class WrongFileTypeDetectionTests
{
    private const double Threshold = FormatDetection.MinConfidence;

    [Theory]
    // Nutanix XLSX trio
    [InlineData("XQ-9100002.xlsx", "nutanix_software_only_xlsx")]
    [InlineData("XQ-9100003.xlsx", "nutanix_hardware_only_xlsx")]
    [InlineData("XQ-9100010.xlsx", "nutanix_renewal_xlsx")]
    // Nutanix PDF trio
    [InlineData("XQ-9100002.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-9100005.pdf", "nutanix_software_only_pdf")]
    [InlineData("XQ-9100004.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100007.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100001.pdf", "nutanix_renewal_pdf")]
    [InlineData("XQ-9100003.pdf", "nutanix_hardware_only_pdf")]
    // HP XLSX quartet
    [InlineData("Deals_Sample_01_HPI.xlsx", "hp_bid_xlsx")]
    [InlineData("Deals_Sample_02_HPI.xlsx", "hp_bid_xlsx")]
    [InlineData("translate_quote_98000001_v25_all.xlsx", "hp_global_bid_xlsx")]
    [InlineData("99000001.xlsx", "hp_oneconfig_xlsx")]
    [InlineData("CH9000000001.xlsx", "hp_services_xlsx")]
    [InlineData("CH9000000002.xlsx", "hp_services_xlsx")]
    // Dell JSON duo
    [InlineData("Dell_CTO_Sample.json", "dell_cto_json")]
    [InlineData("Dell_Peripherals_Sample.json", "dell_cto_json")]
    [InlineData("Dell_APOS_Sample.json", "dell_apos_json")]
    // Lenovo (LBP-E accepts XLS/XLSX while LBP-I accepts PDF, so their accepted
    // MIME sets do not overlap; both carry a Detect() signature for Auto)
    [InlineData("BRDAD019200001.xls", "lenovo_lbpe_isg_xls")]
    [InlineData("Bid_Platform_Bid_Request_Sample_03.xls", "lenovo_lbpe_isg_xls")]
    [InlineData("Bid_Platform_Bid_Request_Sample_04.xlsx", "lenovo_lbpe_isg_xls")]
    [InlineData("Bid_Platform_Bid_Request_Sample_05.xlsx", "lenovo_lbpe_isg_xls")]
    [InlineData("Bid_Platform_Bid_Request_Sample_06.xlsx", "lenovo_lbpe_isg_xls")]
    [InlineData("Bid_Platform_Bid_Request_Sample_07.xlsx", "lenovo_lbpe_isg_xls")]
    [InlineData("Bid_Platform_Bid_Request_Sample_08.xlsx", "lenovo_lbpe_isg_xls")]
    [InlineData("BRDAS019000004V1.pdf", "lenovo_lbpi_isg_pdf")]
    [InlineData("BRDAS019000005V1.pdf", "lenovo_lbpi_isg_pdf")]
    [InlineData("BRDAS019000001V1.pdf", "lenovo_lbpi_isg_pdf")]
    [InlineData("BRDAS019000003V1.pdf", "lenovo_lbpi_isg_pdf")]
    [InlineData("BRDAS019000002V1.pdf", "lenovo_lbpi_isg_pdf")]
    [InlineData("BRDAS019000006V1.pdf", "lenovo_lbpi_isg_pdf")]
    [InlineData("BRDAS019000007V1.pdf", "lenovo_lbpi_isg_pdf")]
    // Lenovo IDG is its own vendor (Vendors.LenovoIdg), so it has no same-vendor PDF sibling for
    // this sweep to exclude — see Detect_LenovoIdgAndIsg_DoNotCrossDetect for the explicit check.
    [InlineData("BRPAS019100001V1.pdf", "lenovo_lbpi_idg_pdf")]
    [InlineData("BRPAS019100002V1.pdf", "lenovo_lbpi_idg_pdf")]
    [InlineData("BRPAS019100003V1.pdf", "lenovo_lbpi_idg_pdf")]
    // Zebra PCR duo (different MIMEs — no same-MIME siblings to exclude)
    [InlineData("Zebra_PC_97000001_V2.0.pdf", "zebra_pcr_pdf")]
    [InlineData("Zebra_PC_97000002_V2.0.pdf", "zebra_pcr_pdf")]
    [InlineData("Zebra_PC_97000003_V1.0.pdf", "zebra_pcr_pdf")]
    [InlineData("Zebra_PC_97000001.xls", "zebra_pcr_xls")]
    [InlineData("Zebra_PC_97000002.xls", "zebra_pcr_xls")]
    [InlineData("Zebra_PC_97000003.xls", "zebra_pcr_xls")]
    [InlineData("Datalogic_PE930003.pdf", "datalogic_quote_pdf")]
    [InlineData("Datalogic_PE930001.pdf", "datalogic_quote_pdf")]
    [InlineData("Datalogic_PE930002.pdf", "datalogic_quote_pdf")]
    [InlineData("Epson_96000002.pdf", "epson_quote_pdf")]
    [InlineData("Epson_96000001.pdf", "epson_quote_pdf")]
    [InlineData("Epson_96000003.pdf", "epson_quote_pdf")]
    [InlineData("Strike_Quote_9202.pdf", "strike_quote_pdf")]
    [InlineData("Strike_Quote_9201.pdf", "strike_quote_pdf")]
    [InlineData("Strike_Quote_9203.pdf", "strike_quote_pdf")]
    [InlineData("Trellix_Quote_900001.pdf", ParserSlugs.TrellixQuotePdf)]
    [InlineData("Trellix_Quote_900002.pdf", ParserSlugs.TrellixQuotePdf)]
    public void Detect_uniquely_identifies_format_among_vendor_siblings(string inputName, string expectedSlug)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var expected = parsers.Single(p => p.Slug == expectedSlug);
        var path = Path.Combine(root, "samples", "inputs", inputName);
        var ext = Path.GetExtension(inputName);
        var mime = ExtensionToMime[ext];

        // The correct parser recognises the file.
        expected.Detect(path).Should().BeGreaterThanOrEqualTo(Threshold,
            "the matching parser should recognise {0}", inputName);

        // No sibling of the same vendor + upload MIME crosses the confidence threshold.
        var siblings = parsers.Where(p =>
            p.Slug != expectedSlug
            && p.Vendor == expected.Vendor
            && p.AcceptedMimes.Contains(mime, StringComparer.OrdinalIgnoreCase));

        foreach (var sibling in siblings)
        {
            sibling.Detect(path).Should().BeLessThan(Threshold,
                "sibling {0} should not claim {1}", sibling.Slug, inputName);
        }
    }

    // Lenovo IDG and ISG now belong to different vendors, so the sweep above never compares them
    // against each other; this pins the cross-format separation explicitly instead.
    [Theory]
    [InlineData("BRPAS019100001V1.pdf")]
    [InlineData("BRPAS019100002V1.pdf")]
    [InlineData("BRPAS019100003V1.pdf")]
    public void Detect_IdgFixtures_DoNotCrossDetectAsIsg(string inputName)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var isg = parsers.Single(p => p.Slug == "lenovo_lbpi_isg_pdf");
        var path = Path.Combine(root, "samples", "inputs", inputName);

        isg.Detect(path).Should().BeLessThan(Threshold, "ISG should not claim IDG fixture {0}", inputName);
    }

    [Theory]
    [InlineData("BRDAS019000004V1.pdf")]
    [InlineData("BRDAS019000001V1.pdf")]
    [InlineData("BRDAS019000003V1.pdf")]
    [InlineData("BRDAS019000002V1.pdf")]
    [InlineData("BRDAS019000006V1.pdf")]
    [InlineData("BRDAS019000007V1.pdf")]
    public void Detect_IsgFixtures_DoNotCrossDetectAsIdg(string inputName)
    {
        var root = TestSample.Root;
        var parsers = new ParserRegistry().Parsers;
        var idg = parsers.Single(p => p.Slug == "lenovo_lbpi_idg_pdf");
        var path = Path.Combine(root, "samples", "inputs", inputName);

        idg.Detect(path).Should().BeLessThan(Threshold, "IDG should not claim ISG fixture {0}", inputName);
    }

    [Theory]
    [InlineData(ParserSlugs.DatalogicQuotePdf, "Datalogic_PE930003.pdf")]
    [InlineData(ParserSlugs.EpsonQuotePdf, "Epson_96000002.pdf")]
    [InlineData(ParserSlugs.StrikeQuotePdf, "Strike_Quote_9202.pdf")]
    public void New_pdf_formats_do_not_cross_detect(string expectedSlug, string inputName)
    {
        var parsers = new ParserRegistry().Parsers;
        var path = Path.Combine(TestSample.Root, "samples", "inputs", inputName);
        foreach (var parser in parsers.Where(parser => parser.AcceptedMimes.Contains("application/pdf") && parser.Slug != expectedSlug))
            parser.Detect(path).Should().BeLessThan(Threshold, "{0} should not claim {1}", parser.Slug, inputName);
    }

    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".json"] = "application/json"
    };

}
