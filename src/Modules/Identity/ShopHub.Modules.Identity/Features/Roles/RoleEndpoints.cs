using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Identity.Features.Roles;

/// <summary><c>/api/v1/roles</c> and <c>/api/v1/permissions</c> (spec §9.1).</summary>
internal static class RoleEndpoints
{
    internal static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var roles = endpoints
            .MapGroup("/api/v1/roles")
            .WithTags("Roles")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        roles.MapGet("/", GetRolesAsync).RequireAuthorization(Permissions.RolesRead);

        roles.MapPost("/", CreateRoleAsync)
            .RequireAuthorization(Permissions.RolesManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        roles.MapPut("/{id:guid}", UpdateRoleAsync)
            .RequireAuthorization(Permissions.RolesManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        roles.MapDelete("/{id:guid}", DeleteRoleAsync)
            .RequireAuthorization(Permissions.RolesManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        roles.MapGet("/{id:guid}/permissions", GetRolePermissionsAsync)
            .RequireAuthorization(Permissions.RolesRead);

        roles.MapPut("/{id:guid}/permissions", SetRolePermissionsAsync)
            .RequireAuthorization(Permissions.AccessManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        endpoints
            .MapGet("/api/v1/permissions", GetPermissionsAsync)
            .WithTags("Roles")
            .RequireRateLimiting(RateLimitPolicies.Authenticated)
            .RequireAuthorization(Permissions.RolesRead);

        return endpoints;
    }

    private static async Task<IResult> GetRolesAsync(
        RoleService service,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize,
        string? sort = null,
        string? search = null) =>
        Results.Ok(await service.GetRolesAsync(new PagedRequest(page, pageSize, sort, search), cancellationToken));

    private static async Task<IResult> CreateRoleAsync(
        CreateRoleRequest request,
        RoleService service,
        IValidator<CreateRoleRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var created = await service.CreateRoleAsync(request, cancellationToken);

        return Results.Created($"/api/v1/roles/{created.Id}", created);
    }

    private static async Task<IResult> UpdateRoleAsync(
        Guid id,
        UpdateRoleRequest request,
        RoleService service,
        IValidator<UpdateRoleRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await service.UpdateRoleAsync(id, request, cancellationToken));
    }

    private static async Task<IResult> DeleteRoleAsync(Guid id, RoleService service, CancellationToken cancellationToken)
    {
        await service.DeleteRoleAsync(id, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> GetPermissionsAsync(RoleService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetPermissionsAsync(cancellationToken));

    private static async Task<IResult> GetRolePermissionsAsync(
        Guid id,
        RoleService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetRolePermissionsAsync(id, cancellationToken));

    private static async Task<IResult> SetRolePermissionsAsync(
        Guid id,
        SetRolePermissionsRequest request,
        RoleService service,
        CancellationToken cancellationToken)
    {
        await service.SetRolePermissionsAsync(id, request.PermissionIds, cancellationToken);

        return Results.NoContent();
    }
}

internal sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(64);
        RuleFor(r => r.Description).MaximumLength(256);
    }
}

internal sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(64);
        RuleFor(r => r.Description).MaximumLength(256);
    }
}
