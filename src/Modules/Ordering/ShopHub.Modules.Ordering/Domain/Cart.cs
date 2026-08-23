using ShopHub.Shared.Kernel.Entities;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Ordering.Domain;

internal enum CartStatus
{
    Active = 1,
    Converted = 2,
    Abandoned = 3,
}

/// <summary>
/// A logged-in customer's saved cart (spec §8.3, ADR-005).
/// <para>
/// Deliberately <em>not</em> an <see cref="Order"/> with a <c>Pending</c> status: an order
/// that was never placed pollutes order numbering, revenue reporting and purchase history,
/// and leaves every downstream query needing <c>WHERE Status &lt;&gt; 'Pending'</c>.
/// </para>
/// </summary>
internal sealed class Cart : Entity
{
    private readonly List<CartItem> _items = [];

    private Cart()
    {
    }

    private Cart(Guid id, Guid? userId, Guid? anonymousId, DateTime nowUtc)
        : base(id)
    {
        UserId = userId;
        AnonymousId = anonymousId;
        Status = CartStatus.Active;
        CreatedUtc = nowUtc;
        ModifiedUtc = nowUtc;
    }

    public Guid? UserId { get; private set; }

    /// <summary>Correlates a pre-login cart, if one was persisted (spec §8.3).</summary>
    public Guid? AnonymousId { get; private set; }

    public CartStatus Status { get; private set; }

    /// <summary>
    /// Null for a signed-in cart: it is the user's saved cart and does not expire after an
    /// hour the way the anonymous localStorage cart does (spec §8.4 state 2).
    /// </summary>
    public DateTime? ExpiresUtc { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime ModifiedUtc { get; private set; }

    /// <summary>
    /// Guards against a replayed merge (spec §8.4 state 3). A retry after a network blip
    /// must not double quantities.
    /// </summary>
    public Guid? LastMergeToken { get; private set; }

    public DateTime? LastMergeUtc { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    public int TotalQuantity => _items.Sum(i => i.Quantity);

    public static Cart ForUser(Guid userId, DateTime nowUtc) => new(SequentialGuid.New(), userId, null, nowUtc);

    /// <summary>
    /// Adds or increases a line. Quantity is validated here so no caller can create a
    /// zero or negative line by sending one.
    /// </summary>
    public void AddItem(Guid productId, int quantity, decimal unitPriceSnapshot, DateTime nowUtc)
    {
        EnsurePositive(quantity);

        var existing = _items.Find(i => i.ProductId == productId);

        if (existing is null)
        {
            _items.Add(CartItem.Create(Id, productId, quantity, unitPriceSnapshot, nowUtc));
        }
        else
        {
            existing.IncreaseBy(quantity, unitPriceSnapshot);
        }

        Touch(nowUtc);
    }

    /// <summary>Sets an absolute quantity. Zero removes the line, which is what the UI's stepper expects.</summary>
    public void SetQuantity(Guid productId, int quantity, DateTime nowUtc)
    {
        if (quantity < 0)
        {
            throw new DomainRuleException("quantity_invalid", "Quantity cannot be negative.");
        }

        if (quantity == 0)
        {
            RemoveItem(productId, nowUtc);
            return;
        }

        var existing = _items.Find(i => i.ProductId == productId)
            ?? throw new NotFoundException("cart_item_not_found", "That product is not in your cart.");

        existing.SetQuantity(quantity);
        Touch(nowUtc);
    }

    public void RemoveItem(Guid productId, DateTime nowUtc)
    {
        _items.RemoveAll(i => i.ProductId == productId);
        Touch(nowUtc);
    }

    public void Clear(DateTime nowUtc)
    {
        _items.Clear();
        Touch(nowUtc);
    }

    /// <summary>
    /// Merge strategy from spec §8.4: <b>sum</b> quantities per product. Clamping to
    /// available stock and dropping unavailable products happen in the handler, which is
    /// the only place that can ask Catalog.
    /// </summary>
    public void MergeItem(Guid productId, int quantity, decimal unitPriceSnapshot, DateTime nowUtc) =>
        AddItem(productId, quantity, unitPriceSnapshot, nowUtc);

    /// <summary>
    /// Records a completed merge so a replay of the same token is ignored.
    /// </summary>
    public void RecordMerge(Guid mergeToken, DateTime nowUtc)
    {
        LastMergeToken = mergeToken;
        LastMergeUtc = nowUtc;
        Touch(nowUtc);
    }

    /// <summary>
    /// True when this exact merge has already been applied inside the idempotency window
    /// (spec §8.4: "ignore a repeat within 5 minutes").
    /// </summary>
    public bool HasAlreadyMerged(Guid mergeToken, DateTime nowUtc, TimeSpan window) =>
        LastMergeToken == mergeToken && LastMergeUtc is { } last && nowUtc - last <= window;

    /// <summary>Marks the cart converted at checkout. A converted cart is never reused.</summary>
    public void MarkConverted(DateTime nowUtc)
    {
        if (Status != CartStatus.Active)
        {
            throw new DomainRuleException("cart_not_active", "This cart has already been checked out.");
        }

        Status = CartStatus.Converted;
        Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc) => ModifiedUtc = nowUtc;

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainRuleException("quantity_invalid", "Quantity must be greater than zero.");
        }
    }
}

/// <summary>One line of a saved cart (spec §8.3).</summary>
internal sealed class CartItem : Entity
{
    private CartItem()
    {
    }

    private CartItem(Guid id, Guid cartId, Guid productId, int quantity, decimal unitPriceSnapshot, DateTime addedUtc)
        : base(id)
    {
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
        UnitPriceSnapshot = unitPriceSnapshot;
        AddedUtc = addedUtc;
    }

    public Guid CartId { get; private set; }

    /// <summary>A plain Guid - no cross-schema foreign key (spec §4.2).</summary>
    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>
    /// The price when the item was added, for display only.
    /// <para>
    /// Never trusted for money. Every cart render and the checkout itself re-price on the
    /// server through Catalog's Contracts - a stored price the client can influence is a
    /// price-tampering hole (spec §8.4).
    /// </para>
    /// </summary>
    public decimal UnitPriceSnapshot { get; private set; }

    public DateTime AddedUtc { get; private set; }

    internal static CartItem Create(Guid cartId, Guid productId, int quantity, decimal unitPriceSnapshot, DateTime addedUtc) =>
        new(SequentialGuid.New(), cartId, productId, quantity, unitPriceSnapshot, addedUtc);

    internal void IncreaseBy(int quantity, decimal unitPriceSnapshot)
    {
        Quantity += quantity;
        UnitPriceSnapshot = unitPriceSnapshot;
    }

    internal void SetQuantity(int quantity) => Quantity = quantity;
}
