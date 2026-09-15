using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Auditing.Features.AuditTrails;
using ShopHub.Modules.Auditing.Infrastructure;
using ShopHub.Modules.Auditing.Persistence;
using ShopHub.Shared.Infrastructure.Modules;
using ShopHub.Shared.Infrastructure.Persistence;

namespace ShopHub.Modules.Auditing;

/// <summary>
/// Registration surface for the Auditing module (spec §5.2).
/// <para>
/// This module owns the trail table and the background writer. The <em>capture</em> side -
/// the SaveChanges interceptor and the queue - lives in Shared.Infrastructure, because it
/// runs inside every module's DbContext and none of them may reference this one.
/// </para>
/// </summary>
public sealed class AuditingModule : IModule
{
    /// <summary>Also the SQL schema name (spec §4.2).</summary>
    public string Name => "audit";

    public IServiceCollection RegisterModule(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<AuditingDbContext>(
            AuditingDbContext.MigrationsHistoryTable,
            AuditingDbContext.Schema);

        services.AddScoped<AuditTrailService>();

        // The Contracts implementation for non-EF events: login, logout, export,
        // permission change and any bulk operation that bypasses the change tracker.
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditModuleApi, AuditModuleApi>();

        // Drains the queue and bulk-inserts, off the request path entirely (spec §6.6).
        services.AddHostedService<AuditTrailWriter>();

        services.AddScoped<IModuleInitializer, AuditingInitializer>();

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapAuditTrailEndpoints();

        return endpoints;
    }
}

/// <summary>
/// Applies the Auditing module's migrations. There is no seeder: the trail is a record of
/// what actually happened and fabricating history would defeat its purpose.
/// </summary>
internal sealed class AuditingInitializer(AuditingDbContext db) : IModuleInitializer
{
    public string ModuleName => "audit";

    public async Task InitializeAsync(CancellationToken cancellationToken) =>
        await db.Database.MigrateAsync(cancellationToken);
}
