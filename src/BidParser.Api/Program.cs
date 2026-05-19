using BidParser.Api.Auth;
using BidParser.Api.Endpoints;
using BidParser.Api.Hosting;
using BidParser.Api.Options;
using BidParser.Domain.Abstractions;
using BidParser.Infrastructure.Persistence;
using BidParser.Infrastructure.Services;
using BidParser.Infrastructure.Storage;
using BidParser.Parsing.Registry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var appOptions = AppOptions.FromConfiguration(builder.Configuration, builder.Environment);
appOptions.EnsureDirectories();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = appOptions.MaxUploadBytes;
});

builder.Services.AddSingleton(appOptions);
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});
builder.Services.AddSingleton<SqlitePragmaConnectionInterceptor>();
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    options.UseSqlite(appOptions.ToSqliteConnectionString());
    options.AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaConnectionInterceptor>());
});
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(appOptions.DataProtectionKeysDir))
    .SetApplicationName(appOptions.SessionSecret);
builder.Services.AddSingleton<AuthRateLimiter>();
builder.Services.AddAuthentication(SessionCookieAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionCookieAuthHandler>(SessionCookieAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.LoggedIn, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(AuthPolicies.ActiveUser, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context => !context.User.HasClaim("must_change_password", "true"));
    });
    options.AddPolicy(AuthPolicies.Admin, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("admin");
        policy.RequireAssertion(context => !context.User.HasClaim("must_change_password", "true"));
    });
});
builder.Services.AddSingleton<IParserRegistry, ParserRegistry>();
builder.Services.AddSingleton(new FileStorage(appOptions.UploadDir));
builder.Services.AddScoped<ParseService>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { detail = "Too many parse requests. Please try again later." },
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

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/api/healthz", () => Results.Ok(new { status = "ok" }));
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/api/test/connection", (HttpContext context) => Results.Ok(new
    {
        remote_ip = context.Connection.RemoteIpAddress?.ToString(),
        scheme = context.Request.Scheme
    }));
}

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapParsersEndpoints();
app.MapUsersEndpoints();
app.MapParseEndpoints();
app.MapHistoryEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
