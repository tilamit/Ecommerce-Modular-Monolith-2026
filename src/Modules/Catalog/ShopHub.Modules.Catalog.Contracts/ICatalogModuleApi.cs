namespace ShopHub.Modules.Catalog.Contracts;

/// <summary>
/// Catalog's public surface to other modules (spec §9.2).
/// <para>
/// Ordering cannot see <c>CatalogDbContext</c> or <c>Product</c> - they are internal - so
/// pricing and stock have to come through here. That is the constraint that keeps
/// "ordering joins to products" from ever being written.
/// </para>
/// </summary>
public interface ICatalogModuleApi
{
    /// <summary>
    /// Point-in-time product facts for a set of ids, batched.
    /// <para>
    /// Ordering snapshots these onto <c>OrderItems</c> at write time, so an order does not
    /// change when a product is later renamed or repriced (spec §4.2).
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ProductSnapshotDto>> GetProductSnapshotsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrements stock for the given lines (spec §8.4).
    /// <para>
    /// All-or-nothing: if any line cannot be satisfied, nothing is reserved and the result
    /// carries a reason per failing line. Per ADR-002 the caller turns that into a 409
    /// rather than silently dropping the line and changing the customer's total.
    /// </para>
    /// <para>
    /// This is an explicit call, not a shared transaction - a real system would use a saga
    /// or an outbox here, and this is the named seam where that would go.
    /// </para>
    /// </summary>
    Task<StockReservationResult> ReserveStockAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken = default);

    /// <summary>Returns reserved stock to the shelf when an order is cancelled (ADR-003).</summary>
    Task ReleaseStockAsync(
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken = default);

    /// <summary>Active product and category counts, for the admin dashboard (spec §10.1).</summary>
    Task<CatalogCountsDto> GetActiveCountsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Product facts as of now. Snapshot these; do not hold a reference and re-read later.</summary>
public sealed record ProductSnapshotDto(
    Guid Id,
    string Name,
    string Sku,
    decimal Price,
    string CurrencyCode,
    bool IsActive,
    int StockQuantity);

/// <summary>One line of a reservation request.</summary>
public sealed record StockReservationLine(Guid ProductId, int Quantity);

/// <summary>
/// Outcome of a reservation attempt. <see cref="Succeeded"/> is false when any line failed;
/// <see cref="Failures"/> then explains each one so the UI can say precisely what changed.
/// </summary>
public sealed record StockReservationResult(bool Succeeded, IReadOnlyList<StockReservationFailure> Failures)
{
    public static StockReservationResult Success() => new(true, []);

    public static StockReservationResult Failed(IReadOnlyList<StockReservationFailure> failures) => new(false, failures);
}

/// <summary>Why one line could not be reserved.</summary>
public sealed record StockReservationFailure(Guid ProductId, string ProductName, string Reason, int Available, int Requested);

public sealed record CatalogCountsDto(int ActiveProducts, int InactiveProducts, int ActiveCategories);
