using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Identity.Features.Users;

/// <summary>
/// <c>/api/v1/users</c> (spec §9.1).
/// <para>
/// Endpoints require a <em>permission</em>, never a role name (spec §7.5) - that is what
/// makes adding role #3 a data change rather than a code change.
/// </para>
/// </summary>
internal static class UserEndpoints
{
    internal static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        group.MapGet("/", GetUsersAsync)
            .RequireAuthorization(Permissions.UsersRead);

        // Declared before "/{id}" so the literal segment is not captured as an id.
        group.MapGet("/me/profile", GetMyProfileAsync)
            .RequireAuthorization();

        group.MapPut("/me/profile", UpdateMyProfileAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapGet("/{id:guid}", GetUserByIdAsync)
            .RequireAuthorization(Permissions.UsersRead);

        group.MapPost("/", CreateUserAsync)
            .RequireAuthorization(Permissions.UsersManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPut("/{id:guid}", UpdateUserAsync)
            .RequireAuthorization(Permissions.UsersManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPatch("/{id:guid}/status", SetStatusAsync)
            .RequireAuthorization(Permissions.UsersManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapDelete("/{id:guid}", DeleteUserAsync)
            .RequireAuthorization(Permissions.UsersManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPut("/{id:guid}/roles", SetRolesAsync)
            .RequireAuthorization(Permissions.UsersManage)
            .RequireRateLimiting(RateLimitPolicies.Write);

        return endpoints;
    }

    private static async Task<IResult> GetUsersAsync(
        UserService users,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize,
        string? sort = null,
        string? search = null,
        Guid? roleId = null,
        bool? isActive = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null)
    {
        var paging = new PagedRequest(page, pageSize, sort, search);
        var filter = new UserFilter(search, roleId, isActive, createdFrom, createdTo);

        return Results.Ok(await users.GetUsersAsync(paging, filter, cancellationToken));
    }

    private static async Task<IResult> GetUserByIdAsync(Guid id, UserService users, CancellationToken cancellationToken) =>
        Results.Ok(await users.GetUserByIdAsync(id, cancellationToken));

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        UserService users,
        IValidator<CreateUserRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var created = await users.CreateUserAsync(request, cancellationToken);

        return Results.Created($"/api/v1/users/{created.Id}", created);
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        UserService users,
        IValidator<UpdateUserRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await users.UpdateUserAsync(id, request, cancellationToken));
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        SetUserStatusRequest request,
        UserService users,
        CancellationToken cancellationToken)
    {
        await users.SetStatusAsync(id, request.IsActive, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteUserAsync(Guid id, UserService users, CancellationToken cancellationToken)
    {
        await users.DeleteUserAsync(id, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> SetRolesAsync(
        Guid id,
        SetUserRolesRequest request,
        UserService users,
        CancellationToken cancellationToken) =>
        Results.Ok(await users.SetRolesAsync(id, request.RoleIds, cancellationToken));

    /// <summary>
    /// Scoped to the caller server-side. It takes no id parameter at all - accepting one
    /// would make it the caller's word for whose profile to return (spec §7.5).
    /// </summary>
    private static async Task<IResult> GetMyProfileAsync(
        UserService users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await users.GetUserByIdAsync(currentUser.RequiredId, cancellationToken));

    private static async Task<IResult> UpdateMyProfileAsync(
        UpdateMyProfileRequest request,
        UserService users,
        ICurrentUser currentUser,
        IValidator<UpdateMyProfileRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await users.UpdateMyProfileAsync(currentUser.RequiredId, request, cancellationToken));
    }
}
