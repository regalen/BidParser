namespace BidParser.Api.Auth;

/// <summary>Authorization policy names used across the API (their requirements are configured in Program.cs).</summary>
public static class AuthPolicies
{
    public const string LoggedIn = "LoggedIn";
    public const string ActiveUser = "ActiveUser";
    public const string Admin = "Admin";
}
