using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Catalog.Features.Products;

/// <summary><c>/api/v1/catalog/products</c> (spec §9.2).</summary>
internal static class ProductEndpoints
{
    internal static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/catalog/products").WithTags("Catalog");

        // --- public storefront ------------------------------------------
        group.MapGet("/", GetProductsAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Anonymous)
            .CacheOutput(policy => policy
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByQuery("*")
                // Tagged so a product write can evict it; without this the response body
                // stays stale for the full TTL even after HybridCache is invalidated.
                .Tag(Infrastructure.OutputCacheTags.Products));

        // Literal segments are declared before "/{idOrSlug}" so they are not swallowed by it.
        group.MapGet("/suggest", SuggestAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Anonymous);

        group.MapGet("/new", GetNewArrivalsAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Anonymous);

        group.MapGet("/{idOrSlug}", GetProductAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Anonymous);

        // --- admin ------------------------------------------------------
        group.MapPost("/", CreateProductAsync)
            .RequireAuthorization(Permissions.ProductsWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPut("/{id:guid}", UpdateProductAsync)
            .RequireAuthorization(Permissions.ProductsWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPatch("/{id:guid}/status", SetStatusAsync)
            .RequireAuthorization(Permissions.ProductsWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapDelete("/{id:guid}", DeleteProductAsync)
            .RequireAuthorization(Permissions.ProductsWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        return endpoints;
    }

    private static async Task<IResult> GetProductsAsync(
        ProductService products,
        HttpContext http,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize,
        string? sort = null,
        string? search = null,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid[]? categoryIds = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool? isActive = null,
        bool? isFeatured = null,
        bool? inStock = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null)
    {
        var paging = new PagedRequest(page, pageSize, sort, search);

        var filter = new ProductFilter(
            search,
            categoryIds,
            minPrice,
            maxPrice,
            // Anonymous callers only ever see active products, whatever they ask for.
            http.User.Identity?.IsAuthenticated == true ? isActive : true,
            isFeatured,
            inStock,
            createdFrom,
            createdTo);

        // Cache only the anonymous path. A signed-in admin filtering by isActive=false must
        // not be served (or populate) the storefront's cached page.
        var useCache = http.User.Identity?.IsAuthenticated != true;

        return Results.Ok(await products.GetProductsAsync(paging, filter, useCache, cancellationToken));
    }

    private static async Task<IResult> GetProductAsync(
        string idOrSlug,
        ProductService products,
        CancellationToken cancellationToken) =>
        Results.Ok(await products.GetProductAsync(idOrSlug, cancellationToken));

    private static async Task<IResult> SuggestAsync(
        ProductService products,
        CancellationToken cancellationToken,
        string? q = null) =>
        Results.Ok(await products.SuggestAsync(q, cancellationToken));

    private static async Task<IResult> GetNewArrivalsAsync(
        ProductService products,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize) =>
        Results.Ok(await products.GetNewArrivalsAsync(new PagedRequest(page, pageSize), cancellationToken));

    private static async Task<IResult> CreateProductAsync(
        CreateProductRequest request,
        ProductService products,
        IValidator<CreateProductRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var created = await products.CreateAsync(request, cancellationToken);

        return Results.Created($"/api/v1/catalog/products/{created.Id}", created);
    }

    private static async Task<IResult> UpdateProductAsync(
        Guid id,
        UpdateProductRequest request,
        ProductService products,
        IValidator<UpdateProductRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await products.UpdateAsync(id, request, cancellationToken));
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        SetStatusRequest request,
        ProductService products,
        CancellationToken cancellationToken)
    {
        await products.SetStatusAsync(id, request.IsActive, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteProductAsync(
        Guid id,
        ProductService products,
        CancellationToken cancellationToken)
    {
        await products.DeleteAsync(id, cancellationToken);

        return Results.NoContent();
    }
}
