using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopHub.Modules.Catalog.Contracts;
using ShopHub.Modules.Identity.Contracts;
using ShopHub.Modules.Ordering.Domain;
using ShopHub.Modules.Ordering.Infrastructure;
using ShopHub.Shared.Infrastructure.Modules;
using ShopHub.Shared.Kernel.Abstractions;

namespace ShopHub.Modules.Ordering.Persistence;

/// <summary>
/// Seeds ~60 orders across the last 60 days, mixed registered and guest, mixed statuses
/// (spec §13) - enough to make dashboards and filters meaningful on day one.
/// </summary>
/// <remarks>
/// Note what this deliberately does <em>not</em> do: reserve stock. It writes historical
/// orders directly rather than replaying checkout, because decrementing catalogue stock 60
/// times would leave the seeded catalogue in a state nobody chose. Seeded orders are
/// history, not transactions.
/// </remarks>
internal sealed class OrderingSeeder(
    OrderingDbContext db,
    IIdentityModuleApi identity,
    ICatalogModuleApi catalog,
    IClock clock,
    ILogger<OrderingSeeder> logger)
{
    private const int RandomSeed = 20260823;
    private const int TargetOrderCount = 60;
    private const int SeedWindowDays = 60;

    /// <summary>How many products and customers to draw the seeded orders from.</summary>
    private const int SeedProductPoolSize = 60;

    private const int SeedCustomerPoolSize = 25;

    /// <summary>One of these has an email that also belongs to a seeded user (spec §13).</summary>
    private static readonly (string Email, string FullName, string Phone)[] GuestSeeds =
    [
        ("guest.one@example.com", "Jordan Reyes", "+1 555 0201"),
        ("guest.two@example.com", "Priya Nair", "+1 555 0202"),
        // Matches the seeded customer ada@example.com, so the §8.4 prompt path has data.
        ("ada@example.com", "Ada L.", "+1 555 0101"),
    ];

    private static readonly OrderStatus[] StatusMix =
    [
        OrderStatus.Pending,
        OrderStatus.Confirmed,
        OrderStatus.Processing,
        OrderStatus.Shipped,
        OrderStatus.Delivered,
        OrderStatus.Delivered,
        OrderStatus.Cancelled,
    ];

    internal async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Orders.AnyAsync(cancellationToken))
        {
            return;
        }

        var products = await LoadSeedProductsAsync(cancellationToken);
        var customerIds = await LoadCustomerIdsAsync(cancellationToken);

        if (products.Count == 0 || customerIds.Count == 0)
        {
            logger.LogWarning("Skipping order seed: catalogue or customers not seeded yet.");
            return;
        }

        var guests = await SeedGuestProfilesAsync(cancellationToken);
        var random = new Random(RandomSeed);
        var now = clock.UtcNow;
        var counter = 1;

        for (var i = 0; i < TargetOrderCount; i++)
        {
            var placedUtc = now.AddDays(-random.Next(0, SeedWindowDays)).AddHours(-random.Next(0, 24));

            // Roughly one in three is a guest order, so the CustomerType filter has both.
            var isGuest = random.Next(0, 3) == 0;
            var address = BuildAddress(random, isGuest ? guests[random.Next(guests.Count)].FullName : "Seeded Customer");
            var paymentMethod = (PaymentMethod)random.Next(1, 4);
            var orderNumber = $"ORD-{placedUtc:yyyyMMdd}-{counter++:D6}";

            var order = isGuest
                ? Order.PlaceForGuest(orderNumber, guests[random.Next(guests.Count)].Id, "USD", paymentMethod, address, null, placedUtc)
                : Order.PlaceForCustomer(orderNumber, customerIds[random.Next(customerIds.Count)], "USD", paymentMethod, address, null, placedUtc);

            var lineCount = random.Next(1, 4);

            for (var line = 0; line < lineCount; line++)
            {
                var product = products[random.Next(products.Count)];

                order.AddItem(
                    product.Id,
                    product.Name,
                    product.Sku,
                    product.Price,
                    random.Next(1, 4));
            }

            order.ApplyTotals(0m, 0m, 0m, null);
            order.RecordPlacement(placedUtc, null);

            // Walk the order forward to its seeded status so OrderStatusHistory is
            // coherent - a Delivered order with no Confirmed row would be a lie.
            AdvanceToStatus(order, StatusMix[random.Next(StatusMix.Length)], placedUtc);

            order.CreatedUtc = placedUtc;
            db.Orders.Add(order);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Ordering seed complete.");
    }

    /// <summary>
    /// Steps through legal transitions rather than jumping, so the history matches the
    /// transition table the domain enforces.
    /// </summary>
    private static void AdvanceToStatus(Order order, OrderStatus target, DateTime placedUtc)
    {
        var path = target switch
        {
            OrderStatus.Pending => Array.Empty<OrderStatus>(),
            OrderStatus.Confirmed => [OrderStatus.Confirmed],
            OrderStatus.Processing => [OrderStatus.Confirmed, OrderStatus.Processing],
            OrderStatus.Shipped => [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped],
            OrderStatus.Delivered => [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered],
            OrderStatus.Cancelled => [OrderStatus.Cancelled],
            _ => [],
        };

        var offset = 1;

        foreach (var step in path)
        {
            order.ChangeStatus(step, placedUtc.AddHours(offset++), null, "Seeded transition.");
        }
    }

    private async Task<List<GuestCheckoutProfile>> SeedGuestProfilesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.GuestCheckoutProfiles.AsTracking().ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            return existing;
        }

        var now = clock.UtcNow;
        var created = new List<GuestCheckoutProfile>();

        foreach (var (email, fullName, phone) in GuestSeeds)
        {
            var address = new ShippingAddress(fullName, "1 Example Way", null, "Springfield", "IL", "62701", "USA", phone);
            var profile = GuestCheckoutProfile.Create(email, fullName, phone, address, PaymentMethod.CashOnDelivery, now);

            db.GuestCheckoutProfiles.Add(profile);
            created.Add(profile);
        }

        await db.SaveChangesAsync(cancellationToken);

        return created;
    }

    /// <summary>
    /// Pulls product facts through Catalog's Contracts, exactly as checkout does. There is
    /// no shortcut available here - <c>catalog.Products</c> is unreachable from Ordering.
    /// </summary>
    private async Task<List<ProductSnapshotDto>> LoadSeedProductsAsync(CancellationToken cancellationToken)
    {
        var ids = await catalog.GetActiveProductIdsAsync(SeedProductPoolSize, cancellationToken);

        if (ids.Count == 0)
        {
            return [];
        }

        var snapshots = await catalog.GetProductSnapshotsAsync(ids, cancellationToken);

        return [.. snapshots];
    }

    /// <summary>Seeded customers to spread the orders across, via Identity's Contracts.</summary>
    private async Task<List<Guid>> LoadCustomerIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await identity.GetUserIdsInRoleAsync("Customer", SeedCustomerPoolSize, cancellationToken);

        return [.. ids];
    }

    private static ShippingAddress BuildAddress(Random random, string fullName) =>
        new(
            fullName,
            $"{random.Next(1, 400)} Market Street",
            random.Next(0, 2) == 0 ? $"Apt {random.Next(1, 40)}" : null,
            "Springfield",
            "IL",
            "62701",
            "USA",
            "+1 555 0100");
}

/// <summary>Applies the Ordering module's migrations and runs its seeder (spec §13).</summary>
internal sealed class OrderingInitializer(OrderingDbContext db, OrderingSeeder seeder) : IModuleInitializer
{
    public string ModuleName => "ordering";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await seeder.SeedAsync(cancellationToken);
    }
}
