using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Modules.Ordering.EventHandlers;
using ShopHub.Modules.Ordering.Features.Carts;
using ShopHub.Modules.Ordering.Features.Checkout;
using ShopHub.Modules.Ordering.Features.Orders;
using ShopHub.Modules.Ordering.Infrastructure;
using ShopHub.Modules.Ordering.Persistence;
using ShopHub.Shared.Contracts.Events;
using ShopHub.Shared.Infrastructure.Events;
using ShopHub.Shared.Infrastructure.Modules;
using ShopHub.Shared.Infrastructure.Persistence;

namespace ShopHub.Modules.Ordering;

/// <summary>
/// Registration surface for the Ordering module (spec §5.2).
/// <para>
/// This type is the module's <em>only</em> public type outside its Contracts assembly.
/// </para>
/// </summary>
public sealed class OrderingModule : IModule
{
    /// <summary>Also the SQL schema name (spec §4.2) and the audit trail's Module column.</summary>
    public string Name => "ordering";

    public IServiceCollection RegisterModule(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<OrderingDbContext>(
            OrderingDbContext.MigrationsHistoryTable,
            OrderingDbContext.Schema);

        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderService>();

        services.AddScoped<ICartPricer, CartPricer>();
        services.AddScoped<IOrderNumberGenerator, OrderNumberGenerator>();

        // Spec A8: no gateway. The seam exists so adding one is a registration change
        // rather than surgery on the checkout flow.
        services.AddSingleton<IPaymentProcessor, NoOpPaymentProcessor>();

        services.AddScoped<Contracts.IOrderingModuleApi, OrderingModuleApi>();

        // Consumes Identity's registration event to link prior guest history (spec §8.4).
        services.AddScoped<IIntegrationEventHandler<UserRegisteredIntegrationEvent>, UserRegisteredHandler>();

        services.AddValidatorsFromAssemblyContaining<OrderingModule>(includeInternalTypes: true);

        services.AddScoped<OrderingSeeder>();
        services.AddScoped<IModuleInitializer, OrderingInitializer>();

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapCartEndpoints();
        endpoints.MapCheckoutEndpoints();
        endpoints.MapOrderEndpoints();

        return endpoints;
    }
}
