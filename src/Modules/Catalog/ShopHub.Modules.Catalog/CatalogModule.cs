using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Modules.Catalog.Features.Categories;
using ShopHub.Modules.Catalog.Features.Offers;
using ShopHub.Modules.Catalog.Features.Products;
using ShopHub.Modules.Catalog.Infrastructure;
using ShopHub.Modules.Catalog.Persistence;
using ShopHub.Shared.Infrastructure.Modules;
using ShopHub.Shared.Infrastructure.Persistence;

namespace ShopHub.Modules.Catalog;

/// <summary>
/// Registration surface for the Catalog module (spec §5.2).
/// <para>
/// This type is the module's <em>only</em> public type outside its Contracts assembly.
/// Everything else - DbContext, entities, endpoints, handlers - is internal, so the
/// compiler enforces the boundary before any architecture test has to.
/// </para>
/// </summary>
public sealed class CatalogModule : IModule
{
    /// <summary>Also the SQL schema name (spec §4.2) and the audit trail's Module column.</summary>
    public string Name => "catalog";

    public IServiceCollection RegisterModule(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<CatalogDbContext>(
            CatalogDbContext.MigrationsHistoryTable,
            CatalogDbContext.Schema);

        services.AddScoped<ProductService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<OfferService>();
        services.AddScoped<ICatalogCacheInvalidator, CatalogCacheInvalidator>();

        // The module's own Contracts implementation - how Ordering reaches Catalog.
        services.AddScoped<Contracts.ICatalogModuleApi, CatalogModuleApi>();

        services.AddValidatorsFromAssemblyContaining<CatalogModule>(includeInternalTypes: true);

        services.AddScoped<CatalogSeeder>();
        services.AddScoped<IModuleInitializer, CatalogInitializer>();

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapProductEndpoints();
        endpoints.MapCategoryEndpoints();
        endpoints.MapOfferEndpoints();

        return endpoints;
    }
}
