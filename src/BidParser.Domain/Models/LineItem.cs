namespace BidParser.Domain.Models;

public sealed record LineItem
{
    public required string Vpn { get; init; }
    public string? Description { get; init; }
    public int? Term { get; init; }
    public decimal? Msrp { get; init; }
    public required decimal Cost { get; init; }
    public required int Qty { get; init; }
    public string? SerialNumber { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public IReadOnlyDictionary<string, string> Raw { get; init; } = new Dictionary<string, string>();
}
