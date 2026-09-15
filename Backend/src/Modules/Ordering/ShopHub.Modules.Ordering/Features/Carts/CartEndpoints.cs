using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Ordering.Features.Carts;

/// <summary><c>/api/v1/carts</c> (spec §9.3).</summary>
internal static class CartEndpoints
{
    internal static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/carts")
            .WithTags("Carts")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

        group.MapGet("/me", GetCartAsync).RequireAuthorization(Permissions.CartOwn);
        group.MapPost("/me/items", AddItemAsync).RequireAuthorization(Permissions.CartOwn);
        group.MapPut("/me/items/{productId:guid}", SetQuantityAsync).RequireAuthorization(Permissions.CartOwn);
        group.MapDelete("/me/items/{productId:guid}", RemoveItemAsync).RequireAuthorization(Permissions.CartOwn);
        group.MapPost("/merge", MergeAsync).RequireAuthorization(Permissions.CartOwn);

        // Public: re-prices an anonymous localStorage cart. Persists nothing.
        group.MapPost("/price", PriceAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Anonymous);

        return endpoints;
    }

    private static async Task<IResult> GetCartAsync(
        CartService carts,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await carts.GetCartAsync(currentUser.RequiredId, cancellationToken));

    private static async Task<IResult> AddItemAsync(
        AddCartItemRequest request,
        CartService carts,
        ICurrentUser currentUser,
        IValidator<AddCartItemRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await carts.AddItemAsync(currentUser.RequiredId, request, cancellationToken));
    }

    private static async Task<IResult> SetQuantityAsync(
        Guid productId,
        UpdateCartItemRequest request,
        CartService carts,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await carts.SetQuantityAsync(currentUser.RequiredId, productId, request.Quantity, cancellationToken));

    private static async Task<IResult> RemoveItemAsync(
        Guid productId,
        CartService carts,
        ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        Results.Ok(await carts.RemoveItemAsync(currentUser.RequiredId, productId, cancellationToken));

    /// <summary>
    /// Idempotent on <c>mergeToken</c> (spec §8.4): a retry after a network blip must not
    /// double quantities.
    /// </summary>
    private static async Task<IResult> MergeAsync(
        MergeCartRequest request,
        CartService carts,
        ICurrentUser currentUser,
        IValidator<MergeCartRequest> validator,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return Results.Ok(await carts.MergeAsync(currentUser.RequiredId, request, cancellationToken));
    }

    private static async Task<IResult> PriceAsync(
        PriceCartRequest request,
        CartService carts,
        CancellationToken cancellationToken) =>
        Results.Ok(await carts.PriceAnonymousAsync(request, cancellationToken));
}

internal sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    /// <summary>An upper bound so a typo cannot request a million units.</summary>
    internal const int MaxLineQuantity = 999;

    public AddCartItemRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.Quantity).InclusiveBetween(1, MaxLineQuantity);
    }
}

internal sealed class MergeCartRequestValidator : AbstractValidator<MergeCartRequest>
{
    /// <summary>Bounds the merge payload; a localStorage cart should never be this large.</summary>
    private const int MaxMergeLines = 200;

    public MergeCartRequestValidator()
    {
        RuleFor(r => r.MergeToken).NotEmpty().WithMessage("A mergeToken is required so the merge can be made idempotent.");
        RuleFor(r => r.Items).NotNull();
        RuleFor(r => r.Items.Count).LessThanOrEqualTo(MaxMergeLines).When(r => r.Items is not null);

        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, AddCartItemRequestValidator.MaxLineQuantity);
        });
    }
}
