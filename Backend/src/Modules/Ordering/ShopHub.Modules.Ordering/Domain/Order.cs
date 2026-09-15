using ShopHub.Shared.Kernel.Entities;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Ordering.Domain;

internal enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Processing = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    Refunded = 7,
}

/// <summary>Recorded, not processed (spec A8). No gateway is integrated.</summary>
internal enum PaymentMethod
{
    CashOnDelivery = 1,
    Card = 2,
    BankTransfer = 3,
}

/// <summary>Always <see cref="NotApplicable"/> for now (spec A8).</summary>
internal enum PaymentStatus
{
    NotApplicable = 1,
    Pending = 2,
    Paid = 3,
    Refunded = 4,
}

/// <summary>
/// A placed order (spec §8.3). Created only at checkout - never as a "pending" cart
/// (ADR-005).
/// <para>
/// Exactly one of <see cref="UserId"/> / <see cref="GuestCheckoutProfileId"/> is set and a
/// CHECK constraint enforces it in the database too. That is what makes "is this a
/// registered customer?" answerable without ambiguity (spec §8.3).
/// </para>
/// </summary>
internal sealed class Order : AuditableEntity, IAuditableEntity
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusHistory> _statusHistory = [];

    private Order()
    {
    }

    private Order(
        Guid id,
        string orderNumber,
        Guid? userId,
        Guid? guestCheckoutProfileId,
        string currencyCode,
        PaymentMethod paymentMethod,
        ShippingAddress shippingAddress,
        string? notes,
        DateTime placedUtc)
        : base(id)
    {
        OrderNumber = orderNumber;
        UserId = userId;
        GuestCheckoutProfileId = guestCheckoutProfileId;
        CurrencyCode = currencyCode;
        PaymentMethod = paymentMethod;
        PaymentStatus = PaymentStatus.NotApplicable;
        ShippingAddress = shippingAddress;
        Notes = notes;
        PlacedUtc = placedUtc;
        Status = OrderStatus.Pending;
    }

    /// <summary>Human-readable and unique, e.g. <c>ORD-20260823-000147</c> (spec §8.3).</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    public Guid? UserId { get; private set; }

    public Guid? GuestCheckoutProfileId { get; private set; }

    public OrderStatus Status { get; private set; }

    public decimal SubTotal { get; private set; }

    public decimal DiscountTotal { get; private set; }

    public decimal TaxTotal { get; private set; }

    public decimal ShippingTotal { get; private set; }

    public decimal GrandTotal { get; private set; }

    public string CurrencyCode { get; private set; } = "USD";

    public string? AppliedOfferCode { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public PaymentStatus PaymentStatus { get; private set; }

    /// <summary>
    /// Snapshot, denormalized. An order's address must not change when the customer later
    /// edits their address book (spec §8.3).
    /// </summary>
    public ShippingAddress ShippingAddress { get; private set; } = null!;

    public DateTime PlacedUtc { get; private set; }

    public string? Notes { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<OrderItem> Items => _items;

    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory;

    public bool IsGuestOrder => GuestCheckoutProfileId is not null;

    /// <summary>
    /// Which statuses each status may move to. Encoded once, here, so the rule holds for
    /// the admin endpoint and the customer cancel path alike.
    /// </summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [OrderStatus.Refunded],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Refunded] = [],
    };

    /// <summary>
    /// Statuses a customer may cancel from (ADR-003). Beyond <see cref="OrderStatus.Confirmed"/>
    /// physical work has started and the decision needs a human.
    /// </summary>
    private static readonly OrderStatus[] CustomerCancellableFrom = [OrderStatus.Pending, OrderStatus.Confirmed];

    public static Order PlaceForCustomer(
        string orderNumber,
        Guid userId,
        string currencyCode,
        PaymentMethod paymentMethod,
        ShippingAddress shippingAddress,
        string? notes,
        DateTime placedUtc) =>
        new(SequentialGuid.New(), orderNumber, userId, null, currencyCode, paymentMethod, shippingAddress, notes, placedUtc);

    public static Order PlaceForGuest(
        string orderNumber,
        Guid guestProfileId,
        string currencyCode,
        PaymentMethod paymentMethod,
        ShippingAddress shippingAddress,
        string? notes,
        DateTime placedUtc) =>
        new(SequentialGuid.New(), orderNumber, null, guestProfileId, currencyCode, paymentMethod, shippingAddress, notes, placedUtc);

    /// <summary>
    /// Adds a line, snapshotting the product's name, SKU and price as of now (spec §4.2).
    /// The order must not change when a product is later renamed or repriced.
    /// </summary>
    public void AddItem(
        Guid productId,
        string productNameSnapshot,
        string skuSnapshot,
        decimal unitPrice,
        int quantity,
        decimal discountAmount = 0m)
    {
        if (quantity <= 0)
        {
            throw new DomainRuleException("quantity_invalid", "Quantity must be greater than zero.");
        }

        _items.Add(OrderItem.Create(Id, productId, productNameSnapshot, skuSnapshot, unitPrice, quantity, discountAmount));
    }

    /// <summary>
    /// Recomputes the money columns from the lines. Called once after all items are added,
    /// so the stored totals can never disagree with the lines that produced them.
    /// </summary>
    public void ApplyTotals(decimal discountTotal, decimal taxTotal, decimal shippingTotal, string? appliedOfferCode)
    {
        SubTotal = Math.Round(_items.Sum(i => i.LineTotal), 2, MidpointRounding.AwayFromZero);
        DiscountTotal = Math.Round(Math.Min(discountTotal, SubTotal), 2, MidpointRounding.AwayFromZero);
        TaxTotal = Math.Round(taxTotal, 2, MidpointRounding.AwayFromZero);
        ShippingTotal = Math.Round(shippingTotal, 2, MidpointRounding.AwayFromZero);
        AppliedOfferCode = appliedOfferCode;

        GrandTotal = SubTotal - DiscountTotal + TaxTotal + ShippingTotal;
    }

    /// <summary>Records the initial status row, so history is complete from placement onward.</summary>
    public void RecordPlacement(DateTime nowUtc, Guid? changedByUserId) =>
        _statusHistory.Add(OrderStatusHistory.Create(Id, null, OrderStatus.Pending, nowUtc, changedByUserId, "Order placed."));

    /// <summary>
    /// Moves the order to a new status, refusing transitions the table above does not allow
    /// - so an order cannot go from Delivered back to Pending, whichever caller asks.
    /// </summary>
    public void ChangeStatus(OrderStatus target, DateTime nowUtc, Guid? changedByUserId, string? reason)
    {
        if (target == Status)
        {
            return;
        }

        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(target))
        {
            throw new DomainRuleException(
                "invalid_status_transition",
                $"An order cannot move from {Status} to {target}.");
        }

        var from = Status;
        Status = target;

        _statusHistory.Add(OrderStatusHistory.Create(Id, from, target, nowUtc, changedByUserId, reason));
    }

    /// <summary>
    /// Customer-initiated cancellation (ADR-003). Distinct from
    /// <see cref="ChangeStatus"/> because the window is narrower than an admin's.
    /// </summary>
    public void CancelByCustomer(DateTime nowUtc, Guid customerId, string? reason)
    {
        if (!CustomerCancellableFrom.Contains(Status))
        {
            throw new DomainRuleException(
                "cancellation_window_closed",
                $"This order is already {Status} and can no longer be cancelled online. Contact support.");
        }

        ChangeStatus(OrderStatus.Cancelled, nowUtc, customerId, reason ?? "Cancelled by customer.");
    }

    /// <summary>True when cancelling should return reserved stock to the shelf.</summary>
    public bool ReleasesStockOnCancellation => Status == OrderStatus.Cancelled;
}

/// <summary>
/// Shipping address as of order time. An owned type, so it lives in the Orders row rather
/// than in a joined table that could be edited independently.
/// </summary>
internal sealed record ShippingAddress(
    string FullName,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country,
    string? PhoneNumber);

/// <summary>
/// One line of a placed order (spec §8.3). Every product fact is a snapshot - there is no
/// foreign key to <c>catalog.Products</c>, by design.
/// </summary>
internal sealed class OrderItem : Entity
{
    private OrderItem()
    {
    }

    private OrderItem(
        Guid id,
        Guid orderId,
        Guid productId,
        string productNameSnapshot,
        string skuSnapshot,
        decimal unitPrice,
        int quantity,
        decimal discountAmount)
        : base(id)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductNameSnapshot = productNameSnapshot;
        SkuSnapshot = skuSnapshot;
        UnitPrice = unitPrice;
        Quantity = quantity;
        DiscountAmount = discountAmount;
        LineTotal = Math.Round((unitPrice * quantity) - discountAmount, 2, MidpointRounding.AwayFromZero);
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductNameSnapshot { get; private set; } = string.Empty;

    public string SkuSnapshot { get; private set; } = string.Empty;

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal LineTotal { get; private set; }

    internal static OrderItem Create(
        Guid orderId,
        Guid productId,
        string productNameSnapshot,
        string skuSnapshot,
        decimal unitPrice,
        int quantity,
        decimal discountAmount) =>
        new(SequentialGuid.New(), orderId, productId, productNameSnapshot, skuSnapshot, unitPrice, quantity, discountAmount);
}

/// <summary>An audit of one status transition (spec §8.3).</summary>
internal sealed class OrderStatusHistory : Entity
{
    private OrderStatusHistory()
    {
    }

    private OrderStatusHistory(
        Guid id,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        DateTime changedUtc,
        Guid? changedByUserId,
        string? reason)
        : base(id)
    {
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedUtc = changedUtc;
        ChangedByUserId = changedByUserId;
        Reason = reason;
    }

    public Guid OrderId { get; private set; }

    /// <summary>Null for the placement row - there was no previous status.</summary>
    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public DateTime ChangedUtc { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public string? Reason { get; private set; }

    internal static OrderStatusHistory Create(
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        DateTime changedUtc,
        Guid? changedByUserId,
        string? reason) =>
        new(SequentialGuid.New(), orderId, fromStatus, toStatus, changedUtc, changedByUserId, reason);
}
