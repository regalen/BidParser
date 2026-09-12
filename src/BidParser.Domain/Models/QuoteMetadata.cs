namespace BidParser.Domain.Models;

/// <summary>Header-level facts about a parsed quote (number, supplier, currency, source file, parser slug).</summary>
public sealed record QuoteMetadata
{
    public required string QuoteNumber { get; init; }

    // Bid identity is display-and-search only — it never reaches the CRM workbook, line items,
    // or validation. It is therefore best-effort: `required` so every parser
    // must state its intent, but nullable so an unreadable header degrades the label instead of
    // failing a parse that would otherwise produce a usable workbook.
    public required string? BidNumber { get; init; }
    public required string? BidRevision { get; init; }
    public required string Supplier { get; init; }
    public required string Currency { get; init; }
    public decimal? QuotedTotal { get; init; }
    public required string SourceFilename { get; init; }
    public required string ParserSlug { get; init; }
}
