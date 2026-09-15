using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Catalog.Infrastructure;
using ShopHub.Modules.Catalog.Persistence;
using ShopHub.Shared.Infrastructure.Auditing;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Catalog.Features.Categories;

/// <summary><c>/api/v1/catalog/categories</c> (spec §9.2).</summary>
internal static class CategoryEndpoints
{
    internal static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/catalog/categories").WithTags("Catalog");

        group.MapGet("/tree", GetTreeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Anonymous)
            .CacheOutput(policy => policy
                .Expire(TimeSpan.FromMinutes(5))
                .WithoutJoinedRequests()
                .Tag(Infrastructure.OutputCacheTags.Categories));

        group.MapGet("/", GetCategoriesAsync)
            .RequireAuthorization(Permissions.CategoriesRead)
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        group.MapGet("/{id:guid}", GetCategoryAsync)
            .RequireAuthorization(Permissions.CategoriesRead)
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(Permissions.CategoriesWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(Permissions.CategoriesWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPatch("/{id:guid}/status", SetStatusAsync)
            .RequireAuthorization(Permissions.CategoriesWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(Permissions.CategoriesWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        return endpoints;
    }

    private static async Task<IResult> GetTreeAsync(CategoryService categories, CancellationToken cancellationToken) =>
        Results.Ok(await categories.GetTreeAsync(cancellationToken));

    private static async Task<IResult> GetCategoriesAsync(
        CategoryService categories,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize,
        string? sort = null,
        string? search = null) =>
        Results.Ok(await categories.GetCategoriesAsync(new PagedRequest(page, pageSize, sort, search), cancellationToken));

    /// <summary>One category, as the edit form loads it. Opening it is recorded as a read.</summary>
    private static async Task<IResult> GetCategoryAsync(
        Guid id,
        CategoryService categories,
        IAuditWriter audit,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var category = await categories.GetCategoryAsync(id, cancellationToken);

        if (http.ShouldRecordRead())
        {
            audit.WriteRead(CatalogDbContext.Schema, "Category", category.Id);
        }

        return Results.Ok(category);
    }

    private static async Task<IResult> CreateAsync(
        CreateCategoryRequest request,
        CategoryService categories,
        IValidator<CreateCategoryRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var created = await categories.CreateAsync(request, cancellationToken);

        return Results.Created($"/api/v1/catalog/categories/{created.Id}", created);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateCategoryRequest request,
        CategoryService categories,
        IValidator<UpdateCategoryRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await categories.UpdateAsync(id, request, cancellationToken));
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        Products.SetStatusRequest request,
        CategoryService categories,
        CancellationToken cancellationToken)
    {
        await categories.SetStatusAsync(id, request.IsActive, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Spec §9.2: deleting a category with products returns 409 unless
    /// <c>?reassignTo={categoryId}</c> is supplied.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        Guid id,
        CategoryService categories,
        CancellationToken cancellationToken,
        Guid? reassignTo = null)
    {
        await categories.DeleteAsync(id, reassignTo, cancellationToken);

        return Results.NoContent();
    }
}
