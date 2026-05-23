namespace BidParser.Infrastructure.Entities;

public sealed class ParseMetric
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public int? ParseJobId { get; set; }

    public required string UserUsername { get; set; }
    public string? UserName { get; set; }

    public required string Vendor { get; set; }
    public required string ParserSlug { get; set; }

    public required string SourceFilename { get; set; }
    public required string Currency { get; set; }
    public decimal? QuotedTotal { get; set; }
    public decimal ComputedTotal { get; set; }
    public bool TotalsMatch { get; set; }
    public decimal FxRate { get; set; }
    public decimal Margin { get; set; }

    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public ParseJob? ParseJob { get; set; }
}
