using System.Text.RegularExpressions;

namespace BidParser.Api.Auth;

public static partial class PasswordPolicy
{
    public static IReadOnlyList<string> Validate(string password)
    {
        var errors = new List<string>();
        if (password.Length < 8)
        {
            errors.Add("Password must be at least 8 characters.");
        }
        if (!UppercaseRegex().IsMatch(password))
        {
            errors.Add("Password must include an uppercase letter.");
        }
        if (!DigitRegex().IsMatch(password))
        {
            errors.Add("Password must include a digit.");
        }
        if (!SymbolRegex().IsMatch(password))
        {
            errors.Add("Password must include a symbol.");
        }
        return errors;
    }

    [GeneratedRegex("[A-Z]")]
    private static partial Regex UppercaseRegex();

    [GeneratedRegex("\\d")]
    private static partial Regex DigitRegex();

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex SymbolRegex();
}
