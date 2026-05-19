namespace BidParser.Api.Contracts;

public sealed record ApiError(string Detail);

public sealed record OkResponse(bool Ok = true);

public sealed record ParseErrorDetail(string Stage, string Hint, string Message);

public sealed record ParseErrorResponse(ParseErrorDetail Detail);

public sealed record PasswordValidationError(IReadOnlyList<string> Detail);
