using ShopHub.Modules.Ordering.Domain;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.UnitTests.Ordering;

/// <summary>
/// Status transitions and cancellation windows live in the domain (ADR-003), so they hold
/// for the admin endpoint and the customer cancel path alike.
/// </summary>
public sealed class OrderTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ShippingAddress Address =
        new("Ada Lovelace", "1 Analytical Way", null, "London", null, "EC1A", "UK", null);

    private static Order CustomerOrder()
    {
        var order = Order.PlaceForCustomer(
            "ORD-20260823-000001",
            Guid.CreateVersion7(),
            "USD",
            PaymentMethod.Card,
            Address,
            notes: null,
            Now);

        order.AddItem(Guid.CreateVersion7(), "Laptop Pro", "SKU-1", 100m, 2);
        order.ApplyTotals(0m, 0m, 0m, null);
        order.RecordPlacement(Now, null);

        return order;
    }

    [Fact]
    public void PlaceForCustomer_SetsUserIdAndLeavesGuestNull()
    {
        var order = CustomerOrder();

        Assert.NotNull(order.UserId);
        Assert.Null(order.GuestCheckoutProfileId);
        Assert.False(order.IsGuestOrder);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void PlaceForGuest_SetsGuestProfileAndLeavesUserNull()
    {
        var order = Order.PlaceForGuest(
            "ORD-20260823-000002",
            Guid.CreateVersion7(),
            "USD",
            PaymentMethod.CashOnDelivery,
            Address,
            null,
            Now);

        Assert.Null(order.UserId);
        Assert.NotNull(order.GuestCheckoutProfileId);
        Assert.True(order.IsGuestOrder);
    }

    /// <summary>Spec A8: nothing is charged, so payment status stays NotApplicable.</summary>
    [Fact]
    public void ANewOrder_HasNoApplicablePaymentStatus() =>
        Assert.Equal(PaymentStatus.NotApplicable, CustomerOrder().PaymentStatus);

    [Fact]
    public void ApplyTotals_DerivesGrandTotalFromTheLines()
    {
        var order = CustomerOrder();

        order.ApplyTotals(discountTotal: 15m, taxTotal: 8m, shippingTotal: 5m, appliedOfferCode: "SAVE15");

        Assert.Equal(200m, order.SubTotal);
        Assert.Equal(15m, order.DiscountTotal);
        Assert.Equal(198m, order.GrandTotal);
        Assert.Equal("SAVE15", order.AppliedOfferCode);
    }

    /// <summary>A discount larger than the cart must not produce a negative total.</summary>
    [Fact]
    public void ApplyTotals_ClampsDiscountToTheSubtotal()
    {
        var order = CustomerOrder();

        order.ApplyTotals(discountTotal: 500m, taxTotal: 0m, shippingTotal: 0m, appliedOfferCode: null);

        Assert.Equal(200m, order.DiscountTotal);
        Assert.Equal(0m, order.GrandTotal);
    }

    [Fact]
    public void RecordPlacement_WritesTheFirstHistoryRow()
    {
        var order = CustomerOrder();

        var first = Assert.Single(order.StatusHistory);
        Assert.Null(first.FromStatus);
        Assert.Equal(OrderStatus.Pending, first.ToStatus);
    }

    // Statuses are passed by name: OrderStatus is internal, and a public xUnit test method
    // cannot take an internal parameter type.
    [Theory]
    [InlineData(nameof(OrderStatus.Confirmed))]
    [InlineData(nameof(OrderStatus.Cancelled))]
    public void Pending_MayMoveToConfirmedOrCancelled(string statusName)
    {
        var target = Enum.Parse<OrderStatus>(statusName);
        var order = CustomerOrder();

        order.ChangeStatus(target, Now.AddHours(1), Guid.CreateVersion7(), "reason");

        Assert.Equal(target, order.Status);
        Assert.Equal(2, order.StatusHistory.Count);
    }

    /// <summary>
    /// The table refuses illegal jumps, so an order cannot go from Pending straight to
    /// Delivered - nor backwards from Delivered to Pending.
    /// </summary>
    [Theory]
    [InlineData(nameof(OrderStatus.Delivered))]
    [InlineData(nameof(OrderStatus.Shipped))]
    [InlineData(nameof(OrderStatus.Refunded))]
    public void Pending_CannotJumpAhead(string statusName)
    {
        var target = Enum.Parse<OrderStatus>(statusName);
        var order = CustomerOrder();

        var exception = Assert.Throws<DomainRuleException>(
            () => order.ChangeStatus(target, Now, null, null));

        Assert.Equal("invalid_status_transition", exception.Code);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void ACancelledOrder_IsTerminal()
    {
        var order = CustomerOrder();
        order.ChangeStatus(OrderStatus.Cancelled, Now, null, null);

        Assert.Throws<DomainRuleException>(() => order.ChangeStatus(OrderStatus.Confirmed, Now, null, null));
    }

    [Fact]
    public void ChangingToTheSameStatus_IsANoOp()
    {
        var order = CustomerOrder();

        order.ChangeStatus(OrderStatus.Pending, Now, null, null);

        Assert.Single(order.StatusHistory);
    }

    // --- ADR-003: the customer cancellation window --------------------------

    [Theory]
    [InlineData(nameof(OrderStatus.Pending))]
    [InlineData(nameof(OrderStatus.Confirmed))]
    public void Customer_MayCancelBeforeProcessing(string fromStatusName)
    {
        var from = Enum.Parse<OrderStatus>(fromStatusName);
        var order = CustomerOrder();
        var customerId = order.UserId!.Value;

        if (from == OrderStatus.Confirmed)
        {
            order.ChangeStatus(OrderStatus.Confirmed, Now, null, null);
        }

        order.CancelByCustomer(Now.AddHours(2), customerId, "Changed my mind.");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(customerId, order.StatusHistory.Last().ChangedByUserId);
    }

    /// <summary>
    /// From Processing onward, physical work has started (ADR-003). An admin may still
    /// cancel; the customer may not.
    /// </summary>
    [Fact]
    public void Customer_CannotCancelOnceProcessing()
    {
        var order = CustomerOrder();
        var customerId = order.UserId!.Value;

        order.ChangeStatus(OrderStatus.Confirmed, Now, null, null);
        order.ChangeStatus(OrderStatus.Processing, Now, null, null);

        var exception = Assert.Throws<DomainRuleException>(
            () => order.CancelByCustomer(Now, customerId, null));

        Assert.Equal("cancellation_window_closed", exception.Code);
        Assert.Equal(OrderStatus.Processing, order.Status);
    }

    [Fact]
    public void Admin_CanStillCancelWhileProcessing()
    {
        var order = CustomerOrder();

        order.ChangeStatus(OrderStatus.Confirmed, Now, null, null);
        order.ChangeStatus(OrderStatus.Processing, Now, null, null);
        order.ChangeStatus(OrderStatus.Cancelled, Now, Guid.CreateVersion7(), "Out of stock at the warehouse.");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void AddItem_RefusesANonPositiveQuantity()
    {
        var order = CustomerOrder();

        Assert.Throws<DomainRuleException>(
            () => order.AddItem(Guid.CreateVersion7(), "Thing", "SKU-2", 10m, 0));
    }

    /// <summary>
    /// Line totals are snapshotted at order time, so a later price change cannot alter a
    /// placed order (spec §4.2).
    /// </summary>
    [Fact]
    public void OrderItems_SnapshotNameSkuAndPrice()
    {
        var order = CustomerOrder();
        var item = order.Items.Single();

        Assert.Equal("Laptop Pro", item.ProductNameSnapshot);
        Assert.Equal("SKU-1", item.SkuSnapshot);
        Assert.Equal(100m, item.UnitPrice);
        Assert.Equal(200m, item.LineTotal);
    }
}
