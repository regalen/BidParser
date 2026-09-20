namespace BidParser.Domain.Constants;

/// <summary>Canonical vendor display names. Use these constants — never inline the literals.</summary>
public static class Vendors
{
    public const string Nutanix = "Nutanix";
    public const string Hp = "HP";
    public const string Hpe = "HPE";
    /// <summary>UI/dropdown vendor grouping for Lenovo's ISG (Infrastructure Solutions Group) formats: LBP-E ISG and LBP-I ISG.</summary>
    public const string LenovoIsg = "Lenovo ISG";
    /// <summary>UI/dropdown vendor grouping for Lenovo's IDG (Intelligent Devices Group) formats: LBP-I IDG.</summary>
    public const string LenovoIdg = "Lenovo IDG";
    /// <summary>The vendor name written to Col B (Vendor Name) of the CRM workbook for every Lenovo format. CRM knows one "LENOVO" vendor; the ISG/IDG split is a BidParser-side UI grouping only. See <c>IParser.OutputVendorName</c>.</summary>
    public const string LenovoOutput = "Lenovo";
    public const string Zebra = "Zebra";
    public const string Dell = "Dell";
    public const string Cisco = "Cisco";
    public const string Datalogic = "Datalogic";
    public const string Epson = "Epson";
    public const string Strike = "Strike";
    public const string Trellix = "Trellix";
}
