using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;

namespace ShopHub.Modules.Auditing.Features.AuditTrails;

/// <summary>
/// <c>/api/v1/audit-trails</c> (spec §9.4).
/// <para>
/// Read-only by design. There is no POST, PUT, PATCH or DELETE here and there never will
/// be: the trail is append-only, with no admin override. An audit trail an administrator
/// can edit is not evidence of anything. <c>AuditTrailsAreAppendOnly</c> in the
/// architecture tests fails the build if a mutating verb is ever added.
/// </para>
/// </summary>
internal static class AuditTrailEndpoints
{
    internal static IEndpointRouteBuilder MapAuditTrailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/audit-trails")
            .WithTags("Audit")
            .RequireRateLimiting(RateLimitPolicies.Authenticated)
            .RequireAuthorization(Permissions.AuditRead);

        // Literal segment before the id route, so it is not captured as an id.
        group.MapGet("/recent", GetRecentAsync);
        group.MapGet("/", GetAsync);
        group.MapGet("/{id:long}", GetByIdAsync);

        return endpoints;
    }

    /// <summary>
    /// Keyset-paginated (spec §6.5). Returns an opaque <c>nextCursor</c> instead of a page
    /// number, because this table grows fastest and offset paging degrades with depth.
    /// </summary>
    private static async Task<IResult> GetAsync(
        AuditTrailService audit,
        CancellationToken cancellationToken,
        string? cursor = null,
        int pageSize = 20,
        string? search = null,
        [Microsoft.AspNetCore.Mvc.FromQuery] AuditAction[]? action = null,
        string? module = null,
        string? entityName = null,
        Guid? userId = null,
        string? screenName = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null)
    {
        var filter = new AuditTrailFilter(search, action, module, entityName, userId, screenName, dateFrom, dateTo);

        return Results.Ok(await audit.GetAsync(cursor, pageSize, filter, cancellationToken));
    }

    private static async Task<IResult> GetByIdAsync(
        long id,
        AuditTrailService audit,
        CancellationToken cancellationToken) =>
        Results.Ok(await audit.GetByIdAsync(id, cancellationToken));

    private static async Task<IResult> GetRecentAsync(
        AuditTrailService audit,
        CancellationToken cancellationToken,
        int take = 10) =>
        Results.Ok(await audit.GetRecentAsync(take, cancellationToken));
}
