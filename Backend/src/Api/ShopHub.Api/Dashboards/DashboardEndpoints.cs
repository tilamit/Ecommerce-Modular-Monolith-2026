using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Api.Dashboards;

/// <summary><c>/api/v1/dashboard</c> (spec §10).</summary>
internal static class DashboardEndpoints
{
    internal static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/dashboard")
            .WithTags("Dashboards")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        group.MapGet("/admin", GetAdminAsync).RequireAuthorization(Permissions.DashboardAdmin);
        group.MapGet("/customer", GetCustomerAsync).RequireAuthorization(Permissions.DashboardOwn);

        return endpoints;
    }

    private static async Task<IResult> GetAdminAsync(
        DashboardService dashboards,
        CancellationToken cancellationToken) =>
        Results.Ok(await dashboards.GetAdminDashboardAsync(cancellationToken));

    /// <summary>
    /// Scoped to the caller server-side. Spec §10.2 is explicit: "Never accept a userId
    /// parameter on this endpoint" - so it takes none and there is nothing to tamper with.
    /// </summary>
    private static async Task<IResult> GetCustomerAsync(
        DashboardService dashboards,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await dashboards.GetCustomerDashboardAsync(currentUser.RequiredId, cancellationToken));
}
