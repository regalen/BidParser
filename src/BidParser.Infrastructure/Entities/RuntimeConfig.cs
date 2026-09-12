namespace BidParser.Infrastructure.Entities;

/// <summary>
/// One independently editable runtime-configuration document. Timestamps are assigned exclusively
/// by <see cref="Persistence.AppDbContext"/>.
/// </summary>
public sealed class RuntimeConfig
{
    public required string Key { get; set; }
    public required string JsonPayload { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
