using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopHub.Shared.Infrastructure.Persistence;

/// <summary>
/// One place where every module's DbContext is configured, so the performance and
/// resilience settings from spec §6.7 cannot drift apart between modules.
/// </summary>
public static class ModuleDbContextExtensions
{
    /// <summary>
    /// Registers a module's DbContext with its own migrations history table (spec §4.2), so
    /// modules migrate independently and one module's pending migration does not block
    /// another's deploy.
    /// </summary>
    /// <remarks>
    /// Uses <c>AddDbContext</c> rather than <c>AddDbContextPool</c>. Pooling requires the
    /// context's dependencies to be poolable, and <see cref="AuditableEntityInterceptor"/>
    /// captures the <em>scoped</em> <c>ICurrentUser</c> - pooling it would leak one
    /// request's identity into another's provenance columns. Spec §6.7 explicitly allows
    /// this trade and asks that the reason be recorded rather than silently making the
    /// interceptor a singleton with captured scoped state. See ADR-013.
    /// </remarks>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        string migrationsHistoryTable,
        string schema)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            // Resolved from the service provider rather than captured at registration time.
            // Reading it eagerly would freeze whatever configuration existed when
            // RegisterModule ran, so any source added afterwards - notably the test host's
            // per-run connection string - would be silently ignored.
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("ShopHub")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:ShopHub is not configured. See README.md for LocalDB setup.");

            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable(migrationsHistoryTable, schema);

                // Transient SQL failures are retried rather than surfaced as a 500.
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            });

            // Spec §6.7: no tracking by default; writes opt in explicitly. A read path that
            // accidentally tracks is a silent memory and change-detection cost on every request.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        return services;
    }
}
