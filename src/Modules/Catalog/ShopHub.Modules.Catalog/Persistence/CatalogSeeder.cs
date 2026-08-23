using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Modules.Catalog.Domain;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Catalog.Persistence;

/// <summary>
/// Seeds ~10 categories over two levels, ~120 products, and 3 offers (spec §13).
/// <para>
/// Idempotent: every step checks before inserting. Deterministic too - a fixed RNG seed
/// means the same catalogue every run, so a screenshot or a failing test is reproducible.
/// </para>
/// </summary>
internal sealed class CatalogSeeder(CatalogDbContext db, IClock clock, ILogger<CatalogSeeder> logger)
{
    /// <summary>Fixed seed: the generated catalogue must be identical on every machine.</summary>
    private const int RandomSeed = 20260823;

    private const int TargetProductCount = 120;

    private static readonly (string Parent, string[] Children)[] CategoryTree =
    [
        ("Electronics", ["Laptops", "Phones", "Audio"]),
        ("Home & Kitchen", ["Cookware", "Appliances"]),
        ("Sports & Outdoors", ["Camping", "Fitness"]),
        ("Books", ["Fiction", "Technical"]),
    ];

    internal async Task SeedAsync(CancellationToken cancellationToken)
    {
        var categories = await SeedCategoriesAsync(cancellationToken);
        await SeedProductsAsync(categories, cancellationToken);
        await SeedOffersAsync(cancellationToken);

        logger.LogInformation("Catalog seed complete.");
    }

    private async Task<Dictionary<string, Category>> SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Categories
            .AsTracking()
            .ToDictionaryAsync(c => c.Name, StringComparer.Ordinal, cancellationToken);

        var order = 0;

        foreach (var (parentName, children) in CategoryTree)
        {
            if (!existing.TryGetValue(parentName, out var parent))
            {
                parent = Category.Create(parentName, parentName, displayOrder: order);
                db.Categories.Add(parent);
                existing[parentName] = parent;
            }

            order += 10;

            // Saved before the children so the parent has a resolved id to reference.
            await db.SaveChangesAsync(cancellationToken);

            var childOrder = 0;

            foreach (var childName in children)
            {
                if (existing.ContainsKey(childName))
                {
                    continue;
                }

                var child = Category.Create(childName, childName, parent.Id, displayOrder: childOrder);
                db.Categories.Add(child);
                existing[childName] = child;
                childOrder += 10;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return existing;
    }

    private async Task SeedProductsAsync(Dictionary<string, Category> categories, CancellationToken cancellationToken)
    {
        var currentCount = await db.Products.CountAsync(cancellationToken);

        if (currentCount >= TargetProductCount)
        {
            return;
        }

        // Only leaf categories get products, so the tree has a realistic shape.
        var leaves = categories.Values
            .Where(c => c.ParentId is not null)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

        if (leaves.Length == 0)
        {
            return;
        }

        var random = new Random(RandomSeed);
        var now = clock.UtcNow;
        var existingSkus = await db.Products.Select(p => p.Sku).ToListAsync(cancellationToken);
        var skus = existingSkus.ToHashSet(StringComparer.Ordinal);

        for (var i = currentCount; i < TargetProductCount; i++)
        {
            var category = leaves[i % leaves.Length];
            var sku = $"SKU-{i + 1:D5}";

            if (!skus.Add(sku))
            {
                continue;
            }

            var name = $"{category.Name.TrimEnd('s')} Model {i + 1:D3}";
            var price = Math.Round((decimal)(random.NextDouble() * 900 + 15), 2);

            var product = Product.Create(category.Id, sku, name, $"{name}-{sku}", price, random.Next(0, 60));

            product.Update(
                category.Id,
                sku,
                name,
                $"{name}-{sku}",
                $"A dependable {category.Name.TrimEnd('s').ToLowerInvariant()} for everyday use.",
                $"Full description for {name}. Part of the {category.Name} range.",
                price,
                // Roughly a third carry a strike-through price, so the UI's discount badge
                // has something to render.
                random.Next(0, 3) == 0 ? Math.Round(price * 1.25m, 2) : null,
                "USD",
                random.Next(0, 60),
                isFeatured: random.Next(0, 8) == 0);

            product.SetRating(Math.Round((decimal)(random.NextDouble() * 2 + 3), 2), random.Next(0, 400));

            product.ReplaceImages(
            [
                ($"https://picsum.photos/seed/{sku}/640/640", name, 0, true),
                ($"https://picsum.photos/seed/{sku}-alt/640/640", $"{name} alternate view", 1, false),
            ]);

            // Roughly one in eight is inactive, so admin filters have something to filter.
            if (random.Next(0, 8) == 0)
            {
                product.Deactivate();
            }

            db.Products.Add(product);

            // Backdate some rows into the last 30 days so "new arrivals" and the dashboard
            // charts have data on day one (spec §13).
            product.CreatedUtc = now.AddDays(-random.Next(0, 90)).AddHours(-random.Next(0, 24));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedOffersAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Spec §13: two live offers (one percentage, one fixed) and one expired.
        var definitions = new (string Code, string Name, DiscountType Type, decimal Value, DateTime Start, DateTime End, decimal? Minimum)[]
        {
            ("WELCOME10", "Welcome discount", DiscountType.Percentage, 10m, now.AddDays(-7), now.AddDays(60), null),
            ("SAVE25", "Save $25 on orders over $200", DiscountType.FixedAmount, 25m, now.AddDays(-3), now.AddDays(45), 200m),
            ("SUMMER20", "Expired summer sale", DiscountType.Percentage, 20m, now.AddDays(-90), now.AddDays(-30), null),
        };

        var existing = await db.Offers.Select(o => o.Code).ToListAsync(cancellationToken);
        var codes = existing.ToHashSet(StringComparer.Ordinal);

        foreach (var (code, name, type, value, start, end, minimum) in definitions)
        {
            if (codes.Contains(code))
            {
                continue;
            }

            db.Offers.Add(Offer.Create(code, name, type, value, start, end, minimum));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
