namespace BidParser.Infrastructure.Entities;

/// <summary>
/// A committed successful parse: the inputs (vendor, parser, template, FX/margin), the stored
/// source/output file paths, and the computed/quoted totals with the match flag. The source of truth
/// for a run — both input and output remain downloadable until purged at RETENTION_DAYS.
/// </summary>
public sealed class ParseJob
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string Vendor { get; set; }
    public required string ParserSlug { get; set; }
    public required string CrmTemplate { get; set; }
    public required string SourceFilename { get; set; }
    public string? BidNumber { get; set; }
    public string? BidRevision { get; set; }
    public required string SourcePath { get; set; }
    public required string OutputPath { get; set; }
    public decimal FxRate { get; set; }
    public decimal Margin { get; set; }
    public decimal ComputedTotal { get; set; }
    public decimal? QuotedTotal { get; set; }
    public bool TotalsMatch { get; set; }
    public bool SplitBySolutionId { get; set; }
    public ImportType? ImportType { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? User { get; set; }
}
