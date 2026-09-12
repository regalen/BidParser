namespace BidParser.Infrastructure.Entities;

/// <summary>
/// An application user: credentials, role, and saved preferences. Of the numeric preferences only
/// <see cref="DefaultVendor"/> is updated by the parse flow — FX rate / margin / IM% are never
/// auto-applied to a parse. Timestamps are stamped by <see cref="Persistence.AppDbContext"/>.
/// </summary>
public sealed class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public string? Name { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public bool MustChangePassword { get; set; } = true;
    public string? DefaultVendor { get; set; }
    public decimal? FxRate { get; set; }
    public decimal? Margin { get; set; }
    public decimal? ImPercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<ParseJob> ParseJobs { get; } = new List<ParseJob>();
}
