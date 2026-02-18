using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace StripeWebhooks.Api.Persistence;

public static class DbMigrationExtensions
{
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
