namespace BidParser.Infrastructure.Entities;

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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<ParseJob> ParseJobs { get; } = new List<ParseJob>();
}
