namespace BidParser.Infrastructure.Entities;

/// <summary>
/// Admin-configurable mapping from a parser slug (vendor + file type combination)
/// to a free-text "report type" string shown to users in the parse-result popup,
/// advising which report type to use when sending the quote to the customer.
/// One row per configured parser slug; absence means "no report type configured".
/// </summary>
public sealed class ReportTypeConfig
{
    public int Id { get; set; }
    public required string ParserSlug { get; set; }
    public required string ReportType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
