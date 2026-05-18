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
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var appOptions = AppOptions.FromConfiguration(builder.Configuration);
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
builder.Services.AddHostedService<MigratorHostedService>();
builder.Services.AddHostedService<BootstrapAdminHostedService>();

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = null
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/healthz", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapParsersEndpoints();
app.MapUsersEndpoints();
app.MapParseEndpoints();
app.MapPhase3ProtectedPlaceholders();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
