namespace BidParser.Infrastructure.Entities;

public sealed class ParseJob
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string Vendor { get; set; }
    public required string ParserSlug { get; set; }
    public required string SourceFilename { get; set; }
    public required string SourcePath { get; set; }
    public required string OutputPath { get; set; }
    public decimal FxRate { get; set; }
    public decimal Margin { get; set; }
    public decimal ComputedTotal { get; set; }
    public decimal? QuotedTotal { get; set; }
    public bool TotalsMatch { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? User { get; set; }
}
