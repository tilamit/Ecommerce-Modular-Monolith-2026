using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Modules.Identity.Features.Access;
using ShopHub.Modules.Identity.Features.Auth;
using ShopHub.Modules.Identity.Features.Roles;
using ShopHub.Modules.Identity.Features.Users;
using ShopHub.Modules.Identity.Infrastructure;
using ShopHub.Modules.Identity.Persistence;
using ShopHub.Shared.Infrastructure.Modules;
using ShopHub.Shared.Infrastructure.Persistence;
using ShopHub.Shared.Infrastructure.Security;

namespace ShopHub.Modules.Identity;

/// <summary>
/// Registration surface for the Identity module (spec §5.2).
/// <para>
/// This type is the module's <em>only</em> public type outside its Contracts assembly.
/// Everything else - DbContext, entities, endpoints, handlers - is internal, so the
/// compiler enforces the boundary before any architecture test has to.
/// </para>
/// </summary>
public sealed class IdentityModule : IModule
{
    /// <summary>Also the SQL schema name (spec §4.2) and the audit trail's Module column.</summary>
    public string Name => "identity";

    public IServiceCollection RegisterModule(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<IdentityDbContext>(
            IdentityDbContext.MigrationsHistoryTable,
            IdentityDbContext.Schema);

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<RoleService>();
        services.AddScoped<AccessService>();

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IMenuService, MenuService>();

        // Satisfies the authorization handler in Shared.Infrastructure, which declares the
        // capability without knowing which module supplies it.
        services.AddScoped<IUserPermissionResolver>(sp => sp.GetRequiredService<IPermissionService>());

        // The module's own Contracts implementation - how other modules reach Identity.
        services.AddScoped<Contracts.IIdentityModuleApi, IdentityModuleApi>();

        services.AddValidatorsFromAssemblyContaining<IdentityModule>(includeInternalTypes: true);

        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IModuleInitializer, IdentityInitializer>();

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapAuthEndpoints();
        endpoints.MapUserEndpoints();
        endpoints.MapRoleEndpoints();
        endpoints.MapAccessEndpoints();

        return endpoints;
    }
}
