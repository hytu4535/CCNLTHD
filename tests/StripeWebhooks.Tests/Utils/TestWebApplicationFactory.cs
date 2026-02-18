using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StripeWebhooks.Api.Persistence;

namespace StripeWebhooks.Tests.Utils;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "whsec_test_123";
    public const string StripeSecretKey = "sk_test_dummy";

    private SqliteConnection? _connection;

    public TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:WebhookSecret"] = WebhookSecret,
                ["Stripe:SecretKey"] = StripeSecretKey,

                // Not used (we override DbContext), but helps if something reads it.
                ["ConnectionStrings:Db"] = "Host=ignored;Database=ignored;Username=ignored;Password=ignored"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing Npgsql registration
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));

            // Create one shared in-memory SQLite connection for the test server lifetime
            _connection = new SqliteConnection("DataSource=:memory:;Cache=Shared");
            _connection.Open();

            services.AddDbContext<AppDbContext>(opt =>
            {
                opt.UseSqlite(_connection);
            });

            // Ensure schema exists (migrations or EnsureCreated)
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // If you have migrations, this is best:
            // db.Database.Migrate();

            // If you don't have migrations yet, this is fine:
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
