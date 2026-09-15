using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopHub.Modules.Auditing.Contracts;
using ShopHub.Modules.Catalog.Domain;
using ShopHub.Modules.Catalog.Persistence;
using ShopHub.Shared.Infrastructure.Auditing;
using ShopHub.Shared.Infrastructure.Persistence;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;
using ShopHub.Shared.Kernel.Exceptions;
using ShopHub.Shared.Kernel.Paging;

namespace ShopHub.Modules.Catalog.Features.Offers;

internal sealed record OfferListItem(
    Guid Id,
    string Code,
    string Name,
    string DiscountType,
    decimal DiscountValue,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal? MinimumOrderAmount,
    int? MaxRedemptions,
    int RedemptionCount,
    bool IsActive,
    bool IsRedeemable,
    IReadOnlyList<Guid> ProductIds);

internal sealed record SaveOfferRequest(
    string Code,
    string Name,
    DiscountType DiscountType,
    decimal DiscountValue,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal? MinimumOrderAmount,
    int? MaxRedemptions,
    bool IsActive,
    IReadOnlyList<Guid>? ProductIds);

/// <summary>Discount offers (spec §9.2).</summary>
internal sealed class OfferService(CatalogDbContext db, IClock clock, IAuditWriter audit)
{
    internal async Task<PagedResult<OfferListItem>> GetOffersAsync(PagedRequest paging, CancellationToken cancellationToken)
    {
        var request = paging.Normalized();
        var now = clock.UtcNow;
        var query = db.Offers.AsQueryable();

        if (request.Search is { } search)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(o => o.Code.StartsWith(term) || o.Name.StartsWith(search));
        }

        var total = await query.LongCountAsync(ReadCancellation.RunToCompletion);

        var items = await query
            .OrderByDescending(o => o.StartUtc)
            .ThenBy(o => o.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(o => new OfferListItem(
                o.Id,
                o.Code,
                o.Name,
                o.DiscountType.ToString(),
                o.DiscountValue,
                o.StartUtc,
                o.EndUtc,
                o.MinimumOrderAmount,
                o.MaxRedemptions,
                o.RedemptionCount,
                o.IsActive,
                // Computed in SQL so the caller does not have to re-derive the window.
                o.IsActive
                    && now >= o.StartUtc
                    && now <= o.EndUtc
                    && (o.MaxRedemptions == null || o.RedemptionCount < o.MaxRedemptions),
                o.Products.Select(p => p.ProductId).ToList()))
            .ToListAsync(ReadCancellation.RunToCompletion);

        return new PagedResult<OfferListItem>(items, request.Page, request.PageSize, total);
    }

    internal async Task<OfferListItem> CreateAsync(SaveOfferRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.Offers.AnyAsync(o => o.Code == code, cancellationToken))
        {
            throw new ConflictException("offer_code_taken", $"An offer with code '{code}' already exists.");
        }

        var offer = Offer.Create(
            request.Code,
            request.Name,
            request.DiscountType,
            request.DiscountValue,
            request.StartUtc,
            request.EndUtc,
            request.MinimumOrderAmount,
            request.MaxRedemptions);

        var products = await ApplyProductsAsync(offer, request.ProductIds, cancellationToken);

        db.Offers.Add(offer);
        await db.SaveChangesAsync(cancellationToken);

        WriteProductsChange(offer, products);

        return await GetOfferAsync(offer.Id, cancellationToken);
    }

    internal async Task<OfferListItem> UpdateAsync(Guid id, SaveOfferRequest request, CancellationToken cancellationToken)
    {
        var offer = await db.Offers
            .AsTracking()
            .Include(o => o.Products)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Offer", id);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.Offers.AnyAsync(o => o.Code == code && o.Id != id, cancellationToken))
        {
            throw new ConflictException("offer_code_taken", $"An offer with code '{code}' already exists.");
        }

        offer.Update(
            request.Code,
            request.Name,
            request.DiscountType,
            request.DiscountValue,
            request.StartUtc,
            request.EndUtc,
            request.MinimumOrderAmount,
            request.MaxRedemptions,
            request.IsActive);

        var products = await ApplyProductsAsync(offer, request.ProductIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        WriteProductsChange(offer, products);

        return await GetOfferAsync(id, cancellationToken);
    }

    /// <summary>
    /// Soft delete. The remove is turned into <c>IsDeleted = true</c> by the persistence
    /// interceptor, so the offer disappears from every list while its row stays for the audit
    /// trail and for any order that recorded its code. The code becomes free for a new offer.
    /// </summary>
    internal async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var offer = await db.Offers.AsTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Offer", id);

        db.Offers.Remove(offer);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the products an offer is limited to and returns their names before and after,
    /// or null when the request left the products untouched.
    /// </summary>
    private async Task<(IReadOnlyList<string> Before, IReadOnlyList<string> After)?> ApplyProductsAsync(
        Offer offer,
        IReadOnlyList<Guid>? productIds,
        CancellationToken cancellationToken)
    {
        if (productIds is null)
        {
            return null;
        }

        if (productIds.Count > 0)
        {
            var found = await db.Products.CountAsync(p => productIds.Contains(p.Id), cancellationToken);

            if (found != productIds.Distinct().Count())
            {
                throw new NotFoundException("product_not_found", "One or more of the supplied products do not exist.");
            }
        }

        var before = await ProductNamesAsync([.. offer.Products.Select(p => p.ProductId)], cancellationToken);
        var after = await ProductNamesAsync(productIds, cancellationToken);

        offer.ReplaceProducts(productIds);

        return (before, after);
    }

    /// <summary>
    /// The products an offer applies to are link rows, so a change to them is written as one
    /// entry with the product names before and after.
    /// </summary>
    private void WriteProductsChange(Offer offer, (IReadOnlyList<string> Before, IReadOnlyList<string> After)? products)
    {
        if (products is not { } change)
        {
            return;
        }

        audit.WriteListChange(
            AuditAction.Update,
            CatalogDbContext.Schema,
            nameof(Offer),
            offer.Id,
            "Offer",
            offer.Code,
            "Products",
            change.Before,
            change.After);
    }

    private async Task<IReadOnlyList<string>> ProductNamesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken) =>
        await db.Products
            .Where(p => productIds.Contains(p.Id))
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(cancellationToken);

    /// <summary>A single offer, for the admin edit form.</summary>
    internal async Task<OfferListItem> GetOfferAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        return await db.Offers
            .Where(o => o.Id == id)
            .Select(o => new OfferListItem(
                o.Id,
                o.Code,
                o.Name,
                o.DiscountType.ToString(),
                o.DiscountValue,
                o.StartUtc,
                o.EndUtc,
                o.MinimumOrderAmount,
                o.MaxRedemptions,
                o.RedemptionCount,
                o.IsActive,
                o.IsActive
                    && now >= o.StartUtc
                    && now <= o.EndUtc
                    && (o.MaxRedemptions == null || o.RedemptionCount < o.MaxRedemptions),
                o.Products.Select(p => p.ProductId).ToList()))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Offer", id);
    }
}

/// <summary><c>/api/v1/catalog/offers</c> (spec §9.2). Admin only.</summary>
internal static class OfferEndpoints
{
    internal static IEndpointRouteBuilder MapOfferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/catalog/offers")
            .WithTags("Catalog")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        group.MapGet("/", GetOffersAsync).RequireAuthorization(Permissions.OffersRead);

        group.MapGet("/{id:guid}", GetOfferAsync).RequireAuthorization(Permissions.OffersRead);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(Permissions.OffersWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(Permissions.OffersWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(Permissions.OffersWrite)
            .RequireRateLimiting(RateLimitPolicies.Write);

        return endpoints;
    }

    private static async Task<IResult> GetOffersAsync(
        OfferService offers,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagedRequest.DefaultPageSize,
        string? sort = null,
        string? search = null) =>
        Results.Ok(await offers.GetOffersAsync(new PagedRequest(page, pageSize, sort, search), cancellationToken));

    /// <summary>One offer, as the edit form loads it. Opening it is recorded as a read.</summary>
    private static async Task<IResult> GetOfferAsync(
        Guid id,
        OfferService offers,
        IAuditWriter audit,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var offer = await offers.GetOfferAsync(id, cancellationToken);

        if (http.ShouldRecordRead())
        {
            audit.WriteRead(CatalogDbContext.Schema, "Offer", offer.Id);
        }

        return Results.Ok(offer);
    }

    private static async Task<IResult> CreateAsync(
        SaveOfferRequest request,
        OfferService offers,
        IValidator<SaveOfferRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var created = await offers.CreateAsync(request, cancellationToken);

        return Results.Created($"/api/v1/catalog/offers/{created.Id}", created);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        SaveOfferRequest request,
        OfferService offers,
        IValidator<SaveOfferRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await offers.UpdateAsync(id, request, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(Guid id, OfferService offers, CancellationToken cancellationToken)
    {
        await offers.DeleteAsync(id, cancellationToken);

        return Results.NoContent();
    }
}

internal sealed class SaveOfferRequestValidator : AbstractValidator<SaveOfferRequest>
{
    public SaveOfferRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(64);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(256);
        RuleFor(r => r.DiscountValue).GreaterThan(0);

        RuleFor(r => r.DiscountValue)
            .LessThanOrEqualTo(100)
            .When(r => r.DiscountType == DiscountType.Percentage)
            .WithMessage("A percentage discount cannot exceed 100.");

        RuleFor(r => r.EndUtc)
            .GreaterThan(r => r.StartUtc)
            .WithMessage("The offer's end date must be after its start date.");

        RuleFor(r => r.MinimumOrderAmount).GreaterThan(0).When(r => r.MinimumOrderAmount.HasValue);
        RuleFor(r => r.MaxRedemptions).GreaterThan(0).When(r => r.MaxRedemptions.HasValue);
    }
}
