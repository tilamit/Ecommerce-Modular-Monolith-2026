using FluentValidation;

namespace ShopHub.Modules.Catalog.Features.Products;

/// <summary>Bounds shared by the create and update validators.</summary>
internal static class ProductRules
{
    internal const decimal MaxPrice = 1_000_000m;
    internal const int MaxStock = 1_000_000;
}

internal sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Sku).NotEmpty().MaximumLength(64);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Slug).MaximumLength(300);
        RuleFor(r => r.ShortDescription).MaximumLength(512);
        RuleFor(r => r.Description).MaximumLength(4000);

        RuleFor(r => r.Price).GreaterThan(0).LessThanOrEqualTo(ProductRules.MaxPrice);
        RuleFor(r => r.CompareAtPrice)
            .GreaterThan(r => r.Price)
            .When(r => r.CompareAtPrice.HasValue)
            .WithMessage("CompareAtPrice must be greater than Price.");

        // ISO-4217 is exactly three letters; anything else would break the money contract.
        RuleFor(r => r.CurrencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");

        RuleFor(r => r.StockQuantity).InclusiveBetween(0, ProductRules.MaxStock);
    }
}

internal sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Sku).NotEmpty().MaximumLength(64);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Slug).MaximumLength(300);
        RuleFor(r => r.ShortDescription).MaximumLength(512);
        RuleFor(r => r.Description).MaximumLength(4000);

        RuleFor(r => r.Price).GreaterThan(0).LessThanOrEqualTo(ProductRules.MaxPrice);
        RuleFor(r => r.CompareAtPrice)
            .GreaterThan(r => r.Price)
            .When(r => r.CompareAtPrice.HasValue)
            .WithMessage("CompareAtPrice must be greater than Price.");

        RuleFor(r => r.CurrencyCode).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(r => r.StockQuantity).InclusiveBetween(0, ProductRules.MaxStock);
    }
}
