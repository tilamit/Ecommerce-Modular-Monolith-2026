using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ShopHub.Shared.Infrastructure.RateLimiting;
using ShopHub.Shared.Infrastructure.Security;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Ordering.Features.Checkout;

/// <summary><c>/api/v1/checkout</c> (spec §9.3), on the <c>checkout</c> rate-limit policy.</summary>
internal static class CheckoutEndpoints
{
    internal static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/checkout")
            .WithTags("Checkout")
            .RequireRateLimiting(RateLimitPolicies.Checkout);

        group.MapPost("/", CheckoutAsync).RequireAuthorization(Permissions.CheckoutOwn);
        group.MapPost("/guest", GuestCheckoutAsync).AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequest request,
        CheckoutService checkout,
        ICurrentUser currentUser,
        IValidator<CheckoutRequest> validator,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        try
        {
            var response = await checkout.CheckoutAsync(currentUser.RequiredId, request, cancellationToken);

            return Results.Created($"/api/v1/orders/me/{response.OrderId}", response);
        }
        catch (StockReservationException ex)
        {
            return ReservationConflict(http, ex);
        }
    }

    private static async Task<IResult> GuestCheckoutAsync(
        GuestCheckoutRequest request,
        CheckoutService checkout,
        IValidator<GuestCheckoutRequest> validator,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        try
        {
            var response = await checkout.GuestCheckoutAsync(request, cancellationToken);

            return Results.Created($"/api/v1/orders/{response.OrderId}", response);
        }
        catch (StockReservationException ex)
        {
            return ReservationConflict(http, ex);
        }
    }

    /// <summary>
    /// ADR-002: a 409 carrying a reason <em>per line</em>.
    /// <para>
    /// Built here rather than in the global exception handler because the failures are
    /// structured payload, not a message - the SPA renders them next to the offending cart
    /// rows so the customer can see exactly what changed and decide.
    /// </para>
    /// </summary>
    private static IResult ReservationConflict(HttpContext http, StockReservationException exception)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Some items are no longer available.",
            Detail = exception.Message,
            Type = "https://httpstatuses.io/409",
            Instance = $"{http.Request.Method} {http.Request.Path}",
        };

        problem.Extensions["code"] = exception.Code;
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? http.TraceIdentifier;
        problem.Extensions["failures"] = exception.Failures;

        return Results.Problem(problem);
    }
}

internal sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(a => a.FullName).NotEmpty().MaximumLength(256);
        RuleFor(a => a.Line1).NotEmpty().MaximumLength(256);
        RuleFor(a => a.Line2).MaximumLength(256);
        RuleFor(a => a.City).NotEmpty().MaximumLength(128);
        RuleFor(a => a.State).MaximumLength(128);
        RuleFor(a => a.PostalCode).NotEmpty().MaximumLength(32);
        RuleFor(a => a.Country).NotEmpty().MaximumLength(128);
        RuleFor(a => a.PhoneNumber).MaximumLength(32);
    }
}

internal sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(r => r.ShippingAddress).NotNull().SetValidator(new AddressRequestValidator());
        RuleFor(r => r.PaymentMethod).IsInEnum();
        RuleFor(r => r.OfferCode).MaximumLength(64);
        RuleFor(r => r.Notes).MaximumLength(1024);
    }
}

internal sealed class GuestCheckoutRequestValidator : AbstractValidator<GuestCheckoutRequest>
{
    public GuestCheckoutRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(256);
        RuleFor(r => r.PhoneNumber).MaximumLength(32);
        RuleFor(r => r.ShippingAddress).NotNull().SetValidator(new AddressRequestValidator());
        RuleFor(r => r.PaymentMethod).IsInEnum();
        RuleFor(r => r.OfferCode).MaximumLength(64);
        RuleFor(r => r.Notes).MaximumLength(1024);
        RuleFor(r => r.Items).NotEmpty().WithMessage("A guest checkout must include at least one item.");

        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}
