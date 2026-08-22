using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Shared.Infrastructure.Modules;

namespace ShopHub.Modules.Identity;

/// <summary>
/// Registration surface for the Identity module (spec §5.2).
/// <para>
/// This type is the module's <em>only</em> public type outside its Contracts assembly.
/// Everything else - DbContext, entities, endpoints, handlers - is internal, so the
/// compiler enforces the boundary before any architecture test has to.
/// </para>
/// <para>Services and endpoints are added in Phase 2 (entities, auth, user CRUD).</para>
/// </summary>
public sealed class IdentityModule : IModule
{
    /// <summary>Also the SQL schema name (spec §4.2) and the audit trail's Module column.</summary>
    public string Name => "identity";

    public IServiceCollection RegisterModule(IServiceCollection services, IConfiguration configuration) => services;

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints;
}
