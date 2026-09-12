using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Domain.Constants;
using BidParser.Infrastructure.Services;

namespace BidParser.Api.Endpoints;

public static class RuntimeConfigurationEndpoints
{
    private static readonly string[] HtmlAllowlist = ["p", "ul", "ol", "li", "strong", "b", "em", "i", "span", "br", "span style=color"];

    public static IEndpointRouteBuilder MapRuntimeConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/parse-ui-config", GetParseUiConfigAsync)
            .RequireAuthorization(AuthPolicies.ActiveUser);

        var admin = app.MapGroup("/api/admin/runtime-config")
            .RequireAuthorization(AuthPolicies.Admin);
        admin.MapGet("", GetAdminAsync);
        admin.MapPut("/guidance-messages", (HttpRequest request, RuntimeConfigurationService service, CancellationToken ct) => UpdateAsync(request, service, RuntimeConfigKeys.GuidanceMessages, ct))
            .AddEndpointFilter<RequireCsrfHeader>();
        admin.MapPut("/vendor-defaults", (HttpRequest request, RuntimeConfigurationService service, CancellationToken ct) => UpdateAsync(request, service, RuntimeConfigKeys.VendorDefaults, ct))
            .AddEndpointFilter<RequireCsrfHeader>();
        return app;
    }

    private static async Task<IResult> GetParseUiConfigAsync(RuntimeConfigurationService service, CancellationToken ct)
    {
        var config = await service.GetParseUiConfigurationAsync(ct);
        var defaults = config.VendorDefaults.ToDictionary(
            pair => pair.Key,
            pair => new RuntimeVendorDefaultsResponse(
                pair.Value.FxRate, pair.Value.Margin, pair.Value.ImPercent, pair.Value.OnCostPct),
            StringComparer.Ordinal);
        return Results.Ok(new ParseUiConfigResponse(defaults, config.GuidanceByParserSlug));
    }

    private static async Task<IResult> GetAdminAsync(RuntimeConfigurationService service, CancellationToken ct)
    {
        var guidance = await service.GetDocumentAsync(RuntimeConfigKeys.GuidanceMessages, ct);
        var defaults = await service.GetDocumentAsync(RuntimeConfigKeys.VendorDefaults, ct);
        return Results.Ok(new RuntimeConfigurationAdminResponse(
            ToResponse(guidance), ToResponse(defaults),
            service.ParserReferences.Select(parser => new RuntimeConfigParserReferenceResponse(parser.Slug, parser.DisplayName, parser.Vendor)).ToList(),
            service.VendorReferences,
            RuntimeConfigurationService.SupportedFields.Select(field => new RuntimeConfigFieldResponse(field.Key, field.Label, field.Scale)).ToList(),
            HtmlAllowlist));
    }

    private static async Task<IResult> UpdateAsync(HttpRequest request, RuntimeConfigurationService service, string key, CancellationToken ct)
    {
        var body = await EndpointHelpers.ReadJsonBodyAsync<RuntimeConfigUpdateRequest>(request, ct);
        if (!body.IsSuccess || body.Value is null || string.IsNullOrWhiteSpace(body.Value.Json))
        {
            return EndpointHelpers.ValidationProblem(body.Error ?? "Invalid request body.");
        }
        try
        {
            return Results.Ok(ToResponse(await service.SaveDocumentAsync(key, body.Value.Json, ct)));
        }
        catch (RuntimeConfigurationValidationException exception)
        {
            return EndpointHelpers.ValidationProblem(exception.Detail);
        }
    }

    private static RuntimeConfigDocumentResponse ToResponse(RuntimeConfigDocument document) =>
        new(document.Json, document.UpdatedAt, document.ValidationError);
}
