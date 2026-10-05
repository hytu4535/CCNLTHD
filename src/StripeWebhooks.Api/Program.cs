using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Stripe;
using StripeWebhooks.Api.Endpoints;
using StripeWebhooks.Api.Infrastructure;
using StripeWebhooks.Api.Persistence;
using StripeWebhooks.Api.Stripe;

var builder = WebApplication.CreateBuilder(args);

var env = builder.Environment;
var cfg = builder.Configuration;

// WebApplicationFactory spins up multiple hosts; Serilog's ReloadableLogger can throw
// "The logger is already frozen" in tests. Keep tests on default MS logging.
var isTesting = env.IsEnvironment("Testing");

// --------------------------------------------------------------------
// Serilog: bootstrap + config-based pipeline (non-testing only)
// --------------------------------------------------------------------
if (!isTesting)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateBootstrapLogger();

    builder.Host.UseSerilog((ctx, services, serilogCfg) =>
    {
        serilogCfg
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    });
}

// --------------------------------------------------------------------
// Configuration helpers
// --------------------------------------------------------------------
static string? GetConnString(IConfiguration cfg, string name)
    => cfg.GetConnectionString(name) ?? cfg[$"ConnectionStrings:{name}"];

static string SanitizeConnString(string cs)
    => Regex.Replace(cs, "(?i)(Password|Pwd)=[^;]*", "$1=***");

static string? SafePrefix(string? value, int len)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    return value[..Math.Min(len, value.Length)];
}

// --------------------------------------------------------------------
// Startup diagnostics (non-testing only)
// --------------------------------------------------------------------
if (!isTesting)
{
    Log.Information("=== Startup Diagnostics ===");
    Log.Information("EnvironmentName: {Env}", env.EnvironmentName);
    Log.Information("ApplicationName: {App}", env.ApplicationName);
    Log.Information("ContentRootPath: {Root}", env.ContentRootPath);

    var stripeSecretKey = cfg["Stripe:SecretKey"];
    var webhookSecret = cfg["Stripe:WebhookSecret"];
    var skipSig = cfg.GetValue<bool>("Stripe:SkipSignatureValidation");

    Log.Information("Stripe:SkipSignatureValidation: {Skip}", skipSig);
    Log.Information("Stripe:SecretKey loaded? {HasKey} Prefix={Prefix} Len={Len}",
        !string.IsNullOrWhiteSpace(stripeSecretKey),
        SafePrefix(stripeSecretKey, 8) ?? "<null>",
        stripeSecretKey?.Length ?? 0);

    Log.Information("Stripe:WebhookSecret loaded? {HasSecret} Prefix={Prefix} Len={Len}",
        !string.IsNullOrWhiteSpace(webhookSecret),
        SafePrefix(webhookSecret, 10) ?? "<null>",
        webhookSecret?.Length ?? 0);

    var dbConn = GetConnString(cfg, "Db");
    Log.Information("Db connection string loaded? {HasDbConn}", !string.IsNullOrWhiteSpace(dbConn));
}

// --------------------------------------------------------------------
// Database + Health checks
// --------------------------------------------------------------------
var dbConnString = GetConnString(cfg, "Db");

if (string.IsNullOrWhiteSpace(dbConnString) && !isTesting)
    throw new InvalidOperationException("ConnectionStrings:Db is not configured.");

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    // In tests you might override AppDbContext registration in the factory anyway,
    // but we still guard against null here to avoid accidental Npgsql misconfig.
    if (!string.IsNullOrWhiteSpace(dbConnString))
        opt.UseNpgsql(dbConnString);
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("db");

// --------------------------------------------------------------------
// Stripe configuration (prod strict, testing relaxed)
// --------------------------------------------------------------------
var stripeKey = cfg["Stripe:SecretKey"];

if (string.IsNullOrWhiteSpace(stripeKey) && !isTesting)
    throw new InvalidOperationException("Stripe:SecretKey is not configured.");

if (!string.IsNullOrWhiteSpace(stripeKey))
    StripeConfiguration.ApiKey = stripeKey;

// --------------------------------------------------------------------
// App services
// --------------------------------------------------------------------
builder.Services.AddScoped<StripeSignatureVerifier>();
builder.Services.AddScoped<StripeWebhookHandler>();

builder.Services.AddCorrelationId();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// --------------------------------------------------------------------
// Database migrations (skip in tests)
// --------------------------------------------------------------------
if (!isTesting)
{
    var runtimeConn = GetConnString(app.Configuration, "Db");
    Log.Information("DB: Using connection string (sanitized): {Conn}",
        string.IsNullOrWhiteSpace(runtimeConn) ? "<null>" : SanitizeConnString(runtimeConn));

    try
    {
        Log.Information("DB: Running migrations...");
        await app.Services.MigrateDatabaseAsync();
        Log.Information("DB: Migrations complete ✅");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "DB: Migration failed ❌");
        throw;
    }
}

// --------------------------------------------------------------------
// Middleware
// --------------------------------------------------------------------

// Correlation first so request logging can include correlation fields if your middleware
// enriches LogContext / headers.
app.UseCorrelationId();
app.UseSwagger();
app.UseSwaggerUI();

if (!isTesting)
{
    app.UseSerilogRequestLogging(opts =>
    {
        // Light enrichment without clutter — very useful in Seq.
        opts.EnrichDiagnosticContext = (diag, http) =>
        {
            diag.Set("RequestHost", http.Request.Host.Value);
            diag.Set("RequestScheme", http.Request.Scheme);
            diag.Set("ClientIP", http.Connection.RemoteIpAddress?.ToString());
        };
    });
}

// --------------------------------------------------------------------
// Endpoints
// --------------------------------------------------------------------
app.MapHealthEndpoints();
app.MapStripeWebhookEndpoints();
app.MapPaymentIntentEndpoints();
app.MapProductEndpoints();

// --------------------------------------------------------------------
// Run + shutdown
// --------------------------------------------------------------------
try
{
    app.Run();
}
catch (Exception ex)
{
    if (!isTesting)
        Log.Fatal(ex, "Application terminated unexpectedly");

    throw;
}
finally
{
    if (!isTesting)
        Log.CloseAndFlush();
}

// Required by WebApplicationFactory<Program>
public partial class Program { }
