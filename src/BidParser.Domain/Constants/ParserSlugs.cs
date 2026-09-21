namespace BidParser.Domain.Constants;

/// <summary>
/// Canonical parser slugs (vendor + file-type), one per registered format. Use these
/// constants everywhere — never inline the string literals. Slugs are stable identifiers
/// persisted on ParseJob and runtime guidance configuration, so treat them as immutable.
/// </summary>
public static class ParserSlugs
{
    public const string NutanixAuto = "nutanix_auto";
    public const string NutanixSoftwareOnlyPdf = "nutanix_software_only_pdf";
    public const string NutanixSoftwareOnlyXlsx = "nutanix_software_only_xlsx";
    public const string NutanixRenewalPdf = "nutanix_renewal_pdf";
    public const string NutanixRenewalXlsx = "nutanix_renewal_xlsx";
    public const string NutanixHardwareOnlyPdf = "nutanix_hardware_only_pdf";
    public const string NutanixHardwareOnlyXlsx = "nutanix_hardware_only_xlsx";
    public const string HpBidXlsx = "hp_bid_xlsx";
    public const string HpOneConfigXlsx = "hp_oneconfig_xlsx";
    public const string HpGlobalBidXlsx = "hp_global_bid_xlsx";
    public const string HpServicesXlsx = "hp_services_xlsx";
    public const string HpeBidXlsx = "hpe_bid_xlsx";
    public const string LenovoAuto = "lenovo_auto";
    public const string LenovoLbpeIsgXls = "lenovo_lbpe_isg_xls";
    public const string LenovoLbpiIsgPdf = "lenovo_lbpi_isg_pdf";
    public const string LenovoLbpiIdgPdf = "lenovo_lbpi_idg_pdf";
    public const string ZebraAuto = "zebra_auto";
    public const string ZebraPcrPdf = "zebra_pcr_pdf";
    public const string ZebraPcrXls = "zebra_pcr_xls";
    public const string DellAuto = "dell_auto";
    public const string DellCtoJson = "dell_cto_json";
    public const string DellAposJson = "dell_apos_json";
    public const string CiscoCcwQuoteXls = "cisco_ccw_quote_xls";
    public const string DatalogicQuotePdf = "datalogic_quote_pdf";
    public const string EpsonQuotePdf = "epson_quote_pdf";
    public const string StrikeQuotePdf = "strike_quote_pdf";
    public const string TrellixAuto = "trellix_auto";
    public const string TrellixQuotePdf = "trellix_quote_pdf";
    public const string TrellixQuoteXlsm = "trellix_quote_xlsm";
}
