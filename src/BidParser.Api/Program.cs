using BidParser.Api.Auth;
using BidParser.Api.Contracts;
using BidParser.Api.Endpoints;
using BidParser.Api.Hosting;
using BidParser.Api.Middleware;
using BidParser.Api.Options;
using BidParser.Application.Output;
using BidParser.Application.Parsing;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Dell;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using BidParser.Infrastructure.Storage;
using BidParser.Parsing.Registry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

// Composition root. Builds configuration (AppOptions), registers services — EF Core pool, data
// protection, cookie auth + the LoggedIn/ActiveUser/Admin authorization policies, the parser registry,
// ParseService, rate limiters, and the migration / admin-seed / retention hosted services — then wires
// the middleware pipeline (forwarded headers → exception handler → security headers → static files →
// auth → rate limiter) and maps the SPA fallback plus the /api endpoint groups. The `public partial
// class Program;` at the bottom lets the WebApplicationFactory test host reference the entry point.
var builder = WebApplication.CreateBuilder(args);

var appOptions = AppOptions.FromConfiguration(builder.Configuration, builder.Environment);
appOptions.EnsureDirectories();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = appOptions.MaxUploadBytes;
});

builder.Services.AddSingleton(appOptions);
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(appOptions.ConnectionString,
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(appOptions.DataProtectionKeysDir))
    .SetApplicationName(appOptions.SessionSecret);
builder.Services.AddSingleton<AuthRateLimiter>();
builder.Services.AddSingleton<SessionTokenService>();
builder.Services.AddAuthentication(SessionCookieAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionCookieAuthHandler>(SessionCookieAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.LoggedIn, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(AuthPolicies.ActiveUser, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => !context.User.HasClaim("mustChangePassword", "true"));
    });
    options.AddPolicy(AuthPolicies.Admin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("admin");
        policy.RequireAssertion(context => !context.User.HasClaim("mustChangePassword", "true"));
    });
});
builder.Services.AddSingleton<IExportEnvironment, DefaultExportEnvironment>();
builder.Services.AddSingleton<IParserRegistry, ParserRegistry>();
builder.Services.AddSingleton<ParserCatalog>();
builder.Services.AddSingleton<SourceFormatInspector>();
builder.Services.AddSingleton<QuoteParseService>();
builder.Services.AddSingleton<WorkbookWriteService>();
builder.Services.AddSingleton(new FileStorage(appOptions.UploadDir));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DellAuthTokenProvider>();
builder.Services.AddSingleton<IDellAuthTokenCache>(services => services.GetRequiredService<DellAuthTokenProvider>());
builder.Services.AddSingleton<DellRateLimiter>();
builder.Services.AddSingleton(new DellQuoteClientOptions(appOptions.MaxUploadBytes));
builder.Services.AddScoped<DellApiSettingsService>();
builder.Services.AddScoped<IDellApiSettingsReader>(services => services.GetRequiredService<DellApiSettingsService>());
builder.Services.AddHttpClient(DellAuthTokenProvider.HttpClientName, client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<DellQuoteClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IDellQuoteClient>(services => services.GetRequiredService<DellQuoteClient>());
builder.Services.AddScoped<ParseService>();
builder.Services.AddScoped<FailedParseJobRecorder>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddScoped<RuntimeConfigurationService>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiError("Too many parse requests. Please try again later."),
            cancellationToken);
    };

    options.AddPolicy("parse", httpContext =>
    {
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            TokensPerPeriod = 5,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            AutoReplenishment = true,
            QueueLimit = 0
        });
    });
});
builder.Services.AddHostedService<MigratorHostedService>();
builder.Services.AddHostedService<BootstrapAdminHostedService>();
builder.Services.AddHostedService<RuntimeConfigurationBootstrapHostedService>();
builder.Services.AddHostedService<RetentionBackgroundService>();

var app = builder.Build();

if (app.Environment.IsEnvironment("Testing"))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var remoteIp)
            && System.Net.IPAddress.TryParse(remoteIp.FirstOrDefault(), out var parsed))
        {
            context.Connection.RemoteIpAddress = parsed;
        }

        await next(context);
    });
}

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownIPNetworks.Clear();
foreach (var ip in appOptions.ForwardedAllowIpAddresses)
{
    forwardedHeadersOptions.KnownProxies.Add(ip);
}

app.UseForwardedHeaders(forwardedHeadersOptions);
app.UseExceptionHandler();
app.UseSecurityHeaders();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/api/healthz", () => Results.Ok(new OkResponse()));
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/api/test/connection", (HttpContext context) => Results.Ok(new
    {
        RemoteIp = context.Connection.RemoteIpAddress?.ToString(),
        Scheme = context.Request.Scheme
    }));
}

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapParsersEndpoints();
app.MapUsersEndpoints();
app.MapDellSettingsEndpoints();
app.MapParseEndpoints();
app.MapHistoryEndpoints();
app.MapMetricsEndpoints();
app.MapMonitoringEndpoints();
app.MapRuntimeConfigurationEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
