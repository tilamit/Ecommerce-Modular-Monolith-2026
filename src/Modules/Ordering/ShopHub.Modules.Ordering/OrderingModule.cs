using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Shared.Infrastructure.Modules;

namespace ShopHub.Modules.Ordering;

/// <summary>
/// Registration surface for the Ordering module (spec §5.2).
/// <para>
/// This type is the module's <em>only</em> public type outside its Contracts assembly.
/// Everything else - DbContext, entities, endpoints, handlers - is internal, so the
/// compiler enforces the boundary before any architecture test has to.
/// </para>
/// <para>Services and endpoints are added in Phase 4 (carts, checkout, orders).</para>
/// </summary>
public sealed class OrderingModule : IModule
{
    /// <summary>Also the SQL schema name (spec §4.2) and the audit trail's Module column.</summary>
    public string Name => "ordering";

    public IServiceCollection RegisterModule(IServiceCollection services, IConfiguration configuration) => services;

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints;
}
