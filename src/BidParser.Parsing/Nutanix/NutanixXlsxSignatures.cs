namespace BidParser.Parsing.Nutanix;

/// <summary>
/// Anchor strings shared by the Nutanix XLSX parsers and their <c>Detect</c> signatures.
/// </summary>
internal static class NutanixXlsxSignatures
{
    /// <summary>
    /// Banner that marks the reseller-facing Quote D section. Present in Hardware Only
    /// (multi-quote) workbooks; absent from single-quote Software Only / Renewal workbooks.
    /// </summary>
    public const string QuoteDBanner = "Quote D For distributor to quote to the reseller only";
}
